#region

using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Logging;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GluttonyCombo.Data;
using System.Runtime.CompilerServices;
using GluttonyCombo.API.Enum;
using GluttonyCombo.Combos.PvE;
using GluttonyCombo.Combos.PvE.Enums;
using GluttonyCombo.Core;
using GluttonyCombo.CustomComboNS;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Extensions;
using GluttonyCombo.Native;
using GluttonyCombo.Services;
using GluttonyCombo.Services.IPC_Subscriber;
using GluttonyCombo.Window.Functions;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;
using static GluttonyCombo.CustomComboNS.Functions.Jobs;
using static GluttonyCombo.Data.ActionWatching;
using ActionType = FFXIVClientStructs.FFXIV.Client.Game.ActionType;
using Content = ECommons.GameHelpers.Content;

#endregion

namespace GluttonyCombo.AutoRotation;

internal unsafe class AutoRotationController
{
    public static AutoRotationConfigIPCWrapper? cfg;

    public static long HealThrottle = 0;
    private static DateTime _friendlyDiagLast = DateTime.MinValue;

    static bool _lockedST = false;
    static bool _lockedAoE = false;

    static DateTime? TimeToHeal;

    const float QueryRange = 30f;

    public static bool WouldLikeToGroundTarget;
    public static bool Paused;
    public static int UnpauseSeconds;

    public static IGameObject? AutorotHealTarget;
    public static bool AutorotRaidwiding;
    public static int AutorotRaidwides = 0;
    internal static DateTime LastRaidwideMitTime = DateTime.MinValue;
    internal const double RaidwideMitCooldownSeconds = 15.0;

    /// <summary>True while we are within the minimum gap from the last
    /// raidwide mitigation cast. Use this to gate both the autorot path
    /// and the combo-replacement path (WHM/AST/etc. RaidwideX helpers).</summary>
    internal static bool RaidwideMitOnCooldown =>
        (DateTime.UtcNow - LastRaidwideMitTime).TotalSeconds < RaidwideMitCooldownSeconds;

    /// <summary>Record a raidwide-mit cast at "now".</summary>
    internal static void MarkRaidwideMitUsed() => LastRaidwideMitTime = DateTime.UtcNow;

    // --- Raidwide AoE SHIELD gate (SGE Eukrasian Prognosis / SCH Succor) ---
    // Tracked separately from the mit gate so a healer fires ONE shield and then
    // immediately follows it with ONE mitigation on the same raidwide. (Joey 2026-06-27)
    internal static DateTime LastRaidwideShieldTime = DateTime.MinValue;
    internal const double RaidwideShieldCooldownSeconds = 10.0;
    internal static bool RaidwideShieldOnCooldown =>
        (DateTime.UtcNow - LastRaidwideShieldTime).TotalSeconds < RaidwideShieldCooldownSeconds;
    internal static void MarkRaidwideShieldUsed() => LastRaidwideShieldTime = DateTime.UtcNow;

    // SGE raidwide shield intention-lock: once we cast Eukrasia FOR the shield, NOTHING else
    // casts until Eukrasian Prognosis is out. (Joey 2026-06-27)
    private static bool _shieldEukrasiaPending;
    private static DateTime _shieldLockExpiry = DateTime.MinValue;
    // SCH raidwide shield is a HARD cast (Joey 2026-06-27): hold the lock through the whole cast
    // and mark the shield done only when the cast COMPLETES. _schSawShieldCast = we watched it cast.
    private static bool _schSawShieldCast;
    // Commit latch (mirrors SGE's _shieldEukrasiaPending): once we start the Succor cast, keep the
    // lock engaged until it completes even after GroupDamageIncoming() flips false. (Joey 2026-06-27)
    private static bool _schShieldPending;
    private static DateTime _schShieldExpiry = DateTime.MinValue;

    // --- WHM/AST timed AoE regen commit-latches (Joey 2026-07-01) ---
    // Medica II/III and Aspected Helios / Helios Conjunction are HARD casts. Same pattern as the
    // SCH shield: once the cast is issued, hold the lock through the WHOLE cast and mark the gate
    // only on COMPLETION, so the rotation can never cancel a half-started cast.
    private static bool _whmRegenPending;
    private static bool _whmSawRegenCast;
    private static DateTime _whmRegenExpiry = DateTime.MinValue;
    private static bool _astRegenPending;
    private static bool _astSawRegenCast;
    private static DateTime _astRegenExpiry = DateTime.MinValue;
    // How long AFTER the boss cast bar resolves the timed regen should FINISH casting. Raidwide
    // damage never applies at the bar - the effect packet lands ~0.6-1.5s later, and that delay is
    // per-spell and NOT available in the game sheets, so it has to be a tuned constant. 1.2s aims
    // the heal at/just after the typical application. (Joey 2026-07-01: 0.5s landed pre-hit.)
    private const float RegenLandDelaySeconds = 1.2f;
    // Arm-at-detect state: the fire time is scheduled the moment the raidwide bar is first seen,
    // so the gates get the whole bar of leeway instead of a fraction-of-a-second sample window.
    private static bool _whmRegenArmed;
    private static DateTime _whmRegenFireAt = DateTime.MinValue;
    private static bool _astRegenArmed;
    private static DateTime _astRegenFireAt = DateTime.MinValue;
    public static bool TankbusterHandled = false;

    private static Dictionary<Preset, bool> _autoActions => Presets.GetJobAutorots;

    public AutoRotationController()
    {
        OnPartyCombatChanged += ResetError;
        Svc.Chat.ChatMessage += ScanForWarnings;
        OnStatusChanged += StatusChanged;
    }

    private void StatusChanged(uint statusId, bool onPlayer)
    {
        Svc.Log.Verbose($"[AutoRotStatusCheck] {((ushort)statusId).StatusName()} {(onPlayer ? "Gained" : "Lost")}");
    }

    private void ScanForWarnings(Dalamud.Game.Chat.IHandleableChatMessage message)
    {
        if (message.LogKind != Dalamud.Game.Text.XivChatType.SystemMessage)
            return;

        bool pauseWarningFound = false;
        bool raidwideWarningFound = false;
        var logMessages = Svc.Data.Excel.GetSheet<LogMessage>();
        switch (Content.TerritoryID)
        {
            default:
                break;
        }

        if (pauseWarningFound)
        {
            Paused = true;
            Svc.Framework.RunOnTick(() => Paused = false, TimeSpan.FromSeconds(UnpauseSeconds));
        }
    }

    /// <summary>
    ///     Toggles the auto-rotation setting.
    /// </summary>
    /// <param name="value">
    ///     Whether to enable or disable auto-rotation.
    /// </param>
    public static void ToggleAutoRotation(bool value)
    {
        Service.Configuration.RotationConfig.Enabled = value;
        Service.Configuration.Save();

        var stateControlled =
            P.UIHelper.AutoRotationStateControlled() is not null;

        Paused = false;

        if (!Service.Configuration.SuppressAutorotCommand)
            DuoLog.Information(
                "Auto-Rotation set to "
                + (Service.Configuration.RotationConfig.Enabled ? "ON" : "OFF")
                + (stateControlled ? " " + OptionControlledByIPC : "")
            );
    }

    public void Dispose()
    {
        OnPartyCombatChanged -= ResetError;
        Svc.Chat.ChatMessage -= ScanForWarnings;
    }

    private void ResetError(bool state)
    {
        if (!state)
            Paused = false;
    }

    static bool CheckRezTarget(WrathPartyMember x) =>
        x.BattleChara is not null &&
        x.BattleChara.IsDead &&
        x.BattleChara.IsTargetable &&
        (cfg.HealerSettings.AutoRezOutOfParty || GetPartyMembers().Any(y => y.GameObjectId == x.BattleChara.GameObjectId)) &&
        GetTargetDistance(x.BattleChara) <= QueryRange &&
        !x.BattleChara.HasRaiseStatus &&
        !x.BattleChara.HasRaiseInvincibility &&
        TimeSpentDead(x.BattleChara.GameObjectId).TotalSeconds > 2;

    public static bool LockedST
    {
        get => _lockedST;
        set
        {
            //if (_lockedST != value)
            //    Svc.Log.Debug($"Locked ST updated to {value}");

            _lockedST = value;
        }
    }
    public static bool LockedAoE
    {
        get => _lockedAoE;
        set
        {
            //if (_lockedAoE != value)
            //    Svc.Log.Debug($"Locked AoE updated to {value}");

            _lockedAoE = value;
        }
    }

    static bool CombatBypass => DPSTargeting.BaseSelection.Any(x => (cfg.BypassQuest && IsQuestMob(x)) || (cfg.BypassFATE && x.Struct()->FateId != 0 && InFATE()));
    static bool NotInCombat => !GetPartyMembers().Any(x => x.BattleChara is not null && x.BattleChara.Struct()->InCombat && !x.IsOutOfPartyNPC) || PartyEngageDuration().TotalSeconds < cfg.CombatDelay;

    private static DateTime? _phantomHealHoldSince;
    private static DateTime _phantomHealHoldBlockedUntil = DateTime.MinValue;
    private static DateTime _phantomHealLastFired = DateTime.MinValue;

    /// <summary>Safety release for the phantom-heal hold, in seconds.</summary>
    private const double PhantomHealHoldSeconds = 4.0;

    /// <summary>
    ///     After the hold self-releases, how long before it may be taken again. Without this the
    ///     release is meaningless - the hold is simply re-taken on the next tick and the rotation
    ///     stutters instead of recovering.
    /// </summary>
    private const double PhantomHealHoldCooldownSeconds = 3.0;

    /// <summary>
    ///     Quiet period after a cure is issued. HP does not update until the cast lands, so
    ///     without this the next tick still sees the same low HP and fires another cure - the
    ///     oGCD self-heals are only gated by animation lock, so several can burn inside a couple
    ///     of seconds for a single dip, Elixir included.
    /// </summary>
    private const double PhantomHealRecastGuardSeconds = 1.5;

    /// <summary>Party members counted as hurt before an AoE cure is preferred to a single target.</summary>
    private const int PhantomHealAoECount = 2;

    /// <summary>
    ///     Animation-lock allowance for instant cures. The queue window (0.3s) is not a weave
    ///     window; a 0.38s lock was observed refusing an otherwise valid instant self-heal.
    /// </summary>
    private const double PhantomHealWeaveWindow = 0.6;


    /// <summary>
    ///     True when the action being cast is one the rotation itself would have fired - i.e.
    ///     something aimed at an enemy. Anything else is a Teleport, a Return, a raise, a Limit
    ///     Break or a deliberate manual cast, and clipping it to top up HP is never a good trade.
    ///     Unknown ids are treated as protected.
    /// </summary>
    private static bool IsOwnDamageCast(uint castActionId) =>
        castActionId != 0 &&
        ActionSheet.TryGetValue(castActionId, out var sheet) &&
        sheet.CanTargetHostile;

    /// <summary>Whether the caster may usefully be healed at all.</summary>
    private static bool SelfHealable() =>
        Player.Available && Player.Object is IBattleChara me &&
        !me.StatusList.Any(st => StatusCache.DoNotHealStatuses.Contains(st.StatusId));

