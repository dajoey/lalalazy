using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;

namespace LazyOccultCrescent;

public static class TargetHelper
{
    public static IEnumerable<IBattleNpc> Enemies { get; private set; } = [];

    public static void Update()
    {
        Enemies = Svc.Objects.OfType<IBattleNpc>()
            .Where(o => o is
            {
                IsDead: false,
                IsTargetable: true,
            }).Where(o => o.IsHostile())
            .OrderBy(Player.DistanceTo);
    }
}

public static class IBattleNpcListEx
{
    public static IBattleNpc? Closest(this IEnumerable<IBattleNpc> enemies)
    {
        return enemies.FirstOrDefault();
    }

    public static IBattleNpc? Furthest(this IEnumerable<IBattleNpc> enemies)
    {
        return enemies.FirstOrDefault();
    }

    public static IBattleNpc? Centroid(this IEnumerable<IBattleNpc> enemies)
    {
        var list = enemies.ToList();

        var sum = Vector3.Zero;
        foreach (var npc in list)
        {
            sum += npc.Position;
        }

        var centroid = sum / list.Count;

        return list
            .OrderBy(npc => Vector3.DistanceSquared(npc.Position, centroid))
            .FirstOrDefault();
    }
}
