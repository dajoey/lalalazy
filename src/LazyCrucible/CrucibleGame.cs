using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyCrucible;

/// <summary> Small live reads the advisor panel needs (captured familiars, the fight in sight). </summary>
internal static unsafe class CrucibleGame
{
    /// <summary> Whether the game has sent the captured-familiar list (it arrives with the Master's Bestiary). </summary>
    public static bool RosterLoaded
    {
        get
        {
            var manager = XBMManager.Instance();
            return manager is not null && manager->State == XBMManager.DataState.Received;
        }
    }

    /// <summary> Captured familiar check; every familiar counts as captured while the list is not loaded. </summary>
    public static bool BeastCaptured(int row)
    {
        var manager = XBMManager.Instance();
        if (manager is null || manager->State != XBMManager.DataState.Received)
            return true;
        return manager->IsPetUnlocked((uint)row);
    }

    /// <summary> Panel battle of the hostile enemies present on the current board, -1 when none. </summary>
    public static int CurrentBattle()
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
}