    /// <summary>
    ///     Emergency phantom healing, scheduled ahead of the damage rotation.
    ///     <para/>
    ///     It considers EVERY usable cure against EVERY eligible target rather than the first
    ///     match, and it HOLDS the GCD when a cure is warranted but blocked purely on recast, so
    ///     the damage rotation cannot queue over the window the cure needs. Without the hold the
    ///     heal wins that race only by luck.
    ///     <para/>
    ///     Guards, each for a failure that actually occurred or was found by audit: the hold is
    ///     never taken for a cast-time cure while moving, self-releases after
    ///     <see cref="PhantomHealHoldSeconds"/> and then cannot be re-taken for
    ///     <see cref="PhantomHealHoldCooldownSeconds"/> (otherwise the release is a no-op and the
    ///     rotation just stutters); a cure is not issued within
    ///     <see cref="PhantomHealRecastGuardSeconds"/> of the last one, because HP has not
    ///     updated yet and a second and third cure would burn for the same dip; and a cast in
    ///     progress is only ever clipped when it is our own damage.
    /// </summary>
    private static unsafe bool TryEmergencyPhantomHeal()
    {
        if (!cfg.Enabled) return BailPhantomHeal("autorotation disabled");
        if (!Player.Available) return BailPhantomHeal("player unavailable");
        if (Player.Object.IsDead) return BailPhantomHeal("player dead");
        if (Player.Mounted) return BailPhantomHeal("mounted");
        if (Paused) return BailPhantomHeal("paused");

        if (IsOccupied())
            return BailPhantomHeal("IsOccupied");

        if (PlayerHasActionPenalty(true))
            return BailPhantomHeal("action penalty (Pyretic etc)");

        // Only ever clip our own damage. Teleport and Return are 5s casts, a raise is worth more
        // than a top-up, and a manual cast is the player's decision.
        if (Player.Object is IBattleChara { IsCasting: true } casting &&
            !IsOwnDamageCast(casting.CastActionId))
            return BailPhantomHeal($"casting a non-damage action ({casting.CastActionId.ActionName()})");

        // Let the previous cure land before considering another.
        if ((DateTime.Now - _phantomHealLastFired).TotalSeconds < PhantomHealRecastGuardSeconds)
            return BailPhantomHeal("recast guard - a cure went out less than " +
                                   $"{PhantomHealRecastGuardSeconds}s ago");

        // Keep holding between throttle ticks, or the rotation slips through the gap.
        if (!EzThrottler.Throttle("AutorotPhantomHeal", cfg.Throttler))
            return _phantomHealHoldSince is not null;

        var options = OccultCrescent.EnumerateHealOptions().ToList();
        if (options.Count == 0)
            return BailPhantomHeal("no cure is enabled, slotted AND off cooldown right now", dumpGates: true);

        var self = Player.Object;
        double selfHp = PlayerHealthPercentageHp();
        bool selfOk = SelfHealable();

        bool warranted = false;

        // 1. Free oGCD self-heals first - a weave slot, not a GCD.
        if (selfOk)
        {
            foreach (var o in options)
            {
                if (!o.IsWeave || o.Scope != OccultCrescent.PhantomHealScope.SelfOnly) continue;
                if (selfHp > o.Threshold) continue;
                if (TryFirePhantomHeal(o.Action, self, ref warranted)) return ReleaseHold();
            }
        }

        // 2. Party-wide AoE once enough people are hurt AND actually inside its radius. Counting
        //    bodies rather than reading the party AVERAGE is the point - three hurt out of eight
        //    barely moves an average - but counting people across the zone would waste the
        //    cooldown just as surely.
        foreach (var o in options)
        {
            if (o.Scope != OccultCrescent.PhantomHealScope.PartyWide) continue;
            if (CountHurtInRange(o.Action, o.Threshold, selfOk && selfHp <= o.Threshold) < PhantomHealAoECount) continue;
            if (TryFirePhantomHeal(o.Action, self, ref warranted)) return ReleaseHold();
        }

        // 3. Targeted cures on whoever is worst off, caster included.
        foreach (var o in options)
        {
            if (o.Scope != OccultCrescent.PhantomHealScope.AnyAlly) continue;
            var worst = WorstHurtBelow(o.Action, o.Threshold, selfHp, selfOk);
            if (worst is null) continue;
            if (TryFirePhantomHeal(o.Action, worst, ref warranted)) return ReleaseHold();
        }

        // 4. Remaining self-only cures on the GCD.
        if (selfOk)
        {
            foreach (var o in options)
            {
                if (o.IsWeave || o.Scope != OccultCrescent.PhantomHealScope.SelfOnly) continue;
                if (selfHp > o.Threshold) continue;
                if (TryFirePhantomHeal(o.Action, self, ref warranted)) return ReleaseHold();
            }
        }

        LogAllyHealDiag(options, selfHp, warranted);

        if (!warranted || NotInCombat || DateTime.Now < _phantomHealHoldBlockedUntil)
        {
            _phantomHealHoldSince = null;
            return false;
        }

        // Warranted but blocked purely on engine timing: hold the slot.
        _phantomHealHoldSince ??= DateTime.Now;
        if ((DateTime.Now - _phantomHealHoldSince.Value).TotalSeconds > PhantomHealHoldSeconds)
        {
            _phantomHealHoldSince = null;
            _phantomHealHoldBlockedUntil = DateTime.Now.AddSeconds(PhantomHealHoldCooldownSeconds);
            return false;
        }

        var holdAm = ActionManager.Instance();
        if (holdAm->QueuedActionId != 0)
        {
            holdAm->QueuedActionId = 0;
            holdAm->QueuedTargetId = 0;
        }

        return true;
    }

    private static DateTime _allyDiagLast = DateTime.MinValue;
    private static DateTime _bailDiagLast = DateTime.MinValue;

    /// <summary>
    ///     Clears the hold and returns false, reporting WHY when there is a hurt party member who
    ///     went unhealed. The ally diagnostic sits after the four selection passes, so every
    ///     early return above it was previously invisible - which is exactly where the misses
    ///     turned out to be hiding.
    /// </summary>
    private static bool BailPhantomHeal(string reason, bool dumpGates = false)
    {
        _phantomHealHoldSince = null;

        // Report whenever ANYONE needs healing - the caster included. The previous version only
        // considered other party members, which is exactly backwards when the person going
        // unhealed is the player.
        if ((DateTime.Now - _bailDiagLast).TotalSeconds >= 5 && SomeoneNeedsHealing())
        {
            _bailDiagLast = DateTime.Now;
            Svc.Log.Debug($"[AllyHealDiag] BAILED before selection: {reason} | selfHp={PlayerHealthPercentageHp():F0}%");

            if (dumpGates)
                foreach (var row in OccultCrescent.DescribeHealGates())
                    Svc.Log.Debug($"[AllyHealDiag] {row}");
        }

        return false;
    }

    /// <summary>Whether the caster or any party member is meaningfully hurt.</summary>
    private static bool SomeoneNeedsHealing() =>
        (Player.Available &&
         (PlayerHealthPercentageHp() <= 85 || NeedsDoomTopUp(Player.Object)))
        || AnyHurtPartyMember();

    /// <summary>Whether any party member other than the caster is meaningfully hurt.</summary>
    private static bool AnyHurtPartyMember()
    {
        foreach (var m in GetPartyMembers())
        {
            var c = m.BattleChara;
            if (c is null || c.IsDead) continue;
            if (Player.Available && c.GameObjectId == Player.Object.GameObjectId) continue;
            if (GetTargetHPPercent(c) <= 85 || NeedsDoomTopUp(c)) return true;
        }

        return false;
    }


    /// <summary>
    ///     Explains, once every few seconds, why a hurt party member was not healed. Only speaks
    ///     when there is actually somebody to heal and nothing fired, and it names the specific
    ///     filter that rejected each candidate rather than leaving it to be guessed.
    /// </summary>
    private static unsafe void LogAllyHealDiag(List<OccultCrescent.PhantomHealOption> options, double selfHp, bool warranted)
    {
        if ((DateTime.Now - _allyDiagLast).TotalSeconds < 5) return;

        var allyOpts = options.Where(o => o.Scope == OccultCrescent.PhantomHealScope.AnyAlly).ToList();
        double maxThreshold = allyOpts.Count > 0 ? allyOpts.Max(o => o.Threshold) : 0;

        var hurt = new List<IBattleChara>();
        foreach (var m in GetPartyMembers())
        {
            var c = m.BattleChara;
            if (c is null || c.IsDead) continue;
            if (Player.Available && c.GameObjectId == Player.Object.GameObjectId) continue;
            if (GetTargetHPPercent(c) <= 85) hurt.Add(c);
        }

        if (hurt.Count == 0) return;

        _allyDiagLast = DateTime.Now;

        string cures = allyOpts.Count == 0
            ? "NONE (no ally-castable cure slotted/ready)"
            : string.Join(", ", allyOpts.Select(o => $"{o.Action.ActionName()}<={o.Threshold}"));

        Svc.Log.Debug($"[AllyHealDiag] allyCures={cures} | totalOptions={options.Count} | selfHp={selfHp:F0} " +
            $"| warranted={warranted} | holdActive={_phantomHealHoldSince is not null} " +
            $"| holdBlocked={DateTime.Now < _phantomHealHoldBlockedUntil} " +
            $"| remainingGCD={RemainingGCD:F2} animLock={AnimationLock:F2} timeMoving={TimeMoving.TotalMilliseconds:F0}ms " +
            $"| sinceLastCure={(_phantomHealLastFired == DateTime.MinValue ? "never" : $"{(DateTime.Now - _phantomHealLastFired).TotalSeconds:F1}s")}");

        uint probe = allyOpts.Count > 0 ? allyOpts[0].Action : 0;

        foreach (var c in hurt)
        {
            string verdict;
            double hp = GetTargetHPPercent(c);

            if (probe == 0)
                verdict = "no ally cure available";
            else if (!c.IsTargetable)
                verdict = "not targetable";
            else if (c.StatusList.Any(st => StatusCache.DoNotHealStatuses.Contains(st.StatusId)))
                verdict = "carries a do-not-heal status";
            else if (hp > maxThreshold)
                verdict = $"above threshold ({maxThreshold})";
            else if (!ActionManager.CanUseActionOnTarget(probe, c.GameObject()))
                verdict = "CanUseActionOnTarget = false";
            else
            {
                uint code = ActionManager.GetActionInRangeOrLoS(probe, Player.GameObject, c.GameObject());
                verdict = code is 0 or 565
                    ? "ELIGIBLE - rejected downstream, see timing on the line above"
                    : $"out of range / no line of sight (code {code})";
            }

            Svc.Log.Debug($"[AllyHealDiag]   {c.Name} hp={hp:F0}% dist={GetTargetDistance(c):F1}y -> {verdict}");
        }
    }

    private static bool ReleaseHold()
    {
        _phantomHealHoldSince = null;
        _phantomHealLastFired = DateTime.Now;
        return true;
    }

    /// <summary>
    ///     Units at or below <paramref name="threshold"/> that an AoE cure centred on the caster
    ///     would actually reach. Range is checked against the action itself, so a party scattered
    ///     across the zone cannot trigger it.
    /// </summary>
    private static unsafe int CountHurtInRange(uint action, double threshold, bool selfCounts)
    {
        // Party-wide cures are AoEs centred on the caster, so the radius that matters is the
        // action's EFFECT range, not its targeting range - the latter is 0 for a self-centred
        // AoE and would exclude the entire party. A zero radius is treated as unknown and left
        // unfiltered rather than silently excluding everyone.
        float radius = GetActionEffectRange(action);

        int n = selfCounts ? 1 : 0;

        foreach (var member in GetPartyMembers())
        {
            var chara = member.BattleChara;
            if (chara is null || chara.IsDead || !chara.IsTargetable) continue;
            if (Player.Available && chara.GameObjectId == Player.Object.GameObjectId) continue;
            if (chara.StatusList.Any(st => StatusCache.DoNotHealStatuses.Contains(st.StatusId))) continue;

            double hp = GetTargetHPPercent(chara);
            if (hp > threshold) continue;
            if (radius > 0 && GetTargetDistance(chara) > radius) continue;
            if (!IsInLineOfSight(chara)) continue;

            n++;
        }

        return n;
    }

    /// <summary>
    ///     Lowest-HP unit at or below <paramref name="threshold"/> that <paramref name="action"/>
    ///     can be cast on right now - caster included. Excludes the dead, the untargetable,
    ///     anyone carrying a do-not-heal status, and anyone out of range or line of sight.
    /// </summary>
    private static unsafe IGameObject? WorstHurtBelow(uint action, double threshold, double selfHp, bool selfOk)
    {
        IGameObject? best = null;
        double bestHp = double.MaxValue;

        if (selfOk && selfHp <= threshold)
        {
            best = Player.Object;
            bestHp = selfHp;
        }

        foreach (var member in GetPartyMembers())
        {
            var chara = member.BattleChara;
            if (chara is null || chara.IsDead || !chara.IsTargetable) continue;
            if (Player.Available && chara.GameObjectId == Player.Object.GameObjectId) continue;
            if (chara.StatusList.Any(st => StatusCache.DoNotHealStatuses.Contains(st.StatusId))) continue;

            double hp = GetTargetHPPercent(chara);
            if (hp > threshold || hp >= bestHp) continue;
            if (!ActionManager.CanUseActionOnTarget(action, chara.GameObject())) continue;
            if (ActionManager.GetActionInRangeOrLoS(action, Player.GameObject, chara.GameObject()) is not (0 or 565)) continue;

            best = chara;
            bestHp = hp;
        }

        return best;
    }

    /// <summary>
    ///     Attempts one cure on one target. Returns true only when the cast was actually issued.
    ///     <paramref name="warranted"/> is set when the cure is usable in principle and the only
    ///     obstacle is engine timing - the signal to hold the GCD rather than hand it back.
    /// </summary>
    private static unsafe bool TryFirePhantomHeal(uint act, IGameObject target, ref bool warranted)
    {
        var attackType = act.ActionAttackType();

        if (attackType is ActionAttackType.Spell && HasStatusEffect(All.Debuffs.Silence))
            return false;
        if (attackType is ActionAttackType.Ability && HasAmnesia)
            return false;

        if (!ActionManager.CanUseActionOnTarget(act, target.Struct()))
            return false;

        var castTime = ActionManager.GetAdjustedCastTime(ActionType.Action, act);
        bool blockedByMovement = castTime > 0 && TimeMoving.TotalMilliseconds > 0;

        // The cure is usable on this target; only engine timing stands in the way. Movement
        // counts as timing, deliberately. Previously movement suppressed `warranted`, so no hold
        // was taken - and the instant the player stood still the damage rotation claimed the GCD
        // before the cure could. Observed 2026-08-02: an eligible party member 14y away, GCD
        // fully up, refused solely because the player had been moving for 214ms. Holding through
        // movement costs the damage instants that would otherwise fire while kiting, which is
        // the right trade for a heal, and the hold is still capped and rate-limited.
        warranted = true;

        // Cooldown is a timing block like any other, and for a GCD cure it is THE timing block:
        // the global cooldown leaves it unready for most of every GCD. Checking it before
        // registering intent is what stopped the hold from ever engaging.
        if (!ActionReady(act))
            return false;

        if (attackType is ActionAttackType.Ability)
        {
            // A weave window is roughly 0.6s, not the queue window. At QueueWindow (0.3) an
            // animation lock of 0.38 was enough to refuse an instant self-heal outright.
            if (AnimationLock > PhantomHealWeaveWindow) return false;
        }
        else if (RemainingGCD > cfg.QueueWindow)
        {
            return false;
        }

        // A cast cannot begin while moving - attempting it would only fail - but we have already
        // registered the intent above, so the hold keeps the slot until the player stops.
        if (blockedByMovement)
            return false;

        var am = ActionManager.Instance();

        // Displace a queued damage action, otherwise the queue consumes this window. In combat
        // only: out of combat whatever is queued is the player's own doing.
        if (!NotInCombat && am->QueuedActionId != 0 && am->QueuedActionId != act)
        {
            am->QueuedActionId = 0;
            am->QueuedTargetId = 0;
        }

        return am->UseAction(ActionType.Action, act, target.GameObjectId);
    }

