using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ECommons.DalamudServices;
using ECommons.Automation;
using Dalamud.Game.ClientState.Conditions;

namespace LazySightseeing;

public enum AutomationState
{
    Idle,
    Teleporting,
    WaitingForTeleport,
    MovingToSight,
    Emoting,
    ReturningToInn
}

public sealed class AutomationService
{
    private readonly Plugin _plugin;
    private AutomationState _state = AutomationState.Idle;
    private SightInfo? _currentTarget;
    private DateTime _nextActionTime = DateTime.MinValue;
    private int _emoteCount = 0;
    private uint _lastTerritory = 0;
    private DateTime _stateChangeTimeout = DateTime.MinValue;
    private bool _triedMount = false;
    private bool _triedTakeoff = false;


    public AutomationState State => _state;
    public SightInfo? CurrentTarget => _currentTarget;
    public bool IsRunning => _state != AutomationState.Idle;

    public AutomationService(Plugin plugin)
    {
        _plugin = plugin;
    }

    public void Start()
    {
        if (IsRunning) return;
        _state = AutomationState.Teleporting;
        _currentTarget = null;
        _emoteCount = 0;
        _nextActionTime = DateTime.MinValue;
        _lastTerritory = Svc.ClientState.TerritoryType;
        _triedMount = false;
        _triedTakeoff = false;
        Svc.Log.Information("LazySightseeing automation started.");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _state = AutomationState.Idle;
        _currentTarget = null;
        Chat.SendMessage("/vnav stop");
        Svc.Log.Information("LazySightseeing automation stopped.");
    }

