#region

using System;
using System.Collections.Generic;
using System.Numerics;
using GluttonyCombo.AutoRotation.Movement;

#endregion

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     PURE movement decision engine for Smart Movement v2 (2026-09 rebuild).
///     No Dalamud types; compiled into <c>tests/GluttonyCombo.SmartMoverHarness</c>.
/// </summary>
/// <remarks>
///     v2 replaces the v1 ring sampler with a time-aware planner: every live
///     telegraph is rasterised into a grid as "seconds until this cell is
///     lethal", goals (attack band, positional) as priorities, and a Theta*
///     search in seconds picks the next waypoint. The output is a STEERING
///     direction the Dalamud half writes into the game's movement input every
///     frame (no vnavmesh for dodges); vnavmesh is only used for a long
///     approach to a target outside the planning grid.
///     Decision order: off -&gt; mounted/knockback -&gt; manual input -&gt; external
///     mover (vnavmesh path we did not issue, BossMod AI) -&gt; out of combat -&gt;
///     plan -&gt; cast rule -&gt; steer / long approach / hold.
///     Game rotation convention: facing dir = (sin r, cos r) over (X, Z); the
///     zone and planner math uses atan2(z, x) angles (see DangerZoneModel).
/// </remarks>
internal static class SmartMoverCore
{
    /// <summary> Planning cadence (ms). Steering between plans keeps the last waypoint. </summary>
    internal const int TickMs = 100;

    /// <summary> Grid resolution (yalms). </summary>
    internal const float MapResolution = 0.5f;

    /// <summary> Half-width of the planning square (yalms). </summary>
    internal const float MapHalfWidth = 30f;

    /// <summary> Map centre is the player position rounded to this, re-centred past <see cref="MapRecenterYalms"/>. </summary>
    internal const float MapQuantum = 5f;
    internal const float MapRecenterYalms = 2.5f;

    /// <summary> Targets farther than this from the player are approached with vnavmesh, not the grid. </summary>
    internal const float LongApproachYalms = 24f;

    /// <summary> Default seconds subtracted from every activation (BossMod release default 1.0). Config overrides. </summary>
    internal const float DefaultActivationCushionSec = 1.0f;

    /// <summary> Yalms next to danger that are de-prioritised so the character over-dodges slightly. </summary>
    internal const float OverdodgeCushionYalms = 0.5f;

    /// <summary> Remaining cast time at/below which movement no longer cancels the cast (slidecast). </summary>
    internal const float SlidecastWindowSec = 0.5f;

    /// <summary> Run speed assumptions (yalms/s). </summary>
    internal const float RunSpeed = 6.0f;
    internal const float SprintSpeed = 7.8f;

    /// <summary> A waypoint closer than this is considered reached. </summary>
    internal const float ArriveYalms = 0.15f;

    /// <summary> Bound on search steps per plan (a 120x120 grid has 14400 cells). </summary>
    internal const int MaxPlanSteps = 40000;

    /// <summary> Bands at or under this depth hug the target (melee/tank 3, SGE 5). </summary>
    internal const float ShortRangeYalms = 5f;

    /// <summary>
    ///     v2 r2: an escape the character cannot possibly reach before the cell
    ///     it stands in resolves is not run. Allowance (yalms) added to the
    ///     reachable distance before an escape counts as hopeless.
    /// </summary>
    internal const float HopelessAllowanceYalms = 3f;

    /// <summary> Coarse floor-mask cell (yalms); one vnavmesh probe per cell per map placement. </summary>
    internal const float WalkCellYalms = 2f;

    /// <summary> Floor mask is rebuilt after this many seconds even if the map did not move. </summary>
    internal const float WalkMaskMaxAgeSec = 10f;

    /// <summary> A mask with more than this fraction blocked is a broken probe, not a wall: ignored (fail open). </summary>
    internal const float WalkMaskMaxBlockedFraction = 0.6f;

    internal enum Decision : byte
    {
        None,   // no command; keep whatever state (steering cleared by the caller when Reason says so)
        Stop,   // stop steering and any own vnavmesh path
        Steer,  // walk straight toward Dest (input override)
        NavTo,  // long approach: ask vnavmesh to path to Dest once
    }

