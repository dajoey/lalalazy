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

    // Smooth pathing and takeoff tracking variables
    private bool _hasSentPathingCommand = false;
    private bool _fallbackToWalk = false;
    private DateTime _takeoffAttemptTime = DateTime.MinValue;
    private bool _wasMounted = false;
    private bool _wasFlying = false;
    private Vector3 _lastPosition = Vector3.Zero;
    private DateTime _lastPositionTime = DateTime.MinValue;

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
        _hasSentPathingCommand = false;
        _fallbackToWalk = false;
        _takeoffAttemptTime = DateTime.MinValue;
        _wasMounted = Svc.Condition[ConditionFlag.Mounted];
        _wasFlying = Svc.Condition[ConditionFlag.InFlight];
        _lastPosition = Vector3.Zero;
        _lastPositionTime = DateTime.MinValue;
        Svc.Log.Information("LazySightseeing automation started.");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _state = AutomationState.Idle;
        _currentTarget = null;
        _hasSentPathingCommand = false;
        Chat.SendMessage("/vnav stop");
        Svc.Log.Information("LazySightseeing automation stopped.");
    }

    public void Tick()
    {
        if (!IsRunning) return;

        // Check for mounting/flying state changes to trigger re-routing
        bool isMounted = Svc.Condition[ConditionFlag.Mounted];
        bool isFlying = Svc.Condition[ConditionFlag.InFlight];
        if (isMounted != _wasMounted || isFlying != _wasFlying)
        {
            _hasSentPathingCommand = false;
            _wasMounted = isMounted;
            _wasFlying = isFlying;
        }

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
                _hasSentPathingCommand = false;
                _fallbackToWalk = false;
                _takeoffAttemptTime = DateTime.MinValue;
                _lastPosition = Vector3.Zero;
                _lastPositionTime = DateTime.MinValue;
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

                var tpTarget = GetTeleportTarget(_currentTarget);
                Svc.Log.Information($"Teleporting to {tpTarget} (mapped from {_currentTarget.Aetheryte}) for sight {_currentTarget.Name}");
                Chat.SendMessage($"/tp {tpTarget}");
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
                    if (Svc.Condition[ConditionFlag.Mounted])
                    {
                        Svc.Log.Information($"Arrived at sight {_currentTarget.Name} while mounted. Stopping movement and dismounting...");
                        Chat.SendMessage("/vnav stop");
                        Chat.SendMessage("/dismount");
                        _nextActionTime = DateTime.UtcNow.AddSeconds(1.5);
                    }
                    else
                    {
                        Svc.Log.Information($"Arrived at sight {_currentTarget.Name}. Stopping movement and executing emote.");
                        Chat.SendMessage("/vnav stop");
                        _state = AutomationState.Emoting;
                        _hasSentPathingCommand = false;
                        _nextActionTime = DateTime.UtcNow.AddMilliseconds(500);
                    }
                }
                else
                {
                    // Check if player is performing an emote and cancel it via jump to allow movement/mounting
                    if (Svc.Condition[ConditionFlag.Emoting])
                    {
                        Svc.Log.Information("Player is performing an emote. Cancelling emote via jump to allow movement...");
                        Chat.SendMessage("/gaction \"Jump\"");
                        _nextActionTime = DateTime.UtcNow.AddSeconds(1.0);
                        break;
                    }

                    bool forceWalk = ShouldForceWalk(_currentTarget);

                    if (forceWalk)
                    {
                        if (Svc.Condition[ConditionFlag.Mounted])
                        {
                            Svc.Log.Information("Forcing walk pathing for indoor/complex vista. Dismounting...");
                            Chat.SendMessage("/dismount");
                            _hasSentPathingCommand = false;
                            _nextActionTime = DateTime.UtcNow.AddSeconds(1.5);
                            break;
                        }
                    }
                    else
                    {
                        // Try to mount up if target is far, we aren't mounted, and haven't tried yet
                        if (distance > 30f && !Svc.Condition[ConditionFlag.Mounted] && !_triedMount && !Svc.Condition[ConditionFlag.Casting])
                        {
                            _triedMount = true;
                            Svc.Log.Information("Target is far. Attempting to mount...");
                            Chat.SendMessage("/gaction \"Mount\"");
                            _hasSentPathingCommand = false;
                            _nextActionTime = DateTime.UtcNow.AddSeconds(2.5); // Wait for mount cast
                            break;
                        }
                    }

                    // Choose and execute pathing command once
                    if (!_hasSentPathingCommand)
                    {
                        string posX = _currentTarget.Position.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        string posY = _currentTarget.Position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        string posZ = _currentTarget.Position.Z.ToString(System.Globalization.CultureInfo.InvariantCulture);

                        bool tryFlying = Svc.Condition[ConditionFlag.Mounted] && !forceWalk && !_fallbackToWalk;

                        if (tryFlying)
                        {
                            Svc.Log.Information($"Sending flyto command for {_currentTarget.Name} to {posX}, {posY}, {posZ}");
                            Chat.SendMessage($"/vnav flyto {posX} {posY} {posZ}");
                            
                            // Schedule takeoff jump after starting movement
                            if (!Svc.Condition[ConditionFlag.InFlight])
                            {
                                _takeoffAttemptTime = DateTime.UtcNow.AddSeconds(1.0);
                                _triedTakeoff = false;
                            }
                        }
                        else
                        {
                            Svc.Log.Information($"Sending moveto command for {_currentTarget.Name} to {posX}, {posY}, {posZ}");
                            Chat.SendMessage($"/vnav moveto {posX} {posY} {posZ}");
                        }
                        _hasSentPathingCommand = true;
                        _lastPosition = playerPos;
                        _lastPositionTime = DateTime.UtcNow;
                    }
                    else
                    {
                        // Pathing is active. Trigger takeoff if trying to fly
                        bool tryFlying = Svc.Condition[ConditionFlag.Mounted] && !forceWalk && !_fallbackToWalk;
                        if (tryFlying && !Svc.Condition[ConditionFlag.InFlight])
                        {
                            if (_takeoffAttemptTime != DateTime.MinValue && DateTime.UtcNow > _takeoffAttemptTime)
                            {
                                if (!_triedTakeoff)
                                {
                                    Svc.Log.Information("Mounted but not flying. Attempting jump to initiate flight...");
                                    Chat.SendMessage("/gaction \"Jump\"");
                                    _triedTakeoff = true;
                                    _takeoffAttemptTime = DateTime.UtcNow.AddSeconds(1.5); // Wait 1.5s to see if flight activates
                                }
                                else
                                {
                                    // Takeoff jump didn't enter flight state (no flying in this zone or not unlocked)
                                    Svc.Log.Warning("Takeoff jump failed. Falling back to ground pathing.");
                                    _fallbackToWalk = true;
                                    _hasSentPathingCommand = false; // Trigger pathing re-evaluation on next frame
                                    _takeoffAttemptTime = DateTime.MinValue;
                                }
                            }
                        }

                        // Track movement to detect stuck / pathing failure
                        if (!Svc.Condition[ConditionFlag.Casting])
                        {
                            if (_lastPosition == Vector3.Zero || Vector3.Distance(playerPos, _lastPosition) > 0.2f)
                            {
                                _lastPosition = playerPos;
                                _lastPositionTime = DateTime.UtcNow;
                            }
                            else if (DateTime.UtcNow - _lastPositionTime > TimeSpan.FromSeconds(3.0))
                            {
                                Svc.Log.Warning("Player has not moved for 3 seconds while pathing. Resetting pathing command to retry...");
                                _hasSentPathingCommand = false;
                                _lastPosition = playerPos;
                                _lastPositionTime = DateTime.UtcNow;
                            }
                        }
                    }

                    // Check distance and status frequently without spamming pathing commands
                    _nextActionTime = DateTime.UtcNow.AddMilliseconds(200);
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

    private string GetTeleportTarget(SightInfo sight)
    {
        switch (sight.Aetheryte)
        {
            // ARR Zones
            case "Middle La Noscea": return "Summerford Farms";
            case "Lower La Noscea": return "Moraby Drydocks";
            case "Western La Noscea":
                // Swiftperch vs Aleport
                return sight.Position.X < 0 ? "Aleport" : "Swiftperch";
            case "Eastern La Noscea":
                // Costa del Sol vs Wineport
                return sight.Position.X > 0 ? "Costa del Sol" : "Wineport";
            case "Upper La Noscea": return "Camp Bronze Lake";
            case "Outer La Noscea": return "Camp Overlook";
            case "Central Shroud": return "Bentbranch Meadows";
            case "East Shroud": return "The Hawthorne Hut";
            case "South Shroud":
                // Quarrymill vs Camp Tranquil
                return sight.Position.Z < 100 ? "Quarrymill" : "Camp Tranquil";
            case "North Shroud": return "Fallgourd Float";
            case "Western Thanalan": return "Horizon";
            case "Central Thanalan": return "Black Brush Station";
            case "Eastern Thanalan": return "Camp Drybone";
            case "Southern Thanalan":
                // Little Ala Mhigo vs Forgotten Springs
                return sight.Position.Z < 0 ? "Little Ala Mhigo" : "Forgotten Springs";
            case "Northern Thanalan": return "Ceruleum Processing Plant";
            case "Coerthas Central Highlands": return "Camp Dragonhead";
            case "Mor Dhona": return "Revenant's Toll";

            // Heavensward Zones
            case "Coerthas Western Highlands": return "Falcon's Nest";
            case "The Dravanian Forelands":
                // Tailfeather vs Anyx Trine
                return sight.Position.X < 100 ? "Anyx Trine" : "Tailfeather";
            case "The Churning Mists":
                // Moghome vs Zenith
                return sight.Position.X > 0 ? "Moghome" : "Zenith";
            case "The Sea of Clouds":
                // Ok' Zundu vs Camp Cloudtop
                return sight.Position.Z < 0 ? "Ok' Zundu" : "Camp Cloudtop";
            case "The Dravanian Hinterlands": return "Idyllshire";
            case "Azys Lla": return "Helix";

            // Stormblood Zones
            case "The Fringes":
                // Castrum Oriens vs The Peering Stones
                return sight.Position.X > 0 ? "Castrum Oriens" : "The Peering Stones";
            case "The Peaks":
                // Ala Gannha vs The Portage
                return sight.Position.X < 0 ? "Ala Gannha" : "The Portage";
            case "The Lochs":
                // Porta Praetoria vs The Ala Mhigan Quarter
                return sight.Position.Z > 0 ? "Porta Praetoria" : "The Ala Mhigan Quarter";
            case "The Ruby Sea":
                // Tamamizu vs Onokoro
                return sight.Position.Z > 0 ? "Tamamizu" : "Onokoro";
            case "Yanxia":
                // Namai vs House of the Fierce
                return sight.Position.X > 100 ? "Namai" : "House of the Fierce";
            case "The Azim Steppe":
                // Reunion vs The Dawn Throne
                return sight.Position.X > 0 ? "Reunion" : "The Dawn Throne";

            // Shadowbringers Zones
            case "Lakeland":
                // Fort Jobb vs The Ostall Imperative
                return sight.Position.X < 150 ? "Fort Jobb" : "The Ostall Imperative";
            case "Kholusia":
                // Stilltide vs Tomra
                return sight.Position.Y < 100 ? "Stilltide" : "Tomra";
            case "Amh Araeng":
                // Twine vs Inn at Journey's Head
                return sight.Position.X > 0 ? "Twine" : "Inn at Journey's Head";
            case "Il Mheg":
                // Lydha Lran vs Wolekdorf vs Pla Enni
                if (sight.Position.X < -100) return "Wolekdorf";
                return sight.Position.Z > 0 ? "Lydha Lran" : "Pla Enni";
            case "The Rak'tika Greatwood":
                // Slitherbough vs Fanow
                return sight.Position.X < 0 ? "Slitherbough" : "Fanow";
            case "The Tempest":
                // Ondo Cups vs Macarenses Angle
                return sight.Position.Y > -300 ? "Ondo Cups" : "Macarenses Angle";

            // Endwalker Zones
            case "Labyrinthos":
                // The Archeion vs Sharlayan Hamlet vs Aporia
                if (sight.Position.Y > 100) return "The Archeion";
                return sight.Position.X < 0 ? "Sharlayan Hamlet" : "Aporia";
            case "Thavnair":
                // Yadovhna's Legacy vs Palaka's Stand vs The Great Work
                if (sight.Position.X < -200) return "Yadovhna's Legacy";
                return sight.Position.Z < -400 ? "The Great Work" : "Palaka's Stand";
            case "Garlemald":
                // Camp Broken Glass vs Tertium
                return sight.Position.X < 0 ? "Camp Broken Glass" : "Tertium";
            case "Elpis":
                // Anagnorisis vs Poeten Oikos
                return sight.Position.X < 100 ? "Anagnorisis" : "Poeten Oikos";
            case "Mare Lamentorum":
                // Sinus Lacrimarum vs Bestways Burrow
                return sight.Position.X < 0 ? "Sinus Lacrimarum" : "Bestways Burrow";
            case "Ultima Thule":
                // Abode of the Ea vs Base Omicron
                return sight.Position.X < 0 ? "Abode of the Ea" : "Base Omicron";

            // Dawntrail Zones
            case "Urqopacha":
                // Wachunpaji vs Many Fires
                return sight.Position.X > 0 ? "Wachunpaji" : "Many Fires";
            case "Kozama'uka":
                // Ok'hanu vs Earth's Eye
                return sight.Position.X > 100 ? "Ok'hanu" : "The Earth's Eye";
            case "Yak T'el":
                // Iq Br'aax vs Mamook
                return sight.Position.Y > 0 ? "Iq Br'aax" : "Mamook";
            case "Shaaloani":
                // Hunu'tey vs Sheshenewezi Springs
                return sight.Position.X < 0 ? "Hunu'tey" : "Sheshenewezi Springs";
            case "Heritage Found":
                // Outskirts vs Electrope Strike
                return sight.Position.Y > 0 ? "The Outskirts" : "Electrope Strike";
            case "Living Memory":
                // Mnemo (south/east - Canal Town/Yesterland)
                // Pyro (north/east - Asyle Volcane)
                // Aero (north/west - Windspath Gardens)
                if (sight.Position.X < 0) return "Leynode Aero";
                return sight.Position.Z > 0 ? "Leynode Mnemo" : "Leynode Pyro";

            default:
                return sight.Aetheryte;
        }
    }

    private bool IsPlayerAvailable()
    {
        if (Svc.Objects.LocalPlayer == null) return false;
        if (Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51]) return false;
        if (Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Svc.Condition[ConditionFlag.OccupiedInQuestEvent]) return false;
        if (Svc.Objects.LocalPlayer.IsDead) return false;
        return true;
    }

    private bool ShouldForceWalk(SightInfo sight)
    {
        // Vistas that are indoor or require ground pathing to not get stuck on walls/ceilings:
        // - ID 340: Steps of the Speaker (Living Memory - inside a circular building on spiral walkways)
        return sight.Id == 340;
    }
}
