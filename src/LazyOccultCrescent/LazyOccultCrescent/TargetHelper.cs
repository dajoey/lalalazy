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
    // IReadOnlyList, not IEnumerable: the type enforces that this is a snapshot
    // taken once per frame rather than a query re-executed on every read.
    public static IReadOnlyList<IBattleNpc> Enemies { get; private set; } = [];

    public static void Update()
    {
        Enemies = Svc.Objects.OfType<IBattleNpc>()
            .Where(o => o is
            {
                IsDead: false,
                IsTargetable: true,
            }).Where(o => o.IsHostile())
            .OrderBy(Player.DistanceTo)
            .ToList();
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
        // Was FirstOrDefault(), identical to Closest(), which is plainly wrong for a
        // method called Furthest - Enemies is ordered nearest-first.
        //
        // Correcting an earlier claim of mine: this had NO callers, so it was not
        // causing a live bug. MobFarmer's stacking step does its own
        // OrderBy(DistanceTo).LastOrDefault() and always did.
        return enemies.LastOrDefault();
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
