// Ported from awgil/ffxiv_bossmod BossMod/Pathfinding/NavigationDecision.cs
// (BSD-3-Clause, Copyright (c) 2022-2024 Andrew Gilewsky). See THIRD_PARTY_NOTICES.md.
// PURE: no Dalamud types; compiled into tests/GluttonyCombo.SmartMoverHarness.

#region

using System;
using System.Collections.Generic;
using System.Numerics;

#endregion

namespace GluttonyCombo.AutoRotation.Movement;

/// <summary>
///     Chooses where the character should go next. Priorities, in order:
///     (1) never be inside a telegraph when it resolves - crossing one that
///     resolves after you have left is fine; (2) uptime: stay in the attack band
///     and do not cut casts unless needed; (3) positionals.
/// </summary>
internal struct NavigationDecision
{
    /// <summary> Reusable allocations. </summary>
    internal sealed class Context
    {
        public float[] ScratchG = Array.Empty<float>();
        public bool[] ScratchD = Array.Empty<bool>();
        public NavMap Map = new();
        public ThetaStar ThetaStar = new();
    }

    /// <summary> One forbidden area: signed distance (negative inside) and the absolute second it becomes lethal. </summary>
    internal readonly record struct Forbidden(Func<Vector2, float> Distance, double ActivationSec, ulong Source,
        Vector2 BoundsMin = default, Vector2 BoundsMax = default)
    {
        /// <summary> Whether a bounding box was supplied (corners outside it skip the distance call). </summary>
        public bool HasBounds => BoundsMax != BoundsMin;
    }

    public Vector2? Destination;
    public Vector2? NextWaypoint;
    public float LeewaySeconds;   // how long the chosen path can wait before it stops being safe (float.MaxValue when nothing threatens)
    public float TimeToGoal;
    public bool StartUnsafe;      // the character's own cell activates at some point
    public float StartMaxG;       // seconds the current cell stays safe (float.MaxValue = safe)
    public ThetaStar.Score Score; // score of the chosen cell
    public int Steps;

    /// <summary> Priority penalty for safe pixels bordering danger (over-dodge cushion). </summary>
    public const float CushionDepriority = 0.5f;

    /// <summary> Activations closer than this are clustered together (tiny differences break pathfinding). </summary>
    public const float ClusterSec = 0.5f;

    /// <summary> Activations beyond this horizon are treated as this far away. </summary>
    public const float HorizonSec = 120f;

    /// <param name="activationCushionSec"> seconds subtracted from every activation - be out this long before the bar ends. </param>
    /// <param name="forbiddenZoneCushion"> yalms next to danger that are de-prioritised (over-dodge). </param>
    public static NavigationDecision Build(
        Context ctx,
        double nowSec,
        NavMap map,
        List<Forbidden> zones,
        List<Func<Vector2, float>> goals,
        bool goalsEnabled,
        Vector2 playerPos,
        float playerSpeed,
        float activationCushionSec,
        float forbiddenZoneCushion,
        int maxSteps = int.MaxValue)
    {
        if (zones.Count > 0)
            RasterizeForbiddenZones(map, zones, nowSec, activationCushionSec, ref ctx.ScratchG, ref ctx.ScratchD, forbiddenZoneCushion);

        // safety over uptime: goals only count when the character's own cell is safe
        var startIdx = map.GridToIndex(map.ClampToGrid(map.WorldToGrid(playerPos)).x, map.ClampToGrid(map.WorldToGrid(playerPos)).y);
        var startSafe = map.PixelMaxG[startIdx] == float.MaxValue;
        if (goals.Count > 0 && goalsEnabled && startSafe)
            RasterizeGoalZones(map, goals, forbiddenZoneCushion > 0);
        else if (forbiddenZoneCushion > 0)
            AddCushion(map);

        ctx.ThetaStar.Start(map, playerPos, 1.0f / MathF.Max(0.1f, playerSpeed));
        var bestNodeIndex = ctx.ThetaStar.Execute(maxSteps);
        ref var bestNode = ref ctx.ThetaStar.NodeByIndex(bestNodeIndex);
        var waypoints = GetFirstWaypoints(ctx.ThetaStar, map, bestNodeIndex, playerPos);
        return new NavigationDecision
        {
            Destination = waypoints.first,
            NextWaypoint = waypoints.second,
            LeewaySeconds = bestNode.PathLeeway,
            TimeToGoal = bestNode.GScore,
            StartUnsafe = !startSafe,
            StartMaxG = map.PixelMaxG[startIdx],
            Score = bestNode.Score,
            Steps = ctx.ThetaStar.NumSteps,
        };
    }

