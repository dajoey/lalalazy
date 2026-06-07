using LazyFateAutomation.Helpers.Services;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Threading.Tasks;
using LazyFateAutomation.Helpers.Extensions;

namespace LazyFateAutomation.Helpers.TaskSystem;

[Flags]
public enum MovementOptions {
    None = 0,
    Mount = 1 << 0,
    Fly = 1 << 1,
    Dismount = 1 << 2,
}

public enum PathingStrategy {
    Auto = 0,
    Navmesh = 1,
    Direct = 2,
}

public static class MovementOptionsExtensions {
    extension(MovementOptions) {
        public static MovementOptions GetCurrent() {
            if (Svc.Objects.LocalPlayer.InFlight)
                return MovementOptions.Mount | MovementOptions.Fly | MovementOptions.Dismount;
            if (Svc.Objects.LocalPlayer.Mounted)
                return MovementOptions.Mount | MovementOptions.Dismount;
            return MovementOptions.None;
        }
    }
}

public readonly record struct MovementConfig(float? Tolerance, MovementOptions Movement, PathingStrategy Pathing) {
    public static MovementConfig Default => new(null, MovementOptions.None, PathingStrategy.Auto);
    public static MovementConfig Everything => new(null, MovementOptions.Mount | MovementOptions.Fly | MovementOptions.Dismount, PathingStrategy.Auto);
    public static MovementConfig GroundMove => new(null, MovementOptions.Mount | MovementOptions.Dismount, PathingStrategy.Auto);
    public static MovementConfig InteractRange => new(3, MovementOptions.None, PathingStrategy.Auto);

    public MovementConfig WithTolerance(float? tolerance) => this with { Tolerance = tolerance };
    public MovementConfig WithOptions(MovementOptions movement) => this with { Movement = movement };
    public MovementConfig WithStrategy(PathingStrategy pathing) => this with { Pathing = pathing };
}

[Flags]
public enum UiSkipOptions {
    None = 0,
    Talk = 1 << 0,
    YesNo = 1 << 1,
    Request = 1 << 2,
    SelectString = 1 << 3,
}

public abstract class TaskBase : AutoTask {
    private readonly OverrideMovement movement = new();
    private static IPlayerCharacter? Player => Svc.Objects.LocalPlayer;

    protected TaskBase() {
        RegisterCleanup(movement);
    }

    private async Task NavmeshReady() {
        using var scope = BeginScope("WaitingForNavmesh");
        Status = "Waiting for Navmesh";
        await WaitUntil(() => Svc.Navmesh.IsReady || Svc.Navmesh.BuildProgress >= 0, "WaitForBuildStart");
        if (Svc.Navmesh.BuildProgress >= 0) {
            await WaitWhile(() => Svc.Navmesh.BuildProgress >= 0, "BuildMesh");
        }
        ErrorIf(!Svc.Navmesh.IsReady, "Failed to build navmesh for the zone");
    }

    protected async Task MoveToFlag(MovementConfig config, bool allowTeleportIfFaster = true, Func<bool>? stopCondition = null, Func<Task>? onStopReached = null) {
        using var scope = BeginScope("MoveToFlag");
        if (FlagMapMarker.Get() is not { } flag) {
            Error($"No flag set!");
            return;
        }
        await TeleportTo(flag.TerritoryId, flag.Position.ToVector3());
        await NavmeshReady();
        if (Svc.Navmesh.FlagToPoint() is not { } pof) {
            Error($"Unable to convert flag to point on floor");
            return;
        }
        await MoveTo(pof, config, allowTeleportIfFaster, stopCondition, onStopReached);
    }