    /// <summary> Everything the engine needs for one tick. Positions are world XZ (Vector2 = X, Z). </summary>
    internal readonly record struct MoverWorld(
        Vector2 PlayerPos,
        float DesiredRange,          // attack band, edge-to-edge
        bool PositionalWanted,       // melee positional phase active
        bool PositionalIsRear,       // rear vs flank
        bool TrueNorth,
        Vector2 TargetPos,
        float TargetRotation,        // GAME-convention radians
        float TargetHitboxRadius,
        bool TargetEngaged,          // DPS target exists, targetable, alive
        IReadOnlyList<DangerZoneModel.Zone> Zones,
        bool ManualInput,            // WASD / gamepad wishdir non-zero this frame
        bool Casting,
        float CastRemainingSec,
        bool ExternalMover,          // vnavmesh path we did not issue, or BossMod AI steering
        bool NavReady,               // vnavmesh available (long approach only)
        bool Enabled,
        bool InCombat,
        double NowSec,               // absolute clock (same as Zone.ActivationSec)
        float Speed = RunSpeed,
        bool Mounted = false,
        bool KnockbackPending = false,
        float ActivationCushionSec = DefaultActivationCushionSec,
        Func<Vector2, bool>? IsPointWalkable = null, // optional vnavmesh floor probe for the coarse floor mask
        bool SteeringUnavailable = false);           // the input hook could not be armed: never plan, report it

    /// <summary> One movement verdict. </summary>
    internal readonly record struct MoveDecision(
        Decision Kind,
        Vector2 Dest,
        byte Reason,
        float LeewaySec,   // how long the chosen path can wait; float.MaxValue when nothing threatens
        bool StartUnsafe,  // the character's cell activates at some point
        float StartMaxG)   // seconds the current cell stays safe
    {
        /// <summary> Longest cast the rotation may start now (exported as MaxCastTime). </summary>
        public float MaxCastTime => LeewaySec;
    }

    /// <summary> Caller-owned state so the engine stays pure. </summary>
    internal sealed class MoverState
    {
        public readonly NavigationDecision.Context Ctx = new();
        public Vector2 MapCenter;
        public bool HasMapCenter;
        public Vector2? Waypoint;     // current steering target
        public Vector2? NextWaypoint;
        public bool OwnNavActive;     // we issued a vnavmesh long approach that may still be running
        public Vector2 OwnNavDest;
        public byte LastReason;
        public int LastPlanSteps;
        public float LastPlanMs;
        public bool[]? WalkMask;      // coarse floor mask over the map (WalkCellYalms blocks), null = unknown
        public Vector2 WalkMaskCenter;
        public double WalkMaskBuiltSec = double.MinValue;
        public int WalkMaskBlocked;

        public void Reset()
        {
            Waypoint = null;
            NextWaypoint = null;
            OwnNavActive = false;
            OwnNavDest = default;
            LastReason = 0;
            HasMapCenter = false;
            WalkMask = null;
            WalkMaskBuiltSec = double.MinValue;
        }
    }

    // Reason codes (bytes for the decision record; strings via ReasonString).
    internal const byte ReasonOffCode = 1;
    internal const byte ReasonNavCode = 2;      // long approach wanted, vnavmesh not ready
    internal const byte ReasonManualCode = 3;
    internal const byte ReasonCastCode = 4;     // finishing a cast; the path can wait
    internal const byte ReasonExternalCode = 5; // someone else is steering (vnavmesh path, BossMod AI)
    internal const byte ReasonDodgeCode = 6;    // steering because a telegraph threatens the path
    internal const byte ReasonEngageCode = 7;   // steering toward the attack band / positional
    internal const byte ReasonSettleCode = 8;   // in place, nothing to do, no zones
    internal const byte ReasonOocCode = 9;
    internal const byte ReasonHoldCode = 10;    // in place, zones live but the cell is safe
    internal const byte ReasonStuckCode = 11;   // cell unsafe and no better cell reachable
    internal const byte ReasonEscapeCode = 12;  // dodge with no leeway left (late)
    internal const byte ReasonMountedCode = 14;
    internal const byte ReasonKnockbackCode = 15;
    internal const byte ReasonApproachCode = 16; // vnavmesh long approach issued / running
    internal const byte ReasonHookCode = 17;     // steering hook unavailable (signatures missing) - nothing can move

