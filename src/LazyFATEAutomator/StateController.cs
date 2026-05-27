using System;
using System.Linq;
using System.Numerics;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Fates;

namespace LazyFATEAutomator;

/// <summary>
/// Inferred from live game state on every Tick — never stored as the primary state. Status
/// strings live elsewhere (Status property) so the UI has something stable to render.
/// </summary>
public enum GrindState
{
    Idle,
    Disabled,
    Unconscious,
    Engaging,
    Mounting,
    BetweenFates,
    WaitingForFates,
    SwapZones,
}

/// <summary>
/// Tick-based state machine. Architecture borrowed from CBT's FateGrind task pattern but
/// adapted to a synchronous Framework.Update loop instead of an async Task — keeps the dep
/// surface tiny.
///
/// Re-evaluates the world state every tick rather than persisting a stored state — this
/// prevents the stored field from drifting out of sync with reality (e.g. after a death,
/// teleport, or game-side mount cancel).
/// </summary>
public class StateController : IDisposable
{
    private readonly Plugin _plugin;

    // Gluttony Combo lease — held while we're running.
    private Guid? _activeLease;
    public bool GluttonyLeaseHeld => _activeLease.HasValue;

    // Throttling. Most state-machine work runs every framework tick (fast), but expensive
    // operations (mount cast wait, swap zone wait) gate themselves with this.
    private DateTime _nextActionTimeUtc = DateTime.MinValue;

    // Mount-cast verification
    private bool _mountInFlight;
    private DateTime _mountCastTimeoutUtc = DateTime.MinValue;
    private int _mountAttempts;

    // Dismount-cast verification (cast is ~2s; without this we'd re-issue every tick and cancel ourselves)
    private bool _dismountInFlight;
    private DateTime _dismountTimeoutUtc = DateTime.MinValue;
    private int _dismountAttempts;

    // Active path tracking — prevents re-issuing the same /vnav command tick after tick.
    // We additionally gate on Navigation.IsBusy (vnav's own state) for robustness.
    private enum PathMode { None, Ground, Flight }
    private PathMode _pathMode = PathMode.None;
    private Vector3 _pathDest = Vector3.Zero;
    private const float REPATH_THRESHOLD_YALMS = 5.0f;

    // Per-target tracking — re-evaluate priorities every tick during BetweenFates
    private uint? _currentTargetFateId;

    // Sticky engagement — once we arrive at a FATE, stay committed to HandleEngaging until
    // it finishes/expires/we die/we change zones. Without this, every tick re-evaluates
    // 3D distance and can boot us back into mount/flight logic mid-dismount or mid-fight.
    private uint? _engagingFateId;
    private const float ENGAGE_LEAVE_THRESHOLD_YALMS = 60f; // bail out if we've ended up this far from our committed FATE
    private uint _lastTerritory;
    private DateTime _zoneDryDeadlineUtc = DateTime.MinValue;

    // Exception back-off — prevents tight error loops
    private int _consecutiveTickErrors;
    private DateTime _errorBackoffUntilUtc = DateTime.MinValue;

    public bool IsEnabled { get; private set; }
    public GrindState State { get; private set; } = GrindState.Idle;
    public string Status { get; private set; } = "Idle";
    public int CompletedFatesCount { get; private set; }
    public DateTime SessionStartTime { get; private set; } = DateTime.MinValue;

    public StateController(Plugin plugin) { _plugin = plugin; }

    public void Start()
    {
        if (IsEnabled) return;
        IsEnabled = true;
        State = GrindState.WaitingForFates;
        Status = "Scanning for FATEs...";
        CompletedFatesCount = 0;
        SessionStartTime = DateTime.UtcNow;
        _mountInFlight = false;
        _mountAttempts = 0;
        _dismountInFlight = false;
        _dismountAttempts = 0;
        _engagingFateId = null;
        _zoneDryDeadlineUtc = DateTime.UtcNow.AddSeconds(15);
        _lastTerritory = Svc.ClientState.TerritoryType;
        _consecutiveTickErrors = 0;
        ClearActivePath();
        _plugin.StuckTracker.Reset();
        AcquireGluttonyLease();
        Plugin.PluginLog.Information("Lazy FATE Automator started.");
    }