    private static bool ShouldSkipAutorotation()
    {
        // Gate autorotation while the player has Pyretic / Acceleration Bomb / similar.
        // PlayerHasActionPenalty (Status.cs) is Wrath dynamic detection: icon-based
        // Pyretic scan + Acceleration Bomb expiry timing + encounter-specific IDs.
        if (PlayerHasActionPenalty(true))
            return true;

        // Enemy damage-reflect / spikes (e.g. Eureka Gelid Charge -> Ice Spikes,
        // Static Charge -> Shock Spikes, and the elemental Counter stances). Scans
        // nearby hostiles and stops + targets self until it clears on every mob.
        // Gated behind the same "Un-target and stop actions for Pyretics" toggle.
        if (cfg.DPSSettings.UnTargetAndDisableForPenalty && EnemyHasReflectPenalty())
            return true;

        return !cfg.Enabled
               || !Player.Available
               || Player.Object.IsDead
               || IsOccupied()
               || Player.Mounted
               || !EzThrottler.Throttle("Autorot", cfg.Throttler)
               || (ActionManager.Instance()->QueuedActionId > 0)
               || Player.Object.HasRaiseInvincibility
               || Paused;
    }

    /// <summary>
    /// Scans nearby hostile enemies (the same selection the rotation targets) for a
    /// damage-reflect / counter / "spikes" status that punishes attackers - often a
    /// one-shot in Eureka (Gelid Charge -> Ice Spikes, Static Charge -> Shock Spikes).
    /// While any such mob is present, targets self and cancels casts; autorotation
    /// resumes once no mob has the status. See StatusCache.PausingStatuses.EnemyReflects.
    /// </summary>
    private static bool EnemyHasReflectPenalty()
    {
        if (!DPSTargeting.BaseSelection.Any(x =>
                StatusCache.HasStatusInCacheList(StatusCache.PausingStatuses.EnemyReflects, x)))
            return false;

        // Stop and target self, mirroring the Pyretic handling but for enemy reflects.
        if (Player.Available)
            Svc.Targets.Target = Player.Object;
        OverrideTarget = null;
        UIState.Instance()->Hotbar.CancelCast();
        return true;
    }

    internal static void Run()
    {
        cfg ??= new AutoRotationConfigIPCWrapper(Service.Configuration.RotationConfig);

        if (!cfg.Enabled)
            OverrideTarget = null;

        // Emergency phantom healing is scheduled BEFORE the damage rotation, and before
        // ShouldSkipAutorotation(), because that method aborts the whole tick whenever an
        // action is queued. See TryEmergencyPhantomHeal().
        if (TryEmergencyPhantomHeal())
            return;

        // Early exit for all conditions that should prevent autorotation
        if (ShouldSkipAutorotation())
            return;

        uint _ = 0;

        // Pre-emptive HoT/Shield for healers
        if (cfg.HealerSettings.PreEmptiveHoT && Player.Job is Job.CNJ or Job.WHM or Job.AST)
            PreEmptiveHot();

        if (cfg.HealerSettings.PreEmptiveHoT && Player.Job is Job.SGE or Job.SCH)
            PreEmptiveShield();

        // Bypass buffs logic
        if (cfg.BypassBuffs && NotInCombat)
        {
            if (ProcessAutoActions(ref _, false, true))
                return;
        }

        // Only run in combat if required
        if (cfg.InCombatOnly && NotInCombat && !CombatBypass)
            return;

        // Check for Pyretic / Reasons to stop
        if (cfg.DPSSettings.UnTargetAndDisableForPenalty && PlayerHasActionPenalty(true))
            return;

        // Healer logic
        bool isHealer = Player.Object?.Role is CombatRole.Healer ||
                        (Player.Job is Job.BLU && BLU.HasHealerMimicry);

        // SGE raidwide AoE shield - HARD intention-lock (Joey 2026-06-27): once we cast Eukrasia
        // for the shield, do ONLY Eukrasian Prognosis (nothing else casts) until it is out.
        if (isHealer && HealerRaidwideShieldLock())
            return;

        if (cfg.HealerSettings.HandleRaidwides)
        {
            if (isHealer && GroupDamageIncoming(out var multi))
            {
                AutorotRaidwiding = true;
                HandleRaidwide(multi);
            }
            else
            {
                if (AutorotRaidwides > 0)
                {
                    Svc.Log.Debug($"Used {AutorotRaidwides} raidwides {string.Join(", ", BlacklistedRaidwides.Select(x => x.ActionName()))}");
                    BlacklistedRaidwides.Clear();
                }
                AutorotRaidwides = 0;
                AutorotRaidwiding = false;
                // The 15s mit gate is intentionally NOT reset here ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â it should carry
                // across the gap between raidwides within the same pull. It is reset
                // out-of-combat by the parent gate (cfg.InCombatOnly) and harmlessly
                // self-resolves after 15s of no raidwides.
            }
        }

        if (cfg.HealerSettings.HandleTankbusters)
        {
            if (isHealer && TryGetTankBusterTarget(out var tbtarget, cfg.HealerSettings.TankbustersBeyondParty))
            {
                HandleTankbuster(tbtarget.SafeGameObjectId);
            }
            else
                TankbusterHandled = false;
        }

        var healTarget = isHealer ? AutoRotationHelper.GetSingleTarget(cfg.HealerRotationMode) : null;

        bool aoeheal = isHealer
                       && HealerTargeting.CanAoEHeal()
                       && _autoActions.Any(x => x.Key.Attributes().AutoAction?.IsHeal == true && x.Key.Attributes().AutoAction?.IsAoE == true);

        bool needsHeal = ((healTarget != null
                           && _autoActions.Any(x => x.Key.Attributes().AutoAction?.IsHeal == true && x.Key.Attributes().AutoAction?.IsAoE != true))
                          || aoeheal)
                         && isHealer;

        if (needsHeal && TimeToHeal is null)
            TimeToHeal = DateTime.Now;
        else if (!needsHeal)
            TimeToHeal = null;

        // Check if any healing action is ready
        bool actCheck = _autoActions.Any(x =>
        {
            var attr = x.Key.Attributes();
            return attr.AutoAction?.IsHeal == true && ActionReady(AutoRotationHelper.InvokeCombo(x.Key, attr, ref _));
        });

        bool canHeal = TimeToHeal is not null
                       && (DateTime.Now - TimeToHeal.Value).TotalSeconds >= cfg.HealerSettings.HealDelay
                       && actCheck;

        // Healer cleanse/rez logic
        if (isHealer ||
            (Player.Job is Job.SMN or Job.RDM && cfg.HealerSettings.AutoRezDPSJobs) ||
            OccultCrescent.CanPhantomRaise() ||
            Variant.CanRaise())
        {
            if (ActionManager.Instance()->QueuedActionId == RoleActions.Healer.Esuna ||
                ActionManager.Instance()->QueuedActionId == BLU.Exuviation)
                ActionManager.Instance()->QueuedActionId = 0;

            if ((!needsHeal || GetPartyMembers().Any(x => HasCleansableDoom(x.BattleChara))) && WrathOpener.CurrentOpener?.CurrentState is not
                OpenerState.InOpener)
            {
                if (cfg.HealerSettings.AutoCleanse && isHealer)
                    CleanseParty();

                // RezParty() reports whether it actually fired something this tick. When it
                // did, end the tick - otherwise Run() falls straight through to
                // ProcessAutoActions below and the damage rotation spends the Swiftcast we
                // just used for the raise on an instant Glare. Swiftcast never queues, so the
                // QueuedActionId guard in ShouldSkipAutorotation() cannot catch it.
                //
                // It returns true ONLY on the line after a real UseAction - never for a state
                // like "mid-cast" or "the buff is up". v1.0.4.126 did the latter and stalled
                // the whole rotation; see CHANGELOG. (Joey 2026-08-03)
                if (cfg.HealerSettings.AutoRez && RezParty())
                    return;
            }
        }

        // SGE Kardia logic
        if (Player.Job is Job.SGE && cfg.HealerSettings.ManageKardia)
            UpdateKardiaTarget();

        // SGE tank-shield upkeep: keep Eukrasian Diagnosis on a heavily-targeted tank and
        // spend capped Addersting via Toxikon so the upkeep doesn't waste the gauge.
        if (Player.Job is Job.SGE)
            UpdateSgeTankShield();

        // WHM Divine Caress logic
        if (Player.Job is Job.WHM && HasStatusEffect(WHM.Buffs.DivineGrace) && AbleToCast(WHM.DivineCaress) && !(Player.Object?.IsCasting() is true))
        {
            WouldLikeToGroundTarget = ActionSheet[WHM.DivineCaress].TargetArea;
            ActionManager.Instance()->UseAction(ActionType.Action, WHM.DivineCaress);
            WouldLikeToGroundTarget = false;
        }

        // Reset locks if no action for 3 seconds
        if (TimeSinceLastAction.TotalSeconds >= 3)
        {
            LockedAoE = false;
            LockedST = false;
        }

        ProcessAutoActions(ref _, canHeal, false);
    }

    public static IEnumerable<uint> TankbusterActions =
    [
        WHM.Aquaveil,
        WHM.DivineBenison,
        SCH.Protraction,
        SCH.Adloquium,
        SCH.Manifestation,
        AST.Spire,
        AST.Bole,
        AST.CelestialIntersection,
        AST.Exaltation,
        SGE.Taurochole,
        SGE.Eukrasia,
        SGE.EukrasianDiagnosis,
    ];

    private static void HandleTankbuster(ulong? safeGameObjectId)
    {
        if (safeGameObjectId == null)
            return;

        foreach (var spell in TankbusterActions)
        {
            if (TankbusterHandled)
                return;

            if (AbleToCast(spell, safeGameObjectId))
            {
                var act = spell;
                if (act == AST.Bole) act = AST.Play2;
                if (act == AST.Spire) act = AST.Play3;
                WouldLikeToGroundTarget = ActionSheet.TryGetValue(act, out var s) && s.TargetArea;
                ActionManager.Instance()->UseAction(ActionType.Action, act is SGE.Eukrasia ? act.Retarget(SimpleTarget.Self) : act.Retarget(safeGameObjectId.GetObject()), safeGameObjectId!.Value);
                WouldLikeToGroundTarget = false;
                if (act != SGE.Eukrasia)
                    TankbusterHandled = true;
                return;
            }
        }
    }

    private static bool AbleToCast(uint spell, ulong? safeGameObjectId = null)
    {
        return ActionReady(spell) && (safeGameObjectId != null ? !JustUsedOn(spell, safeGameObjectId.GetObject(), 5) : !JustUsed(spell, 10)) && LocalPlayer.CastActionId != spell && (!IsMoving(true) || ActionManager.GetAdjustedCastTime(ActionType.Action, spell) == 0);
    }

    public static IEnumerable<(uint Action, bool MultiHitOnly)> RaidwideActions =
    [
        (WHM.LiturgyOfTheBell.Retarget(SimpleTarget.Self), true),
        (WHM.PlenaryIndulgence, false),
        (WHM.Temperance, false),
        (WHM.DivineCaress, false),
        (WHM.Asylum.Retarget(SimpleTarget.Self), false),
        (WHM.Medica2, false),
        (WHM.Medica3, false),
        (SCH.Expedient, false),
        (SCH.Seraphism, false),
        (SCH.Succor, false),
        (SCH.Accession, false),
        (SCH.Concitation, false),
        (AST.CollectiveUnconscious, false),
        (AST.SunSign, false),
        (AST.CelestialOpposition, false),
        (AST.AspectedHelios, false),
        (AST.HeliosConjuction, false),
        (SGE.Panhaima, true),
        (SGE.Kerachole, false),
        (SGE.Physis, false),
        (SGE.Physis2, false),
        (SGE.Holos, false),
        (SGE.Eukrasia, false),
        (SGE.EukrasianPrognosis, false),
        (SGE.EukrasianPrognosis2, false),
    ];

    public static List<uint> BlacklistedRaidwides = [];

    /// <summary>SGE/SCH still owe the party their AoE shield this raidwide: preset on,
    /// shield not on its short cooldown, and the party is not already shielded. While this
    /// is true we hold mitigation back so the shield always lands FIRST.</summary>
    /// <summary>SGE/SCH still owe the party an AoE shield this raidwide (not on its short
    /// cooldown, party not already shielded). Auto-rotation owns the shield directly here -
    /// preset-INDEPENDENT, exactly like the mit list - so it fires as reliably as the
    /// mitigations instead of depending on the per-job combo path. (Joey 2026-06-27)</summary>
    private static bool RaidwideShieldPending()
    {
        if (RaidwideShieldOnCooldown)
            return false;
        return Player.Job switch
        {
            Job.SGE => IsEnabled(Preset.SGE_Raidwide_EPrognosis) && ActionLearned(SGE.Eukrasia) &&
                       GetPartyBuffPercent(SGE.Buffs.EukrasianPrognosis) <= 50,
            Job.SCH => IsEnabled(Preset.SCH_Raidwide_Succor) && ActionLearned(SCH.Succor) &&
                       GetPartyBuffPercent(SCH.Buffs.Galvanize) <= 50,
            _ => false
        };
    }