    protected async Task MoveTo(Vector3 dest, MovementConfig config, bool allowTeleportIfFaster = true, Func<bool>? stopCondition = null, Func<Task>? onStopReached = null) {
        using var scope = BeginScope("MoveTo");
        await WaitUntil(() => Player.Available, "WaitingForPlayer");
        var tolerance = config.Tolerance ?? Svc.Navmesh.GetTolerance();
        if (Player.WithinRange(dest, tolerance))
            return;

        if (allowTeleportIfFaster && Coords.IsTeleportingFaster(dest)) {
            await TeleportTo(Svc.ClientState.TerritoryType, dest, allowSameZoneTeleport: true);
            await WaitWhile(() => Player.IsBusy, "WaitForAvailable");
        }

        if (config.Movement.HasFlag(MovementOptions.Mount) || config.Movement.HasFlag(MovementOptions.Fly)) {
            await Mount();
            // Retry mounting loop until successful if CanMount is true (e.g. if combat started during cast)
            while (Player.CanMount && !Player.Mounted) {
                if (Svc.Condition[ConditionFlag.InCombat]) {
                    Status = "Waiting for combat to end to mount";
                    await WaitWhile(() => Svc.Condition[ConditionFlag.InCombat], "WaitForCombatEndMountRetry");
                }
                await Mount();
                if (!Player.Mounted) {
                    await NextFrame(30); // wait 0.5s before retrying
                }
            }
        }

        if (config.Pathing == PathingStrategy.Direct)
            await MoveToDirectly(dest, tolerance);
        else {
            await NavmeshReady();
            await WaitUntil(() => !Svc.Navmesh.PathfindingInProgress, "WaitingForInProgressCalls");
            ErrorIf(!Svc.Navmesh.PathfindAndMoveTo(dest, Player.InFlight || config.Movement.HasFlag(MovementOptions.Fly) && Control.CanFly), "Failed to start pathfinding to destination");
            Status = $"Moving to {dest}";
            using var stop = new OnDispose(Svc.Navmesh.Stop);
            using var restoreMovement = new OnDispose(() => Svc.Navmesh.SetMovementAllowed(true));

            var wasCasting = false;
            if (stopCondition is null) {
                while (!Player.WithinRange(dest, tolerance)) {
                    // Check if we got dismounted mid-travel and need to remount
                    if (config.Movement.HasFlag(MovementOptions.Mount) && Player.CanMount && !Player.Mounted) {
                        Svc.Navmesh.Stop();
                        while (Player.CanMount && !Player.Mounted) {
                            if (Svc.Condition[ConditionFlag.InCombat]) {
                                Status = "Waiting for combat to end to remount";
                                await WaitWhile(() => Svc.Condition[ConditionFlag.InCombat], "WaitForCombatEndMountRetry");
                            }
                            await Mount();
                            if (!Player.Mounted) {
                                await NextFrame(30);
                            }
                        }
                        ErrorIf(!Svc.Navmesh.PathfindAndMoveTo(dest, Player.InFlight || config.Movement.HasFlag(MovementOptions.Fly) && Control.CanFly), "Failed to resume pathfinding to destination");
                    }

                    var isCasting = Player?.IsCasting ?? false;
                    if (isCasting != wasCasting) {
                        Svc.Navmesh.SetMovementAllowed(!isCasting);
                        wasCasting = isCasting;
                    }
                    await NextFrame();
                }
            }
            else {
                while (!(Player.WithinRange(dest, tolerance) || stopCondition())) {
                    // Check if we got dismounted mid-travel and need to remount
                    if (config.Movement.HasFlag(MovementOptions.Mount) && Player.CanMount && !Player.Mounted) {
                        Svc.Navmesh.Stop();
                        while (Player.CanMount && !Player.Mounted) {
                            if (Svc.Condition[ConditionFlag.InCombat]) {
                                Status = "Waiting for combat to end to remount";
                                await WaitWhile(() => Svc.Condition[ConditionFlag.InCombat], "WaitForCombatEndMountRetry");
                            }
                            await Mount();
                            if (!Player.Mounted) {
                                await NextFrame(30);
                            }
                        }
                        ErrorIf(!Svc.Navmesh.PathfindAndMoveTo(dest, Player.InFlight || config.Movement.HasFlag(MovementOptions.Fly) && Control.CanFly), "Failed to resume pathfinding to destination");
                    }

                    var isCasting = Player?.IsCasting ?? false;
                    if (isCasting != wasCasting) {
                        Svc.Navmesh.SetMovementAllowed(!isCasting);
                        wasCasting = isCasting;
                    }
                    await NextFrame();
                }
                if (stopCondition() && onStopReached is not null) {
                    Svc.Navmesh.Stop(); // must be stopped because onStopReached's MoveTo (if present) calls !PathfindingInProgress
                    await onStopReached();
                }
            }
        }

        if (config.Movement.HasFlag(MovementOptions.Dismount) && Player.WithinRange(dest, tolerance)) // only dismount if we're close
            await Dismount();
    }

    protected async Task MoveToDirectly(Vector3 dest, Func<bool> stopCondition) {
        using var scope = BeginScope("MoveDirectly");
        if (stopCondition())
            return;

        Status = $"Moving to {dest}";
        var wasCasting = false;
        using var stop = new OnDispose(() => movement.Enabled = false);
        
        while (!stopCondition()) {
            var isCasting = Player?.IsCasting ?? false;
            if (isCasting != wasCasting) {
                movement.Enabled = !isCasting;
                wasCasting = isCasting;
            }
            if (!isCasting) {
                movement.DesiredPosition = dest;
            }
            await NextFrame();
        }
    }

