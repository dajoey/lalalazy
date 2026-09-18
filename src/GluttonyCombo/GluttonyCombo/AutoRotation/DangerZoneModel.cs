#region

using System;
using System.Collections.Generic;
using System.Numerics;

#endregion

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     PURE (no Dalamud types) model of a telegraphed danger zone, plus the
///     cast-primitive -&gt; zone mapping ported from BossMod's generic
///     auto-hints builder (awgil/ffxiv_bossmod <c>BossModule/AIHintsBuilder.cs</c>,
///     <c>GuessShape</c>). Compiled into <c>tests/GluttonyCombo.SmartMoverHarness</c>;
///     a Dalamud type leaking in breaks that build, which is the point.
/// </summary>
/// <remarks>
///     Fork feature (t_356159a8, v1.0.4.191). Zones are derived from enemy cast
///     data ONLY - never consumed from BossMod - so the mover works with BMR
///     uninstalled. BMR's IsPositionSafe oracle is layered on top optionally by
///     the Dalamud half.
/// </remarks>
internal static class DangerZoneModel
{
    /// <summary> Quantisation error allowance from AIHintsBuilder (2000/65535 yalms). </summary>
    internal const float MaxError = 2000f / 65535f;

    /// <summary> CastType 2/5 with at least this EffectRange is a raidwide - not dodgeable, skip it. </summary>
    internal const float RaidwideSize = 30f;

    internal enum ShapeKind : byte
    {
        Circle,
        Donut,
        Rect,
        ChargeRect,
        Cone,
        Cross,
    }

    /// <summary> One live telegraphed zone. Positions are world XZ. </summary>
    internal readonly record struct Zone(
        ShapeKind Kind,
        Vector2 Origin,
        float Rotation,      // math-convention radians (atan2(z, x)) for the rect/cone/cross axis
        float Radius,        // circle radius / cone reach / rect LENGTH (caster-anchored)
        float InnerRadius,   // donut only
        float HalfWidth,     // rect/cross/charge half-width
        float HalfAngle,     // cone half-angle, radians
        Vector2 End,         // charge rect: destination point
        float RemainingSec,  // seconds until the cast resolves (as reported by the cast bar)
        double ActivationSec = 0, // v2: absolute second (MoverWorld.NowSec clock) the zone becomes lethal; 0 = now
        ulong Source = 0,    // v2: casting actor id (0 = unknown / lingering field)
        uint ActionId = 0);  // v2: action id for telemetry

    /// <summary> Raw per-cast data the Dalamud half extracts; omen-derived values are passed in pre-parsed. </summary>
    internal readonly record struct CastPrimitive(
        byte CastType,
        float EffectRange,
        float XAxisModifier,
        float HitboxRadius,     // caster's hitbox
        Vector2 CasterPos,
        Vector2 CastTargetLoc,  // the cast's own target location, snapshotted when the cast started
        ulong CastTargetId,     // 0 = ground/none
        ulong PlayerId,
        float RemainingSec,
        float ConeHalfAngleDeg, // cone HALF-angle in degrees, read from the action's omen (fan fragment)
        float DonutInnerYalms,  // 0 = unknown -> treated as a plain circle (conservative)
        float? AimRotation = null); // v1.0.4.209: the cast's own rotation, math convention (atan2(z, x));
                                    // null only when the caller has none, then the caster->location vector