    public void Stop()
    {
        if (!IsEnabled) return;
        IsEnabled = false;
        State = GrindState.Idle;
        Status = "Idle";
        _plugin.FatesSolver.ClearTarget();
        _engagingFateId = null;
        ClearActivePath();
        ReleaseGluttonyLease();
        Plugin.PluginLog.Information("Lazy FATE Automator stopped.");
    }

    public void Toggle()
    {
        if (IsEnabled) Stop();
        else Start();
    }

    public void Tick()
    {
        if (!IsEnabled) return;
        var now = DateTime.UtcNow;
        if (now < _nextActionTimeUtc) return;
        if (now < _errorBackoffUntilUtc) return;

        try
        {
            TickInner(now);
            _consecutiveTickErrors = 0;
        }
        catch (Exception ex)
        {
            _consecutiveTickErrors++;
            Plugin.PluginLog.Error(ex, $"Lazy FATE Automator tick failed (#{_consecutiveTickErrors})");
            if (_consecutiveTickErrors >= 5)
            {
                Plugin.PluginLog.Error("5 consecutive tick failures — stopping automator");
                Status = "Stopped: 5 consecutive errors. Check /xllog.";
                Stop();
                return;
            }
            _errorBackoffUntilUtc = now.AddSeconds(2 * _consecutiveTickErrors);
        }
    }

    private void TickInner(DateTime now)
    {
        var player = Svc.Objects.LocalPlayer;
        if (player == null)
        {
            _nextActionTimeUtc = now.AddMilliseconds(500);
            return;
        }

        // Zone change wipes path
        if (Svc.ClientState.TerritoryType != _lastTerritory)
        {
            _lastTerritory = Svc.ClientState.TerritoryType;
            _zoneDryDeadlineUtc = now.AddSeconds(15);
            _pathMode = PathMode.None;
            _pathDest = Vector3.Zero;
            _engagingFateId = null;
            _plugin.StuckTracker.Reset();
        }

        // Death short-circuit
        if (player.IsDead)
        {
            if (State != GrindState.Unconscious)
            {
                State = GrindState.Unconscious;
                Status = "Dead. Waiting for revive...";
                ClearActivePath();
                _engagingFateId = null;
                _dismountInFlight = false;
                _dismountAttempts = 0;
            }
            _nextActionTimeUtc = now.AddSeconds(2);
            return;
        }

        // Sticky engagement — if we previously committed to a FATE, stay in HandleEngaging
        // until it ends. Don't re-evaluate distance or route to movement.
        if (_engagingFateId.HasValue)
        {
            var stickyFate = System.Linq.Enumerable.FirstOrDefault(Svc.Fates, f => f.FateId == _engagingFateId.Value);
            if (stickyFate == null)
            {
                // FATE vanished from the table (completed, expired, or zone changed). Release.
                Plugin.PluginLog.Information($"Releasing engagement on FATE {_engagingFateId.Value} (no longer in table)");
                _engagingFateId = null;
                _plugin.FatesSolver.ClearTarget();
            }
            else if (_plugin.Navigation.GetDistance2DTo(stickyFate.Position) > ENGAGE_LEAVE_THRESHOLD_YALMS && !Plugin.Condition[ConditionFlag.InCombat])
            {
                // We've drifted very far from the FATE and aren't fighting anything — let go and re-route.
                Plugin.PluginLog.Information($"Releasing engagement on FATE {_engagingFateId.Value} (drifted {_plugin.Navigation.GetDistance2DTo(stickyFate.Position):F0}y away)");
                _engagingFateId = null;
                _plugin.FatesSolver.ClearTarget();
            }
            else
            {
                HandleEngaging(stickyFate, now);
                return;
            }
        }

        // No sticky engagement — pick a target if we don't have one (or revalidate the existing one)
        var target = _plugin.FatesSolver.ActiveTarget;
        if (target == null || !_plugin.FatesSolver.IsEligible(target))
        {
            target = _plugin.FatesSolver.SelectNextTarget();
            _currentTargetFateId = target?.FateId;
            ClearActivePath();
        }
        else
        {
            // Re-evaluate priority — if a higher-rank FATE has spawned, switch
            var best = _plugin.FatesSolver.SelectNextTarget();
            if (best != null && best.FateId != target.FateId)
            {
                Plugin.PluginLog.Information($"Switching target FATE {target.FateId} -> {best.FateId} (higher priority)");
                _currentTargetFateId = best.FateId;
                target = best;
                ClearActivePath();
                _plugin.StuckTracker.Reset();
            }
        }

        if (target == null)
        {
            HandleNoFate(now);
            return;
        }

        HandleBetweenFates(target, now);
    }

