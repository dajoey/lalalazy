#region

using System;
using System.Collections.Generic;
using System.Numerics;

#endregion

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     PURE movement decision engine for SmartMover (fork, t_356159a8, v1.0.4.191).
///     No Dalamud types; compiled into <c>tests/GluttonyCombo.SmartMoverHarness</c>.
/// </summary>
/// <remarks>
///     Input is an immutable <see cref="MoverWorld"/> snapshot; output is one
///     <see cref="MoveDecision"/> per tick. Hysteresis state lives in the
///     caller-owned <see cref="Hysteresis"/> class so the engine stays pure and
///     replayable. Decision order: off/nav -&gt; manual input -&gt; casting hold
///     -&gt; BMR navigating pause -&gt; DODGE -&gt; ENGAGE -&gt; SETTLE.
///     Deliberately NEVER consults "is BossMod AI enabled" - the mover does not
///     stand down just because BMR is installed (the PositionalMover trap).
///     Game rotation convention: FFXIV rotations are radians CCW from +Z/south,
///     i.e. facing dir = (sin(rot), cos(rot)) in an XZ plane mapped to (x= X, y= Z).
/// </remarks>
internal static class SmartMoverCore
{
    /// <summary> Decision cadence. A const, not a config knob (a tunable spam limit is a tunable footgun). </summary>
    internal const int TickMs = 250;

    /// <summary> Minimum travel before a move is worth issuing (anti-jitter). </summary>
    internal const float MinMoveYalms = 1.0f;

    /// <summary> A new destination must differ from the held one by at least this, during the hold. </summary>
    internal const float DestChangeYalms = 1.0f;

    /// <summary> How long a chosen destination is kept once movement started (anti-flap). </summary>
    internal const float DestHoldSeconds = 1.0f;

    /// <summary> Remaining cast time at/below which slidecasting movement is allowed. </summary>
    internal const float SlidecastWindowSec = 0.5f;

    /// <summary> Rings x directions sampled to find a dodge destination. </summary>
    internal const int DodgeRingCount = 24;
    internal const int DodgeDirCount = 16;
    internal const float DodgeRingStep = 1.0f;

    internal enum Decision : byte
    {
        None,
        Stop,
        Move,
    }

    /// <summary> Everything the engine needs for one tick. Positions are world XZ (Vector2 = X, Z). </summary>
    internal readonly record struct MoverWorld(
        Vector2 PlayerPos,
        float DesiredRange,          // attack range band centre, edge-to-edge
        bool PositionalWanted,       // melee positional phase active
        bool PositionalIsRear,       // rear vs flank
        bool TrueNorth,              // positionals disabled by True North
        Vector2 TargetPos,
        float TargetRotation,        // GAME-convention radians (CCW from +Z)
        float TargetHitboxRadius,
        bool TargetEngaged,          // DPS target exists, targetable, alive
        IReadOnlyList<DangerZoneModel.Zone> Zones,
        bool ManualInput,            // WASD / gamepad wishdir non-zero
        bool Casting,                // player is casting
        float CastRemainingSec,      // &gt;0 while casting
        bool BmrNavigating,          // BMR's AI controller is actively steering right now
        bool NavReady,               // vnavmesh ready
        bool Enabled,                // SmartMover toggle
        bool InCombat,
        float DeltaSec,              // time since last tick
        double NowSec);              // absolute seconds clock (hysteresis)

    /// <summary> One movement verdict. Reason is a byte code; see <see cref="ReasonString"/>. </summary>
    internal readonly record struct MoveDecision(Decision Kind, Vector2 Dest, byte Reason);

    /// <summary> Caller-owned state so the engine stays pure. </summary>
    internal sealed class Hysteresis
    {
        public Vector2 LastDest;
        public bool HasLastDest;
        public double HoldUntilSec;
        public bool WasMoving;

        public void Reset()
        {
            LastDest = default;
            HasLastDest = false;
            HoldUntilSec = 0;
            WasMoving = false;
        }
    }

    // Reason codes (bytes for the decision record; strings in ReasonString for telemetry).
    internal const byte ReasonOffCode = 1;
    internal const byte ReasonNavCode = 2;
    internal const byte ReasonManualCode = 3;
    internal const byte ReasonCastCode = 4;
    internal const byte ReasonBmrCode = 5;
    internal const byte ReasonDodgeCode = 6;
    internal const byte ReasonEngageCode = 7;
    internal const byte ReasonSettleCode = 8;