    internal static string ReasonString(byte code) => code switch
    {
        ReasonOffCode => "off",
        ReasonNavCode => "nav",
        ReasonManualCode => "man",
        ReasonCastCode => "cast",
        ReasonExternalCode => "ext",
        ReasonDodgeCode => "ddg",
        ReasonEngageCode => "eng",
        ReasonSettleCode => "stl",
        ReasonOocCode => "ooc",
        ReasonHoldCode => "hold",
        ReasonStuckCode => "stuck",
        ReasonEscapeCode => "esc",
        ReasonMountedCode => "mnt",
        ReasonKnockbackCode => "kb",
        ReasonApproachCode => "appr",
        ReasonHookCode => "hook",
        _ => "hold",
    };

    /// <summary> Chooses the movement for this tick. </summary>
    internal static MoveDecision Decide(MoverWorld w, MoverState s)
    {
        var d = DecideInner(w, s);
        s.LastReason = d.Reason;
        return d;
    }

    private static MoveDecision DecideInner(MoverWorld w, MoverState s)
    {
        if (!w.Enabled)
            return StandDown(s, ReasonOffCode);
        if (w.SteeringUnavailable)
            return StandDown(s, ReasonHookCode);
        if (w.Mounted)
            return StandDown(s, ReasonMountedCode);
        if (w.KnockbackPending)
            return Hold(s, ReasonKnockbackCode); // positions snapshot at resolve: do not re-plan mid-knockback
        if (w.ManualInput)
            return StandDown(s, ReasonManualCode);
        if (w.ExternalMover)
            return StandDown(s, ReasonExternalCode);
        if (!w.InCombat)
            return StandDown(s, ReasonOocCode);

        // ---- map placement (quantised, with hysteresis) ----
        if (!s.HasMapCenter || Vector2.Distance(w.PlayerPos, s.MapCenter) > MapRecenterYalms + MapQuantum)
        {
            s.MapCenter = new Vector2(MathF.Round(w.PlayerPos.X / MapQuantum) * MapQuantum, MathF.Round(w.PlayerPos.Y / MapQuantum) * MapQuantum);
            s.HasMapCenter = true;
        }
        else if (Vector2.Distance(w.PlayerPos, s.MapCenter) > MapRecenterYalms)
        {
            s.MapCenter = new Vector2(MathF.Round(w.PlayerPos.X / MapQuantum) * MapQuantum, MathF.Round(w.PlayerPos.Y / MapQuantum) * MapQuantum);
        }

        // ---- forbidden zones ----
        var forbidden = new List<NavigationDecision.Forbidden>(w.Zones.Count);
        for (var i = 0; i < w.Zones.Count; i++)
        {
            var z = w.Zones[i];
            var activation = z.ActivationSec > 0 ? z.ActivationSec : w.NowSec + z.RemainingSec;
            var (bmin, bmax) = DangerZoneModel.Bounds(in z);
            forbidden.Add(new NavigationDecision.Forbidden(DangerZoneModel.Sdf(in z), activation, z.Source, bmin, bmax));
        }

        // ---- goals ----
        var goals = new List<Func<Vector2, float>>(1);
        var targetInMap = w.TargetEngaged && Vector2.Distance(w.PlayerPos, w.TargetPos) <= LongApproachYalms;
        if (targetInMap)
            goals.Add(GoalAttackBand(w));

        // ---- floor mask: the map IS the obstacle map (one probe per 2x2 block per placement) ----
        var map = s.Ctx.Map;
        map.Init(MapResolution, s.MapCenter, MapHalfWidth, MapHalfWidth);
        if (w.IsPointWalkable is not null)
        {
            var blocks = (int)MathF.Ceiling(2 * MapHalfWidth / WalkCellYalms);
            if (s.WalkMask is null || s.WalkMaskCenter != s.MapCenter || w.NowSec - s.WalkMaskBuiltSec > WalkMaskMaxAgeSec)
            {
                var mask = s.WalkMask is { } m && m.Length == blocks * blocks ? m : new bool[blocks * blocks];
                var blocked = 0;
                for (var by = 0; by < blocks; by++)
                    for (var bx = 0; bx < blocks; bx++)
                    {
                        var c = s.MapCenter + new Vector2((bx + 0.5f) * WalkCellYalms - MapHalfWidth, (by + 0.5f) * WalkCellYalms - MapHalfWidth);
                        var ok = w.IsPointWalkable(c);
                        mask[by * blocks + bx] = ok;
                        if (!ok) blocked++;
                    }
                s.WalkMask = mask;
                s.WalkMaskCenter = s.MapCenter;
                s.WalkMaskBuiltSec = w.NowSec;
                s.WalkMaskBlocked = blocked;
            }
            if (s.WalkMask is { } wm && s.WalkMaskBlocked > 0 && s.WalkMaskBlocked < blocks * blocks * WalkMaskMaxBlockedFraction)
            {
                var per = (int)MathF.Round(WalkCellYalms / MapResolution);
                var playerCell = map.ClampToGrid(map.WorldToGrid(w.PlayerPos));
                for (var by = 0; by < blocks; by++)
                    for (var bx = 0; bx < blocks; bx++)
                    {
                        if (wm[by * blocks + bx]) continue;
                        for (var y = by * per; y < (by + 1) * per && y < map.Height; y++)
                            for (var x = bx * per; x < (bx + 1) * per && x < map.Width; x++)
                                if (x != playerCell.x || y != playerCell.y) // never block the cell the character stands in
                                    map.BlockPixel(map.GridToIndex(x, y));
                    }
            }
        }

        // ---- plan ----
        var nav = NavigationDecision.Build(s.Ctx, w.NowSec, map, forbidden, goals, goals.Count > 0, w.PlayerPos, w.Speed,
            w.ActivationCushionSec, OverdodgeCushionYalms, MaxPlanSteps);
        s.LastPlanSteps = nav.Steps;

        var leeway = forbidden.Count == 0 ? float.MaxValue : nav.LeewaySeconds;

        // ---- long approach (target beyond the grid), only while nothing threatens ----
        if (w.TargetEngaged && !targetInMap && !nav.StartUnsafe && nav.Destination is null)
        {
            if (!w.NavReady)
                return Hold(s, ReasonNavCode, leeway, nav);
            var ring = w.TargetHitboxRadius + w.DesiredRange;
            var toPlayer = w.PlayerPos - w.TargetPos;
            var dir = toPlayer.LengthSquared() < 0.01f ? new Vector2(1, 0) : Vector2.Normalize(toPlayer);
            var dest = w.TargetPos + dir * ring;
            s.Waypoint = null;
            if (s.OwnNavActive && Vector2.Distance(s.OwnNavDest, dest) < 3f)
                return new MoveDecision(Decision.None, s.OwnNavDest, ReasonApproachCode, leeway, false, nav.StartMaxG);
            s.OwnNavActive = true;
            s.OwnNavDest = dest;
            return new MoveDecision(Decision.NavTo, dest, ReasonApproachCode, leeway, false, nav.StartMaxG);
        }

        // ---- nothing better to reach ----
        if (nav.Destination is null)
        {
            s.Waypoint = null;
            if (nav.StartUnsafe)
                return new MoveDecision(StopIfNav(s), default, ReasonStuckCode, leeway, true, nav.StartMaxG);
            return new MoveDecision(StopIfNav(s), default, forbidden.Count > 0 ? ReasonHoldCode : ReasonSettleCode, leeway, false, nav.StartMaxG);
        }

        var target = nav.Destination.Value;
        var dodging = forbidden.Count > 0 && (nav.StartUnsafe || leeway < float.MaxValue);

        // v2 r2: standing in a zone whose exit is beyond reach is a hit either way;
        // running 25 y toward the arena edge for it only makes things worse
        // (Abductor's Buffet before it was filtered; Tendon Ripper stars from the
        // centre). Hold, log "stuck", and let the rotation keep casting.
        if (nav.StartUnsafe && leeway <= 0f)
        {
            var reachable = w.Speed * MathF.Max(0f, nav.StartMaxG) + HopelessAllowanceYalms;
            if (Vector2.Distance(w.PlayerPos, target) > reachable)
            {
                s.Waypoint = null;
                return new MoveDecision(StopIfNav(s), default, ReasonStuckCode, leeway, true, nav.StartMaxG);
            }
        }

        // ---- cast rule: finish the cast when the path can wait for it ----
        if (w.Casting && w.CastRemainingSec > SlidecastWindowSec && leeway > w.CastRemainingSec - SlidecastWindowSec)
        {
            s.Waypoint = null; // do not steer (movement would cancel the cast); re-evaluated next tick
            return new MoveDecision(StopIfNav(s), target, ReasonCastCode, leeway, nav.StartUnsafe, nav.StartMaxG);
        }

        // ---- steer ----
        if (s.OwnNavActive)
            s.OwnNavActive = false; // own long approach is superseded by grid steering; the caller stops the path
        s.Waypoint = target;
        s.NextWaypoint = nav.NextWaypoint;
        var reason = !dodging ? ReasonEngageCode : leeway <= 0f ? ReasonEscapeCode : ReasonDodgeCode;
        return new MoveDecision(Decision.Steer, target, reason, leeway, nav.StartUnsafe, nav.StartMaxG);
    }

