using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;

namespace LazyOccultCrescent;

public static class IGameObjectEx
{
    public static bool HasTarget(this IGameObject obj)
    {
        return obj.TargetObject != null;
    }

    public static bool IsTargetingPlayer(this IGameObject obj)
    {
        return obj.TargetObject?.Address == Player.Object?.Address;
    }
}
