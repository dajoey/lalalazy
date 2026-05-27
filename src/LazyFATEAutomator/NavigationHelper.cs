using System;
using System.Numerics;
using System.Globalization;
using ECommons.DalamudServices;
using ECommons.Automation;

namespace LazyFATEAutomator;

public class NavigationHelper
{
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
    /// Natively triggers mounting using FFXIVClientStructs ActionManager.
    /// </summary>
    public unsafe void Mount()
    {
        var actionManager = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
        if (actionManager != null)
        {
            // ActionType 4 = GeneralAction, ID 24 = Mount Roulette
            actionManager->UseAction((FFXIVClientStructs.FFXIV.Client.Game.ActionType)4, 24);
        }
    }

    /// <summary>
    /// Natively triggers dismounting using FFXIVClientStructs ActionManager.
    /// </summary>
    public unsafe void Dismount()
    {
        var actionManager = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
        if (actionManager != null)
        {
            // ActionType 4 = GeneralAction, ID 10 = Dismount
            actionManager->UseAction((FFXIVClientStructs.FFXIV.Client.Game.ActionType)4, 10);
        }
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