    /// <summary>
    ///     Maps one live enemy cast to a danger zone, or null when the cast is
    ///     not a dodgeable telegraph (single-target, raidwide, or an unknown
    ///     shape). Target-anchored shapes aimed at the player (ground circle,
    ///     donut, cross, location rect) build zones like any other.
    /// </summary>
    /// <remarks>
    ///     v1.0.4.209: geometry follows BossMod's auto-hints exactly. The
    ///     anchor for location shapes is the cast's snapshotted target
    ///     location (a telegraph placed under the character stays where it
    ///     was drawn), and every directional shape aims along the cast's own
    ///     rotation. Self-targeted cones and lines used to aim along the
    ///     zero caster-to-self vector (always +X), and helper casts at a ground
    ///     point were drawn on the helper instead of the point.
    /// </remarks>
    internal static Zone? BuildZone(in CastPrimitive p)
    {
        if (p.CastType is 1 or 6 or 7 or 9 or 14 or 15)
            return null;

        // Raidwide: CastType 2/5 at raidwide size (AIHintsBuilder "AutomaticConservative").
        if (p.CastType is 2 or 5 && p.EffectRange >= RaidwideSize)
            return null;

        // v2 r2: a rect at least RaidwideSize long AND wide is the whole room, not a
        // line (Abductor's Buffet: CastType 12, 60 x 60 from the arena edge, hit all
        // 19 players and knocked back). A cone of 360 degrees is a circle. Neither
        // can be dodged by position; building them sent the character to the edge.
        if (p.CastType is 4 or 12 && p.EffectRange >= RaidwideSize && p.XAxisModifier >= RaidwideSize)
            return null;
        if (p.CastType is 3 or 13 && p.EffectRange >= RaidwideSize && p.ConeHalfAngleDeg >= 180f)
            return null;

        var aim = p.AimRotation ?? AimRot(p.CastTargetLoc - p.CasterPos);

        switch (p.CastType)
        {
            case 2: // circle at the cast location; no caster hitbox padding
                return new Zone(ShapeKind.Circle, p.CastTargetLoc, 0f, p.EffectRange + MaxError, 0f, 0f, 0f, default, p.RemainingSec);

            case 5: // point-blank circle around the caster
                return new Zone(ShapeKind.Circle, p.CasterPos, 0f, p.EffectRange + p.HitboxRadius + MaxError, 0f, 0f, 0f, default, p.RemainingSec);

            case 3: // cone from the caster toward the target
                return new Zone(ShapeKind.Cone, p.CasterPos, aim, p.EffectRange + p.HitboxRadius, 0f, 0f,
                    p.ConeHalfAngleDeg * MathF.PI / 180f, default, p.RemainingSec);

            case 13: // cone without hitbox padding
                return new Zone(ShapeKind.Cone, p.CasterPos, aim, p.EffectRange, 0f, 0f,
                    p.ConeHalfAngleDeg * MathF.PI / 180f, default, p.RemainingSec);

            case 4: // rect from the caster toward the target
                return new Zone(ShapeKind.Rect, p.CasterPos, aim, p.EffectRange + p.HitboxRadius + MaxError, 0f,
                    p.XAxisModifier * 0.5f + MaxError, 0f, default, p.RemainingSec);

            case 12: // location-targeted rect centred on the cast location
                return new Zone(ShapeKind.Rect, p.CastTargetLoc, aim, p.EffectRange + MaxError, 0f,
                    p.XAxisModifier * 0.5f + MaxError, 0f, default, p.RemainingSec);

            case 8: // charge: rect from caster to the charge destination
                return new Zone(ShapeKind.ChargeRect, p.CasterPos, 0f, 0f, 0f,
                    p.XAxisModifier * 0.5f + MaxError, 0f, p.CastTargetLoc, p.RemainingSec);

            case 10: // donut around the cast location; unknown inner -> full circle (conservative)
                return p.DonutInnerYalms <= 0f
                    ? new Zone(ShapeKind.Circle, p.CastTargetLoc, 0f, p.EffectRange + MaxError, 0f, 0f, 0f, default, p.RemainingSec)
                    : new Zone(ShapeKind.Donut, p.CastTargetLoc, 0f, p.EffectRange + MaxError,
                        MathF.Max(0f, p.DonutInnerYalms - MaxError), 0f, 0f, default, p.RemainingSec);

            case 11: // cross: two rects through the cast location at 90 degrees
                return new Zone(ShapeKind.Cross, p.CastTargetLoc, aim, p.EffectRange + MaxError, 0f,
                    p.XAxisModifier * 0.5f + MaxError, 0f, default, p.RemainingSec);

            default:
                return null;
        }
    }

    /// <summary> Whether <paramref name="p"/> lies inside the zone dilated by <paramref name="buffer"/> yalms. </summary>
    internal static bool Contains(in Zone z, Vector2 p, float buffer)
    {
        var d = p - z.Origin;
        switch (z.Kind)
        {
            case ShapeKind.Circle:
                return d.Length() <= z.Radius + buffer;

            case ShapeKind.Donut:
                var dl = d.Length();
                return dl <= z.Radius + buffer && dl >= MathF.Max(0f, z.InnerRadius - buffer);

            case ShapeKind.Rect:
                return InRect(d, z.Rotation, z.Radius, z.HalfWidth, buffer);

            case ShapeKind.ChargeRect:
                var span = z.End - z.Origin;
                var len = span.Length();
                var rot = len < 0.01f ? 0f : MathF.Atan2(span.Y, span.X);
                return InRect(d, rot, len + MaxError, z.HalfWidth, buffer);

            case ShapeKind.Cone:
                var dist = d.Length();
                if (dist > z.Radius + buffer)
                    return false;
                if (dist < 0.01f)
                    return true;
                var ang = MathF.Abs(AngleDiff(MathF.Atan2(d.Y, d.X), z.Rotation));
                return ang <= z.HalfAngle + (buffer / MathF.Max(dist, 0.5f));

            case ShapeKind.Cross:
                return InRect(d, z.Rotation, z.Radius, z.HalfWidth, buffer)
                       || InRect(d, z.Rotation + MathF.PI / 2f, z.Radius, z.HalfWidth, buffer);

            default:
                return false;
        }
    }