    protected async Task MoveToDirectly(Vector3 dest, float tolerance) {
        using var scope = BeginScope("MoveDirectlyWithTolerance");
        await MoveToDirectly(dest, () => Player.WithinRange(dest, tolerance));
    }

    protected async Task TeleportTo(uint territoryId, FlagMapMarker flag, bool allowSameZoneTeleport = false)
        => await TeleportTo(territoryId, new Vector3(flag.XFloat, 0, flag.YFloat), allowSameZoneTeleport);

    protected async Task TeleportTo(uint territoryId, Vector3 destination, bool allowSameZoneTeleport = false) {
        using var scope = BeginScope("Teleport");
        if (!allowSameZoneTeleport && Svc.ClientState.TerritoryType == territoryId)
            return; // already in correct zone

        // If we are flying, we must land and dismount before we can cast teleport
        if (Player.InFlight) {
            await Dismount();
        }
        await WaitWhile(() => Player.IsBusy, "WaitForNotBusyBeforeTeleport");

        if (Svc.Condition[ConditionFlag.InCombat]) {
            Status = "Waiting for combat to end before teleporting";
            await WaitWhile(() => Svc.Condition[ConditionFlag.InCombat], "WaitForCombatEndTeleport");
        }

        var closestAetheryteId = Coords.FindClosestAetheryte(territoryId, destination) ?? 0;
        var teleportAetheryteId = Coords.FindPrimaryAetheryte(closestAetheryteId);
        ErrorIf(teleportAetheryteId == 0, $"Failed to find aetheryte in [{territoryId}] {Svc.Data.GetRef<Sheets.TerritoryType>(territoryId).Value.PlaceName.Value.Name}");
        
        if (Svc.Data.GetRef<Sheets.Aetheryte>(teleportAetheryteId) is { Value.Territory.RowId: var destinationId, Value.PlaceName.Value.Name: var destinationName } && Svc.ClientState.TerritoryType != destinationId) {
            Status = $"Teleporting to {destinationName}";
            
            var success = false;
            for (var attempt = 0; attempt < 3; attempt++) {
                if (ActionManager.Teleport(teleportAetheryteId)) {
                    success = true;
                    break;
                }
                Warning($"Teleport attempt {attempt + 1} failed. Waiting to retry...");
                await NextFrame(30); // wait 0.5s before retrying
            }
            
            var started = false;
            if (success) {
                // Wait up to 2 seconds for teleport cast to start
                for (var i = 0; i < 100; i++) {
                    if (Player.IsBusy) {
                        started = true;
                        break;
                    }
                    await NextFrame();
                }
            }
            
            if (started) {
                await WaitUntilTerritory(destinationId);
            } else {
                Warning($"Teleport to {destinationName} failed to start casting. Attempting Return as fallback.");
                Svc.Chat.PrintMessage("Teleport stuck. Executing '/return' to reset...");
                Svc.Chat.ExecuteCommand("/return");
                // Wait for and accept the Return confirmation dialog
                await WaitUntilSkipping(
                    () => Player.IsBusy || AtkUnitBase.IsAddonReady("SelectYesno"),
                    "WaitReturnConfirm",
                    UiSkipOptions.None
                );
                if (AtkUnitBase.IsAddonReady("SelectYesno"))
                    AddonSelectYesno.Yes();
                // Wait for Return cast to start and finish
                var returnStarted = false;
                for (var j = 0; j < 100; j++) {
                    if (Player.IsBusy) {
                        returnStarted = true;
                        break;
                    }
                    await NextFrame();
                }
                if (returnStarted) {
                    await WaitUntil(() => !Player.IsBusy, "ReturnFinish");
                } else {
                    Warning("Failed to start Return to reset stuck teleport state.");
                }
            }
            
            if (destinationId == territoryId) return; // we're in target zone; otherwise fall through to aethernet to get from primary zone to target zone
        }

        if (Svc.ClientState.TerritoryType == territoryId) {
            Status = "Teleporting to aetheryte";
            
            var success = false;
            for (var attempt = 0; attempt < 3; attempt++) {
                if (ActionManager.Teleport(teleportAetheryteId)) {
                    success = true;
                    break;
                }
                Warning($"Same-zone teleport attempt {attempt + 1} failed. Waiting to retry...");
                await NextFrame(30);
            }
            
            var started = false;
            if (success) {
                // Wait up to 2 seconds for same-zone teleport cast to start
                for (var i = 0; i < 100; i++) {
                    if (Player.IsBusy) {
                        started = true;
                        break;
                    }
                    await NextFrame();
                }
            }
            
            if (started) {
                await WaitUntil(() => !Player.IsBusy, "TeleportFinish");
            } else {
                Warning("Same-zone teleport failed to start casting. Attempting Return as fallback.");
                Svc.Chat.PrintMessage("Teleport stuck. Executing '/return' to reset...");
                Svc.Chat.ExecuteCommand("/return");
                // Wait for and accept the Return confirmation dialog
                await WaitUntilSkipping(
                    () => Player.IsBusy || AtkUnitBase.IsAddonReady("SelectYesno"),
                    "WaitReturnConfirm",
                    UiSkipOptions.None
                );
                if (AtkUnitBase.IsAddonReady("SelectYesno"))
                    AddonSelectYesno.Yes();
                // Wait for Return cast to start and finish
                var returnStarted = false;
                for (var j = 0; j < 100; j++) {
                    if (Player.IsBusy) {
                        returnStarted = true;
                        break;
                    }
                    await NextFrame();
                }
                if (returnStarted) {
                    await WaitUntil(() => !Player.IsBusy, "ReturnFinish");
                } else {
                    Warning("Failed to start Return to reset stuck teleport state.");
                }
            }

            if (teleportAetheryteId == closestAetheryteId) return;

            var (aetheryteId, aetherytePos) = Coords.FindAetheryte(teleportAetheryteId);
            if (!Player.WithinRange(aetherytePos, 15))
                await MoveTo(aetherytePos, MovementConfig.GroundMove.WithTolerance(10));
            ErrorIf(!TargetSystem.InteractWith(aetheryteId), "Failed to interact with aetheryte");
            await WaitUntilSkipping(() => AtkUnitBase.IsAddonReady("SelectString"), "WaitSelectAethernet", UiSkipOptions.Talk);
            PacketDispatcher.TeleportToAethernet(teleportAetheryteId, closestAetheryteId);
            await WaitUntil(() => Player.IsBusy, "TeleportStart");
            await WaitUntil(() => Svc.ClientState.TerritoryType == territoryId && GameMain.IsTerritoryLoaded && Player.Interactable, "TeleportFinish");
            return;
        }

        if (teleportAetheryteId != closestAetheryteId) {
            Status = $"Interacting with aethernet to get to [{territoryId}]";
            var (aetheryteId, aetherytePos) = Coords.FindAetheryte(teleportAetheryteId);
            await MoveTo(aetherytePos, MovementConfig.Default.WithTolerance(10));
            ErrorIf(!TargetSystem.InteractWith(aetheryteId), "Failed to interact with aetheryte");
            await WaitUntilSkipping(() => AtkUnitBase.IsAddonReady("SelectString"), "WaitSelectAethernet", UiSkipOptions.Talk);
            PacketDispatcher.TeleportToAethernet(teleportAetheryteId, closestAetheryteId);
            await WaitUntil(() => Player.IsBusy, "TeleportStart"); // TODO: something better
            await WaitUntil(() => Svc.ClientState.TerritoryType == territoryId && GameMain.IsTerritoryLoaded && Player.Interactable, "TeleportFinish");
        }

        if (territoryId == 886) {
            // firmament special case
            Status = $"Interacting with aetheryte to get to the Firmament";
            var (aetheryteId, aetherytePos) = Coords.FindAetheryte(teleportAetheryteId);
            await MoveTo(aetherytePos, MovementConfig.Default.WithTolerance(10));
            ErrorIf(!TargetSystem.InteractWith(aetheryteId), "Failed to interact with aetheryte");
            await WaitUntilSkipping(() => AtkUnitBase.IsAddonReady("SelectString"), "WaitSelectFirmament", UiSkipOptions.Talk);
            PacketDispatcher.TeleportToFirmament(teleportAetheryteId);
            await WaitUntilTerritory(territoryId);
        }

        // I think this check gives more problems than it solves
        WarningIf(Svc.ClientState.TerritoryType != territoryId, $"Failed to teleport to expected zone (exp: {territoryId}, act: {Svc.ClientState.TerritoryType})");
    }