    /// <summary> Chooses the movement for this tick: Move = go to Dest, Stop = stop pathing, None = no command. </summary>
    internal static MoveDecision Decide(MoverWorld w, Hysteresis h)
    {
        if (!w.Enabled || !w.NavReady)
            return StandDown(h, ReasonOffCode);

        if (w.ManualInput)
            return StandDown(h, ReasonManualCode);

        if (w.Casting && w.CastRemainingSec > SlidecastWindowSec)
            return StandDown(h, ReasonCastCode);

        if (w.BmrNavigating)
            return StandDown(h, ReasonBmrCode);

        if (!w.InCombat)
            return StandDown(h, ReasonOffCode);

        // ---- DODGE ----
        if (UnsafeAt(w.PlayerPos, w.Zones, 0f) is not null)
        {
            var dest = FindSafePoint(w.PlayerPos, w.Zones);
            if (dest is { } d)
                return Commit(w, h, d, ReasonDodgeCode, overrideHold: true);
            return None(); // no sampled safe point - hold rather than walk blind
        }

        // ---- ENGAGE / SETTLE ----
        if (!w.TargetEngaged)
            return StandDown(h, ReasonSettleCode);

        if (PositionOk(w.PlayerPos, w, out var ideal))
            return StandDown(h, ReasonSettleCode);

        var finalDest = ideal;
        if (UnsafeAt(ideal, w.Zones, 0.5f) is not null)
        {
            var alt = FindSafeRingPoint(ideal, w.TargetPos, w.Zones);
            if (alt is { } a)
                finalDest = a;
            else
                return None(); // whole ring unsafe - hold
        }

        return Commit(w, h, finalDest, ReasonEngageCode, overrideHold: false);
    }

    private static MoveDecision StandDown(Hysteresis h, byte reason)
    {
        var was = h.HasLastDest;
        h.Reset();
        return was ? new MoveDecision(Decision.Stop, default, reason) : None();
    }

    private static MoveDecision Commit(MoverWorld w, Hysteresis h, Vector2 dest, byte reason, bool overrideHold)
    {
        var travel = Vector2.Distance(w.PlayerPos, dest);
        if (!overrideHold && travel < MinMoveYalms)
            return StandDown(h, reason);

        if (!overrideHold && h.HasLastDest && w.NowSec < h.HoldUntilSec)
        {
            // Keep the held destination unless the new one is materially different.
            if (Vector2.Distance(h.LastDest, dest) < DestChangeYalms)
                return new MoveDecision(Decision.Move, h.LastDest, reason);
            return new MoveDecision(Decision.Move, h.LastDest, reason);
        }

        h.LastDest = dest;
        h.HasLastDest = true;
        h.HoldUntilSec = w.NowSec + DestHoldSeconds;
        h.WasMoving = true;
        return new MoveDecision(Decision.Move, dest, reason);
    }

    /// <summary> Whether the player's position is inside any live zone dilated by <paramref name="buffer"/>. </summary>
    internal static DangerZoneModel.Zone? UnsafeAt(Vector2 p, IReadOnlyList<DangerZoneModel.Zone> zones, float buffer)
    {
        for (var i = 0; i < zones.Count; i++)
            if (DangerZoneModel.Contains(zones[i], p, buffer))
                return zones[i];
        return null;
    }

    /// <summary>
    ///     Whether the player already stands in the desired range band and (if
    ///     wanted) the correct positional. Also outputs the ideal standing point.
    /// </summary>
    internal static bool PositionOk(Vector2 playerPos, MoverWorld w, out Vector2 ideal)
    {
        var toward = playerPos - w.TargetPos;
        var dist = toward.Length();
        var ringR = w.TargetHitboxRadius + w.DesiredRange;

        var curAngle = dist < 0.01f ? 0f : MathF.Atan2(toward.Y, toward.X);
        var idealAngle = IdealAngle(w, curAngle);
        ideal = w.TargetPos + new Vector2(MathF.Cos(idealAngle), MathF.Sin(idealAngle)) * (ringR + 0.5f);

        var inRange = dist <= ringR + RangeTolerance(w) && dist >= ringR * 0.5f;
        var posOk = !w.PositionalWanted || w.TrueNorth || AtPositional(playerPos, w);
        return inRange && posOk;
    }

    private static float RangeTolerance(MoverWorld w) => w.PositionalWanted ? 1.0f : 2.0f;

