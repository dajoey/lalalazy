#region Dependencies

using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using GluttonyCombo.Extensions;
using GluttonyCombo.Resources.Localization.JobConfigs;
using System;
using System.Collections.Generic;
using System.Numerics;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;

#endregion

namespace GluttonyCombo.Combos.PvE;

// Crucible of the Unbroken: the LIVE half. Reads the board, the panel enemies present, the target's casts and
// statuses, familiar and character HP into BstState. The rules are in BST_CrucibleLogic.cs (pure, harness-tested).
internal partial class BST
{
    /// <summary>
    ///     Last HP % seen per familiar (XBMPet row) this run. Familiars do not regenerate and HP carries from node to node;
    ///     only a campsite rest restores it (the party agent read below catches that once it is verified).
    /// </summary>
    private static readonly Dictionary<int, float> CruciblePetHp = [];

    /// <summary>
    ///     Tick when a familiar left combat below <see cref="BST_CrucibleLogic.PartyHpLow"/> (Parting Blow / horn-swap).
    ///     Party-agent full-heal samples for that row are ignored until this ages past
    ///     <see cref="PartyHpLagGraceMs"/> (agent lag after a leave).
    /// </summary>
    private static readonly Dictionary<int, long> CruciblePetLeftLowAt = [];

    private static uint _crucibleTerritory;
    private static ulong _parryTargetId;
    private static long _parryEndedTick;
    private static string _hornWarningKey = "";
    private static int _lastLivePetRow;
    private static bool _shellWasUp;
    private static long _shellBrokeTick;

    /// <summary> Heal-potion actions whose press did not land (a stale-stock race), and the tick they are refused until. </summary>
    private static readonly Dictionary<uint, long> RefusedHealPotions = [];

    /// <summary> The heal-potion press the rotation last made, awaiting its outcome (0 = none pending). </summary>
    private static uint _healPressPending;

    private static long _healPressPendingTick;

    /// <summary> HP % when the pending press was made: a rise since then is evidence the potion was drunk. </summary>
    private static float _healPressHp;

    /// <summary>
    ///     The heal (the potion's recast running, or HP rising) must show within this of the request: the item menu takes up to
    ///     <see cref="ItemMenuWaitMs"/> to appear, the use and its recast follow. Still nothing later means the press was dead.
    /// </summary>
    private const long HealPressGraceMs = 3000;

    /// <summary> How long a refused heal-potion id is skipped before it is offered again (stock may have been re-bought meanwhile). </summary>
    private const long HealRefuseHoldMs = 10_000;

    /// <summary> The tick of the last heal-potion press: the next offer waits for the heal to land and HP to move (stock is the scarce resource). </summary>
    private static long _healPressTick;

    /// <summary> Spacing between heal-potion presses. The throttle keys on a REAL press only — an idle offer never delays a need that appears next tick. </summary>
    private const long HealPressSpacingMs = 2000;

    private static readonly HashSet<uint> RefusedHealScratch = [];
    private static readonly HashSet<uint> ExpiredHealScratch = [];

    /// <summary> Quelling Wave's range (30 y): a carrier further out than this does not call for a dispel or steer the aim. </summary>
    private const float DispelReach = 30f;

    /// <summary> The Caw's range (25 y, as <see cref="BST_CrucibleLogic.TryDispel"/> gates it). </summary>
    private const float CawReach = 25f;

    /// <summary>
    ///     The Caw can be cast at <paramref name="enemy"/>: in its range and no do-not-attack enemy (the Morphos) within the
    ///     area around it, the same refusal <see cref="BST_CrucibleLogic.TryDispel"/> applies to the target.
    /// </summary>
    internal static bool CawCanDispel(IBattleChara enemy)
    {
        if (GetTargetDistance(enemy) > CawReach)
            return false;
        foreach (var obj in Svc.Objects)
        {
            if (obj is IBattleNpc npc && !npc.IsDead && npc.IsTargetable && npc.CurrentHp != 0 && npc.IsHostile()
                && npc.GameObjectId != enemy.GameObjectId && BST_CrucibleData.DoNotAttack.TryGetValue(npc.NameId, out var reach)
                && (reach == float.MaxValue || Vector3.Distance(npc.Position, enemy.Position) <= reach + enemy.HitboxRadius))
                return false;
        }
        return true;
    }

    /// <summary> Seconds a broken shell still counts as "just broke" (the stun / bind window of the Ymir). </summary>
    private const long ShellBrokeWindowMs = 6000;