    /// <summary>Healer raidwide AoE shield HARD intention-lock. Returns true when Run() must
    /// lock to the shield this tick (and casts the right step). SGE: once Eukrasia is pressed for
    /// the shield, nothing else fires until Eukrasian Prognosis is out. SCH: claim the next GCD for
    /// Succor/Concitation and lock until the shield lands. WHM/AST: timed AoE regen hard casts
    /// on the same commit-latch, started so the HoT lands right as/after the raidwide hits.
    /// (Joey 2026-06-27 / 2026-07-01)</summary>
    private static bool HealerRaidwideShieldLock()
    {
        if (!cfg.HealerSettings.HandleRaidwides || Player.Job is not (Job.SGE or Job.SCH or Job.WHM or Job.AST))
        {
            _shieldEukrasiaPending = false;
            return false;
        }

        return Player.Job switch
        {
            Job.SCH => SchRaidwideShieldLock(),
            Job.SGE => SgeRaidwideShieldLock(),
            Job.WHM => WhmRaidwideRegenLock(),
            _ => AstRaidwideRegenLock(),
        };
    }

    /// <summary>SCH shield lock: Succor/Concitation is a single hard cast (no 2-step), so just
    /// claim the next GCD for it and lock until the Galvanize shield is up. (Joey 2026-06-27)</summary>
    private static bool SchRaidwideShieldLock()
    {
        // No Galvanize gate (Joey 2026-06-27): a Galvanize already on the party - e.g. a lingering
        // Adloquium shield on the tank - must NOT stop the raidwide Succor from going out.
        // Commit-latch safety: never lock forever if the cast somehow never lands.
        if (_schShieldPending && DateTime.UtcNow > _schShieldExpiry)
        {
            _schShieldPending = false;
            _schSawShieldCast = false;
        }

        bool wanted = _schShieldPending ||
                      (GroupDamageIncoming() &&
                       IsEnabled(Preset.SCH_Raidwide_Succor) &&
                       ActionLearned(SCH.Succor) &&
                       !RaidwideShieldOnCooldown);
        if (!wanted)
        {
            _schSawShieldCast = false;
            return false;
        }

        uint shield = OriginalHook(SCH.Succor);   // Succor -> Concitation (96+) / Accession (Seraphism)
        bool castingShield = Player.Object?.IsCasting() is true &&
                             (LocalPlayer.CastActionId == SCH.Succor ||
                              LocalPlayer.CastActionId == SCH.Concitation ||
                              LocalPlayer.CastActionId == SCH.Accession);

        // Succor/Concitation is a ~2s HARD CAST (no instant version - Recitation only removes the
        // cost + crits, it does NOT make it instant). So we mark the shield gate only when the cast
        // COMPLETES - never when it merely starts - then release so a mitigation weaves in. Completion
        // is detected by having watched it cast (then stop), or the spell registering as just-used.
        if (!castingShield &&
            (_schSawShieldCast || JustUsed(SCH.Succor, 1.5f) || JustUsed(SCH.Concitation, 1.5f) || JustUsed(SCH.Accession, 1.5f)))
        {
            _schSawShieldCast = false;
            _schShieldPending = false;
            MarkRaidwideShieldUsed();
            return false;
        }

        // Currently hard-casting the shield -> HOLD the lock through the WHOLE cast so the rotation
        // never resumes mid-cast and nothing perturbs it. (v1.0.4.66 released the instant it started.)
        if (castingShield)
        {
            _schSawShieldCast = true;
            _schShieldPending = true;
            return true;
        }

        // Not casting the shield yet -> claim the next GCD for it (queues if a GCD is mid-cast,
        // fires the instant the GCD frees).
        bool cast = ActionManager.Instance()->UseAction(ActionType.Action, shield, Player.Object.GameObjectId);
        if (!_schShieldPending)
        {
            _schShieldPending = true;
            _schShieldExpiry = DateTime.UtcNow.AddSeconds(4);
        }
        return true; // LOCK
    }

    /// <summary>SGE raidwide AoE shield HARD intention-lock (2-step Eukrasia -> Eukrasian
    /// Prognosis). Only reached for SGE via HealerRaidwideShieldLock. (Joey 2026-06-27)</summary>
    private static bool SgeRaidwideShieldLock()
    {
        // Safety: never lock forever if the 2-step somehow stalls.
        if (_shieldEukrasiaPending && DateTime.UtcNow > _shieldLockExpiry)
            _shieldEukrasiaPending = false;

        bool wanted = _shieldEukrasiaPending ||
                      (GroupDamageIncoming() &&
                       IsEnabled(Preset.SGE_Raidwide_EPrognosis) &&
                       ActionLearned(SGE.Eukrasia) &&
                       !RaidwideShieldOnCooldown &&
                       GetPartyBuffPercent(SGE.Buffs.EukrasianPrognosis) <= 50);
        if (!wanted)
            return false;

        if (HasStatusEffect(SGE.Buffs.Eukrasia))
        {
            // Eukrasia up -> cast Eukrasian Prognosis (base Prognosis + self target; the game
            // transforms it). Succeeds the moment the GCD is free.
            bool cast = ActionManager.Instance()->UseAction(ActionType.Action, OriginalHook(SGE.Prognosis), Player.Object.GameObjectId);
            if (cast)
            {
                MarkRaidwideShieldUsed();
                _shieldEukrasiaPending = false;
            }
        }
        else if (!_shieldEukrasiaPending)
        {
            // Begin the 2-step: cast Eukrasia, then lock until Prognosis is out.
            if (ActionReady(SGE.Eukrasia) &&
                ActionManager.Instance()->UseAction(ActionType.Action, SGE.Eukrasia))
            {
                _shieldEukrasiaPending = true;
                _shieldLockExpiry = DateTime.UtcNow.AddSeconds(4);
            }
        }
        // else: pending but Eukrasia not applied yet -> hold (lock).

        return true; // LOCK: caller returns; nothing else this tick.
    }

    /// <summary>WHM timed AoE regen lock. Medica II / Medica III is a ~2s HARD cast, aimed to
    /// COMPLETE ~RegenLandDelaySeconds after the raidwide cast bar resolves (damage applies
    /// ~0.6-1.5s after the bar, never at it). ARM-at-detect: the fire time is scheduled as soon
    /// as the bar appears - gates are evaluated with the whole bar of leeway. (v71 sampled the
    /// gates only inside the final castS-minus-delay sliver of the bar, which for short casts
    /// like AST's 1.5s Helios was ~0.3s wide and missed almost every time.) Same commit-latch as
    /// the SCH shield: hold through the whole cast, mark the gate only on COMPLETION.
    /// (Joey 2026-07-01)</summary>
    private static bool WhmRaidwideRegenLock()
    {
        // Commit-latch safety: never lock forever if the cast somehow never lands.
        if (_whmRegenPending && DateTime.UtcNow > _whmRegenExpiry)
        {
            _whmRegenPending = false;
            _whmSawRegenCast = false;
        }

        uint regen = OriginalHook(WHM.Medica2);   // Medica II -> Medica III (85+)
        ushort hot = ActionLearned(WHM.Medica3) ? WHM.Buffs.Medica3 : WHM.Buffs.Medica2;
        float castS = ActionManager.GetAdjustedCastTime(ActionType.Action, regen) / 1000f;
        float? rem = RaidwideTimeRemaining();
        bool gates = IsEnabled(Preset.WHM_Raidwide_Medica) &&
                     ActionLearned(WHM.Medica2) &&
                     !RaidwideShieldOnCooldown &&
                     GetPartyBuffPercent(hot) <= 50;

        // ARM: raidwide bar is up and the gates pass -> schedule the fire time
        // (bar end + land delay - our own cast time), then fire by the clock below.
        if (!_whmRegenPending && !_whmRegenArmed && gates && rem is { } armRem)
        {
            float fireIn = Math.Max(0f, armRem + RegenLandDelaySeconds - castS);
            _whmRegenArmed = true;
            _whmRegenFireAt = DateTime.UtcNow.AddSeconds(fireIn);
        }

        // Disarm if the bar vanished before the fire time (interrupted / resolved early) or the
        // party picked up the HoT some other way while we waited.
        if (_whmRegenArmed && !_whmRegenPending &&
            ((rem is null && DateTime.UtcNow < _whmRegenFireAt) || GetPartyBuffPercent(hot) > 50))
        {
            _whmRegenArmed = false;
            return false;
        }

        bool fireNow = _whmRegenArmed && DateTime.UtcNow >= _whmRegenFireAt;
        // VFX/stack-marker detections carry no cast bar to time against - fire immediately.
        bool vfxFire = rem is null && gates && GroupDamageIncoming();
        bool wanted = _whmRegenPending || fireNow || vfxFire;
        if (!wanted)
        {
            _whmSawRegenCast = false;
            return false;
        }

        bool castingRegen = Player.Object?.IsCasting() is true &&
                            (LocalPlayer.CastActionId == WHM.Medica2 ||
                             LocalPlayer.CastActionId == WHM.Medica3);

        // Cast COMPLETE -> mark the gate + release (a mitigation can follow immediately).
        if (!castingRegen &&
            (_whmSawRegenCast || JustUsed(WHM.Medica2, 1.5f) || JustUsed(WHM.Medica3, 1.5f)))
        {
            _whmSawRegenCast = false;
            _whmRegenPending = false;
            _whmRegenArmed = false;
            MarkRaidwideShieldUsed();
            return false;
        }

        // Currently hard-casting the regen -> HOLD the lock through the WHOLE cast.
        if (castingRegen)
        {
            _whmSawRegenCast = true;
            _whmRegenPending = true;
            return true;
        }

        // Movement kills a hard cast. Not committed yet -> stand down this tick but STAY ARMED;
        // it fires the moment movement stops (slightly late beats never).
        if (!_whmRegenPending && IsMoving())
            return false;

        // Claim the next GCD for the regen (queues if a GCD is mid-cast).
        bool cast = ActionManager.Instance()->UseAction(ActionType.Action, regen, Player.Object.GameObjectId);
        if (!_whmRegenPending)
        {
            _whmRegenPending = true;
            _whmRegenExpiry = DateTime.UtcNow.AddSeconds(4);
        }
        return true; // LOCK
    }

    /// <summary>AST timed AoE regen lock. Aspected Helios / Helios Conjunction is a ~1.5s HARD
    /// cast; same arm-at-detect + fire-by-clock + commit-latch as WHM. Fires with or without
    /// Neutral Sect (under Neutral Sect it also grants the party shield). (Joey 2026-07-01)</summary>
    private static bool AstRaidwideRegenLock()
    {
        // Commit-latch safety: never lock forever if the cast somehow never lands.
        if (_astRegenPending && DateTime.UtcNow > _astRegenExpiry)
        {
            _astRegenPending = false;
            _astSawRegenCast = false;
        }

        uint regen = OriginalHook(AST.AspectedHelios);   // Aspected Helios -> Helios Conjunction (96+)
        float castS = ActionManager.GetAdjustedCastTime(ActionType.Action, regen) / 1000f;
        float? rem = RaidwideTimeRemaining();
        // Under Neutral Sect fire for the enhanced shield+regen when the party lacks it; otherwise
        // fire as the recovery regen when the party is not already under the HoT.
        bool partyNeedsIt = HasStatusEffect(AST.Buffs.NeutralSect)
            ? !HasStatusEffect(AST.Buffs.NeutralSectShield)
            : GetPartyBuffPercent(AST.Buffs.AspectedHelios) <= 50 &&
              GetPartyBuffPercent(AST.Buffs.HeliosConjunction) <= 50;
        bool gates = IsEnabled(Preset.AST_Raidwide_AspectedHelios) &&
                     ActionLearned(AST.AspectedHelios) &&
                     !RaidwideShieldOnCooldown &&
                     partyNeedsIt;

        // ARM: raidwide bar is up and the gates pass -> schedule the fire time.
        if (!_astRegenPending && !_astRegenArmed && gates && rem is { } armRem)
        {
            float fireIn = Math.Max(0f, armRem + RegenLandDelaySeconds - castS);
            _astRegenArmed = true;
            _astRegenFireAt = DateTime.UtcNow.AddSeconds(fireIn);
        }

        // Disarm if the bar vanished early or the party got covered while we waited.
        if (_astRegenArmed && !_astRegenPending &&
            ((rem is null && DateTime.UtcNow < _astRegenFireAt) || !partyNeedsIt))
        {
            _astRegenArmed = false;
            return false;
        }

        bool fireNow = _astRegenArmed && DateTime.UtcNow >= _astRegenFireAt;
        // VFX/stack-marker detections carry no cast bar to time against - fire immediately.
        bool vfxFire = rem is null && gates && GroupDamageIncoming();
        bool wanted = _astRegenPending || fireNow || vfxFire;
        if (!wanted)
        {
            _astSawRegenCast = false;
            return false;
        }

        bool castingRegen = Player.Object?.IsCasting() is true &&
                            (LocalPlayer.CastActionId == AST.AspectedHelios ||
                             LocalPlayer.CastActionId == AST.HeliosConjuction);

        // Cast COMPLETE -> mark the gate + release.
        if (!castingRegen &&
            (_astSawRegenCast || JustUsed(AST.AspectedHelios, 1.5f) || JustUsed(AST.HeliosConjuction, 1.5f)))
        {
            _astSawRegenCast = false;
            _astRegenPending = false;
            _astRegenArmed = false;
            MarkRaidwideShieldUsed();
            return false;
        }

        // Currently hard-casting the regen -> HOLD the lock through the WHOLE cast.
        if (castingRegen)
        {
            _astSawRegenCast = true;
            _astRegenPending = true;
            return true;
        }

        // Movement: not committed yet -> stand down this tick but STAY ARMED.
        if (!_astRegenPending && IsMoving())
            return false;

        // Claim the next GCD for the regen (queues if a GCD is mid-cast).
        bool cast = ActionManager.Instance()->UseAction(ActionType.Action, regen, Player.Object.GameObjectId);
        if (!_astRegenPending)
        {
            _astRegenPending = true;
            _astRegenExpiry = DateTime.UtcNow.AddSeconds(4);
        }
        return true; // LOCK
    }

