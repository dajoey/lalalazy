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
        _plugin.Navigation.Stop();

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
        }

        // Death state evaluation
        if (player.IsDead)
        {
            if (State != GrindState.Unconscious)
            {
                State = GrindState.Unconscious;
                Status = "Character is dead. Waiting for resurrect or release...";
                _plugin.Navigation.Stop();
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
                    _plugin.Navigation.Stop(); // Ensure all prior movement is stopped
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
                    _plugin.Navigation.Stop();
                    break;
                }

                float dist = _plugin.Navigation.GetDistanceTo(target.Position);

                // Run stuck evaluation
                var stuckReason = _plugin.StuckTracker.Update(player.Position);
                if (stuckReason == MoveStopReason.StuckRetry)
                {
                    // Trigger flight jump or path re-routing
                    _plugin.Navigation.Stop();
                    if (Plugin.Condition[ConditionFlag.Mounted] && !Plugin.Condition[ConditionFlag.InFlight])
                    {
                        Chat.SendMessage("/gaction \"Jump\"");
                    }
                    _nextTickTime = DateTime.Now.AddSeconds(2);
                    _plugin.Navigation.FlyTo(target.Position);
                    break;
                }
                else if (stuckReason == MoveStopReason.StuckTeleport)
                {
                    // Unstuck failsafe: Teleport to local zone Aetheryte
                    State = GrindState.WaitingForFates;
                    _plugin.Navigation.Stop();
                    _plugin.Navigation.LifestreamTravel("nearest");
                    _nextTickTime = DateTime.Now.AddSeconds(10);
                    break;
                }

                if (dist < 15.0f)
                {
                    // Arrived! Transition to engaging
                    State = GrindState.Engaging;
                    Status = $"Engaging FATE: {target.Name}";
                    _plugin.Navigation.Stop();
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
                                _plugin.Navigation.FlyTo(target.Position);
                                Chat.SendMessage("/gaction \"Jump\"");
                                _nextTickTime = DateTime.Now.AddSeconds(2);
                            }
                            else
                            {
                                _plugin.Navigation.FlyTo(target.Position);
                                _nextTickTime = DateTime.Now.AddMilliseconds(500);
                            }
                        }
                        else if (_isMounting)
                        {
                            if (Plugin.Condition[ConditionFlag.InCombat])
                            {
                                Plugin.PluginLog.Warning("Entered combat while mounting. Aborting mount attempt.");
                                _isMounting = false;
                                _mountAttempts = 3; // Force immediate fallback to running on foot
                                _nextTickTime = DateTime.Now.AddMilliseconds(100);
                            }
                            else if (DateTime.Now > _mountCastTimeout.AddSeconds(-2.8) && !Plugin.Condition[ConditionFlag.Casting] && !Plugin.Condition[ConditionFlag.Mounted])
                            {
                                // Cast was interrupted or failed to start (latency buffer elapsed)
                                _mountAttempts++;
                                _isMounting = false;
                                Plugin.PluginLog.Warning($"Mount attempt {_mountAttempts} was interrupted or failed to start.");
                                _nextTickTime = DateTime.Now.AddMilliseconds(200);
                            }
                            else if (DateTime.Now > _mountCastTimeout)
                            {
                                // Mount cast timed out
                                _mountAttempts++;
                                _isMounting = false;
                                Plugin.PluginLog.Warning($"Mount attempt {_mountAttempts} timed out.");
                                _nextTickTime = DateTime.Now.AddMilliseconds(500);
                            }
                            else
                            {
                                // Active cast in progress, wait and poll without sending movement commands
                                Status = $"Waiting for mount cast ({_mountAttempts + 1}/3)...";
                                _nextTickTime = DateTime.Now.AddMilliseconds(200);
                            }
                        }
                        else
                        {
                            // Not mounted, not currently casting. Can we try?
                            if (_mountAttempts < 3 && !Plugin.Condition[ConditionFlag.Casting] && !Plugin.Condition[ConditionFlag.InCombat])
                            {
                                _isMounting = true;
                                _mountCastTimeout = DateTime.Now.AddSeconds(3.5);
                                Status = $"Attempting to mount (Try {_mountAttempts + 1}/3)...";
                                _plugin.Navigation.Stop();
                                _plugin.Navigation.Mount();
                                _nextTickTime = DateTime.Now.AddMilliseconds(500); // Give cast time to start
                            }
                            else
                            {
                                // Fallback to ground running
                                Status = "Target is far, mounting failed. Running on foot...";
                                _plugin.Navigation.MoveTo(target.Position);
                                _nextTickTime = DateTime.Now.AddMilliseconds(500);
                            }
                        }
                    }
                    else
                    {
                        // Short distance: always ground move
                        _isMounting = false;
                        _plugin.Navigation.MoveTo(target.Position);
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
                _plugin.Navigation.Stop();
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
