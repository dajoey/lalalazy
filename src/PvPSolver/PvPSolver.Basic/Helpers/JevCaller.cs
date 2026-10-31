using System.Net.Http;
using System.Text;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.GameHelpers;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using RotationSolver.Decisions;

namespace RotationSolver.Basic.Helpers;

/// <summary>
/// "Let Jev call the shots": once a second in a PvP fight, send a snapshot (jobs, numbers and relative positions,
/// no names) to the PvP Jev relay and keep its latest answer: <see cref="JevCall.FocusPick"/> for the target order
/// and <see cref="TryLimitBreak"/> for firing the job's PvP Limit Break. Off unless <c>JevCallsShots</c> is on.
/// One request in flight at a time, started on a pool thread (so no part of the HTTP call, proxy lookup included,
/// runs on the game thread); any failure just leaves the previous answer to go stale, so PvP Solver falls back to
/// its own logic. Failures are counted and summed up in one log line per fight. Nothing here moves the character.
/// </summary>
public static class JevCaller
{
    // The relay is a LAN address: no proxy, so Windows proxy auto-detection (WPAD) never runs for it.
    private static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromMilliseconds(800) })
    {
        Timeout = TimeSpan.FromMilliseconds(1500),
    };
    private static JevAnswer? _latest;
    private static double _lastSent = double.MinValue;
    private static double _lastFireAttempt = double.MinValue;
    private static double _fireUntil = double.MinValue;
    private static ulong? _fireTarget;
    private static int _inFlight;
    private static readonly object UsedLock = new();
    private static readonly List<JevUsed> UsedSinceSnapshot = [];
    private static readonly JevRecentUses RecentUses = new();
    private static readonly object FireLogLock = new();
    private static readonly JevFireLog FireLog = new();
    private static readonly JevCallStats Stats = new();
    private static readonly JevHealShadow HealShadow = new();

    /// <summary>
    /// Called when the game reports an action the player used (PvP Solver's Watcher): kept until the next snapshot,
    /// so the relay log shows what was actually cast, and on whom, next to Jev's pick. Nothing is kept with the switch off.
    /// </summary>
    public static void NoteActionUsed(uint actionId, ulong? targetId)
    {
        if (!Service.Config.JevCallsShots || !DataCenter.IsPvP)
        {
            return;
        }

        ulong? target = targetId is null or 0 or NoTarget ? null : targetId;
        double now = Now;
        // The game reports each action twice: the same action on the same target within 1.5 s is recorded once.
        lock (UsedLock)
        {
            if (UsedSinceSnapshot.Count < 16 && RecentUses.IsNew(actionId, target, now))
            {
                UsedSinceSnapshot.Add(new JevUsed(actionId, target));
            }
        }
    }

    /// <summary>
    /// Called for every effect of an action the player used (PvP Solver's Watcher), before any filtering: when it is
    /// the Limit Break of an open Jev fire window, the log says it fired.
    /// </summary>
    public static void NoteActionEffect(uint actionId)
    {
        if (!Service.Config.JevCallsShots || !DataCenter.IsPvP)
        {
            return;
        }

        string? line;
        lock (FireLogLock)
        {
            line = FireLog.Used(actionId, Now);
        }

        LogFire(line);
    }

    private static void LogFire(string? line)
    {
        if (line != null)
        {
            PluginLog.Information($"[JevCaller] {line}");
        }
    }

    /// <summary>The relay's latest answer (may be stale; the readers check freshness).</summary>
    public static JevAnswer? Latest => Volatile.Read(ref _latest);

    /// <summary>Monotonic seconds, the clock every answer is stamped with.</summary>
    public static double Now => Environment.TickCount64 / 1000.0;

    /// <summary>Called from the plugin's frame update.</summary>
    public static void Update()
    {
        double now = Now;
        bool fighting = Service.Config.JevCallsShots && DataCenter.IsPvP && DataCenter.InCombat;
        // Every frame, before the gate: a fire window's verdict and the relay summary are written even after the fight ends.
        string? fireLine;
        lock (FireLogLock)
        {
            fireLine = FireLog.Tick(now);
        }

        LogFire(fireLine);
        if (Stats.Tick(now, fighting) is { } summary)
        {
            PluginLog.Information($"[JevCaller] {summary}");
        }

        if (!Service.Config.JevCallsShots || !DataCenter.IsPvP || !DataCenter.InCombat || Player.Object == null)
        {
            DropUsed();
            HealShadow.Reset();
            return;
        }

        if (now - _lastSent < JevCall.IntervalSeconds || Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            return;
        }

        _lastSent = now;
        string? body;
        HashSet<ulong> hostileIds = [];
        try
        {
            body = BuildSnapshot(hostileIds);
        }
        catch (Exception ex)
        {
            Stats.Count(JevCallOutcome.Error);
            PluginLog.Debug($"[JevCaller] snapshot failed: {ex.Message}");
            body = null;
        }

        if (body == null)
        {
            Interlocked.Exchange(ref _inFlight, 0);
            return;
        }

        string url = Service.Config.JevRelayUrl;
        _ = Task.Run(() => SendAsync(body, url, hostileIds));
    }

    /// <summary>How long one Jev "fire" stays live: the rotation only gets some frames to act, so a single frame is not enough.</summary>
    public const double FireWindowSeconds = 1.0;

    /// <summary>The enemy a live Jev Limit Break fire is aimed at (null when none, or the Limit Break has no enemy target).</summary>
    public static ulong? PendingLimitBreakTarget => Now < _fireUntil ? _fireTarget : null;

    /// <summary>
    /// The job's PvP Limit Break is usable as itself (not turned into its follow-up action) on this target, by the game's
    /// own check. Casting and the GCD do not count against it: PvP Solver's action timing handles those. A placed
    /// (ground) Limit Break is checked with no target, as the game does for placed actions.
    /// </summary>
    public static unsafe bool LimitBreakUsable(IBaseAction? limitBreak, ulong targetId)
    {
        ActionManager* manager = ActionManager.Instance();
        if (limitBreak == null || manager == null || limitBreak.Info.AdjustedID != limitBreak.ID)
        {
            return false;
        }

        ulong onWhom = limitBreak.Info.TargetAreaAction ? NoTarget : targetId;
        return manager->GetActionStatus(ActionType.Action, limitBreak.ID, onWhom, checkRecastActive: false, checkCastingActive: false) == 0;
    }

    private const ulong NoTarget = 0xE0000000;

    /// <summary>
    /// Called by the rotation before it picks its next action. A fresh Jev "fire" opens a window of
    /// <see cref="FireWindowSeconds"/>: on every frame of it, while the game says the Limit Break is usable on Jev's
    /// target, it is handed back as the next action (PvP Solver's own timing decides the frame it goes off). The window
    /// closes as soon as the Limit Break is used or unusable, and the next window opens no sooner than
    /// <see cref="JevCall.FireRetrySeconds"/> after it, so a use the game rejects never stalls the rotation. The log says
    /// once per window whether the Limit Break fired or why the window closed without it (<see cref="JevFireLog"/>).
    /// </summary>
    public static bool TryLimitBreak(IBaseAction? limitBreak, out IAction? act)
    {
        act = null;
        double now = Now;
        if (limitBreak == null || Player.Object == null)
        {
            return false;
        }

        if (now >= _fireUntil)
        {
            if (!JevCall.ShouldFireLimitBreak(Latest, now, _lastFireAttempt, out ulong? target))
            {
                return false;
            }

            _fireUntil = now + FireWindowSeconds;
            _fireTarget = target;
            _lastFireAttempt = _fireUntil; // the retry gap counts from the end of this window
            // One line per window (at most one per FireRetrySeconds), for the post-play check in dalamud.log / ffxivdb.
            PluginLog.Information($"[JevCaller] JV|lb-fire-window action={limitBreak.ID} target={(target == null ? "none" : "enemy")}");
            string? flushed;
            lock (FireLogLock)
            {
                flushed = FireLog.Open(limitBreak.ID, now, _fireUntil);
            }

            LogFire(flushed);
        }

        ulong? aim = _fireTarget;
        if (!LimitBreakUsable(limitBreak, aim ?? Player.Object.GameObjectId))
        {
            _fireUntil = now; // used (it turned into its follow-up, or the gauge emptied) or not usable: close the window
            // Whether it fired is told by the game's report of the use (NoteActionEffect), within the grace; a Limit Break
            // in its follow-up state was used, so a skip then says follow-up (the report did not match), not unusable.
            lock (FireLogLock)
            {
                FireLog.Close(now, limitBreak.Info.AdjustedID != limitBreak.ID ? "follow-up" : "unusable");
            }

            return false;
        }

        // The relay sends the Limit Break target as the focus too, and FindHostileRaw puts a pending target first.
        if (!limitBreak.CanUse(out act, skipStatusProvideCheck: true, skipTargetStatusNeedCheck: true, usedUp: true,
                skipAoeCheck: true, skipTTKCheck: true))
        {
            act = null;
            FrameWithoutReason(); // PvP Solver's own timing: a later frame of the window may still fire
            return false;
        }

        // Line, cone and splash Limit Breaks pick their own "most enemies" target; Jev chose one, and the game has
        // just confirmed the Limit Break is usable on it, so aim there (Use() fires at Target as set here).
        if (aim != null && !limitBreak.Info.TargetAreaAction && limitBreak.Target.Target?.GameObjectId != aim)
        {
            IBattleChara? chosen = null;
            foreach (IBattleChara b in DataCenter.AllHostileTargets)
            {
                if (b != null && b.GameObjectId == aim && !b.IsDead)
                {
                    chosen = b;
                    break;
                }
            }

            if (chosen == null)
            {
                act = null;
                lock (FireLogLock)
                {
                    FireLog.Refused("no-target");
                }

                return false;
            }

            // The game's usable check ignores range; the enemy may have moved since Jev's snapshot.
            if (!InRangeAndSight(limitBreak.AdjustedID, chosen))
            {
                act = null;
                lock (FireLogLock)
                {
                    FireLog.Refused("out-of-range");
                }

                return false;
            }

            limitBreak.Target = new TargetResult(chosen, [chosen], chosen.Position);
        }

        FrameWithoutReason();
        return true;
    }

    /// <summary>A frame of the fire window with no reason to report (offered, or held back by PvP Solver's timing).</summary>
    private static void FrameWithoutReason()
    {
        lock (FireLogLock)
        {
            FireLog.Refused(null);
        }
    }

    /// <summary>Range and line of sight from the player to the target, as TargetHelper checks it (565: facing only).</summary>
    private static unsafe bool InRangeAndSight(uint actionId, IBattleChara target)
    {
        IBattleChara? me = Player.Object;
        if (me == null || target.Address == IntPtr.Zero)
        {
            return false;
        }

        uint err = ActionManager.GetActionInRangeOrLoS(actionId,
            (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)me.Address,
            (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address);
        return err == 0 || err == 565;
    }

    private static void DropUsed()
    {
        lock (UsedLock)
        {
            UsedSinceSnapshot.Clear();
        }
    }

    private static async Task SendAsync(string body, string url, HashSet<ulong> hostileIds)
    {
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(url, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Stats.Count(JevCallOutcome.Non200);
                PluginLog.Debug($"[JevCaller] relay replied {(int)response.StatusCode}");
                return;
            }

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (JevCall.Parse(json, Now) is not { } answer)
            {
                Stats.Count(JevCallOutcome.Error);
                PluginLog.Debug("[JevCaller] relay reply did not parse");
                return;
            }

            Stats.Count(JevCallOutcome.Ok);
            // Shadow only in this version: the heal call is logged once per change (a fight's first call included) and
            // nothing acts on it.
            if (HealShadow.Next(answer.Heal) is { } healLine)
            {
                PluginLog.Information($"[JevCaller] {healLine}");
            }

            // An unsure answer keeps the last good pick while it is fresh and still hostile (as of this snapshot).
            Volatile.Write(ref _latest, JevCall.KeepLastPick(Volatile.Read(ref _latest), answer, hostileIds.Contains));
        }
        catch (Exception ex)
        {
            Stats.Count(JevCall.Classify(ex));
            PluginLog.Debug($"[JevCaller] relay call failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    private static string? BuildSnapshot(HashSet<ulong> hostileIds)
    {
        IBattleChara? me = Player.Object;
        if (me == null)
        {
            return null;
        }

        // Drained first, so casts from a second with no snapshot (no enemy in view) are never matched to a later pick.
        JevUsed[] used;
        lock (UsedLock)
        {
            used = [.. UsedSinceSnapshot];
            UsedSinceSnapshot.Clear();
        }

        IBaseAction? lb = (DataCenter.CurrentRotation as CustomRotation)?.JevLimitBreak;
        bool LbOk(ulong id) => LimitBreakUsable(lb, id);
        bool selfOk = LbOk(me.GameObjectId);
        bool areaLb = lb?.Info.TargetAreaAction ?? false;
        float lbRange = lb?.TargetInfo.Range ?? 0f;
        bool sam = me.ClassJob.Value.Abbreviation.ToString() == "SAM";

        var party = new List<IBattleChara>();
        var partyIds = new HashSet<ulong> { me.GameObjectId };
        foreach (IBattleChara a in DataCenter.PartyMembers)
        {
            if (a != null && a.GameObjectId != me.GameObjectId && !a.IsDead && a.MaxHp != 0)
            {
                party.Add(a);
                _ = partyIds.Add(a.GameObjectId);
            }
        }

        var hostiles = new List<IBattleChara>();
        foreach (IBattleChara b in DataCenter.AllHostileTargets)
        {
            if (b != null && !b.IsDead && b.MaxHp != 0)
            {
                hostiles.Add(b);
                _ = hostileIds.Add(b.GameObjectId);
            }
        }

        if (hostiles.Count == 0)
        {
            return null;
        }

        // Alliance members (Frontline): the friendly player characters outside the party. PvP Solver's own alliance
        // list is empty in PvP, so they are read from every target in view.
        var allianceMembers = new List<IBattleChara>();
        foreach (IBattleChara a in DataCenter.AllTargets)
        {
            if (a is IPlayerCharacter && !partyIds.Contains(a.GameObjectId) && !a.IsDead && a.MaxHp != 0 && !a.IsEnemy())
            {
                allianceMembers.Add(a);
            }
        }

        var enemies = new List<JevEnemy>(hostiles.Count);
        foreach (IBattleChara b in hostiles)
        {
            float dist = b.DistanceToPlayer();
            enemies.Add(new JevEnemy(
                b.GameObjectId,
                b.ClassJob.Value.Abbreviation.ToString(),
                b.GetHealthRatio(),
                b.CurrentHp,
                dist,
                b.Position.X - me.Position.X,
                b.Position.Z - me.Position.Z,
                b.HasStatus(false, StatusID.Guard),
                HasInvincibleStatus(b),
                b.HasStatus(false, StatusID.Stun_1343, StatusID.Bind_1345, StatusID.Heavy_1344, StatusID.DeepFreeze_3219, StatusID.MiracleOfNature),
                b.TargetObjectId == me.GameObjectId,
                party.Count(a => a.TargetObjectId == b.GameObjectId),
                sam && b.HasStatus(true, StatusID.Kuzushi),
                // A placed (ground) Limit Break goes where PvP Solver aims it; the game answers for the player.
                areaLb ? selfOk && dist <= lbRange : LbOk(b.GameObjectId)));
        }

        // Real HP (CurrentHp/MaxHp) for the player and allies: GetHealthRatio adds heals still in flight.
        var allies = new List<JevAlly>(party.Count + allianceMembers.Count);
        foreach (IBattleChara a in party)
        {
            allies.Add(Ally(a, alliance: false));
        }

        foreach (IBattleChara a in allianceMembers)
        {
            allies.Add(Ally(a, alliance: true));
        }

        JevAlly Ally(IBattleChara a, bool alliance) =>
            new JevAlly(a.GameObjectId, a.ClassJob.Value.Abbreviation.ToString(), a.CurrentHp, a.MaxHp, a.DistanceToPlayer(),
                a.Position.X - me.Position.X, a.Position.Z - me.Position.Z, hostiles.Count(b => b.TargetObjectId == a.GameObjectId), alliance);

        ulong myTarget = me.TargetObjectId;
        var self = new JevSelf(me.ClassJob.Value.Abbreviation.ToString(), me.CurrentHp, me.MaxHp, me.Rotation,
            me.HasStatus(false, StatusID.Heavy_1344, StatusID.Bind_1345), selfOk,
            myTarget is 0 or NoTarget ? null : myTarget, used);
        return JevCall.SnapshotJson(self, enemies, allies);
    }

    private static bool HasInvincibleStatus(IBattleChara b)
    {
        if (b.StatusList == null)
        {
            return false;
        }

        foreach (var status in b.StatusList)
        {
            if (status != null && status.IsInvincible())
            {
                return true;
            }
        }

        return false;
    }
}
