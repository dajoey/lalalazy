using System;
using System.Numerics;
using System.Linq;
using ECommons.DalamudServices;
using ECommons.Automation;
using ECommons.GameHelpers;
using Dalamud.Game.ClientState.Conditions;

namespace LazyFATEAutomator;

public enum GrindState
{
    Idle = 0,
    WaitingForFates = 1,
    WaitingForFollowUp = 2,
    BetweenFates = 3,
    SwapZones = 4,
    Engaging = 5,
    Unconscious = 6
}

public class StateController : IDisposable
{
    private readonly Plugin _plugin;
    private Guid? _activeLease;
    private DateTime _nextTickTime = DateTime.MinValue;
    private Vector3 _lastTargetPosition = Vector3.Zero;
    private DateTime _stateChangeTimeout = DateTime.MinValue;

    // Robust mount verification states
    private bool _isMounting = false;
    private DateTime _mountCastTimeout = DateTime.MinValue;
    private int _mountAttempts = 0;

    // Active vnavmesh path tracking - prevents re-issuing the same /vnav command every tick
    // (re-issuing tears down the in-progress path and forces a fresh recompute, which causes
    // the visible "3 steps forward, 2 steps back" stutter)
    private enum ActivePath { None, Ground, Flight }
    private ActivePath _activePath = ActivePath.None;
    private Vector3 _activePathDest = Vector3.Zero;
    private const float REPATH_THRESHOLD_YALMS = 5.0f;

    private DateTime _dryZoneDetectionTime = DateTime.MinValue;
    private uint _lastTerritory = 0;

    public bool IsEnabled { get; private set; } = false;
    public GrindState State { get; private set; } = GrindState.Idle;
    public string Status { get; private set; } = "Idle";
    public int CompletedFatesCount { get; private set; } = 0;
    public DateTime SessionStartTime { get; private set; } = DateTime.MinValue;

    public StateController(Plugin plugin)
    {
        _plugin = plugin;
    }

    /// <summary>
    /// Issues /vnav moveto only if no ground path is currently active to this destination.
    /// Reissues if mode changed (flight->ground) or target shifted by more than REPATH_THRESHOLD_YALMS.
    /// </summary>
    private void EnsureGroundPath(Vector3 dest)
    {
        if (_activePath != ActivePath.Ground ||
            Vector3.Distance(_activePathDest, dest) > REPATH_THRESHOLD_YALMS)
        {
            _plugin.Navigation.MoveTo(dest);
            _activePath = ActivePath.Ground;
            _activePathDest = dest;
        }
    }

    /// <summary>
    /// Issues /vnav flyto only if no flight path is currently active to this destination.
    /// </summary>
    private void EnsureFlightPath(Vector3 dest)
    {
        if (_activePath != ActivePath.Flight ||
            Vector3.Distance(_activePathDest, dest) > REPATH_THRESHOLD_YALMS)
        {
            _plugin.Navigation.FlyTo(dest);
            _activePath = ActivePath.Flight;
            _activePathDest = dest;
        }
    }

    /// <summary>
    /// Halts vnavmesh and clears active path tracking. Use this anywhere we previously
    /// called Navigation.Stop() so the next EnsureGroundPath/EnsureFlightPath actually fires.
    /// </summary>
    private void ClearActivePath()
    {
        _plugin.Navigation.Stop();
        _activePath = ActivePath.None;
        _activePathDest = Vector3.Zero;
    }

    public void Start()
    {
        if (IsEnabled) return;

        IsEnabled = true;
        State = GrindState.WaitingForFates;
        Status = "Scanning for FATEs...";
        CompletedFatesCount = 0;
        SessionStartTime = DateTime.Now;
        _plugin.StuckTracker.Reset();

        _isMounting = false;
        _mountAttempts = 0;
        _dryZoneDetectionTime = DateTime.Now.AddSeconds(15);
        _lastTerritory = Svc.ClientState.TerritoryType;

        ClearActivePath();

        // Lock Gluttony Combo configuration for optimal automated combat
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
        ClearActivePath();

        // Cleanly release Gluttony Combo IPC control back to player defaults
        ReleaseGluttonyLease();

        Plugin.PluginLog.Information("Lazy FATE Automator stopped.");
    }