    /// <summary> Seconds a lost Directional Parry still counts as "just ended" (Challenge back). </summary>
    private const long ParryEndedWindowMs = 6000;

    /// <summary>
    ///     How long after a low leave the party agent may still report 100 for that familiar (first-board run:
    ///     ~48 s of lag). Camp restores after this window still raise remembered HP.
    /// </summary>
    private const long PartyHpLagGraceMs = 90_000;

    /// <summary> BNpcName of the current target, sampled for the CR| collector. </summary>
    internal static uint LastCrucibleTargetNameId;

    /// <summary>
    ///     Dispels sent that left a status standing, per (enemy, status): after <see cref="BST_CrucibleLogic.DispelMaxTries"/> the
    ///     status stops counting as dispellable on that enemy (<see cref="CarriesDispellableBuff"/>).
    /// </summary>
    private static readonly Dictionary<(ulong Enemy, uint Status), int> DispelTries = [];

    private static readonly HashSet<uint> StatusScratch = [];
    private static readonly List<uint> DispelStatuses = [];
    private static ulong _dispelEnemy;
    private static uint _dispelEnemyName;
    private static readonly BST_CrucibleLogic.DispelUseTracker DispelUses = new();

    /// <summary> A dispellable buff (<see cref="BST_CrucibleData.DispellableBuffs"/>) the rotation has not given up on stands on this enemy. </summary>
    internal static bool CarriesDispellableBuff(IBattleChara enemy)
    {
        foreach (var status in enemy.StatusList)
        {
            var id = status.StatusId;
            if (id != 0 && BST_CrucibleData.DispellableBuffs.Contains(id) && !BST_CrucibleLogic.DispelFutile(DispelTries, enemy.GameObjectId, id))
                return true;
        }
        return false;
    }

    /// <summary> The enemy carries a buff the panel itself flags dispellable: one the game is known to take off. </summary>
    internal static bool CarriesProvenDispellableBuff(IBattleChara enemy)
    {
        foreach (var status in enemy.StatusList)
            if (status.StatusId != 0 && BST_CrucibleData.PanelFlagsDispellable(status.StatusId))
                return true;
        return false;
    }

    /// <summary>
    ///     The rotation decided to dispel (a <c>crucible:dispel-*</c> decision this tick): remember WHICH enemy and which
    ///     dispellable statuses it carried, because by the time the cast has gone out the aim may have moved on.
    /// </summary>
    internal static void NoteDispelDecision(long now)
    {
        if (CurrentTarget is not IBattleChara target)
            return;

        DispelUses.NoteDecision(now);
        _dispelEnemy = target.GameObjectId;
        _dispelEnemyName = target.NameId;
        DispelStatuses.Clear();
        foreach (var status in target.StatusList)
            if (status.StatusId != 0 && BST_CrucibleData.DispellableBuffs.Contains(status.StatusId))
                DispelStatuses.Add(status.StatusId);
    }

    /// <summary>
    ///     Per tick: a status that is no longer on the target starts its count over, and a dispel that has gone out since the
    ///     decision (Quelling Wave, or Tempered Release for the Caw) counts one try on each UNPROVEN status the enemy carried
    ///     (<see cref="BST_CrucibleData.PanelFlagsDispellable"/>: the panel's own are never given up on). One <c>DS|</c> line
    ///     per dispel names the enemy, the statuses and the ones given up on, so a run shows whether the buff came off
    ///     (status_events) and whether the rotation stopped trying.
    /// </summary>
    private static void TrackDispels(IBattleChara? target, long now)
    {
        if (target is not null)
        {
            StatusScratch.Clear();
            foreach (var status in target.StatusList)
                if (status.StatusId != 0)
                    StatusScratch.Add(status.StatusId);
            BST_CrucibleLogic.ForgetGoneStatuses(DispelTries, target.GameObjectId, StatusScratch);
        }

        if (DispelStatuses.Count == 0)
            return;

        var since = Math.Min(SinceUsed(QuellingWave), SinceUsed(TemperedRelease));
        if (!DispelUses.CountUse(now, since))
            return;

        BST_CrucibleLogic.NoteDispelSent(DispelTries, _dispelEnemy, DispelStatuses.FindAll(id => !BST_CrucibleData.PanelFlagsDispellable(id)));
        var futile = DispelStatuses.FindAll(id => BST_CrucibleLogic.DispelFutile(DispelTries, _dispelEnemy, id));
        Svc.Log.Information(
            $"DS|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|enemy={_dispelEnemyName}|st={string.Join(',', DispelStatuses)}|futile={string.Join(',', futile)}");
    }