    public static float ActivationToG(double activationSec, double nowSec, float cushionSec) =>
        MathF.Max(0f, (float)(activationSec - nowSec) - cushionSec);

    /// <summary>
    ///     Corner-based rasterisation: a pixel's max-g is the minimum over its four
    ///     corners of the earliest zone covering that corner (conservative). Zones
    ///     must be sorted by activation ascending (done here).
    /// </summary>
    public static void RasterizeForbiddenZones(NavMap map, List<Forbidden> zones, double nowSec, float cushionSec, ref float[] gScratch, ref bool[] dScratch, float cushion)
    {
        zones.Sort((a, b) => a.ActivationSec.CompareTo(b.ActivationSec));

        var zonesFixed = new (Func<Vector2, float> d, float g, bool hasBox, Vector2 min, Vector2 max)[zones.Count];
        double clusterEnd = double.MinValue;
        var globalEnd = nowSec + HorizonSec;
        float clusterG = 0;
        for (var i = 0; i < zonesFixed.Length; ++i)
        {
            var activation = Math.Clamp(zones[i].ActivationSec, nowSec, globalEnd);
            if (activation > clusterEnd)
            {
                clusterG = ActivationToG(activation, nowSec, cushionSec);
                clusterEnd = activation + ClusterSec;
            }
            zonesFixed[i] = (zones[i].Distance, clusterG, zones[i].HasBounds, zones[i].BoundsMin, zones[i].BoundsMax);
        }

        map.MaxG = clusterG;
        var cornerCount = (map.Width + 1) * (map.Height + 1);
        if (gScratch.Length < cornerCount)
            gScratch = new float[cornerCount];
        if (dScratch.Length < cornerCount)
            dScratch = new bool[cornerCount];

        // evaluate every grid corner once
        var ci = 0;
        for (var y = 0; y <= map.Height; ++y)
            for (var x = 0; x <= map.Width; ++x, ++ci)
            {
                var (g, d) = CalculateMaxG(zonesFixed, map.CornerToWorld(x, y), cushion);
                gScratch[ci] = g;
                dScratch[ci] = d;
            }

        var numBlocked = 0;
        var stride = map.Width + 1;
        for (var y = 0; y < map.Height; ++y)
            for (var x = 0; x < map.Width; ++x)
            {
                var c0 = y * stride + x;
                var g = MathF.Min(MathF.Min(gScratch[c0], gScratch[c0 + 1]), MathF.Min(gScratch[c0 + stride], gScratch[c0 + stride + 1]));
                var idx = y * map.Width + x;
                var cellG = map.PixelMaxG[idx] = MathF.Min(map.PixelMaxG[idx], g);
                if (cellG != float.MaxValue)
                {
                    map.PixelPriority[idx] = float.MinValue;
                    ++numBlocked;
                }
                else if (dScratch[c0] || dScratch[c0 + 1] || dScratch[c0 + stride] || dScratch[c0 + stride + 1])
                    map.PixelAvoid[idx] = true;
            }

        if (numBlocked == map.Width * map.Height)
        {
            // everything is dangerous: free the least dangerous cells so the search has somewhere to go
            float realMaxG = 0;
            for (var i = 0; i < numBlocked; ++i)
                realMaxG = MathF.Max(realMaxG, map.PixelMaxG[i]);
            for (var i = 0; i < numBlocked; ++i)
                if (map.PixelMaxG[i] == realMaxG)
                {
                    map.PixelMaxG[i] = float.MaxValue;
                    map.PixelPriority[i] = 0;
                }
        }
    }