    private static void HandleRaidwide(bool multihit)
    {
        // Shield-first: hold mitigation while SGE/SCH still owe the AoE shield. The shield itself
        // is cast by the per-job combo (RaidwideEprognosis / RaidwideSuccor) through the heal-cast
        // path, which prioritises the shield over Eukrasian Dosis so the Eukrasia is not stolen.
        if (RaidwideShieldPending())
            return;

        // Hard minimum gap between any two raidwide mitigations from autorot.
        if (RaidwideMitOnCooldown)
            return;

        foreach (var (spell, multihitter) in RaidwideActions)
        {
            int numberOfCasts = GetPartyAvgHPPercent() switch
            {
                <= 30 => 3,
                <= 60 => 2,
                _ => 1
            };

            if (AutorotRaidwides >= numberOfCasts)
                return;

            if (!multihit && multihitter)
                continue;

            if (BlacklistedRaidwides.Contains(spell))
                continue;

            // The timed-regen presets OWN these casts - don't burn them early as generic mits at
            // detect time (that would apply the HoT too soon and make the timed cast skip itself
            // on the party-already-has-the-HoT check).
            if (IsEnabled(Preset.WHM_Raidwide_Medica) && (spell == WHM.Medica2 || spell == WHM.Medica3))
                continue;
            if (IsEnabled(Preset.AST_Raidwide_AspectedHelios) && (spell == AST.AspectedHelios || spell == AST.HeliosConjuction))
                continue;

            if (AbleToCast(spell))
            {
                WouldLikeToGroundTarget = ActionSheet.TryGetValue(spell, out var s) && s.TargetArea;
                ActionManager.Instance()->UseAction(ActionType.Action, spell);
                WouldLikeToGroundTarget = false;
                BlacklistedRaidwides.Add(spell);
                AutorotRaidwides++;
                MarkRaidwideMitUsed();
                return;
            }
        }
    }

    private static bool BluAutoActionAllowed(PresetStorage.PresetData data)
    {
        if (Player.Job is not Job.BLU)
            return true;

        if (data.IsBlueTank)
            return BLU.HasTankMimicry;

        if (data.IsBlueDPS)
            return !BLU.HasTankMimicry;

        if (data.IsBlueHealer)
            return BLU.HasHealerMimicry;

        return true;
    }

    private static bool ProcessAutoActions(ref uint _, bool canHeal, bool stOnly)
    {
        // Pre-filter and cache attributes to avoid repeated lookups
        var filteredActions = _autoActions
            .Select(x => new { Preset = x.Key, Attributes = x.Key.Attributes() })
            .Where(x => x.Attributes is { AutoAction: not null, ReplaceSkill: not null })
            .Where(x => x.Attributes.AutoAction.IsHeal == canHeal)
            .Where(x => !stOnly || x.Attributes.AutoAction.IsAoE == false)
            .Where(x => BluAutoActionAllowed(x.Attributes))
            .OrderByDescending(x => x.Attributes.AutoAction.IsAoE);

        foreach (var entry in filteredActions)
        {
            var attributes = entry.Attributes;
            var action = attributes.AutoAction!;

            // Skip if locked
            if ((action.IsAoE && LockedST) || (!action.IsAoE && LockedAoE))
                continue;

            // Skip if rez invuln is up
            if (!action.IsHeal && HasStatusEffect(418))
                continue;


            // Amnesia (incl. deep dungeon floor enchantments/traps): skip oGCD abilities entirely
            if (HasAmnesia)
            {
                uint amnCheckAct = attributes.ReplaceSkill!.ActionIDs.First();
                if (amnCheckAct.ActionAttackType() is ActionAttackType.Ability)
                    continue;
            }

            // Pacification: skip weaponskills entirely
            if (HasStatusEffect(All.Debuffs.Pacification))
            {
                uint pacCheckAct = attributes.ReplaceSkill!.ActionIDs.First();
                if (pacCheckAct.ActionAttackType() is ActionAttackType.Weaponskill)
                    continue;
            }

            // Silence: try echo drops first, then skip spells
            if (HasStatusEffect(All.Debuffs.Silence))
            {
                uint silCheckAct = attributes.ReplaceSkill!.ActionIDs.First();
                if (silCheckAct.ActionAttackType() is ActionAttackType.Spell)
                {
                    // Try to use Echo Drops (item ID 4566, +1000000 for HQ offset)
                    if (AnimationLock == 0 && ActionManager.Instance()->GetActionStatus(ActionType.Item, 4566 + 1000000) == 0)
                    {
                        ActionManager.Instance()->UseAction(ActionType.Item, 4566 + 1000000);
                        return false;
                    }
                    continue; // Skip this spell, fall through to weaponskills
                }
            }

            uint gameAct = attributes.ReplaceSkill!.ActionIDs.First();

            // FORK: BLU's filler is user-selectable (see BLU_Fillers.cs), but the
            // [ReplaceSkill] attribute is frozen at startup and still names the stock
            // spell. Gating on it would kill auto-rotation for anyone who does not
            // have Sonic Boom / Electrogenesis / Goblin Punch / Right Round slotted.
            if (BLU.AutoActionOverride(entry.Preset) is { } bluAct)
                gameAct = bluAct;

            var status = ActionManager.Instance()->GetActionStatus(ActionType.Action, gameAct, checkCastingActive: false, checkRecastActive: false);

            if (!ActionLearned(gameAct) || status == 581)
                continue;

            if (action.IsHeal)
            {
                AutomateHealing(entry.Preset, attributes, gameAct);
                continue;
            }

            // Tank logic
            if (Player.Object?.GetRole() is CombatRole.Tank ||
                (Player.Job is Job.BLU && BLU.HasTankMimicry))
            {
                AutomateTanking(entry.Preset, attributes, gameAct);
                continue;
            }

            // DPS logic
            if (!action.IsHeal && AutomateDPS(entry.Preset, attributes, gameAct))
                return false;
        }

        return false;
    }

    private static void PreEmptiveHot()
    {
        if (PartyInCombat() || SimpleTarget.FocusTarget is null || (InDuty() && !Svc.DutyState.IsDutyStarted))
            return;

        ushort regenBuff = Player.Job switch
        {
            Job.AST => AST.Buffs.AspectedBenefic,
            Job.CNJ or Job.WHM => WHM.Buffs.Regen,
            _ => 0
        };

        uint regenSpell = Player.Job switch
        {
            Job.AST => AST.AspectedBenefic,
            Job.CNJ or Job.WHM => WHM.Regen,
            _ => 0
        };

        if (regenSpell != 0 && !JustUsed(regenSpell, 4) && SimpleTarget.FocusTarget != null && (!HasStatusEffect(regenBuff, out var regen, SimpleTarget.FocusTarget) || regen?.RemainingTime <= 5f))
        {
            var query = Svc.Objects.GetBattleCharas().Where(x => !x.IsDead && x.IsTargetable && x.IsHostile());
            if (!query.Any())
                return;

            if (query.Min(x => GetTargetDistance(x, SimpleTarget.FocusTarget)) <= QueryRange)
            {
                var spell = ActionManager.Instance()->GetAdjustedActionId(regenSpell).Retarget(SimpleTarget.FocusTarget);

                if (SimpleTarget.FocusTarget.IsDead)
                    return;

                if (!ActionReady(spell))
                    return;

                if (Player.Object is not null && ActionManager.CanUseActionOnTarget(spell, SimpleTarget.FocusTarget.GameObject()) && !OutOfRange(spell, Player.Object, SimpleTarget.FocusTarget) && ActionManager.Instance()->GetActionStatus(ActionType.Action, spell) == 0)
                {
                    ActionManager.Instance()->UseAction(ActionType.Action, regenSpell, SimpleTarget.FocusTarget.GameObjectId);
                    return;
                }
            }
        }
    }

    private static void PreEmptiveShield()
    {
        if (PartyInCombat() || SimpleTarget.FocusTarget is null || (InDuty() && !Svc.DutyState.IsDutyStarted))
            return;

        ushort shieldBuff = Player.Job switch
        {
            Job.SGE => SGE.Buffs.EukrasianDiagnosis,
            Job.SCH => SCH.Buffs.Galvanize,
            _ => 0
        };

        uint shieldSpell = Player.Job switch
        {
            Job.SGE => SGE.EukrasianDiagnosis,
            Job.SCH => SCH.Adloquium,
            _ => 0
        };

        uint prepSpell = Player.Job switch
        {
            Job.SGE => SGE.Eukrasia,
            _ => 0
        };

        if (shieldSpell != 0 && !JustUsed(shieldSpell, 4) && SimpleTarget.FocusTarget != null && (!HasStatusEffect(shieldBuff, out var shield, SimpleTarget.FocusTarget) || shield?.RemainingTime <= 1f))
        {
            if (prepSpell != 0 && !JustUsed(prepSpell, 4) && !HasStatusEffect(SGE.Buffs.Eukrasia))
            {
                var spell = ActionManager.Instance()->GetAdjustedActionId(prepSpell).Retarget(SimpleTarget.FocusTarget);

                if (!ActionReady(prepSpell))
                    return;

                if (ActionManager.Instance()->GetActionStatus(ActionType.Action, spell) == 0)
                {
                    ActionManager.Instance()->UseAction(ActionType.Action, prepSpell);
                    return;
                }
            }

            var query = Svc.Objects.GetBattleCharas().Where(x => !x.IsDead && x.IsTargetable && x.IsHostile());
            if (!query.Any())
                return;

            if (query.Min(x => GetTargetDistance(x, SimpleTarget.FocusTarget)) <= QueryRange)
            {
                var spell = ActionManager.Instance()->GetAdjustedActionId(shieldSpell).Retarget(SimpleTarget.FocusTarget);

                if (SimpleTarget.FocusTarget.IsDead)
                    return;

                if (!ActionReady(spell) ||
                    ActionManager.GetAdjustedCastTime(ActionType.Action, spell) > 0 && TimeStoodStill < TimeSpan.FromSeconds(1))
                    return;

                if (Player.Object is not null && ActionManager.CanUseActionOnTarget(spell, SimpleTarget.FocusTarget.GameObject()) && !OutOfRange(spell, Player.Object, SimpleTarget.FocusTarget) && ActionManager.Instance()->GetActionStatus(ActionType.Action, spell) == 0)
                {
                    ActionManager.Instance()->UseAction(ActionType.Action, spell, SimpleTarget.FocusTarget.GameObjectId);
                    return;
                }
            }
        }
    }