    /// <summary> Game-convention positional check: facing dir = (sin rot, cos rot). </summary>
    internal static bool AtPositional(Vector2 playerPos, MoverWorld w)
    {
        var rel = playerPos - w.TargetPos;
        if (rel.LengthSquared() < 0.0001f)
            return false;
        var facing = new Vector2(MathF.Sin(w.TargetRotation), MathF.Cos(w.TargetRotation));
        var cos = Vector2.Dot(facing, Vector2.Normalize(rel));
        return w.PositionalIsRear ? cos < -0.5f : MathF.Abs(cos) < 0.5f;
    }

    /// <summary> The math-convention approach angle satisfying the positional want. </summary>
    private static float IdealAngle(MoverWorld w, float currentAngle)
    {
        if (!w.PositionalWanted || w.TrueNorth)
            return currentAngle;

        var facing = w.TargetRotation; // game convention
        // rear = opposite the facing dir; flanks = perpendicular
        var rearDir = new Vector2(MathF.Sin(facing + MathF.PI), MathF.Cos(facing + MathF.PI));
        var rear = MathF.Atan2(rearDir.Y, rearDir.X);
        var flankA = rear + MathF.PI / 2f;
        var flankB = rear - MathF.PI / 2f;

        if (w.PositionalIsRear)
            return rear;

        return MathF.Abs(AngleDiff(currentAngle, flankA)) <= MathF.Abs(AngleDiff(currentAngle, flankB)) ? flankA : flankB;
    }

    /// <summary>
    ///     Samples rings around the player for the nearest point outside every
    ///     live zone. Zones that already contain the player are being ESCAPED -
    ///     the exit segment inevitably crosses them, so only OTHER zones block
    ///     the route (otherwise a player inside a zone could never find a dodge).
    /// </summary>
    internal static Vector2? FindSafePoint(Vector2 playerPos, IReadOnlyList<DangerZoneModel.Zone> zones)
    {
        List<DangerZoneModel.Zone>? others = null;
        foreach (var z in zones)
        {
            if (DangerZoneModel.Contains(z, playerPos, 0f))
                continue;
            (others ??= new List<DangerZoneModel.Zone>(zones.Count)).Add(z);
        }

        for (var ring = 1; ring <= DodgeRingCount; ring++)
        {
            var r = ring * DodgeRingStep;
            Vector2? best = null;
            var bestDist = float.MaxValue;
            for (var i = 0; i < DodgeDirCount; i++)
            {
                var ang = i * (2f * MathF.PI / DodgeDirCount);
                var p = playerPos + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r;
                if (UnsafeAt(p, zones, 0.25f) is null && (others is null || !SegmentBlocked(p, playerPos, others)))
                {
                    var d = Vector2.Distance(playerPos, p);
                    if (d < bestDist) { bestDist = d; best = p; }
                }
            }
            if (best is { } found)
                return found;
        }
        return null;
    }

    /// <summary> Samples the target ring for a safe variant when the ideal point is covered. </summary>
    internal static Vector2? FindSafeRingPoint(Vector2 ideal, Vector2 targetPos, IReadOnlyList<DangerZoneModel.Zone> zones)
    {
        var ringR = Vector2.Distance(ideal, targetPos);
        var idealAngle = MathF.Atan2(ideal.Y - targetPos.Y, ideal.X - targetPos.X);
        var bestAngle = 0f;
        var bestScore = float.MaxValue;
        var found = false;
        for (var i = 0; i < 24; i++)
        {
            var ang = i * (2f * MathF.PI / 24);
            var p = targetPos + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * ringR;
            if (UnsafeAt(p, zones, 0.5f) is not null)
                continue;
            var score = MathF.Abs(AngleDiff(ang, idealAngle));
            if (score < bestScore) { bestScore = score; bestAngle = ang; found = true; }
        }
        return found ? targetPos + new Vector2(MathF.Cos(bestAngle), MathF.Sin(bestAngle)) * ringR : null;
    }

    private static bool SegmentBlocked(Vector2 p, Vector2 from, IReadOnlyList<DangerZoneModel.Zone> zones)
    {
        var len = Vector2.Distance(from, p);
        var steps = Math.Max(1, (int)(len / 0.5f));
        for (var s = 1; s < steps; s++)
        {
            var q = from + (p - from) * (s / (float)steps);
            if (UnsafeAt(q, zones, 0f) is not null)
                return true;
        }
        return false;
    }

    private static float AngleDiff(float a, float b)
    {
        var d = a - b;
        while (d > MathF.PI) d -= 2f * MathF.PI;
        while (d < -MathF.PI) d += 2f * MathF.PI;
        return d;
    }

    private static MoveDecision None() => new(Decision.None, default, 0);
}