    private static bool InRect(Vector2 d, float rot, float length, float halfWidth, float buffer)
    {
        var cos = MathF.Cos(-rot);
        var sin = MathF.Sin(-rot);
        var along = d.X * cos - d.Y * sin;   // position along the rect axis
        var lateral = d.X * sin + d.Y * cos; // position across it
        return along >= -MaxError - buffer && along <= length + buffer && MathF.Abs(lateral) <= halfWidth + buffer;
    }

    /// <summary>
    ///     Tracks ground-danger zones that keep hurting after the cast bar
    ///     ends (v1.0.4.193). The Dalamud half feeds every RESOLVED
    ///     target-anchored zone (ground circles, donuts, crosses, location
    ///     rects); each stays live as danger for <see cref="LingerSec"/>
    /// seconds so the dodge branch keeps avoiding the field while it
    /// persists. PURE - compiled into the offline harness.
    /// </summary>
    internal sealed class LingeringZones
    {
        /// <summary> How long a resolved ground zone stays dangerous, seconds. </summary>
        internal const float LingerSec = 1f; // v2: was 3 s - stale re-dodges 1-2 s after resolution (Live Evidence)

        private readonly List<(Zone Zone, double ExpirySec)> active = new();

        public void Add(in Zone z, double nowSec) => active.Add((z, nowSec + LingerSec));

        public void Clear() => active.Clear();

        public void Sweep(double nowSec) => active.RemoveAll(e => e.ExpirySec <= nowSec);

        /// <summary> Appends still-live lingering zones with their remaining seconds rewritten. </summary>
        public int AppendTo(List<Zone> dest, double nowSec)
        {
            var n = 0;
            for (var i = 0; i < active.Count; i++)
            {
                var rem = (float)(active[i].ExpirySec - nowSec);
                if (rem <= 0f)
                    continue;
                dest.Add(active[i].Zone with { RemainingSec = rem });
                n++;
            }
            return n;
        }
    }

    /// <summary>
    ///     Game rotation (radians, facing = (sin r, cos r) in the XZ plane) to
    ///     the math-convention angle atan2(z, x) the zones use (v1.0.4.209).
    /// </summary>
    /// <summary>
    ///     v2: signed distance function of a zone (negative inside), for the
    ///     time-aware planner. The buffer already dilated into the zone at
    ///     collection time is included; the planner adds its own over-dodge cushion.
    /// </summary>
    internal static Func<Vector2, float> Sdf(in Zone z)
    {
        switch (z.Kind)
        {
            case ShapeKind.Circle:
                return Movement.ShapeDistance.Circle(z.Origin, z.Radius);
            case ShapeKind.Donut:
                return Movement.ShapeDistance.Donut(z.Origin, z.InnerRadius, z.Radius);
            case ShapeKind.Rect:
                return Movement.ShapeDistance.Rect(z.Origin, z.Rotation, z.Radius, MaxError, z.HalfWidth);
            case ShapeKind.ChargeRect:
                return Movement.ShapeDistance.Rect(z.Origin, z.End, z.HalfWidth);
            case ShapeKind.Cone:
                return Movement.ShapeDistance.Cone(z.Origin, z.Radius, z.Rotation, z.HalfAngle);
            case ShapeKind.Cross:
                return Movement.ShapeDistance.Cross(z.Origin, z.Rotation, z.Radius, z.HalfWidth);
            default:
                return _ => float.MaxValue;
        }
    }

    /// <summary> Axis-aligned bounding box of a zone (conservative), for rasterisation culling. </summary>
    internal static (Vector2 Min, Vector2 Max) Bounds(in Zone z)
    {
        switch (z.Kind)
        {
            case ShapeKind.ChargeRect:
            {
                var min = Vector2.Min(z.Origin, z.End) - new Vector2(z.HalfWidth + MaxError);
                var max = Vector2.Max(z.Origin, z.End) + new Vector2(z.HalfWidth + MaxError);
                return (min, max);
            }
            case ShapeKind.Rect:
            case ShapeKind.Cross:
            {
                var r = z.Radius + z.HalfWidth + MaxError;
                return (z.Origin - new Vector2(r), z.Origin + new Vector2(r));
            }
            default:
                return (z.Origin - new Vector2(z.Radius), z.Origin + new Vector2(z.Radius));
        }
    }

    internal static float GameRotationToMath(float gameRotation) =>
        MathF.Atan2(MathF.Cos(gameRotation), MathF.Sin(gameRotation));

    private static float AimRot(Vector2 v) => v.LengthSquared() < 0.0001f ? 0f : MathF.Atan2(v.Y, v.X);

    private static float AngleDiff(float a, float b)
    {
        var d = a - b;
        while (d > MathF.PI) d -= 2f * MathF.PI;
        while (d < -MathF.PI) d += 2f * MathF.PI;
        return d;
    }
}