    // Note: Similar to Kardia, because this has its own set of rules but regarding timings I'm not sure if I want to wire this up to retargeting
    private static bool RezParty()
    {
        if (HasStatusEffect(418)) return false;
        uint resSpell = 0;

        // Phantom White Mage's Occult Raise leads: it is instant with a 5s recast, so it beats
        // Chemist Revive and every job raise, none of which are instant. It was previously
        // reachable ONLY through the combo path, which requires the dead player to be the
        // current target - impossible under DPS autorotation, where the target is an enemy. On
        // any job without its own raise the switch below fell through to 0 and RezParty simply
        // returned, so the phantom rez never fired.
        if (OccultCrescent.IsEnabledAndUsable(Preset.Phantom_WhiteMage_OccultRaise, OccultCrescent.OccultRaise))
        {
            resSpell = OccultCrescent.OccultRaise;
        }
        else if (OccultCrescent.IsEnabledAndUsable(Preset.Phantom_Chemist_Revive, OccultCrescent.Revive))
        {
            resSpell = OccultCrescent.Revive;
        }
        else if (Variant.CanRaise())
        {
            resSpell = Variant.Raise;
        }
        else
        {
            resSpell = Player.Job switch
            {
                Job.CNJ or Job.WHM => WHM.Raise,
                Job.SCH or Job.SMN => SCH.Resurrection,
                Job.AST => AST.Ascend,
                Job.SGE => SGE.Egeiro,
                Job.RDM => RDM.Verraise,
                Job.BLU => IsSpellActive(BLU.AngelWhisper) ? BLU.AngelWhisper : 0,
                _ => 0,
            };
        }

        if (resSpell == 0)
            return false;

        IEnumerable<WrathPartyMember> deadPeople = DeadPeople.Where(x => x.BattleChara.CanUseOn(resSpell));

        if (cfg.HealerSettings.AutoRezDPSJobsHealersOnly && (resSpell is RDM.Verraise || (Player.Job is Job.SMN && resSpell is SCH.Resurrection)))
        {
            deadPeople = deadPeople.Where(x => x.GetRole() is CombatRole.Healer || x.RealJob?.GetJob() is Job.SMN or Job.RDM);
        }

        if (ActionManager.Instance()->QueuedActionId == resSpell)
            ActionManager.Instance()->QueuedActionId = 0;

        if (Player.Object.CurrentMp >= GetResourceCost(resSpell) && ActionReady(resSpell))
        {
            var timeSinceLastRez = TimeSinceLastSuccessfulCast(resSpell);
            if ((timeSinceLastRez != -1f && timeSinceLastRez < 4f) || Player.Object.IsCasting())
                return false;

            if (deadPeople.Where(CheckRezTarget).FindFirst(x => x is not null, out var member))
            {
                if (resSpell == OccultCrescent.Revive || resSpell == OccultCrescent.OccultRaise)
                {
                    ActionManager.Instance()->UseAction(ActionType.Action, resSpell, member.BattleChara.GameObjectId);
                    return true;
                }

                if (resSpell is Variant.Raise)
                {
                    //Try to Swiftcast if Magic DPS
                    if (GetRoleFromJob(Player.Job) is JobRole.MagicalDPS)
                    {
                        if (ActionReady(RoleActions.Magic.Swiftcast) && !HasStatusEffect(RDM.Buffs.Dualcast) &&
                            !HasOrExpectsOccultInstantCast)
                        {
                            if (ActionManager.Instance()->GetActionStatus(ActionType.Action, RoleActions.Magic.Swiftcast) == 0)
                            {
                                ActionManager.Instance()->UseAction(ActionType.Action, RoleActions.Magic.Swiftcast);
                                return true;
                            }
                        }
                    }

                    if (HasStatusEffect(RoleActions.Magic.Buffs.Swiftcast) || HasStatusEffect(RDM.Buffs.Dualcast) ||
                        HasOrExpectsOccultInstantCast || !IsMoving())
                    {
                        ActionManager.Instance()->UseAction(ActionType.Action, resSpell, member.BattleChara.GameObjectId);
                        return true;
                    }
                }

                if (Player.Job is Job.RDM)
                {
                    if (ActionReady(RoleActions.Magic.Swiftcast) && !HasStatusEffect(RDM.Buffs.Dualcast) &&
                        !HasOrExpectsOccultInstantCast)
                    {
                        ActionManager.Instance()->UseAction(ActionType.Action, RoleActions.Magic.Swiftcast);
                        return true;
                    }

                    if (ActionManager.GetAdjustedCastTime(ActionType.Action, resSpell) == 0)
                    {
                        ActionManager.Instance()->UseAction(ActionType.Action, resSpell, member.BattleChara.GameObjectId);
                        return true;
                    }

                }
                else
                {
                    if (ActionReady(RoleActions.Magic.Swiftcast) &&
                        !HasOrExpectsOccultInstantCast)
                    {
                        if (ActionManager.Instance()->GetActionStatus(ActionType.Action, RoleActions.Magic.Swiftcast) == 0)
                        {
                            ActionManager.Instance()->UseAction(ActionType.Action, RoleActions.Magic.Swiftcast);
                            return true;
                        }
                    }

                    if (!IsMoving() || HasStatusEffect(RoleActions.Magic.Buffs.Swiftcast) ||
                        HasOrExpectsOccultInstantCast)
                    {

                        if ((cfg is not null) && ((cfg.HealerSettings.AutoRezRequireSwift && ActionManager.GetAdjustedCastTime(ActionType.Action, resSpell) == 0) || !cfg.HealerSettings.AutoRezRequireSwift))
                        {
                            ActionManager.Instance()->UseAction(ActionType.Action, resSpell, member.BattleChara.GameObjectId);
                            return true;
                        }
                    }
                }
            }
        }

        // Nothing fired this tick. Returning false leaves Run() to carry on exactly as it did
        // before this change, so RezParty can never stall the rotation.
        return false;
    }

    private static void CleanseParty()
    {
        if (Player.Object is not { } || !EzThrottler.Throttle("CleanseThrottle", 50)) return;

        uint cleanseSpell = Player.Job is Job.BLU ? BLU.Exuviation : RoleActions.Healer.Esuna;
        if (Player.Job is Job.BLU && !IsSpellActive(BLU.Exuviation))
            return;

        if (SimpleTarget.Stack.AllyToEsuna is IBattleChara memberBC)
        {
            var res = ActionManager.GetActionInRangeOrLoS(cleanseSpell, Player.GameObject, memberBC.GameObject());
            if (res is 0 or 565)
            {
                Svc.Log.Debug($"Cleansing {memberBC.Name}");
                ActionManager.Instance()->UseAction(ActionType.Action, cleanseSpell.Retarget(memberBC), memberBC.GameObjectId);
            }
        }
    }

    // Note: Not entirely sure what to do when the Kardia standalone retargeting is on since it doesn't follow this ruleset so this will be untouched for now but
    // it is known if it acts funny with the standalone retarget then that's what causes it.
    private static void UpdateKardiaTarget()
    {
        if (!ActionLearned(SGE.Kardia)) return;
        if (CombatEngageDuration().TotalSeconds < 3) return;

        foreach (var member in GetPartyMembers().Where(x => !x.BattleChara.IsDead).OrderByDescending(x => x.BattleChara?.GetRole() is CombatRole.Tank))
        {
            if (cfg.HealerSettings.KardiaTanksOnly && member.BattleChara?.GetRole() is not CombatRole.Tank &&
                !HasStatusEffect(3615, member.BattleChara, true)) continue; // Duty Support Gosetsu Tank Stance

            var enemiesTargeting = Svc.Objects.GetBattleCharas().Count(x => x.IsTargetable && x.IsHostile() && x.TargetObjectId == member.BattleChara.GameObjectId);
            if (enemiesTargeting > 0 && !HasStatusEffect(SGE.Buffs.Kardion, member.BattleChara))
            {
                ActionManager.Instance()->UseAction(ActionType.Action, SGE.Kardia.Retarget(member.BattleChara), member.BattleChara.GameObjectId);
                return;
            }
        }

    }

    // SGE tank-shield upkeep (Joey 2026-06-27). When MORE THAN 2 enemies are on a tank, keep
    // Eukrasian Diagnosis up on that tank; and when Addersting is capped, spend it with Toxikon
    // so the gauge the breaking shields generate isn't wasted. Gated by the SGE_TankShield preset.
    // The pending flag drives the Eukrasia -> Eukrasian Diagnosis two-step WITHOUT hijacking an
    // Eukrasia the rotation pressed for Eukrasian Dosis.
    private static bool _sgeShieldEukrasiaPending;
    private static void UpdateSgeTankShield()
    {
        if (!IsEnabled(Preset.SGE_TankShield) || !ActionLearned(SGE.Eukrasia))
        {
            _sgeShieldEukrasiaPending = false;
            return;
        }
        if (CombatEngageDuration().TotalSeconds < 3) return;

        // Finish a tank-shield Eukrasia WE started: cast Eukrasian Diagnosis on the tank.
        if (_sgeShieldEukrasiaPending)
        {
            if (HasStatusEffect(SGE.Buffs.Eukrasia))
            {
                var t = MostTargetedUnshieldedTank();
                if (t is not null)
                {
                    ActionManager.Instance()->UseAction(ActionType.Action, SGE.EukrasianDiagnosis.Retarget(t), t.GameObjectId);
                    _sgeShieldEukrasiaPending = false;
                    return;
                }
            }
            else
            {
                _sgeShieldEukrasiaPending = false; // Eukrasia gone (consumed elsewhere) - stand down.
            }
        }

        // Don't touch an Eukrasia the rotation pressed for something else (e.g. Eukrasian Dosis).
        if (HasStatusEffect(SGE.Buffs.Eukrasia)) return;

        // 1) Spend capped Addersting (max 3) via Toxikon.
        if (GetJobGauge<SGEGauge>().Addersting >= 3 &&
            ActionReady(OriginalHook(SGE.Toxikon)) && HasBattleTarget() && InActionRange(OriginalHook(SGE.Toxikon)))
        {
            ActionManager.Instance()->UseAction(ActionType.Action, OriginalHook(SGE.Toxikon));
            return;
        }

        // 2) Start the Eukrasia -> Diagnosis sequence for a heavily-targeted, unshielded tank.
        if (MostTargetedUnshieldedTank() is not null && ActionReady(SGE.Eukrasia))
        {
            ActionManager.Instance()->UseAction(ActionType.Action, SGE.Eukrasia);
            _sgeShieldEukrasiaPending = true;
        }
    }

    // The first party tank that more than 2 enemies are targeting and that lacks the Eukrasian
    // Diagnosis shield, or null if none qualifies.
    private static IGameObject? MostTargetedUnshieldedTank()
    {
        foreach (var member in GetPartyMembers())
        {
            var bc = member.BattleChara;
            if (bc is null || bc.IsDead || bc.GetRole() is not CombatRole.Tank) continue;
            int enemies = Svc.Objects.Count(x => x.IsTargetable && x.IsHostile() && x.TargetObjectId == bc.GameObjectId);
            if (enemies <= 2) continue;
            if (HasStatusEffect(SGE.Buffs.EukrasianDiagnosis, bc, true)) continue;
            return bc;
        }
        return null;
    }

    private static bool AutomateDPS(Preset preset, PresetStorage.PresetData attributes, uint gameAct)
    {
        var mode = cfg.DPSRotationMode;
        if (attributes.AutoAction!.IsAoE)
        {
            return AutoRotationHelper.ExecuteAoE(mode, preset, attributes, gameAct);
        }
        else
        {
            // Auto positional movement for melee DPS
            if (cfg.DPSSettings.AutoPositionals &&
                Jobs.GetRoleFromJob(Player.Job) is Jobs.JobRole.MeleeDPS)
            {
                var target = AutoRotationHelper.GetSingleTarget(mode);
                PositionalMover.MoveToPositional(target);
            }

            return AutoRotationHelper.ExecuteST(mode, preset, attributes, gameAct);
        }
    }

    private static bool AutomateTanking(Preset preset, PresetStorage.PresetData attributes, uint gameAct)
    {
        var mode = cfg.DPSRotationMode;
        if (attributes.AutoAction!.IsAoE)
        {
            return AutoRotationHelper.ExecuteAoE(mode, preset, attributes, gameAct);
        }
        else
        {
            return AutoRotationHelper.ExecuteST(mode, preset, attributes, gameAct);
        }
    }

    private static bool AutomateHealing(Preset preset, PresetStorage.PresetData attributes, uint gameAct)
    {
        var mode = cfg.HealerRotationMode;
        if (Player.Object?.IsCasting() is true) return false;
        if (Environment.TickCount64 < HealThrottle) return false;

        if (attributes.AutoAction!.IsAoE)
        {
            var ret = AutoRotationHelper.ExecuteAoE(mode, preset, attributes, gameAct);
            return ret;
        }
        else
        {
            var ret = AutoRotationHelper.ExecuteST(mode, preset, attributes, gameAct);
            return ret;
        }
    }

    public static class AutoRotationHelper
    {
        public static IBattleChara? GetSingleTarget(Enum rotationMode)
        {
            if (rotationMode is DPSRotationMode dpsmode)
            {
                if (Player.Object?.Role is CombatRole.Tank ||
                    (Player.Job is Job.BLU && BLU.HasTankMimicry))
                {
                    IBattleChara? target = dpsmode switch
                    {
                        DPSRotationMode.Manual => SimpleTarget.HardTarget,
                        DPSRotationMode.Highest_Max => TankTargeting.GetHighestMaxTarget(),
                        DPSRotationMode.Lowest_Max => TankTargeting.GetLowestMaxTarget(),
                        DPSRotationMode.Highest_Current => TankTargeting.GetHighestCurrentTarget(),
                        DPSRotationMode.Lowest_Current => TankTargeting.GetLowestCurrentTarget(),
                        DPSRotationMode.Tank_Target => SimpleTarget.HardTarget,
                        DPSRotationMode.Nearest => DPSTargeting.GetNearestTarget(),
                        DPSRotationMode.Furthest => DPSTargeting.GetFurthestTarget(),
                        _ => SimpleTarget.HardTarget,
                    };
                    return target;
                }
                else
                {
                    IBattleChara? target = dpsmode switch
                    {
                        DPSRotationMode.Manual => SimpleTarget.HardTarget,
                        DPSRotationMode.Highest_Max => DPSTargeting.GetHighestMaxTarget(),
                        DPSRotationMode.Lowest_Max => DPSTargeting.GetLowestMaxTarget(),
                        DPSRotationMode.Highest_Current => DPSTargeting.GetHighestCurrentTarget(),
                        DPSRotationMode.Lowest_Current => DPSTargeting.GetLowestCurrentTarget(),
                        DPSRotationMode.Tank_Target => DPSTargeting.GetTankTarget(),
                        DPSRotationMode.Nearest => DPSTargeting.GetNearestTarget(),
                        DPSRotationMode.Furthest => DPSTargeting.GetFurthestTarget(),
                        _ => SimpleTarget.HardTarget,
                    };
                    return target;
                }
            }
            if (rotationMode is HealerRotationMode healermode)
            {
                if (Player.Object?.Role != CombatRole.Healer &&
                    !(Player.Job is Job.BLU && BLU.HasHealerMimicry)) return null;
                IBattleChara? target = healermode switch
                {
                    HealerRotationMode.Manual => HealerTargeting.ManualTarget(),
                    HealerRotationMode.Highest_Current => HealerTargeting.GetHighestCurrent(),
                    HealerRotationMode.Lowest_Current => HealerTargeting.GetLowestCurrent(),
                    _ => HealerTargeting.ManualTarget(),
                };
                AutorotHealTarget = target;
                return target;
            }

            return null;
        }