    private static long _degreeNextPollMs;
    private static int _degreeRead = CrucibleDegree.Unknown;

    /// <summary>
    ///     The board's difficulty degree, from LazyCrucible's IPC (<see cref="CrucibleDegree.IpcName"/>): once a second while a Crucible board is
    ///     under the character, every five seconds while the plugin is not answering (not installed, or no degree seen yet), so a missing
    ///     LazyCrucible costs one caught exception per five seconds. Unread stays "unknown": the rules then keep the conservative reading
    ///     (<see cref="CrucibleDegree.CoverMargin"/>).
    /// </summary>
    private static void ReadDegree(ref BST_RotationLogic.BstState s)
    {
        var now = Environment.TickCount64;
        if (now >= _degreeNextPollMs)
        {
            try
            {
                _degreeRead = Svc.PluginInterface.GetIpcSubscriber<int>(CrucibleDegree.IpcName).InvokeFunc();
            }
            catch
            {
                _degreeRead = CrucibleDegree.Unknown;
            }
            _degreeNextPollMs = now + (CrucibleDegree.IsDegree(_degreeRead) ? 1000 : 5000);
        }

        s.CrucibleDegreeKnown = CrucibleDegree.IsDegree(_degreeRead);
        s.CrucibleDegreeLevel = s.CrucibleDegreeKnown ? _degreeRead : 0;
    }

