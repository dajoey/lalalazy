using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using GluttonyCombo.Core;
using GluttonyCombo.Services;
using Lalalazy.Telemetry;

namespace GluttonyCombo.Data;

/// <summary>
///     The shadow move planner (fork, 2026-11-05; part 2 of the movement tap). For each
///     casting hostile that lands within the horizon it builds a few checked options a few
///     seconds ahead - when to leave, which spot (near the group, least travel), how long to
///     stay (v1: one arrival decision per cast) - carries each option's consequence, and
///     picks one by a plain code rule.
///     It logs ONE <c>MP|</c> line per decision: the options and the code pick.
///     What BMR actually did and what Joey actually did are not logged here - the 10 Hz
///     recorder captures both, and the grader joins them.
/// </summary>
/// <remarks>
///     <b>Log only.</b> This class never presses a button, never writes a BMR preset or transient,
///     never calls pathfinding, and never moves the character - its only outputs are lines.
///     Executing the motion (curved path, natural start and stop, timing drawn from
///     the human profile) is a later build behind the Fight Brain fail-safes, gated on these shadow
///     results. When stock BMR is present its <c>Hints.IsDashSafe</c> endpoint checks each spot;
///     when it is absent the options say so (<c>c0</c>) and the code rule downgrades to
///     "shortest walk, prefer stay".
/// </remarks>
internal static class MovementPlanner
{
    private const long TickIntervalMs = 1000;
    private const float HorizonS = 6f;          // casts landing inside this window are planned
    private const float ArrivalCushionS = 0.8f; // a move must arrive this long before the hit
    private const float RunSpeedYalms = 6f;     // FFXIV run speed, for travel-time estimates
    private const float GcdSeconds = 2.5f;
    private const float StayNearCasterYalms = 6f;  // inside this radius a cast is "his problem"
    private const float StepOutYalms = 7f;         // candidate spots sit this far from the current spot

    private static readonly object Lock = new();
    private static long _lastTick;

    private sealed class CastState
    {
        public int Decisions;
        public string LastSignature = "";
    }

    /// <summary> Per (source, action) decision bookkeeping, cleared when combat ends. </summary>
    private static readonly Dictionary<(ulong Source, uint Action), CastState> Casts = new();

    public static void Tick(long nowMs, uint pullId, uint zoneId, List<MovementRecorder.Hazard> hazards)
    {
        try
        {
            if (!Service.Configuration.MovementPlannerShadow || Player.Object is null || Svc.ClientState.IsPvP)
                return;
            if (nowMs - _lastTick < TickIntervalMs)
                return;
            _lastTick = nowMs;

            var player = Player.Object;
            var playerPos = player.Position;

            // Combat end clears the bookkeeping (recorder flips _wasInCombat before this is reached,
            // and hazards are empty between pulls).
            if (hazards.Count == 0)
            {
                lock (Lock)
                    Casts.Clear();
                return;
            }

            var partyMean = PartyMean(playerPos);

            foreach (var h in hazards)
            {
                if (h.RemainS is <= 0f or > HorizonS)
                    continue;
                if (!Vector2Dist(playerPos, h.Pos, out var distFromCaster))
                    continue;

                // Far from the cast's anchor and not the target: not his problem this tick.
                var aimedAtPlayer = h.CastTargetId == player.GameObjectId;
                if (!aimedAtPlayer && distFromCaster > StayNearCasterYalms + StepOutYalms)
                    continue;

                var castKey = (h.SourceId, h.CastActionId);
                CastState state;
                lock (Lock)
                {
                    if (!Casts.TryGetValue(castKey, out state!))
                        Casts[castKey] = state = new CastState();
                    if (state.Decisions >= MovementFormat.MaxDecisionsPerCast)
                        continue;
                }

                var options = BuildOptions(playerPos, h.Pos, partyMean, distFromCaster);
                var signature = Signature(options);
                bool changed;
                lock (Lock)
                    changed = signature != state.LastSignature;
                if (!changed && state.Decisions > 0)
                    continue;

                var codePick = CodePick(options, h.RemainS, aimedAtPlayer, out var codeWhy);
                var decisionMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                state.Decisions++;
                state.LastSignature = signature;

                Emit(decisionMs, pullId, zoneId, h, options, codePick, codeWhy);
            }

            // Drop bookkeeping for casts that ended (the dictionary stays tiny either way).
            lock (Lock)
            {
                if (Casts.Count > 64)
                    Casts.Clear();
            }
        }
        catch (Exception ex)
        {
            LalaTelemetry.Swallowed("movement.planner.tick", ex);
        }
    }