    public void Tick()
    {
        if (!IsEnabled) return;
        if (DateTime.Now < _nextTickTime) return;

        var player = Svc.Objects.LocalPlayer;
        if (player == null) return;

        if (Svc.ClientState.TerritoryType != _lastTerritory)
        {
            _lastTerritory = Svc.ClientState.TerritoryType;
            _dryZoneDetectionTime = DateTime.Now.AddSeconds(15);
            // Path is invalid in a new zone
            _activePath = ActivePath.None;
            _activePathDest = Vector3.Zero;
        }

        // Death state evaluation
        if (player.IsDead)
        {
            if (State != GrindState.Unconscious)
            {
                State = GrindState.Unconscious;
                Status = "Character is dead. Waiting for resurrect or release...";
                ClearActivePath();
            }
            _nextTickTime = DateTime.Now.AddSeconds(2);
            return;
        }
        else if (State == GrindState.Unconscious)
        {
            // Character revived
            State = GrindState.WaitingForFates;
            _plugin.StuckTracker.Reset();
        }

        // Run State Machine Transitions
        switch (State)
        {
            case GrindState.WaitingForFates:
                var nextFate = _plugin.FatesSolver.SelectNextTarget();
                if (nextFate != null)
                {
                    State = GrindState.BetweenFates;
                    Status = $"Heading to FATE: {nextFate.Name}";
                    _lastTargetPosition = nextFate.Position;
                    _plugin.StuckTracker.Reset();
                    _isMounting = false;
                    _mountAttempts = 0;
                    ClearActivePath(); // ensure any prior movement is dropped
                    _nextTickTime = DateTime.Now.AddMilliseconds(500);
                }
                else
                {
                    Status = "No active FATEs found. Scanning...";
                    if (_plugin.Config.SwapZones && DateTime.Now > _dryZoneDetectionTime)
                    {
                        State = GrindState.SwapZones;
                        Status = "Zone dry. Preparing same-expansion zone swap...";
                        _stateChangeTimeout = DateTime.Now.AddSeconds(10);
                    }
                    _nextTickTime = DateTime.Now.AddSeconds(4);
                }
                break;

            case GrindState.BetweenFates:
                var target = _plugin.FatesSolver.ActiveTarget;
                if (target == null)
                {
                    State = GrindState.WaitingForFates;
                    ClearActivePath();
                    break;
                }

                float dist = _plugin.Navigation.GetDistanceTo(target.Position);

                // Run stuck evaluation
                var stuckReason = _plugin.StuckTracker.Update(player.Position);
                if (stuckReason == MoveStopReason.StuckRetry)
                {
                    // Force a fresh path computation by clearing state then re-issuing flight
                    ClearActivePath();
                    if (Plugin.Condition[ConditionFlag.Mounted] && !Plugin.Condition[ConditionFlag.InFlight])
                    {
                        Chat.SendMessage("/gaction \"Jump\"");
                    }
                    _nextTickTime = DateTime.Now.AddSeconds(2);
                    EnsureFlightPath(target.Position);
                    break;
                }
                else if (stuckReason == MoveStopReason.StuckTeleport)
                {
                    // Unstuck failsafe: Teleport to local zone Aetheryte
                    State = GrindState.WaitingForFates;
                    ClearActivePath();
                    _plugin.Navigation.LifestreamTravel("nearest");
                    _nextTickTime = DateTime.Now.AddSeconds(10);
                    break;
                }

                if (dist < 15.0f)
                {
                    // Arrived! Transition to engaging
                    State = GrindState.Engaging;
                    Status = $"Engaging FATE: {target.Name}";
                    ClearActivePath();
                    if (Plugin.Condition[ConditionFlag.Mounted])
                    {
                        _plugin.Navigation.Dismount();
                    }
                    _nextTickTime = DateTime.Now.AddSeconds(1);
                }
                else
                {
                    // Choose ground or flight pathing based on distance
                    if (dist > 35.0f)
                    {
                        if (Plugin.Condition[ConditionFlag.Mounted])
                        {
                            _isMounting = false; // Successfully mounted

                            if (!Plugin.Condition[ConditionFlag.InFlight])
                            {
                                // Trigger flight takeoff
                                EnsureFlightPath(target.Position);
                                Chat.SendMessage("/gaction \"Jump\"");
                                _nextTickTime = DateTime.Now.AddSeconds(2);
                            }
                            else
                            {
                                // Already in flight - just keep vnav heading there
                                // EnsureFlightPath is idempotent if dest hasn't shifted >5y,
                                // so this no-ops most ticks (fixing the stutter).
                                EnsureFlightPath(target.Position);
                                _nextTickTime = DateTime.Now.AddMilliseconds(500);
                            }
                        }
                        else if (_isMounting)
                        {
                            if (Plugin.Condition[ConditionFlag.InCombat])
                            {
                                Plugin.PluginLog.Warning("Entered combat while mounting. Aborting mount attempt.");
                                _isMounting = false;
                                _mountAttempts = 3; // Force immediate fallback
                                _nextTickTime = DateTime.Now.AddMilliseconds(100);
                            }
                            else if (DateTime.Now > _mountCastTimeout)
                            {
                                // Mount cast timed out (waited full 3.0 seconds without getting mounted)
                                _mountAttempts++;
                                _isMounting = false;
                                Plugin.PluginLog.Warning($"Mount attempt {_mountAttempts} timed out.");
                                _nextTickTime = DateTime.Now.AddMilliseconds(500);
                            }
                            else
                            {
                                // Active mount attempt in progress, wait and poll for Mounted state every 200ms
                                Status = $"Waiting for mount ({_mountAttempts + 1}/3)...";
                                _nextTickTime = DateTime.Now.AddMilliseconds(200);
                            }
                        }
                        else
                        {
                            // Not mounted. Can we try?
                            if (_mountAttempts < 3 && !Plugin.Condition[ConditionFlag.InCombat])
                            {
                                _isMounting = true;
                                _mountCastTimeout = DateTime.Now.AddSeconds(3.0); // Wait up to 3 seconds for mounted condition
                                Status = $"Attempting to mount (Try {_mountAttempts + 1}/3)...";
                                Plugin.PluginLog.Information($"[LazyFATE] Gaction mount requested. Try: {_mountAttempts + 1}/3. Distance to FATE: {dist:F1}y");
                                ClearActivePath(); // Pathfinder must be halted for the mount cast to register.
                                _plugin.Navigation.Mount();
                                _nextTickTime = DateTime.Now.AddMilliseconds(500); // Give FFXIV and Dalamud time to register
                            }
                            else
                            {
                                // 3 attempts exhausted. Walk on foot only if target is nearby, otherwise abort to avoid bot behavior
                                if (dist > 80.0f)
                                {
                                    Plugin.PluginLog.Warning($"Mounting failed 3 times and target is far ({dist:F1}y). Aborting FATE to avoid suspicious walking on foot.");
                                    _plugin.FatesSolver.ClearTarget();
                                    State = GrindState.WaitingForFates;
                                    ClearActivePath();
                                    _nextTickTime = DateTime.Now.AddSeconds(2);
                                }
                                else
                                {
                                    Status = "Target is nearby, mounting failed. Walking on foot...";
                                    EnsureGroundPath(target.Position);
                                    _nextTickTime = DateTime.Now.AddMilliseconds(500);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Short distance: always ground move
                        _isMounting = false;
                        EnsureGroundPath(target.Position);
                        _nextTickTime = DateTime.Now.AddMilliseconds(500);
                    }
                }
                break;

            case GrindState.Engaging:
                var activeFate = _plugin.FatesSolver.ActiveTarget;
                if (activeFate == null || activeFate.Progress >= 100 || activeFate.TimeRemaining <= 0)
                {
                    // FATE finished or expired
                    Plugin.PluginLog.Information("FATE concluded. Returning to scan.");
                    CompletedFatesCount++;
                    _plugin.FatesSolver.ClearTarget();
                    State = GrindState.WaitingForFates;
                    _nextTickTime = DateTime.Now.AddSeconds(2);
                    break;
                }

                // Handle Level Sync
                if (_plugin.Config.AutoSyncLevel && player.Level > activeFate.Level + 4)
                {
                    // In FFXIV, ECommons Player.IsLevelSynced is set when synced
                    if (!Player.IsLevelSynced)
                    {
                        Status = $"Syncing level to {activeFate.Level}...";
                        _plugin.Navigation.LevelSync();
                        _nextTickTime = DateTime.Now.AddSeconds(2);
                        break;
                    }
                }

                Status = $"Fighting in FATE: {activeFate.Name} ({activeFate.Progress}%)";
                _nextTickTime = DateTime.Now.AddSeconds(1);
                break;

            case GrindState.SwapZones:
                // Teleport randomly to escape dry zones and avoid bot profiles
                ClearActivePath();
                _plugin.Navigation.LifestreamTravel("random");
                State = GrindState.WaitingForFates;
                _nextTickTime = DateTime.Now.AddSeconds(15);
                break;
        }
    }

    /// <summary>
    /// Integrates with Gluttony Combo IPC to request a temporary configuration lease.
    /// </summary>
    private void AcquireGluttonyLease()
    {
        try
        {
            var registerSubscriber = Plugin.PluginInterface.GetIpcSubscriber<string, string, Guid?>("GluttonyCombo.RegisterForLease");
            _activeLease = registerSubscriber.InvokeFunc("LazyFATEAutomator", "Lazy FATE Automator");
            if (_activeLease != null)
            {
                Plugin.PluginLog.Information($"Successfully acquired Gluttony Combo configuration lease: {_activeLease}");

                // Lock Auto-Rotation setting to ENABLED (true)
                var setState = Plugin.PluginInterface.GetIpcSubscriber<Guid, bool, int>("GluttonyCombo.SetAutoRotationState");
                setState.InvokeFunc(_activeLease.Value, true);

                // Lock InCombatOnly config parameter to FALSE (0) so rotation attacks immediately upon target acquisition
                var setConfig = Plugin.PluginInterface.GetIpcSubscriber<Guid, string, object, int>("GluttonyCombo.SetAutoRotationConfigState");
                setConfig.InvokeFunc(_activeLease.Value, "InCombatOnly", 0);

                // Lock FATEPriority config parameter to TRUE (1)
                setConfig.InvokeFunc(_activeLease.Value, "FATEPriority", 1);
            }
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Error(ex, "Failed to register or lease Gluttony Combo configuration via IPC");
        }
    }

    /// <summary>
    /// Releases the configuration lease back to Gluttony Combo, restoring the user's defaults.
    /// </summary>
    private void ReleaseGluttonyLease()
    {
        if (_activeLease == null) return;

        try
        {
            var releaseSubscriber = Plugin.PluginInterface.GetIpcSubscriber<Guid, object>("GluttonyCombo.ReleaseControl");
            releaseSubscriber.InvokeAction(_activeLease.Value);
            Plugin.PluginLog.Information($"Released Gluttony Combo lease successfully: {_activeLease}");
            _activeLease = null;
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Error(ex, "Failed to release Gluttony Combo IPC lease registration");
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