    // ---------- Handlers ----------

    private void HandleNoFate(DateTime now)
    {
        State = GrindState.WaitingForFates;
        Status = "No active FATEs in zone. Scanning...";

        if (_plugin.Config.SwapZones && now > _zoneDryDeadlineUtc && !_plugin.FatesSolver.PlayerHasTwistOfFate())
        {
            var aetheryteName = ZoneSwapper.PickRandomSameExpacAetheryte();
            if (aetheryteName == null)
            {
                Status = "Zone dry, but no other unlocked aetherytes in this expansion. Sitting tight.";
                _nextActionTimeUtc = now.AddSeconds(30);
                _zoneDryDeadlineUtc = now.AddSeconds(60);
                return;
            }

            State = GrindState.SwapZones;
            Status = $"Zone dry. Teleporting to {aetheryteName}...";
            ClearActivePath();
            // Dismount before teleport — /tp ignored while mounted in some cases
            if (Plugin.Condition[ConditionFlag.Mounted])
                _plugin.Navigation.Dismount();
            ZoneSwapper.TeleportTo(aetheryteName);
            _nextActionTimeUtc = now.AddSeconds(20);   // teleport + load time
            _zoneDryDeadlineUtc = now.AddSeconds(60);  // anti-loop, refreshed after zone change too
            return;
        }

        _nextActionTimeUtc = now.AddSeconds(3);
    }

    // Clear target before dismounting so any in-flight auto-attack stops (prevents
    // interrupting the dismount cast).
    private void ClearTarget()
    {
        try { Svc.Targets.Target = null; } catch { }
    }

