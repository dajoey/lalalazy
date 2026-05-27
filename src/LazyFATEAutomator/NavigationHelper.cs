using System;
using System.Numerics;
using System.Globalization;
using ECommons.DalamudServices;
using ECommons.Automation;
using ActionType = FFXIVClientStructs.FFXIV.Client.Game.ActionType;
using ActionManager = FFXIVClientStructs.FFXIV.Client.Game.ActionManager;

namespace LazyFATEAutomator;

public class NavigationHelper
{
    // GeneralAction IDs from Lumina/XIVAPI:
    //   9  = Mount Roulette        (summons any unlocked mount — works in every zone)
    //   23 = Dismount
    //   24 = Flying Mount Roulette (only summons flying mounts — fails outside flight-unlocked zones)
    // We use 9 so the call succeeds whether or not flight is unlocked; the state machine
    // handles takeoff separately once mounted.
    private const uint GENERAL_ACTION_MOUNT_ROULETTE = 9;
    private const uint GENERAL_ACTION_DISMOUNT       = 23;

    /// <summary>
    /// Commands vnavmesh to stop all pathfinding and movement immediately.
    /// </summary>
    public void Stop()
    {
        Chat.SendMessage("/vnav stop");
    }

    /// <summary>
    /// Commands vnavmesh to fly to the specified 3D coordinate.
    /// </summary>
    public void FlyTo(Vector3 pos)
    {
        string x = pos.X.ToString(CultureInfo.InvariantCulture);
        string y = pos.Y.ToString(CultureInfo.InvariantCulture);
        string z = pos.Z.ToString(CultureInfo.InvariantCulture);
        Chat.SendMessage($"/vnav flyto {x} {y} {z}");
    }

    /// <summary>
    /// Commands vnavmesh to move along the ground mesh to the specified 3D coordinate.
    /// </summary>
    public void MoveTo(Vector3 pos)
    {
        string x = pos.X.ToString(CultureInfo.InvariantCulture);
        string y = pos.Y.ToString(CultureInfo.InvariantCulture);
        string z = pos.Z.ToString(CultureInfo.InvariantCulture);
        Chat.SendMessage($"/vnav moveto {x} {y} {z}");
    }

    /// <summary>
    /// Natively triggers Mount Roulette via FFXIVClientStructs ActionManager.
    /// Must be called from the framework thread (StateController.Tick is wired to Framework.Update,
    /// so this is satisfied by default).
    /// </summary>
    public unsafe void Mount()
    {
        var am = ActionManager.Instance();
        if (am == null) return;
        am->UseAction(ActionType.GeneralAction, GENERAL_ACTION_MOUNT_ROULETTE);
    }

    /// <summary>
    /// Natively triggers Dismount via FFXIVClientStructs ActionManager.
    /// </summary>
    public unsafe void Dismount()
    {
        var am = ActionManager.Instance();
        if (am == null) return;
        am->UseAction(ActionType.GeneralAction, GENERAL_ACTION_DISMOUNT);
    }

    /// <summary>
    /// Syncs the player's level to the active FATE.
    /// </summary>
    public void LevelSync()
    {
        Chat.SendMessage("/levelsync");
    }

    /// <summary>
    /// Initiates a teleportation to a target Aetheryte.
    /// </summary>
    public void Teleport(string aetheryteName)
    {
        Chat.SendMessage($"/tp {aetheryteName}");
    }

    /// <summary>
    /// Triggers a lifestream teleport (zone or aethernet travel).
    /// </summary>
    public void LifestreamTravel(string target)
    {
        Chat.SendMessage($"/li {target}");
    }

    /// <summary>
    /// Calculates the 3D distance between the player and the target coordinates.
    /// </summary>
    public float GetDistanceTo(Vector3 target)
    {
        var player = Svc.Objects.LocalPlayer;
        if (player == null) return float.MaxValue;

        return Vector3.Distance(player.Position, target);
    }
}