        public static bool ExecuteAoE(Enum mode, Preset preset, PresetStorage.PresetData attributes, uint gameAct)
        {
            if (LocalPlayer is not { } player)
                return false;

            if (ActionManager.Instance()->QueuedActionId != 0)
                return true;

            var autoTarget = DPSTargeting.BaseSelection.MaxBy(x => NumberOfEnemiesInRange(OriginalHook(gameAct), x, true));
            var manualTarget = SimpleTarget.HardTarget;

            IBattleChara? target = null;
            // Determine target according to rotation mode and AoE settings

            var useAutoTarget = cfg.DPSRotationMode != DPSRotationMode.Manual || (cfg.DPSRotationMode == DPSRotationMode.Manual && cfg.DPSSettings.AoEIgnoreManual && (!cfg.DPSSettings.AoEOnlyWhenTargeting || manualTarget is not null));
            target = useAutoTarget ? autoTarget : manualTarget;

            if ((target is not IBattleChara t || (!t.IsHostile() && !t.IsFriendly())) && cfg.PauseWhenNoTarget) return true;

            if (attributes.AutoAction!.IsHeal)
            {
                LockedAoE = false;
                LockedST = false;

                uint outAct = OriginalHook(InvokeCombo(preset, attributes, ref gameAct, Player.Object));
                if (!ActionReady(outAct))
                    return false;

                var canQueue = outAct.ActionAttackType() is { } type && (type is ActionAttackType.Ability || type is not ActionAttackType.Ability && RemainingGCD <= cfg.QueueWindow);
                if (!canQueue)
                    return false;

                if (HealerTargeting.CanAoEHeal(outAct))
                {
                    var castTime = ActionManager.GetAdjustedCastTime(ActionType.Action, outAct);
                    bool orbwalking = cfg.OrbwalkerIntegration && OrbwalkerIPC.CanOrbwalk;
                    if (TimeMoving.TotalMilliseconds > 0 && castTime > 0 && !orbwalking)
                        return false;

                    var targetId = player.GameObjectId;
                    var changed = CheckForChangedTarget(gameAct, ref targetId, out var replacedWith);
                    WouldLikeToGroundTarget = ActionSheet.TryGetValue(outAct, out var s) && s.TargetArea;
                    var ret = ActionManager.Instance()->UseAction(ActionType.Action, Service.Configuration.ActionChanging ? gameAct : outAct, targetId);
                    WouldLikeToGroundTarget = false;

                    return true;
                }
            }
            else
            {
                if (!LockedAoE)
                {
                    var st = GetSingleTarget(mode);
                    var maxHit = NumberOfEnemiesInRange(DontChangeForAoe(gameAct) ? gameAct : OriginalHook(gameAct), target, true);
                    var singleTargetModeTarget = NumberOfEnemiesInRange(OriginalHook(gameAct), st, true);

                    if (singleTargetModeTarget >= maxHit)
                        target = st;

                    if (cfg.DPSSettings.DPSAoETargets == null || maxHit < cfg.DPSSettings.DPSAoETargets)
                        return false;

                }
                ulong targetId = target?.GameObjectId ?? 0;
                var changed = CheckForChangedTarget(gameAct, ref targetId, out var replacedWith) && targetId != target?.GameObjectId;
                if (changed) target = targetId.GetBattleChara();

                OverrideTarget = target ?? OverrideTarget;
                uint outAct = OriginalHook(InvokeCombo(preset, attributes, ref gameAct, OverrideTarget));
                if (outAct is All.Cease) return true;
                if (!ActionReady(outAct))
                    return false;

                var canQueue = outAct.ActionAttackType() is { } type && ((type is ActionAttackType.Ability && AnimationLock <= cfg.QueueWindow) || (type is not ActionAttackType.Ability && RemainingGCD <= cfg.QueueWindow));
                if (!canQueue)
                    return false;

                var s = ActionSheet.TryGetValue(outAct, out var sheet);
                var targetsHostile = s && sheet.CanTargetHostile;
                var canUseSelf = s && sheet.CanTargetSelf;
                var areaTargeted = s && sheet.TargetArea;

                // Same defect as ExecuteST, and worse here - this path had no IsHeal check at
                // all before forcing the hard target onto the enemy. A friendly-only action can
                // resolve out of a DAMAGE preset, because phantom cures come out of DPS combos.
                // Derive it from the resolved action: usable on us, not usable on hostiles, not
                // ground-targeted.
                var resolvedFriendlyOnly = canUseSelf && !targetsHostile && !areaTargeted;

                bool switched = SwitchOnDChole(attributes, outAct, ref target);
                var castTime = ActionManager.GetAdjustedCastTime(ActionType.Action, outAct);
                bool orbwalking = cfg.OrbwalkerIntegration && OrbwalkerIPC.CanOrbwalk;

                if (TimeMoving.TotalMilliseconds > 0 && castTime > 0 && !orbwalking)
                    return false;

                if (cfg.DPSSettings.DPSAlwaysHardTarget && OverrideTarget is not null && !resolvedFriendlyOnly)
                    Svc.Targets.Target = OverrideTarget;
                var acRangeCheck = ActionManager.GetActionInRangeOrLoS(outAct, player.GameObject(), OverrideTarget is null ? player.GameObject() : OverrideTarget.Struct());
                var inRange = acRangeCheck is 0 or 565 || canUseSelf || areaTargeted;

                if (targetsHostile && OverrideTarget is not null)
                {
                    Svc.GameConfig.TryGet(Dalamud.Game.Config.UiControlOption.AutoFaceTargetOnAction, out uint original);
                    Svc.GameConfig.Set(Dalamud.Game.Config.UiControlOption.AutoFaceTargetOnAction, 1);
                    Vector3 pos = new(Player.Object.Position.X, Player.Object.Position.Y, Player.Object.Position.Z);
                    ActionManager.Instance()->AutoFaceTargetPosition(&pos, OverrideTarget.GameObjectId);
                    Svc.GameConfig.Set(Dalamud.Game.Config.UiControlOption.AutoFaceTargetOnAction, original);
                }

                if (inRange)
                {
                    WouldLikeToGroundTarget = areaTargeted;
                    var actToSend = Service.Configuration.ActionChanging && !resolvedFriendlyOnly
                        ? gameAct
                        : outAct;
                    var ret = ActionManager.Instance()->UseAction(ActionType.Action, actToSend, targetId);
                    WouldLikeToGroundTarget = false;
                    if (NIN.MudraSigns.Contains(outAct))
                        _lockedAoE = true;
                    else
                        _lockedAoE = false;

                    return true;
                }

            }
            return false;
        }

        private static bool DontChangeForAoe(uint gameAct)
        {
            return gameAct is DNC.Windmill or DNC.Bladeshower or DNC.RisingWindmill or DNC.Bloodshower;
        }

        public static bool ExecuteST(Enum mode, Preset preset, PresetStorage.PresetData attributes, uint gameAct)
        {
            if (LocalPlayer is not { } player)
                return false;

            if (ActionManager.Instance()->QueuedActionId != 0)
                return true;

            var target = GetSingleTarget(mode);

            if ((target is not { } t || (!t.IsHostile() && !t.IsFriendly())) && cfg.PauseWhenNoTarget) return true;

            ulong targetId = target?.GameObjectId ?? 0;
            var changed = CheckForChangedTarget(gameAct, ref targetId, out var replacedWith) && targetId != target?.GameObjectId;
            if (changed) target = targetId.GetBattleChara();

            OverrideTarget = target ?? OverrideTarget;
            var outAct = OriginalHook(InvokeCombo(preset, attributes, ref gameAct, target));
            if (outAct >= All.Items)
            {
                Svc.Log.Debug($"Using item {outAct.ActionName()}");
                ActionManager.Instance()->UseAction(ActionType.Action, outAct, extraParam: 0xFFFF);
                return true;
            }

            if (!ActionReady(outAct))
            {
                return false;
            }

            bool switched = SwitchOnDChole(attributes, outAct, ref target);
            if (outAct is DNC.ClosedPosition && DNC.DancePartnerResolver() is IBattleChara dp)
                target = dp;

            var canUseSelf = NIN.MudraSigns.Contains(outAct)
                ? target is not null && target.IsHostile()
                : ActionManager.CanUseActionOnTarget(outAct, Player.GameObject);

            var blockedSelfBuffs = GetCooldown(outAct).CooldownTotal >= 5;

            if (cfg.InCombatOnly && NotInCombat && !CombatBypass && !(canUseSelf && cfg.BypassBuffs && !blockedSelfBuffs))
                return false;

            if (target is null && !canUseSelf)
                return false;

            var areaTargeted = ActionSheet.TryGetValue(outAct, out var s) && s.TargetArea;
            var canUseTarget = target is not null && ActionManager.CanUseActionOnTarget(outAct, target.GameObject());

            var acRangeCheck = ActionManager.GetActionInRangeOrLoS(outAct, player.GameObject(), target is null ? player.GameObject() : target.GameObject());
            var inRange = acRangeCheck is 0 or 565 || canUseSelf;

            var canUse = (canUseSelf || canUseTarget || areaTargeted) && outAct.ActionAttackType() is { } type && ((type is ActionAttackType.Ability && AnimationLock <= cfg.QueueWindow) || (type is not ActionAttackType.Ability && RemainingGCD <= cfg.QueueWindow));
            // A friendly-only action can resolve out of a DAMAGE preset. IsHeal is a property of
            // the PRESET, not of whatever InvokeCombo actually returned, so a phantom cure fired
            // from a DPS combo arrived here flagged as damage: the DPS branch below then slammed
            // the hard target onto the enemy immediately before the cast, and the cure died on an
            // invalid target. Jobs carrying their own healing presets (RDM, SMN, the healers) have
            // a heal-flagged path that targets friendly correctly, which is exactly why phantom
            // healing worked there and never worked on Black Mage or any other pure-DPS job.
            //
            // Derive it from the resolved action instead: if the action can be used on us but NOT
            // on the current (hostile) target, and it is not ground-targeted, it is a friendly
            // action regardless of which preset emitted it. canUseSelf / canUseTarget /
            // areaTargeted are all computed above, so this costs nothing extra.
            var resolvedFriendlyOnly = canUseSelf && !canUseTarget && !areaTargeted;
            var isHeal = attributes.AutoAction!.IsHeal || resolvedFriendlyOnly;

            var castTime = ActionManager.GetAdjustedCastTime(ActionType.Action, outAct);
            bool orbwalking = cfg.OrbwalkerIntegration && OrbwalkerIPC.CanOrbwalk;

            // Diagnostic: friendly-only actions (phantom cures) that reach here but never fire.
            // Prints every gate between this point and UseAction so the blocking one is named
            // rather than guessed. Throttled; only for friendly resolutions.
            if (resolvedFriendlyOnly && (DateTime.Now - _friendlyDiagLast).TotalSeconds >= 5)
            {
                _friendlyDiagLast = DateTime.Now;
                Svc.Log.Debug($"[FriendlyDiag] act={outAct.ActionName()} ({outAct}) inCombat={!NotInCombat} " +
                    $"castTime={castTime} timeMoving={TimeMoving.TotalMilliseconds}ms leeway={Service.Configuration.MovementLeeway} orbwalk={orbwalking} " +
                    $"=> movementBail={(TimeMoving.TotalMilliseconds > 0 && castTime > 0 && !orbwalking)} | " +
                    $"canUse={canUse} inRange={inRange} rangeCheck={acRangeCheck} canUseSelf={canUseSelf} canUseTarget={canUseTarget} " +
                    $"areaTargeted={areaTargeted} attackType={outAct.ActionAttackType()} animLock={AnimationLock} remainingGCD={RemainingGCD} queueWindow={cfg.QueueWindow} " +
                    $"queuedActionId={ActionManager.Instance()->QueuedActionId}");
            }

            if (TimeMoving.TotalMilliseconds > 0 && castTime > 0 && !orbwalking)
                return false;

            if (canUse && (inRange || areaTargeted))
            {
                if (target is not null)
                {
                    if ((!isHeal && cfg.DPSSettings.DPSAlwaysHardTarget && mode is not DPSRotationMode.Manual) || (isHeal && cfg.HealerSettings.HealerAlwaysHardTarget && mode is not HealerRotationMode.Manual))
                        Svc.Targets.Target = target;
                }

                WouldLikeToGroundTarget = areaTargeted;
                if (changed)
                    Svc.Log.Debug($"Updated target to {target.Name} for {replacedWith.ActionName()}");
                // Friendly-only resolutions bypass ActionChanging: handing the hook `gameAct`
                // makes it re-derive the action from the pressed damage button, which cannot be
                // relied on to reproduce a content-specific replacement like a phantom cure.
                var actToSend = Service.Configuration.ActionChanging && !resolvedFriendlyOnly
                    ? gameAct
                    : outAct;
                var ret = ActionManager.Instance()->UseAction(ActionType.Action, actToSend, targetId);
                WouldLikeToGroundTarget = false;

                if (NIN.MudraSigns.Contains(outAct))
                    _lockedST = true;
                else
                    _lockedST = false;

                return true;
            }

            return false;
        }