    internal static unsafe void ReadCrucible(ref BST_RotationLogic.BstState s)
    {
        var territory = Svc.ClientState.TerritoryType;
        if (territory != _crucibleTerritory)
        {
            CruciblePetHp.Clear();
            CruciblePetLeftLowAt.Clear();
            _crucibleTerritory = territory;
            _hornWarningKey = "";
            _parryTargetId = 0;
            _lastLivePetRow = 0;
            _shellWasUp = false;
            _shellBrokeTick = 0;
            PartyHpVerified = false;
            PlayerHpSamples.Clear();
            TargetHpSamples.Clear();
            DispelTries.Clear();
            RefusedHealPotions.Clear();
            _healPressPending = 0;
            _healPressPendingTick = 0;
            _healPressTick = 0;
            _itemMenuOpenedTick = 0;
        }

        s.CrucibleBoard = BST_CrucibleData.BoardOfTerritory(territory);
        s.CrucibleBattle = -1;
        s.PetHpPercent = 100f;
        LastCrucibleTargetNameId = 0;
        if (s.CrucibleBoard == 0 || LocalPlayer is not { } player)
            return;

        ReadDegree(ref s);
        var now = Environment.TickCount64;
        var target = s.HasHostileTarget ? CurrentTarget as IBattleChara : null;

        // Enemies present: count, highest HP, which panel battle they belong to, eggs / morphos near the target.
        var shellUp = false;
        foreach (var obj in Svc.Objects)
        {
            if (obj is not IBattleNpc npc || npc.IsDead || !npc.IsTargetable || npc.CurrentHp == 0 || !npc.IsHostile())
                continue;

            var nameId = npc.NameId;
            if (BST_CrucibleData.DoNotAttack.TryGetValue(nameId, out var reach))
            {
                if (target is null)
                    continue;
                if (npc.GameObjectId == target.GameObjectId)
                    s.TargetDoNotAttack = true;
                else if (reach == float.MaxValue || Vector3.Distance(npc.Position, target.Position) <= reach + target.HitboxRadius)
                    s.ProtectedNearTarget = true;
                continue;
            }

            s.EnemyCount++;
            if (!s.EnemyHasDispellableBuff && CarriesDispellableBuff(npc) && GetTargetDistance(npc) <= DispelReach)
                s.EnemyHasDispellableBuff = true;
            if (!s.DispelCawReachable && CarriesDispellableBuff(npc) && CawCanDispel(npc))
                s.DispelCawReachable = true;
            if (BST_CrucibleData.ShellTargets.Contains(nameId))
            {
                foreach (var status in npc.StatusList)
                {
                    if (BST_CrucibleData.IsDamageImmunityStatus(status.StatusId))
                    {
                        shellUp = true;
                        break;
                    }
                }
            }
            var hp = npc.MaxHp == 0 ? 0f : 100f * npc.CurrentHp / npc.MaxHp;
            if (hp > s.HighestEnemyHpPercent)
                s.HighestEnemyHpPercent = hp;

            if (s.CrucibleBattle < 0 && BST_CrucibleData.Enemy(nameId) is { } enemy && enemy.Board == s.CrucibleBoard)
                s.CrucibleBattle = enemy.Battle;
        }

        if (s.CrucibleBattle >= 0)
            s.CrucibleNeeds = BattleNeedsCached(s.CrucibleBoard, s.CrucibleBattle);

        // A shell (the Ymir's Vulnerability Down) that was up and is gone: the window the guide wants a stun or bind in.
        if (_shellWasUp && !shellUp)
            _shellBrokeTick = now;
        _shellWasUp = shellUp;
        s.ShellJustBroke = !shellUp && _shellBrokeTick != 0 && now - _shellBrokeTick < ShellBrokeWindowMs;

        s.PlayerHpPercent = player.MaxHp == 0 ? 0f : 100f * player.CurrentHp / player.MaxHp;
        s.PlayerHp = player.CurrentHp;

        // Crucible heal potions: the strongest grade HELD (the HUD stock walk in ReadHeldHealActions) whose recast is
        // clear. Recast alone pressed a dead G4 id while a G1 sat in the bag, and a press that does not land (a
        // stale-stock race) refuses that one id for a while so the next grade down answers — no blanket offer throttle,
        // which starved a need appearing just after an idle offer.
        TrackHealPressOutcome(now, s.PlayerHpPercent);
        s.ReadyHealPotion = _healPressPending != 0 || now - _healPressTick < HealPressSpacingMs
            ? 0
            : BST_CrucibleLogic.PickHealPotion(
                BST_CrucibleData.HealPotionActions, HeldHealActions(), RefusedHealActions(now),
                id => GetCooldownRemainingTime(id) < 0.1f);
        s.PlayerIntakePerSecond = TrackIntake(now, player.CurrentHp);
        s.PlayerHasCleansableDebuff = player.HasCleansableDebuff;

        // Familiar HP, remembered per beast so a low familiar is not summoned straight back into danger.
        var party = ReadPartyHp();
        var activeRow = 0;
        if (s.ActiveSlot != 0 && Svc.Buddies.PetBuddy?.GameObject is IBattleChara pet && pet.MaxHp > 0)
        {
            s.PetHpPercent = 100f * pet.CurrentHp / pet.MaxHp;
            s.PetHp = pet.CurrentHp;
            activeRow = BST_RotationLogic.SlotBeast(s, s.ActiveSlot);
            if (activeRow == 0)
                activeRow = s.PetObjectBeast;
            if (activeRow != 0)
            {
                CruciblePetHp[activeRow] = Math.Max(s.PetHpPercent, 0.5f);
                _lastLivePetRow = activeRow;
                // The party agent read is trusted only after it agrees with a summoned familiar's live HP.
                if (party.TryGetValue(activeRow, out var agentHp) && Math.Abs(agentHp - s.PetHpPercent) <= 3f && s.PetHpPercent < 99.5f)
                    PartyHpVerified = true;
            }
        }
        else if (_lastLivePetRow != 0)
        {
            // Familiar just left (Parting Blow / horn-swap / end of fight). Remember the leave so a lagged
            // party-agent 100 cannot wipe the live HP we already stored.
            if (CruciblePetHp.TryGetValue(_lastLivePetRow, out var leftHp) && leftHp < BST_CrucibleLogic.PartyHpLow)
                CruciblePetLeftLowAt[_lastLivePetRow] = now;
            _lastLivePetRow = 0;
        }
        if (PartyHpVerified)
        {
            foreach (var (row, hp) in party)
            {
                if (row == activeRow)
                    continue;
                var recentlyLeftLow = CruciblePetLeftLowAt.TryGetValue(row, out var leftAt)
                    && now - leftAt < PartyHpLagGraceMs;
                BST_CrucibleLogic.ApplyPartyHpSample(CruciblePetHp, row, hp, recentlyLeftLow);
            }
        }
        s.Slot1PetHp = CruciblePetHp.GetValueOrDefault(s.Slot1Beast);
        s.Slot2PetHp = CruciblePetHp.GetValueOrDefault(s.Slot2Beast);
        s.Slot3PetHp = CruciblePetHp.GetValueOrDefault(s.Slot3Beast);

        if (target is not null)
        {
            LastCrucibleTargetNameId = target.NameId;
            s.TargetNameId = target.NameId;

            foreach (var status in target.StatusList)
            {
                var id = status.StatusId;
                if (id == 0)
                    continue;
                if (BST_CrucibleData.StanceStatuses.Contains(id))
                    s.TargetInStance = true;
                if (BST_CrucibleData.DispellableBuffs.Contains(id) && !BST_CrucibleLogic.DispelFutile(DispelTries, target.GameObjectId, id))
                    s.TargetHasDispellableBuff = true;
                if (BST_CrucibleData.ParryStatuses.Contains(id)
                    || (id == BST_CrucibleData.BoneKnightParryStatus && BST_CrucibleData.BoneKnightNameIds.Contains(target.NameId)))
                    s.TargetHasParry = true;
                if (id == BST_CrucibleData.PhysicalVulnerabilityUp)
                    s.TargetVulnerabilityRemaining = Math.Max(s.TargetVulnerabilityRemaining, status.RemainingTime);
                if (BST_CrucibleData.InvulnerableStatuses.Contains(id))
                    s.TargetInvulnerable = true;
            }

            if (target.IsInvincible)
                s.TargetInvulnerable = true;

            if (s.TargetHasDispellableBuff)
                s.EnemyHasDispellableBuff = true;

            s.TargetTimeToDeath = TrackTimeToDeath(now, target.GameObjectId, s.TargetHpPercent);

            if (target.IsCasting)
            {
                s.TargetCastId = target.CastActionId;
                s.TargetCastRemaining = Math.Max(0f, target.TotalCastTime - target.CurrentCastTime);
            }

            var petId = Svc.Buddies.PetBuddy?.GameObject?.GameObjectId ?? 0;
            s.EnemyTargetsPet = petId != 0 && target.TargetObjectId == petId;
            s.EnemyTargetsPlayer = target.TargetObjectId == player.GameObjectId;

            if (s.TargetHasParry)
            {
                _parryTargetId = target.GameObjectId;
                _parryEndedTick = 0;
            }
            else if (_parryTargetId == target.GameObjectId)
            {
                if (_parryEndedTick == 0)
                    _parryEndedTick = now;
                s.ParryJustEnded = now - _parryEndedTick < ParryEndedWindowMs;
                if (!s.ParryJustEnded)
                    _parryTargetId = 0;
            }
        }

        TrackDispels(target, now);

        s.PartingBlowRecast = GetCooldownRemainingTime(PartingBlow);
        s.ReadySnarl = ActionReady(Snarl);
        var sinceSnarl = TimeSinceActionUsed(Snarl);
        s.SinceSnarl = sinceSnarl < 0 ? float.MaxValue : sinceSnarl;
        s.ReadyChallenge = ActionReady(Challenge);

        WarnEmptyHorns(s);
    }