    private static Decision StopIfNav(MoverState s)
    {
        if (!s.OwnNavActive)
            return Decision.None;
        s.OwnNavActive = false;
        return Decision.Stop;
    }

    private static MoveDecision StandDown(MoverState s, byte reason)
    {
        var hadNav = s.OwnNavActive;
        var hadWp = s.Waypoint is not null;
        s.Waypoint = null;
        s.NextWaypoint = null;
        s.OwnNavActive = false;
        return new MoveDecision(hadNav || hadWp ? Decision.Stop : Decision.None, default, reason, float.MaxValue, false, float.MaxValue);
    }

    private static MoveDecision Hold(MoverState s, byte reason, float leeway = float.MaxValue, NavigationDecision nav = default)
    {
        s.Waypoint = null;
        return new MoveDecision(Decision.None, default, reason, leeway, nav.StartUnsafe, nav.StartMaxG == 0 && !nav.StartUnsafe ? float.MaxValue : nav.StartMaxG);
    }

    /// <summary>
    ///     Goal: 1 inside the attack band (hitbox + range + 0.5, edge-to-edge as
    ///     the game measures it), 2 if additionally at the wanted positional.
    ///     Ported from BossMod AIHints.GoalSingleTarget.
    /// </summary>
    internal static Func<Vector2, float> GoalAttackBand(MoverWorld w)
    {
        var radius = w.TargetHitboxRadius + w.DesiredRange + 0.5f;
        var rsq = radius * radius;
        var target = w.TargetPos;
        if (!w.PositionalWanted || w.TrueNorth)
            return p => (p - target).LengthSquared() <= rsq ? 1f : 0f;

        var facing = new Vector2(MathF.Sin(w.TargetRotation), MathF.Cos(w.TargetRotation));
        var side = new Vector2(-facing.Y, facing.X);
        var rear = w.PositionalIsRear;
        return p =>
        {
            var off = p - target;
            if (off.LengthSquared() > rsq)
                return 0f;
            var front = Vector2.Dot(facing, off);
            var lat = MathF.Abs(Vector2.Dot(side, off));
            var inPos = rear ? -front - lat > 0f : lat - MathF.Abs(front) > 0f;
            return inPos ? 2f : 1f;
        };
    }