    public void Tick()
    {
        if (!IsRunning) return;

        // Rate limit tick evaluation
        if (DateTime.UtcNow < _nextActionTime) return;

        // Check if player is fully loaded and alive
        if (!IsPlayerAvailable())
        {
            // If we are waiting for teleport/loading screen, that's fine
            if (_state == AutomationState.WaitingForTeleport)
            {
                // Still loading, keep waiting
                _nextActionTime = DateTime.UtcNow.AddMilliseconds(500);
                return;
            }
            
            // Otherwise wait a bit for player to become available
            _nextActionTime = DateTime.UtcNow.AddMilliseconds(1000);
            return;
        }

        // Get the best target based on current time/weather windows
        var bestTarget = GetNextTarget();

        // If no target is available, we are either finished or waiting for windows
        if (bestTarget == null)
        {
            if (HasAnyUncompletedSightsSelected())
            {
                // We have sights left, but none of their weather/time windows are open
                _currentTarget = null;
                if (_state != AutomationState.WaitingForTeleport)
                {
                    Chat.SendMessage("/vnav stop");
                }
                _nextActionTime = DateTime.UtcNow.AddMilliseconds(2000);
                return;
            }
            else
            {
                // All selected/available sights are complete! Time to return to default inn.
                Svc.Log.Information("All sights complete! Returning to inn.");
                _state = AutomationState.ReturningToInn;
                _currentTarget = null;
                Chat.SendMessage("/vnav stop");
                Chat.SendMessage("/li inn");
                _nextActionTime = DateTime.UtcNow.AddMilliseconds(10000); // Give it time to start teleporting
                return;
            }
        }

        // Handle active re-prioritization / target swapping
        if (_currentTarget == null || _currentTarget.Id != bestTarget.Id)
        {
            // Only switch target if we don't have one, or aren't currently mid-teleport/cast
            if (_currentTarget == null || (_state != AutomationState.Teleporting && _state != AutomationState.WaitingForTeleport))
            {
                Svc.Log.Information($"Target changed: {bestTarget.Name} (ID: {bestTarget.Id}) is now active.");
                _currentTarget = bestTarget;
                _emoteCount = 0;
                _triedMount = false;
                _triedTakeoff = false;
                Chat.SendMessage("/vnav stop");
                
                if (Svc.ClientState.TerritoryType == _currentTarget.TerritoryType)
                {
                    _state = AutomationState.MovingToSight;
                }
                else
                {
                    _state = AutomationState.Teleporting;
                }
            }
        }

        // Run State Machine
        switch (_state)
        {
            case AutomationState.Teleporting:
                if (_currentTarget == null) break;

                Svc.Log.Information($"Teleporting to {_currentTarget.Aetheryte} for sight {_currentTarget.Name}");
                Chat.SendMessage($"/tp {_currentTarget.Aetheryte}");
                _state = AutomationState.WaitingForTeleport;
                _stateChangeTimeout = DateTime.UtcNow.AddSeconds(15); // Safety timeout for cast to start
                _nextActionTime = DateTime.UtcNow.AddSeconds(3); // Wait for cast start
                break;

            case AutomationState.WaitingForTeleport:
                // Check if we are between areas (loading screen) or casting
                bool isBetweenAreas = Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];
                
                if (isBetweenAreas)
                {
                    // Refresh timeout since we are actively loading
                    _stateChangeTimeout = DateTime.UtcNow.AddSeconds(20);
                    _nextActionTime = DateTime.UtcNow.AddMilliseconds(500);
                }
                else if (Svc.ClientState.TerritoryType == _currentTarget?.TerritoryType)
                {
                    // We successfully arrived in the target zone!
                    Svc.Log.Information("Arrived in target territory. Starting pathing.");
                    _state = AutomationState.MovingToSight;
                    _nextActionTime = DateTime.UtcNow.AddSeconds(2); // Short delay to let mesh load
                }
                else if (DateTime.UtcNow > _stateChangeTimeout)
                {
                    // Safety retry if teleport cast didn't start or got interrupted
                    Svc.Log.Warning("Teleport timed out or failed. Retrying...");
                    _state = AutomationState.Teleporting;
                    _nextActionTime = DateTime.UtcNow.AddMilliseconds(1000);
                }
                else
                {
                    // Still waiting for cast to start or complete
                    _nextActionTime = DateTime.UtcNow.AddMilliseconds(500);
                }
                break;

            case AutomationState.MovingToSight:
                if (_currentTarget == null) break;

                var playerPos = Svc.Objects.LocalPlayer!.Position;
                float distance = Vector3.Distance(playerPos, _currentTarget.Position);

                if (distance < 1.8f)
                {
                    Svc.Log.Information($"Arrived at sight {_currentTarget.Name}. executing emote.");
                    _state = AutomationState.Emoting;
                    _nextActionTime = DateTime.UtcNow.AddMilliseconds(500);
                }
                else
                {
                    // Try to mount up if target is far, we aren't mounted, and haven't tried yet
                    if (distance > 30f && !Svc.Condition[ConditionFlag.Mounted] && !_triedMount && !Svc.Condition[ConditionFlag.Casting])
                    {
                        _triedMount = true;
                        Svc.Log.Information("Target is far. Attempting to mount...");
                        Chat.SendMessage("/gaction \"Mount\"");
                        _nextActionTime = DateTime.UtcNow.AddSeconds(2.5); // Wait for mount cast
                        break;
                    }

                    // Try to takeoff if mounted, not flying, and haven't tried yet
                    if (Svc.Condition[ConditionFlag.Mounted] && !Svc.Condition[ConditionFlag.InFlight] && !_triedTakeoff)
                    {
                        _triedTakeoff = true;
                        Svc.Log.Information("Mounted but not flying. Attempting to jump to enter flight...");
                        Chat.SendMessage("/gaction \"Jump\"");
                        _nextActionTime = DateTime.UtcNow.AddSeconds(1.0); // Wait for takeoff/jump to register
                        break;
                    }

                    // Choose pathing command based on flight state and use culture-invariant float formatting
                    string posX = _currentTarget.Position.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    string posY = _currentTarget.Position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    string posZ = _currentTarget.Position.Z.ToString(System.Globalization.CultureInfo.InvariantCulture);

                    if (Svc.Condition[ConditionFlag.Mounted] && Svc.Condition[ConditionFlag.InFlight])
                    {
                        Chat.SendMessage($"/vnav flyto {posX} {posY} {posZ}");
                    }
                    else
                    {
                        Chat.SendMessage($"/vnav moveto {posX} {posY} {posZ}");
                    }
                    _nextActionTime = DateTime.UtcNow.AddSeconds(4);
                }
                break;

            case AutomationState.Emoting:
                if (_currentTarget == null) break;

                // Check if sight is already complete
                if (IsSightCompleted(_currentTarget.Id))
                {
                    Svc.Log.Information($"Sight {_currentTarget.Name} completed successfully!");
                    _currentTarget = null;
                    _state = AutomationState.Teleporting; // Next tick will re-evaluate best target
                    _nextActionTime = DateTime.UtcNow.AddMilliseconds(500);
                    break;
                }

                // If window suddenly closed while emoting (weather/time shifted), stop emoting
                if (_plugin.Config.SkipIfWindowNotOpen && !IsWindowOpen(_currentTarget))
                {
                    Svc.Log.Information($"Window closed for {_currentTarget.Name} while emoting. Re-evaluating.");
                    _currentTarget = null;
                    _state = AutomationState.Teleporting;
                    _nextActionTime = DateTime.UtcNow.AddMilliseconds(500);
                    break;
                }

                // Execute emote
                Svc.Log.Debug($"Executing emote /{_currentTarget.Emote} (Attempt {++_emoteCount})");
                Chat.SendMessage($"/{_currentTarget.Emote}");
                
                // Wait for the configured interval before checking or re-executing
                _nextActionTime = DateTime.UtcNow.AddMilliseconds(_plugin.Config.EmoteIntervalMs);
                break;

            case AutomationState.ReturningToInn:
                // Lifestream handles moving into the inn room. Once fully loaded inside the inn, we stop.
                // We check if the territory name or ID matches an inn room, or if a reasonable duration has elapsed.
                // Since /li inn finishes the task, we can safely stop automation after a safety period.
                Svc.Log.Information("Inn return completed. Stopping LazySightseeing.");
                Stop();
                break;
        }
    }

    public bool IsWindowOpen(SightInfo sight)
    {
        // 1. Time check
        int currentET = WeatherService.GetEorzeaHour();
        if (!WeatherService.IsTimeInWindow(sight.TimeWindow, currentET))
        {
            return false;
        }

        // 2. Weather check
        if (sight.Weathers != null && sight.Weathers.Count > 0)
        {
            if (!WeatherService.IsWeatherMatching(sight.TerritoryType, sight.Weathers))
            {
                return false;
            }
        }

        return true;
    }

    public SightInfo? GetNextTarget()
    {
        foreach (var sight in SightseeingDatabase.Sights)
        {
            // Skip if completed
            if (IsSightCompleted(sight.Id)) continue;

            // Skip if not selected in user checklist
            if (_plugin.Config.SelectedSightIds.Count > 0 && !_plugin.Config.SelectedSightIds.Contains(sight.Id)) continue;

            // Check if window is open
            if (_plugin.Config.SkipIfWindowNotOpen)
            {
                if (!IsWindowOpen(sight)) continue;
            }

            // Return the first uncompleted, selected, ready sight!
            return sight;
        }

        return null;
    }

    public bool HasAnyUncompletedSightsSelected()
    {
        foreach (var sight in SightseeingDatabase.Sights)
        {
            if (IsSightCompleted(sight.Id)) continue;
            if (_plugin.Config.SelectedSightIds.Count > 0 && !_plugin.Config.SelectedSightIds.Contains(sight.Id)) continue;
            return true;
        }
        return false;
    }

    public static unsafe bool IsSightCompleted(uint sightId)
    {
        var playerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        if (playerState == null) return false;
        return playerState->IsAdventureComplete(sightId - 1);
    }

    private bool IsPlayerAvailable()
    {
        if (Svc.Objects.LocalPlayer == null) return false;
        if (Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51]) return false;
        if (Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Svc.Condition[ConditionFlag.OccupiedInQuestEvent]) return false;
        if (Svc.Objects.LocalPlayer.IsDead) return false;
        return true;
    }
}