    /// <summary> A heal potion was requested through the item menu (BST_CrucibleLive.RequestHealItem): remember it so its outcome is judged and the next offer is spaced. </summary>
    private static void NoteHealPress(uint actionId, long now, float hpPercent)
    {
        _healPressPending = actionId;
        _healPressPendingTick = now;
        _healPressTick = now;
        _healPressHp = hpPercent;
    }

    /// <summary>
    ///     Judge the pending heal-potion request from the heal itself (<see cref="BST_CrucibleLogic.ResolveHealPress"/>): the
    ///     potion's recast running or HP risen means it was drunk; nothing by the grace refuses that id for
    ///     <see cref="HealRefuseHoldMs"/>, so the offer steps down a grade instead of re-pressing a dead id. The use STAMP is no
    ///     evidence: the game client writes it when it accepts a press, whether or not the server honours it.
    /// </summary>
    private static void TrackHealPressOutcome(long now, float hpPercent)
    {
        if (_healPressPending == 0)
            return;
        var pending = _healPressPending;
        var elapsed = now - _healPressPendingTick;
        var outcome = BST_CrucibleLogic.ResolveHealPress(
            elapsed, GetCooldownRemainingTime(pending) > 0.1f, hpPercent - _healPressHp, HealPressGraceMs);
        if (outcome == BST_CrucibleLogic.HealPressOutcome.Pending)
            return;

        _healPressPending = 0;
        if (outcome == BST_CrucibleLogic.HealPressOutcome.Landed)
            RefusedHealPotions.Remove(pending);
        else
            RefusedHealPotions[pending] = now + HealRefuseHoldMs;
        LogHealItem($"{(outcome == BST_CrucibleLogic.HealPressOutcome.Landed ? "landed" : "dead")}|act={pending}|ms={elapsed}|hp={hpPercent:0}|from={_healPressHp:0}");
    }