        private static bool SwitchOnDChole(PresetStorage.PresetData attributes, uint outAct, ref IBattleChara? newtarget)
        {
            if (outAct is SGE.Druochole && !attributes.AutoAction!.IsHeal)
            {
                if (GetPartyMembers()
                    .Where(x => !x.BattleChara.IsDead &&
                                x.BattleChara.IsTargetable &&
                                GetTargetDistance(x.BattleChara) <= QueryRange &&
                                IsInLineOfSight(x.BattleChara))
                    .OrderBy(x => GetTargetHPPercent(x.BattleChara))
                    .Select(x => x.BattleChara)
                    .TryGetFirst(out newtarget))
                {
                    return true;
                }
            }

            return false;
        }

        public static uint InvokeCombo(Preset preset, PresetStorage.PresetData attributes, ref uint originalAct, IGameObject? optionalTarget = null)
        {
            if (attributes.ReplaceSkill is null) return originalAct;
            var outAct = attributes.ReplaceSkill.ActionIDs.FirstOrDefault();
            var customReplaceType = CustomActionHelper.GetTypeByAttribute(attributes.AutoAction!);
            var customReplaced = CustomActionHelper.CustomActionEnabled(customReplaceType);
            var customCombo = Service.ActionReplacer.CustomCombos.FirstOrDefault(x => x.Preset == preset);
            foreach (var act in attributes.ReplaceSkill.ActionIDs)
            {
                var actToCheck = customReplaced ? CustomActionHelper.GetActionId(customReplaceType) : act;

                if (customCombo != null)
                {
                    if (customCombo.TryInvoke(actToCheck, out var changedAct, optionalTarget))
                    {
                        originalAct = actToCheck;
                        outAct = changedAct;
                        Service.ActionReplacer.LastActionInvokeFor[actToCheck] = outAct;
                        break;
                    }
                }
            }

            return outAct;
        }
    }

    public class DPSTargeting
    {
        private static bool Query(IBattleChara chara) =>
            !chara.IsDead &&
            GetTargetCurrentHP(chara, true) > 0 &&
            chara.IsTargetable &&
            chara.IsHostile() &&
            IsInRange(chara, InBossEncounter() && cfg.DPSSettings.IgnoreRangeInBoss ? 50f : cfg.DPSSettings.MaxDistance) &&
            GetTargetHeightDifference(chara) <= (InBossEncounter() && cfg.DPSSettings.IgnoreRangeInBoss ? 100f : cfg.DPSSettings.MaxDistance) &&
            !chara.IsInvincible &&
            !Service.Configuration.IgnoredNPCs.ContainsKey(chara.BaseId) &&
            ((cfg.DPSSettings.OnlyAttackInCombat && chara.Struct()->InCombat) || !cfg.DPSSettings.OnlyAttackInCombat) &&
            IsInLineOfSight(chara);

        public static IEnumerable<IBattleChara> BaseSelection
        {
            get
            {
                var validTargets = Svc.Objects.GetBattleCharas()
                    .Where(Query)
                    .ToList();

                if (cfg.DPSSettings.FATEPriority || cfg.DPSSettings.QuestPriority)
                {
                    bool playerInFate = cfg.DPSSettings.FATEPriority && InFATE();

                    var priorityMatches = validTargets
                        .Where(x =>
                            (playerInFate && x.Struct()->FateId != 0) ||
                            (cfg.DPSSettings.QuestPriority && IsQuestMob(x)))
                        .ToList();

                    if (priorityMatches.Count > 0)
                        validTargets = priorityMatches;
                }

                if (cfg.DPSSettings.TreasureHuntPriority)
                    validTargets = RestrictToTreasureHuntKillOrder(validTargets);

                return validTargets;
            }
        }

        private static List<IBattleChara> RestrictToTreasureHuntKillOrder(List<IBattleChara> targets)
        {
            var minOrder = 0;
            List<IBattleChara>? atMinOrder = null;

            foreach (var target in targets)
            {
                var order = GetTreasureHuntOrder(target);
                if (order == 0)
                    continue;

                if (minOrder == 0 || order < minOrder)
                {
                    minOrder = order;
                    atMinOrder ??= new List<IBattleChara>(targets.Count);
                    atMinOrder.Clear();
                    atMinOrder.Add(target);
                }
                else if (order == minOrder)
                {
                    atMinOrder!.Add(target);
                }
            }

            return minOrder == 0 ? targets : atMinOrder!;
        }

        public static bool IsCombatPriority(IBattleChara x)
        {
            if (!cfg.DPSSettings.PreferNonCombat) return true;
            bool inCombat = cfg.DPSSettings.PreferNonCombat && !x.Struct()->InCombat;
            return inCombat;
        }

        public static IBattleChara? GetTankTarget()
        {
            var tank = GetPartyMembers().FirstOrDefault(x => x.BattleChara?.GetRole() == CombatRole.Tank ||
                HasStatusEffect(1719, x.BattleChara, true) || // BLU Mighty Guard
                HasStatusEffect(3615, x.BattleChara, true));  // Duty Support Gosetsu Tank Stance
            if (tank == null)
                return null;

            return tank.BattleChara.TargetObject as IBattleChara;
        }

        public static IBattleChara? GetNearestTarget()
        {
            return BaseSelection
                .OrderByDescending(x => IsCombatPriority(x))
                .ThenBy(x => GetTargetDistance(x))
                .FirstOrDefault();
        }

        public static IBattleChara? GetFurthestTarget()
        {
            return BaseSelection
                .OrderByDescending(x => IsCombatPriority(x))
                .ThenByDescending(x => GetTargetDistance(x))
                .FirstOrDefault();
        }

        public static IBattleChara? GetLowestCurrentTarget()
        {
            return BaseSelection
                .OrderByDescending(x => IsCombatPriority(x))
                .ThenBy(x => GetTargetCurrentHP(x))
                .FirstOrDefault();
        }

        public static IBattleChara? GetHighestCurrentTarget()
        {
            return BaseSelection
                .OrderByDescending(x => IsCombatPriority(x))
                .ThenByDescending(x => GetTargetCurrentHP(x))
                .FirstOrDefault();
        }

        public static IBattleChara? GetLowestMaxTarget()
        {

            return BaseSelection
                .OrderByDescending(x => IsCombatPriority(x))
                .ThenBy(x => GetTargetMaxHP(x))
                .ThenBy(x => GetTargetHPPercent(x))
                .ThenBy(x => GetTargetDistance(x))
                .FirstOrDefault();
        }

        public static IBattleChara? GetHighestMaxTarget()
        {
            return BaseSelection
                .OrderByDescending(x => IsCombatPriority(x))
                .ThenByDescending(x => GetTargetMaxHP(x))
                .ThenBy(x => GetTargetHPPercent(x))
                .ThenBy(x => GetTargetDistance(x))
                .FirstOrDefault();
        }
    }

    public static class HealerTargeting
    {
        internal static IBattleChara? ManualTarget()
        {
            if (SimpleTarget.HardTarget is not { } t) return null;
            bool goodToHeal = t.IsFriendly() &&
                              (NeedsDoomTopUp(t) ||
                               GetTargetHPPercent(t) <=
                               (TargetHasExcog(t) ? cfg.HealerSettings.SingleTargetExcogHPP :
                                TargetHasRegen(t) ? cfg.HealerSettings.SingleTargetRegenHPP :
                                cfg.HealerSettings.SingleTargetHPP));
            if (goodToHeal && !t.IsHostile())
            {
                return t;
            }
            return null;
        }
        internal static IBattleChara? GetHighestCurrent()
        {
            if (GetPartyMembers().Count == 0) return Player.Object;
            return HealTargets().ThenByDescending(x => GetTargetHPPercent(x)).FirstOrDefault();
        }

        internal static IBattleChara? GetLowestCurrent()
        {
            if (GetPartyMembers().Count == 0) return Player.Object;
            return HealTargets().ThenBy(x => GetTargetHPPercent(x)).FirstOrDefault();
        }

        internal static IOrderedEnumerable<IBattleChara?> HealTargets()
        {
            return GetPartyMembers()
                .Where(x => !x.BattleChara.IsDead &&
                            x.BattleChara.IsTargetable &&
                            GetTargetDistance(x.BattleChara) <= QueryRange &&
                            !TargetHasImmortality(x.BattleChara) &&
                            !StatusCache.HasStatusInCacheList(StatusCache.DoNotHealStatuses, x.BattleChara) &&
                            (NeedsDoomTopUp(x.BattleChara) ||
                             GetTargetHPPercent(x.BattleChara, cfg.HealerSettings.IncludeShields) <=
                             (TargetHasExcog(x.BattleChara) ? cfg.HealerSettings.SingleTargetExcogHPP :
                                 TargetHasRegen(x.BattleChara) ? cfg.HealerSettings.SingleTargetRegenHPP :
                                 cfg.HealerSettings.SingleTargetHPP)) &&
                            IsInLineOfSight(x.BattleChara))
                .Select(x => x.BattleChara)
                .OrderByDescending(x => NeedsDoomTopUp(x))
                .ThenBy(x => TargetHasTrueInvuln(x));
        }

        internal static bool CanAoEHeal(uint outAct = 0)
        {
            int memberCount;
            try
            {
                memberCount = GetPartyMembers()
                    .Count(x => x.BattleChara is not null &&
                                !x.BattleChara.IsDead &&
                                x.BattleChara.IsTargetable &&
                                !x.IsOutOfPartyNPC &&
                                !StatusCache.HasStatusInCacheList(StatusCache.DoNotHealStatuses, x.BattleChara) &&
                                (outAct == 0
                                    ? GetTargetDistance(x.BattleChara) <= 20f
                                    : InActionRange(outAct, x.BattleChara)) &&
                                (NeedsDoomTopUp(x.BattleChara) ||
                                 GetTargetHPPercent(x.BattleChara, cfg.HealerSettings.IncludeShields) <= cfg.HealerSettings.AoETargetHPP));
            }
            catch { memberCount = 0; }

            return memberCount >= cfg.HealerSettings.AoEHealTargetCount;
        }

        private static bool TargetHasRegen(IBattleChara target)
        {
            return JobID switch
            {
                Job.AST => target.HasStatus(AST.Buffs.AspectedBenefic),
                Job.WHM => target.HasStatus(WHM.Buffs.Regen),
                _ => false,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TargetHasExcog(IBattleChara target) =>
            target.HasStatus(SCH.Buffs.Excogitation, true);

        /// Used to skip the healing of tanks that are invuln but still receive damage
        private static bool TargetHasImmortality(IBattleChara target)
        {
            //if (target is null) return false;

            return target.Status(DRK.Buffs.LivingDead, true).RemainingTimeOrZero() >= 3 ||
                   target.Status(DRK.Buffs.WalkingDead, true).RemainingTimeOrZero() >= 5 ||
                   target.Status(WAR.Buffs.Holmgang, true).RemainingTimeOrZero() >= 5;
        }
        /// Used to de-prioritize (not skip) the healing of invuln tanks
        private static bool TargetHasTrueInvuln(IBattleChara target)
        {
            //if (target is null) return false;

            return target.Status(GNB.Buffs.Superbolide).RemainingTimeOrZero() >= 5 ||
                   target.Status(PLD.Buffs.HallowedGround).RemainingTimeOrZero() >= 5;
        }
    }

    public static class TankTargeting
    {
        public static IBattleChara? GetLowestCurrentTarget()
        {
            return DPSTargeting.BaseSelection
                .OrderByDescending(x => DPSTargeting.IsCombatPriority(x))
                .ThenByDescending(x => x.TargetObject?.GameObjectId != Player.Object?.GameObjectId)
                .ThenBy(x => GetTargetCurrentHP(x))
                .ThenBy(x => GetTargetHPPercent(x)).FirstOrDefault();
        }

        public static IBattleChara? GetHighestCurrentTarget()
        {
            return DPSTargeting.BaseSelection
                .OrderByDescending(x => DPSTargeting.IsCombatPriority(x))
                .ThenByDescending(x => x.TargetObject?.GameObjectId != Player.Object?.GameObjectId)
                .ThenByDescending(x => GetTargetCurrentHP(x))
                .ThenBy(x => GetTargetHPPercent(x)).FirstOrDefault();
        }

        public static IBattleChara? GetLowestMaxTarget()
        {
            var t = DPSTargeting.BaseSelection
                .OrderByDescending(x => DPSTargeting.IsCombatPriority(x))
                .ThenByDescending(x => x.TargetObject?.GameObjectId != Player.Object?.GameObjectId)
                .ThenBy(x => GetTargetMaxHP(x))
                .ThenBy(x => GetTargetHPPercent(x)).FirstOrDefault();

            return t;
        }

        public static IBattleChara? GetHighestMaxTarget()
        {
            return DPSTargeting.BaseSelection
                .OrderByDescending(x => DPSTargeting.IsCombatPriority(x))
                .ThenByDescending(x => x.TargetObject?.GameObjectId != Player.Object?.GameObjectId)
                .ThenByDescending(x => GetTargetMaxHP(x))
                .ThenBy(x => GetTargetHPPercent(x)).FirstOrDefault();
        }
    }
}