    protected async Task Mount() {
        using var scope = BeginScope(nameof(Mount));
        if (!Player.CanMount) return; // early return if not in mounting territories
        if (Player.Mounted) return;

        if (Svc.Condition[ConditionFlag.InCombat]) {
            Status = "Waiting for combat to end before mounting";
            await WaitWhile(() => Svc.Condition[ConditionFlag.InCombat], "WaitForCombatEndMount");
        }

        Status = "Mounting";
        while (!Player.Mounted && !Svc.Condition[ConditionFlag.InCombat]) {
            if (!Player.IsBusy && !ActionManager.IsActionInUse(ActionType.GeneralAction, 24))
                ActionManager.UseAction(ActionType.GeneralAction, 24);
            await NextFrame();
        }
    }

    protected async Task Dismount() {
        using var scope = BeginScope("Dismount");
        if (Player is null || !Player.Mounted) return;

        if (Svc.Navmesh.NearestPointReachable(Player.Position) is { } nearestPoint)
            await MoveTo(nearestPoint, MovementConfig.Everything);
        else
            Warning($"No nearest landable point found from {Player.Position}. Dismounting may fail");

        Status = "Dismounting";
        while (Player.Mounted) {
            // we are assuming from here on out that you cannot possibly be above ground that is unlandable
            if (Player.InFlight && !Player.IsAirDismountable) {
                Log($"Descending");
                ActionManager.UseAction(ActionType.GeneralAction, 23); // TODO: find a force ground function
                // await WaitWhile(() => Player.InFlight || !Player.IsAirDismountable, "WaitForGround");
            }
            else if (Player.InFlight && Player.IsAirDismountable) {
                Log($"Air Dismount");
                GameMain.ExecuteLocationCommand(LocationCommandFlag.Dismount, Player.Position, (int)Player.PackedRotation);
                //await WaitWhile(() => Player.Mounted, "WaitForDismount");
            }
            else if (Player.Mounted && !Player.InFlight) {
                Log($"Ground Dismount");
                GameMain.ExecuteCommand(CommandFlag.Dismount, 1);
                //await WaitWhile(() => Player.Mounted, "WaitForDismount");
            }
            await NextFrame();
        }
    }