    /// <summary> The still-refused heal-potion ids, pruning expired entries (a refusal expires so re-bought stock is offered again). </summary>
    private static HashSet<uint> RefusedHealActions(long now)
    {
        ExpiredHealScratch.Clear();
        foreach (var (action, until) in RefusedHealPotions)
            if (until <= now)
                ExpiredHealScratch.Add(action);
        foreach (var action in ExpiredHealScratch)
            RefusedHealPotions.Remove(action);
        RefusedHealScratch.Clear();
        foreach (var (action, _) in RefusedHealPotions)
            RefusedHealScratch.Add(action);
        return RefusedHealScratch;
    }

    private static int _needsBoard = -1, _needsBattle = -1;
    private static CrucibleNeeds _needs;

    private static CrucibleNeeds BattleNeedsCached(int board, int battle)
    {
        if (board != _needsBoard || battle != _needsBattle)
        {
            // Gluttony ships no guide file: the guide's counters come as generated shared source, installed once.
            if (CrucibleNeedModel.Extras is null)
                BST_CrucibleGuideNeeds.Install();
            _needs = BST_CrucibleLogic.FightNeeds(board, battle);
            _needsBoard = board;
            _needsBattle = battle;
        }
        return _needs;
    }

    /// <summary> Horns deselect after every Crucible encounter: say so once when a battle is up and none is assigned. </summary>
    private static void WarnEmptyHorns(in BST_RotationLogic.BstState s)
    {
        if (!Config.BST_CrucibleHornWarning || s.InCombat || s.CrucibleBattle < 0 || s.SlotBeastsKnown || s.ActiveSlot != 0)
            return;

        var key = $"{s.CrucibleBoard}:{s.CrucibleBattle}";
        if (key == _hornWarningKey)
            return;
        _hornWarningKey = key;

        var picks = BST_CrucibleLogic.HornWarningPicks(s.CrucibleBoard, s.CrucibleBattle, CrucibleBeastCaptured);
        var names = string.Join(", ", picks.ConvertAll(p =>
        {
            var name = BST_Beasts.All[p.Row].Name;
            return name.Length == 0 ? "?" : char.ToUpperInvariant(name[0]) + name[1..];
        }));
        var message = picks.Count == 0
            ? BST_Config.CrucibleHornWarningMessage
            : string.Format(BST_Config.CrucibleHornWarningPicks0And1, BST_CrucibleData.BattleLabel(s.CrucibleBoard, s.CrucibleBattle), names);

        Svc.Toasts.ShowError(BST_Config.CrucibleHornWarningMessage);
        Svc.Chat.PrintError(message);
    }

    /// <summary> Panel battle of the hostile enemies present on the current board, -1 when none. </summary>
    internal static int CurrentCrucibleBattle()
    {
        var board = BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType);
        if (board == 0)
            return -1;
        foreach (var obj in Svc.Objects)
            if (obj is IBattleNpc npc && !npc.IsDead && npc.IsHostile()
                && BST_CrucibleData.Enemy(npc.NameId) is { } enemy && enemy.Board == board)
                return enemy.Battle;
        return -1;
    }

    /// <summary> One-line Crucible readout for the options panel. </summary>
    internal static string CrucibleStatusText()
    {
        var board = BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType);
        if (board == 0)
            return BST_Config.CrucibleStatusOutside;

        var info = BST_CrucibleData.Boards[board - 1];
        var battle = -1;
        foreach (var obj in Svc.Objects)
        {
            if (obj is IBattleNpc npc && !npc.IsDead && npc.IsHostile()
                && BST_CrucibleData.Enemy(npc.NameId) is { } enemy && enemy.Board == board)
            {
                battle = enemy.Battle;
                break;
            }
        }

        return battle < 0
            ? string.Format(BST_Config.CrucibleStatusBoard0, info.Name)
            : string.Format(BST_Config.CrucibleStatusBattle0And1And2, info.Name, BST_CrucibleData.BattleLabel(board, battle),
                BST_CrucibleData.BattleNeeds(board, battle));
    }
}