    private static (float G, bool D) CalculateMaxG((Func<Vector2, float> d, float g, bool hasBox, Vector2 min, Vector2 max)[] zones, Vector2 p, float cushion)
    {
        var g = float.MaxValue;
        var d = false;
        for (var i = 0; i < zones.Length; ++i)
        {
            ref var z = ref zones[i];
            if (z.hasBox && (p.X < z.min.X - cushion || p.X > z.max.X + cushion || p.Y < z.min.Y - cushion || p.Y > z.max.Y + cushion))
                continue;
            var dist = z.d(p);
            if (dist < 0)
            {
                g = zones[i].g; // sorted ascending, first hit is the earliest
                break;
            }
            if (dist < cushion)
                d = true;
        }
        return (g, d);
    }

    /// <summary> Goal priority per pixel = min over its corners of the summed goal functions; blocked pixels stay at float.MinValue. </summary>
    public static void RasterizeGoalZones(NavMap map, List<Func<Vector2, float>> goals, bool cushion)
    {
        var stride = map.Width + 1;
        var cornerCount = stride * (map.Height + 1);
        var corner = new float[cornerCount];
        var ci = 0;
        for (var y = 0; y <= map.Height; ++y)
            for (var x = 0; x <= map.Width; ++x, ++ci)
            {
                var p = map.CornerToWorld(x, y);
                float sum = 0;
                for (var i = 0; i < goals.Count; ++i)
                    sum += goals[i](p);
                corner[ci] = sum;
            }

        map.MaxPriority = 0;
        for (var y = 0; y < map.Height; ++y)
            for (var x = 0; x < map.Width; ++x)
            {
                var idx = y * map.Width + x;
                if (map.PixelMaxG[idx] != float.MaxValue)
                {
                    map.PixelPriority[idx] = float.MinValue;
                    continue;
                }
                var c0 = y * stride + x;
                var prio = MathF.Min(MathF.Min(corner[c0], corner[c0 + 1]), MathF.Min(corner[c0 + stride], corner[c0 + stride + 1]));
                map.PixelPriority[idx] = prio;
                map.MaxPriority = MathF.Max(map.MaxPriority, prio);
            }

        if (cushion)
            AddCushion(map);
    }

    public static void AddCushion(NavMap map)
    {
        var pMax = float.MinValue;
        for (var i = 0; i < map.Width * map.Height; i++)
        {
            if (map.PixelPriority[i] >= 0 && map.PixelAvoid[i])
                map.PixelPriority[i] -= CushionDepriority;
            pMax = MathF.Max(map.PixelPriority[i], pMax);
        }
        map.MaxPriority = pMax;
    }

    public static (Vector2? first, Vector2? second) GetFirstWaypoints(ThetaStar pf, NavMap map, int cell, Vector2 startingPos)
    {
        ref var chosen = ref pf.NodeByIndex(cell);
        if (chosen.GScore == 0 && chosen.PathMinG == float.MaxValue)
            return (null, null); // already safe, nothing better reachable

        var nextCell = cell;
        while (true)
        {
            ref var node = ref pf.NodeByIndex(cell);
            if (pf.NodeByIndex(node.ParentIndex).GScore == 0)
            {
                var destCoord = map.IndexToGrid(cell);
                var playerFrac = map.WorldToGridFrac(startingPos);
                var playerCoord = map.FracToGrid(playerFrac);
                var dest = map.GridToWorld(destCoord.x, destCoord.y,
                    destCoord.x == playerCoord.x ? playerFrac.X - playerCoord.x : 0.5f,
                    destCoord.y == playerCoord.y ? playerFrac.Y - playerCoord.y : 0.5f);
                var next = map.CellCenter(nextCell);
                return (dest, next);
            }
            if (node.ParentIndex == cell)
                return (null, null); // start node
            nextCell = cell;
            cell = node.ParentIndex;
        }
    }
}