    /// <summary> Whether the player's position is inside any live zone dilated by <paramref name="buffer"/> (gate / telemetry helper). </summary>
    internal static DangerZoneModel.Zone? UnsafeAt(Vector2 p, IReadOnlyList<DangerZoneModel.Zone> zones, float buffer)
    {
        for (var i = 0; i < zones.Count; i++)
            if (DangerZoneModel.Contains(zones[i], p, buffer))
                return zones[i];
        return null;
    }

    /// <summary>
    ///     Whether a point is unsafe for an arrival <paramref name="arriveInSec"/>
    ///     from now: inside a zone whose activation (minus cushion) is not later than the arrival.
    /// </summary>
    internal static bool UnsafeAtTime(Vector2 p, IReadOnlyList<DangerZoneModel.Zone> zones, float buffer, double nowSec, float arriveInSec, float cushionSec)
    {
        for (var i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            if (!DangerZoneModel.Contains(z, p, buffer))
                continue;
            var activation = z.ActivationSec > 0 ? z.ActivationSec : nowSec + z.RemainingSec;
            if (activation - cushionSec <= nowSec + arriveInSec)
                return true;
        }
        return false;
    }

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

    /// <summary> Steering direction for this frame toward the held waypoint, or null when arrived / none. </summary>
    internal static Vector2? SteerDirection(MoverState s, Vector2 playerPos)
    {
        if (s.Waypoint is not { } wp)
            return null;
        var d = wp - playerPos;
        if (d.Length() < ArriveYalms)
        {
            s.Waypoint = null;
            return null;
        }
        return Vector2.Normalize(d);
    }
}
