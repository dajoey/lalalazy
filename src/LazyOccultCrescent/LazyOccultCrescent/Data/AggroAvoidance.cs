using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameHelpers;

namespace LazyOccultCrescent.Data;

// Bends a route around things that will kill you.
//
// vnavmesh solves for geometry: it will happily walk a straight line through the
// middle of a pack because the floor is walkable. It has no aggro concept and no
// avoidance API, and Occult Crescent has no flying, so we cannot route over the
// problem either. What it does expose is Pathfind (hand back the waypoint list)
// and FollowPath (walk a list I give you) - so the fix is to take the geometric
// path, push the waypoints away from anything hostile, and walk the result.
public static class AggroAvoidance
{
    // Synced from PathfinderConfig by PathfinderModule. Chains are built in
    // a dozen places without a module handle, so this is the pragmatic seam.
    public static bool Enabled { get; set; } = true;

    // FFXIV sight aggro is roughly 10-13y for most overworld enemies; field
    // operation elites reach further and hit far harder, so this errs wide.
    private const float DangerRadius = 16f;

    // Clearance to leave beyond the danger radius when stepping around.
    private const float Margin = 4f;

    // Anything this close to where we are going is the objective, not an
    // obstacle. Without this the route refuses to approach its own FATE.
    private const float ObjectiveGrace = 30f;

    // Bound the work: a pathological pack could otherwise generate detours
    // forever and the walk would never start.
    private const int MaxDetours = 6;

    private sealed record Threat(Vector3 Position, float Radius);

    private static List<Threat> Threats(Vector3 destination)
    {
        var player = Svc.Objects.LocalPlayer;
        if (player == null)
        {
            return [];
        }

        var threats = new List<Threat>();

        foreach (var obj in Svc.Objects)
        {
            if (obj is not IBattleNpc npc)
            {
                continue;
            }

            if (npc.SubKind != (byte)BattleNpcSubKind.Combatant || npc.IsDead || !npc.IsTargetable)
            {
                continue;
            }

            // Already fighting us: running away from it is pointless, and the
            // combat handlers own that situation.
            if (npc.TargetObjectId == player.GameObjectId)
            {
                continue;
            }

            if (Vector3.Distance(npc.Position, destination) <= ObjectiveGrace)
            {
                continue;
            }

            threats.Add(new Threat(npc.Position, DangerRadius));
        }

        return threats;
    }

    // Shortest distance from point p to segment ab.
    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        if (lengthSquared < 0.0001f)
        {
            return Vector3.Distance(p, a);
        }

        var t = Math.Clamp(Vector3.Dot(p - a, ab) / lengthSquared, 0f, 1f);
        return Vector3.Distance(p, a + (ab * t));
    }

    // Returns the path to actually walk. Falls back to the input untouched when
    // avoidance is off, there is nothing to avoid, or no clear detour exists -
    // a longer walk is better than refusing to move.
    public static List<Vector3> Apply(dynamic vnav, List<Vector3> path, Vector3 destination, bool enabled)
    {
        if (!enabled || path == null || path.Count < 2)
        {
            return path ?? [];
        }

        var threats = Threats(destination);
        if (threats.Count == 0)
        {
            return path;
        }

        var result = new List<Vector3>(path);

        for (var detours = 0; detours < MaxDetours; detours++)
        {
            var worstIndex = -1;
            Threat? worstThreat = null;
            var worstDistance = float.MaxValue;

            for (var i = 0; i < result.Count - 1; i++)
            {
                foreach (var threat in threats)
                {
                    var d = DistanceToSegment(threat.Position, result[i], result[i + 1]);
                    if (d < threat.Radius && d < worstDistance)
                    {
                        worstDistance = d;
                        worstIndex = i;
                        worstThreat = threat;
                    }
                }
            }

            if (worstIndex < 0 || worstThreat == null)
            {
                break;
            }

            var a = result[worstIndex];
            var b = result[worstIndex + 1];
            var segment = b - a;
            segment.Y = 0;

            if (segment.LengthSquared() < 0.0001f)
            {
                break;
            }

            // Step perpendicular to the segment, on the side away from the threat.
            var forward = Vector3.Normalize(segment);
            var perpendicular = new Vector3(-forward.Z, 0, forward.X);

            var toThreat = worstThreat.Position - a;
            toThreat.Y = 0;
            if (Vector3.Dot(perpendicular, toThreat) > 0)
            {
                perpendicular = -perpendicular;
            }

            var midpoint = (a + b) * 0.5f;
            var candidate = midpoint + (perpendicular * (worstThreat.Radius + Margin));

            Vector3? grounded = null;
            try
            {
                grounded = vnav.FindPointOnFloor(candidate, false, 5f);
            }
            catch
            {
                // Treated as "no reachable detour here".
            }

            if (grounded == null)
            {
                // Cannot step aside at this point; accept the risk on this
                // segment rather than stalling, and stop trying.
                break;
            }

            result.Insert(worstIndex + 1, grounded.Value);
        }

        if (result.Count != path.Count)
        {
            Svc.Log.Debug($"[AggroAvoidance] {threats.Count} threat(s), inserted {result.Count - path.Count} detour waypoint(s)");
        }

        return result;
    }
}