    protected async Task WaitUntilSkipping(Func<bool> condition, string scopeName, UiSkipOptions skip) {
        using var scope = BeginScope(scopeName);
        var startTime = Environment.TickCount64;
        while (!condition()) {
            var talkReady = AtkUnitBase.IsAddonReady("Talk");
            var yesNoReady = AtkUnitBase.IsAddonReady("SelectYesno");
            var requestReady = AtkUnitBase.IsAddonReady("Request");
            var selectStringReady = AtkUnitBase.IsAddonReady("SelectString");

            if (skip.HasFlag(UiSkipOptions.Talk) && talkReady) {
                Log("progressing talk...");
                AddonTalk.Progress();
            }
            if (skip.HasFlag(UiSkipOptions.YesNo) && yesNoReady) {
                Log("progressing yes/no...");
                AddonSelectYesno.Yes();
            }
            if (skip.HasFlag(UiSkipOptions.Request) && requestReady) {
                Log("progressing request...");
                AgentNpcTrade.TurnInRequests();
            }
            if (skip.HasFlag(UiSkipOptions.SelectString) && selectStringReady) {
                Log("progressing select string...");
                AddonSelectString.Select(0);
            }

            // If we've been waiting for at least 1.5 seconds, and the player is not busy and no dialogue addons are open, we're done.
            if (Environment.TickCount64 - startTime > 1500 && !Player.IsBusy && !talkReady && !yesNoReady && !requestReady && !selectStringReady) {
                Log("Dialogue closed and player is not busy, exiting wait");
                break;
            }

            Log("waiting...");
            await NextFrame();
        }
    }

    protected async Task WaitUntilTerritory(uint territoryId) {
        using var scope = BeginScope("WaitUntilTerritory");
        await WaitUntil(() => Svc.ClientState.TerritoryType == territoryId && GameMain.IsTerritoryLoaded && Player.Interactable, "WaitingForTerritory");
    }

    protected async Task InteractWith(IGameObject obj, Func<bool>? waitUntil = null, int? selectStringIndex = null, UiSkipOptions skip = UiSkipOptions.None) {
        using var scope = BeginScope("InteractWith");

        if (!obj.IsInInteractRange()) {
            Log("Not in interact range, moving closer");
            await MoveToDirectly(obj.Position, obj.IsInInteractRange);
        }

        Status = $"Interacting with {obj.GameObjectId}";
        await WaitWhile(() => Player.IsJumping, "WaitForAbleToInteract");
        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++) {
            if (TargetSystem.InteractWith(obj.GameObjectId)) {
                if (selectStringIndex is { } index) {
                    await WaitUntil(() => AtkUnitBase.IsAddonReady("SelectString"), "WaitingForSelectString");
                    AddonSelectString.Select(index);
                }
                if (waitUntil is { } condition) {
                    await WaitUntilSkipping(condition, "WaitingForNpcInteractionToFinish", skip);
                    return;
                }
                else return;
            }
            await NextFrame();
        }
        ErrorIf(true, $"Failed to interact with object after {maxAttempts} tries");
    }
}