    private static List<MovementFormat.MoveOption> BuildOptions(
        Vector3 playerPos, Vector3 casterPos, Vector3 partyMean, float distFromCaster)
    {
        var options = new List<MovementFormat.MoveOption>(MovementFormat.MaxOptions) { Stay(playerPos, partyMean) };

        // Two perpendicular side-steps and a straight step-out, all in the ground plane (y is height in FFXIV).
        var toCaster = new Vector3(casterPos.X - playerPos.X, 0f, casterPos.Z - playerPos.Z);
        if (toCaster.LengthSquared() < 0.01f)
            toCaster = new Vector3(0f, 0f, 1f);
        toCaster /= toCaster.Length();
        var left = new Vector3(-toCaster.Z, 0f, toCaster.X);

        Span<Vector3> dirs =
        [
            left * StepOutYalms,
            -left * StepOutYalms,
            -toCaster * StepOutYalms, // straight away from the caster
        ];

        var id = 1;
        foreach (var d in dirs)
        {
            if (id >= MovementFormat.MaxOptions)
                break;
            var spot = playerPos + d;
            var travel = StepOutYalms / RunSpeedYalms;
            var partyDist = Vector3.Distance(spot, partyMean);
            options.Add(new MovementFormat.MoveOption(
                id, spot.X, spot.Z, 0f, travel, travel / GcdSeconds, partyDist, IsBmrSafe(playerPos, spot)));
            id++;
        }

        return options;
    }

    private static MovementFormat.MoveOption Stay(Vector3 playerPos, Vector3 partyMean) =>
        new(0, playerPos.X, playerPos.Z, 0f, 0f, 0f, Vector3.Distance(playerPos, partyMean), BmrChecked: false);

    private static bool IsBmrSafe(Vector3 from, Vector3 to)
    {
        try
        {
            return MovementRecorderBmrSafe(from, to) ?? false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary> The stock BMR safety check, or null when BMR is absent (the option ships unchecked). </summary>
    private static bool? MovementRecorderBmrSafe(Vector3 from, Vector3 to)
    {
        var check = Conflicts.ConflictingPluginsChecks.BossModReborn;
        return check.IsDashSafe(from, to);
    }

    /// <summary>
    ///     The code rule: prefer stay; move only to a checked spot with real arrival margin
    ///     (arrives at least <see cref="ArrivalCushionS"/> before the hit), then the cheapest
    ///     GCDs, then nearest the party.
    /// </summary>
    private static int CodePick(List<MovementFormat.MoveOption> options, float hitInS, bool aimedAtPlayer, out string why)
    {
        var best = -1;
        var bestGcd = float.MaxValue;
        var bestParty = float.MaxValue;
        for (var i = 1; i < options.Count; i++)
        {
            var o = options[i];
            if (!o.BmrChecked)
                continue;
            if (hitInS - o.ArriveInS < ArrivalCushionS)
                continue;
            if (o.GcdsLost < bestGcd || (o.GcdsLost == bestGcd && o.PartyDist < bestParty))
            {
                best = o.Id;
                bestGcd = o.GcdsLost;
                bestParty = o.PartyDist;
            }
        }

        if (best > 0)
        {
            why = "checked-spot";
            return best;
        }

        why = options.Count > 1 ? (aimedAtPlayer ? "no-checked-spot" : "far-stay") : "stay";
        return 0;
    }

    private static string Signature(List<MovementFormat.MoveOption> options)
    {
        var sb = new StringBuilder(64);
        foreach (var o in options)
            sb.Append(o.Id).Append(o.BmrChecked ? '1' : '0').Append(';');
        return sb.ToString();
    }

    private static Vector3 PartyMean(Vector3 playerPos)
    {
        var sum = Vector3.Zero;
        var n = 0;
        foreach (var member in Svc.Party)
        {
            var obj = member.GameObject;
            if (obj is null)
                continue;
            sum += obj.Position;
            n++;
        }

        return n == 0 ? playerPos : sum / n;
    }

    private static bool Vector2Dist(Vector3 a, Vector3 b, out float dist)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        dist = MathF.Sqrt(dx * dx + dz * dz);
        return true;
    }

    // ------------------------------------------------------------- emit (log + ingest)

    private static void Emit(
        long decisionMs, uint pullId, uint zoneId, MovementRecorder.Hazard h,
        List<MovementFormat.MoveOption> options, int codePick, string codeWhy)
    {
        try
        {
            // The MP| wire format keeps its second pick slot for schema stability (the
            // movement database's decision table parses it); the decision layer that used
            // to fill it was removed, so it now always carries "-"/"off", the same values
            // it carried whenever the ask side was switched off before.
            var line = MovementFormat.BuildDecisionLine(
                decisionMs, pullId, zoneId, h.CastActionId, h.RemainS,
                options, codePick, jevPick: -1, codeWhy, jevWhy: "off");
            Svc.Log.Information(line);
            MovementUploader.Enqueue(line);
        }
        catch (Exception ex)
        {
            LalaTelemetry.Swallowed("movement.planner.emit", ex);
        }
    }
}