    /// <summary>Pick the nearest live, targetable FATE-area enemy.</summary>
    private Dalamud.Game.ClientState.Objects.Types.IGameObject? PickFateMob(IFate fate)
    {
        var player = Svc.Objects.LocalPlayer;
        if (player == null) return null;
        Dalamud.Game.ClientState.Objects.Types.IGameObject? best = null;
        float bestDist = float.MaxValue;
        var fateRadiusPlus = MathF.Max(fate.Radius + 5f, 25f);
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc bnpc) continue;
            if (bnpc.BattleNpcKind != Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Combatant) continue;
            if (bnpc.IsDead) continue;
            if (!bnpc.IsTargetable) continue;
            if (Vector3.Distance(bnpc.Position, fate.Position) > fateRadiusPlus) continue;
            var d = Vector3.Distance(bnpc.Position, player.Position);
            if (d < bestDist) { bestDist = d; best = bnpc; }
        }
        return best;
    }

    /// <summary>Find the FATE-starter / motivation NPC near the fate. Non-aggressive characters only.</summary>
    private Dalamud.Game.ClientState.Objects.Types.IGameObject? FindMotivationNpc(IFate fate)
    {
        var player = Svc.Objects.LocalPlayer;
        if (player == null) return null;
        var searchRadius = MathF.Max(fate.Radius + 5f, 30f);
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.ICharacter ic) continue;
            if (ic.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc) continue;
            if (ic.IsDead) continue;
            if (!ic.IsTargetable) continue;
            // Skip aggressive enemy mobs — we want a friendly starter
            if (obj is Dalamud.Game.ClientState.Objects.Types.IBattleNpc bnpc
                && bnpc.BattleNpcKind == Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Combatant) continue;
            if (Vector3.Distance(obj.Position, fate.Position) > searchRadius) continue;
            return obj;
        }
        return null;
    }

    /// <summary>Calls FFXIVClientStructs TargetSystem.InteractWithObject(GameObject*).</summary>
    private unsafe void InteractWith(Dalamud.Game.ClientState.Objects.Types.IGameObject obj)
    {
        try
        {
            var ts = FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem.Instance();
            if (ts == null) return;
            ts->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address);
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Warning(ex, "InteractWith failed");
        }
    }

    private void HandleEngaging(IFate fate, DateTime now)
    {
        State = GrindState.Engaging;

        // FATE finished while we were arriving
        if (fate.Progress >= 100 || fate.TimeRemaining <= 0)
        {
            Plugin.PluginLog.Information($"FATE {fate.FateId} concluded ({fate.Progress}%).");
            CompletedFatesCount++;
            _plugin.FatesSolver.ClearTarget();
            _currentTargetFateId = null;
            _engagingFateId = null;
            _dismountInFlight = false;
            _dismountAttempts = 0;
            _nextActionTimeUtc = now.AddSeconds(2);
            return;
        }

        // Still mounted? Dismount, then sit tight until !Mounted.
        // The dismount cast is ~2s — we must NOT re-issue it every tick or we'd
        // cancel our own cast and never finish. Track _dismountInFlight with a timeout.
        if (Plugin.Condition[ConditionFlag.Mounted])
        {
            if (_dismountInFlight && now < _dismountTimeoutUtc)
            {
                // cast in progress; poll for the Mounted flag to flip
                Status = "Dismounting...";
                _nextActionTimeUtc = now.AddMilliseconds(250);
                return;
            }

            if (_dismountAttempts >= 3)
            {
                // Pathological: 3 dismount attempts failed. Walk away from FATE so we don't loop forever.
                Plugin.PluginLog.Warning($"Dismount failed 3 times on FATE {fate.FateId}. Abandoning target.");
                _plugin.FatesSolver.ClearTarget();
                _currentTargetFateId = null;
                _engagingFateId = null;
                _dismountInFlight = false;
                _dismountAttempts = 0;
                _nextActionTimeUtc = now.AddSeconds(2);
                return;
            }

            _dismountAttempts++;
            _dismountInFlight = true;
            _dismountTimeoutUtc = now.AddSeconds(4);
            Status = $"Dismounting (try {_dismountAttempts}/3)...";
            Plugin.PluginLog.Information($"[LazyFATE] dismount cast requested. try={_dismountAttempts}/3");
            // Clear any target so nothing fires auto-attack and interrupts the dismount channel
            ClearTarget();
            _plugin.Navigation.Dismount();
            _nextActionTimeUtc = now.AddMilliseconds(500);
            return;
        }

        // We're on the ground.
        _dismountInFlight = false;
        _dismountAttempts = 0;

        var player = Svc.Objects.LocalPlayer;
        if (player == null)
        {
            _nextActionTimeUtc = now.AddMilliseconds(500);
            return;
        }

        // FATE in Preparing state — needs an NPC interaction to start.
        // Find the motivation NPC, walk close if needed, then interact.
        if (fate.State == Dalamud.Game.ClientState.Fates.FateState.Preparing)
        {
            var npc = FindMotivationNpc(fate);
            if (npc == null)
            {
                Status = $"Waiting for FATE NPC to spawn: {fate.Name}";
                _nextActionTimeUtc = now.AddSeconds(1);
                return;
            }

            var npcDist = Vector3.Distance(player.Position, npc.Position);
            if (npcDist > 3.5f)
            {
                Status = $"Approaching FATE NPC ({npcDist:F0}y)...";
                _plugin.Navigation.MoveTo(npc.Position);
                _nextActionTimeUtc = now.AddMilliseconds(500);
                return;
            }

            // In range — target + interact
            Status = $"Activating FATE: {fate.Name}";
            try { Svc.Targets.Target = npc; } catch { }
            InteractWith(npc);
            _nextActionTimeUtc = now.AddSeconds(2);
            return;
        }

        // Level sync if overlevel by 5+
        if (_plugin.Config.AutoSyncLevel && player.Level > fate.Level + 4)
        {
            if (!Player.IsLevelSynced)
            {
                Status = $"Syncing level to {fate.Level}...";
                _plugin.Navigation.LevelSync();
                _nextActionTimeUtc = now.AddSeconds(2);
                return;
            }
        }

        // Make sure we have a valid target on a FATE mob so Gluttony's rotation has something to fire on.
        // (Gluttony is now configured DPSRotationMode=Manual + AlwaysHardTarget so it only fires on our pick.)
        var currentTarget = Svc.Targets.Target;
        bool needNewTarget = currentTarget == null
            || currentTarget.IsDead
            || (currentTarget is Dalamud.Game.ClientState.Objects.Types.IBattleNpc cbn
                && Vector3.Distance(cbn.Position, fate.Position) > fate.Radius + 5f);

        if (needNewTarget)
        {
            var mob = PickFateMob(fate);
            if (mob != null)
            {
                try { Svc.Targets.Target = mob; } catch { }
                Status = $"Engaging: {mob.Name} ({fate.Progress}%, {fate.TimeRemaining:F0}s)";
            }
            else
            {
                Status = $"Waiting for FATE mobs to spawn ({fate.Progress}%, {fate.TimeRemaining:F0}s)";
            }
        }
        else
        {
            Status = $"Fighting: {currentTarget!.Name} ({fate.Progress}%, {fate.TimeRemaining:F0}s)";
        }

        _nextActionTimeUtc = now.AddMilliseconds(750);
    }

    private void HandleBetweenFates(IFate target, DateTime now)
    {
        var player = Svc.Objects.LocalPlayer!;
        var reachable = _plugin.Navigation.NearestReachable(target.Position);
        var dist = _plugin.Navigation.GetDistanceTo(reachable);
        // Use 2D distance to the FATE *center* for the arrival decision: when flying down to
        // a ground FATE, the 3D distance can stay 20-30y while we're descending, but
        // horizontally we're already on top. The dismount cast handles the actual descent.
        var dist2DToFate = _plugin.Navigation.GetDistance2DTo(target.Position);
        var arriveThreshold = MathF.Max(15f, MathF.Min(target.Radius * 0.85f, 30f));

        // Stuck evaluation
        var stuck = _plugin.StuckTracker.Update(player.Position);
        if (stuck == MoveStopReason.StuckTeleport)
        {
            Plugin.PluginLog.Warning("Stuck twice in a row — teleporting to nearest aetheryte");
            Status = "Stuck. Teleporting to nearest aetheryte...";
            ClearActivePath();
            _plugin.Navigation.LifestreamTravel("nearest");
            _plugin.FatesSolver.ClearTarget();
            _currentTargetFateId = null;
            _plugin.StuckTracker.Reset();
            _nextActionTimeUtc = now.AddSeconds(10);
            return;
        }
        if (stuck == MoveStopReason.StuckRetry)
        {
            Plugin.PluginLog.Warning("Stuck — forcing path recompute");
            ClearActivePath();
            if (Plugin.Condition[ConditionFlag.Mounted] && !Plugin.Condition[ConditionFlag.InFlight])
                ECommons.Automation.Chat.SendMessage("/gaction \"Jump\"");
            _nextActionTimeUtc = now.AddMilliseconds(800);
        }

        // Arrived? Commit to engagement (sticky) and hand off to HandleEngaging which owns the
        // dismount + combat lifecycle. Once committed we won't re-route via BetweenFates until
        // the FATE ends/expires/we drift far away.
        if (dist2DToFate < arriveThreshold)
        {
            Plugin.PluginLog.Information($"Arrived at FATE {target.FateId} (2D dist {dist2DToFate:F1}y, threshold {arriveThreshold:F1}y). Committing engagement.");
            _engagingFateId = target.FateId;
            ClearActivePath();
            HandleEngaging(target, now);
            return;
        }

        // Far FATE — mount + fly preferred
        if (dist > 35f)
        {
            HandleMountedTravel(target, reachable, dist, now);
            return;
        }

        // Short distance: walk
        State = GrindState.BetweenFates;
        Status = $"Walking to: {target.Name} ({dist:F0}y)";
        _mountInFlight = false;
        EnsureGroundPath(reachable);
        _nextActionTimeUtc = now.AddMilliseconds(500);
    }

    private void HandleMountedTravel(IFate target, Vector3 reachable, float dist, DateTime now)
    {
        if (Plugin.Condition[ConditionFlag.Mounted])
        {
            _mountInFlight = false;
            _mountAttempts = 0;

            if (!Plugin.Condition[ConditionFlag.InFlight])
            {
                // Try to take off — issue flyto then jump
                State = GrindState.BetweenFates;
                Status = $"Taking off to: {target.Name} ({dist:F0}y)";
                EnsureFlightPath(reachable);
                ECommons.Automation.Chat.SendMessage("/gaction \"Jump\"");
                _nextActionTimeUtc = now.AddSeconds(2);
            }
            else
            {
                State = GrindState.BetweenFates;
                Status = $"Flying to: {target.Name} ({dist:F0}y)";
                EnsureFlightPath(reachable);
                _nextActionTimeUtc = now.AddMilliseconds(500);
            }
            return;
        }

        // Not mounted — try to mount, but only if not in combat
        if (Plugin.Condition[ConditionFlag.InCombat])
        {
            // Walk on foot, you're stuck
            State = GrindState.BetweenFates;
            Status = $"In combat — walking to: {target.Name}";
            EnsureGroundPath(reachable);
            _nextActionTimeUtc = now.AddMilliseconds(500);
            return;
        }

        if (_mountInFlight)
        {
            if (now > _mountCastTimeoutUtc)
            {
                _mountAttempts++;
                _mountInFlight = false;
                Plugin.PluginLog.Warning($"Mount attempt {_mountAttempts} timed out.");
                _nextActionTimeUtc = now.AddMilliseconds(500);
            }
            else
            {
                State = GrindState.Mounting;
                Status = $"Mounting ({_mountAttempts + 1}/3)...";
                _nextActionTimeUtc = now.AddMilliseconds(200);
            }
            return;
        }

        if (_mountAttempts < 3)
        {
            _mountInFlight = true;
            _mountCastTimeoutUtc = now.AddSeconds(3);
            State = GrindState.Mounting;
            Status = $"Mounting (try {_mountAttempts + 1}/3)...";
            Plugin.PluginLog.Information($"[LazyFATE] mount cast requested. dist={dist:F1}y try={_mountAttempts + 1}/3");
            ClearActivePath(); // vnav must be idle for the mount cast to register
            _plugin.Navigation.Mount();
            _nextActionTimeUtc = now.AddMilliseconds(500);
            return;
        }

        // Mounting failed 3x — walk if reasonable, otherwise abort
        if (dist > 80f)
        {
            Plugin.PluginLog.Warning($"Mount failed 3x and target is {dist:F1}y away. Aborting FATE.");
            _plugin.FatesSolver.ClearTarget();
            _currentTargetFateId = null;
            ClearActivePath();
            _mountAttempts = 0;
            _nextActionTimeUtc = now.AddSeconds(2);
            return;
        }

        State = GrindState.BetweenFates;
        Status = $"Mount failed. Walking to: {target.Name}";
        EnsureGroundPath(reachable);
        _nextActionTimeUtc = now.AddMilliseconds(500);
    }

    // ---------- Path tracking ----------

    private void EnsureGroundPath(Vector3 dest)
    {
        bool needsNew =
            _pathMode != PathMode.Ground ||
            Vector3.Distance(_pathDest, dest) > REPATH_THRESHOLD_YALMS ||
            (!_plugin.Navigation.IsBusy && Vector3.Distance(_pathDest, dest) > 1f);
        if (!needsNew) return;
        _plugin.Navigation.MoveTo(dest);
        _pathMode = PathMode.Ground;
        _pathDest = dest;
    }

    private void EnsureFlightPath(Vector3 dest)
    {
        bool needsNew =
            _pathMode != PathMode.Flight ||
            Vector3.Distance(_pathDest, dest) > REPATH_THRESHOLD_YALMS ||
            (!_plugin.Navigation.IsBusy && Vector3.Distance(_pathDest, dest) > 1f);
        if (!needsNew) return;
        _plugin.Navigation.FlyTo(dest);
        _pathMode = PathMode.Flight;
        _pathDest = dest;
    }

    private void ClearActivePath()
    {
        _plugin.Navigation.Stop();
        _pathMode = PathMode.None;
        _pathDest = Vector3.Zero;
    }

    // ---------- Gluttony Combo IPC ----------

    private void AcquireGluttonyLease()
    {
        try
        {
            var reg = Plugin.PluginInterface.GetIpcSubscriber<string, string, Guid?>("GluttonyCombo.RegisterForLease");
            _activeLease = reg.InvokeFunc("LazyFATEAutomator", "Lazy FATE Automator");
            if (_activeLease == null)
            {
                Plugin.PluginLog.Warning("Gluttony Combo lease returned null — auto-rotation not configured by us.");
                return;
            }
            Plugin.PluginLog.Information($"Acquired Gluttony Combo lease: {_activeLease}");

            try
            {
                var setState = Plugin.PluginInterface.GetIpcSubscriber<Guid, bool, int>("GluttonyCombo.SetAutoRotationState");
                setState.InvokeFunc(_activeLease.Value, true);
                var setCfg = Plugin.PluginInterface.GetIpcSubscriber<Guid, string, object, int>("GluttonyCombo.SetAutoRotationConfigState");

                // Gluttony's defaults are too aggressive for FATE grinding — they auto-pick random
                // distant targets and fire actions out of combat, which (a) interrupts our dismount
                // cast and (b) makes the player ranged-attack from wrong positions. Force the
                // rotation into a passive mode that fires only on OUR explicit hard target.
                setCfg.InvokeFunc(_activeLease.Value, "InCombatOnly", 1);      // default: only fire in combat
                setCfg.InvokeFunc(_activeLease.Value, "BypassFATE", 1);        // ... but bypass that rule once inside a FATE
                setCfg.InvokeFunc(_activeLease.Value, "FATEPriority", 1);      // prioritize FATE mobs when filtering
                setCfg.InvokeFunc(_activeLease.Value, "DPSRotationMode", 0);   // Manual — never auto-pick a target
                setCfg.InvokeFunc(_activeLease.Value, "HealerRotationMode", 0);
                setCfg.InvokeFunc(_activeLease.Value, "DPSAlwaysHardTarget", 1);    // always use OUR hard target
                setCfg.InvokeFunc(_activeLease.Value, "HealerAlwaysHardTarget", 1);
                setCfg.InvokeFunc(_activeLease.Value, "OnlyAttackInCombat", 0);     // we control target acquisition
            }
            catch (Exception cfgEx)
            {
                Plugin.PluginLog.Warning(cfgEx, "Partial Gluttony config — some settings not applied.");
            }
        }
        catch (Dalamud.Plugin.Ipc.Exceptions.IpcNotReadyError)
        {
            Plugin.PluginLog.Warning("Gluttony Combo IPC not ready — combat will use the player's existing rotation settings.");
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Warning(ex, "Gluttony Combo lease registration failed.");
        }
    }

    private void ReleaseGluttonyLease()
    {
        if (_activeLease == null) return;
        try
        {
            var rel = Plugin.PluginInterface.GetIpcSubscriber<Guid, object>("GluttonyCombo.ReleaseControl");
            rel.InvokeAction(_activeLease.Value);
            Plugin.PluginLog.Information($"Released Gluttony Combo lease: {_activeLease}");
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Warning(ex, "Failed to release Gluttony lease (probably benign on plugin reload)");
        }
        _activeLease = null;
    }

    public void Dispose() => Stop();
}
