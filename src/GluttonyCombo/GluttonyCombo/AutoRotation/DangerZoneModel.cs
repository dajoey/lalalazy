#region

using System;
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
        float RemainingSec); // seconds until the cast resolves

    /// <summary> Raw per-cast data the Dalamud half extracts; omen-derived values are passed in pre-parsed. </summary>
    internal readonly record struct CastPrimitive(
        byte CastType,
        float EffectRange,
        float XAxisModifier,
        float HitboxRadius,     // caster's hitbox
        Vector2 CasterPos,
        Vector2 CastTargetLoc,  // resolved target object position, else caster position
        ulong CastTargetId,     // 0 = ground/none
        ulong PlayerId,
        float RemainingSec,
        float ConeFallbackDeg,  // HALF-angle fallback when the omen has no fan angle
        float DonutInnerYalms); // 0 = unknown -> treated as a plain circle (conservative)

    /// <summary>
    ///     Maps one live enemy cast to a danger zone, or null when the cast is
    ///     not a dodgeable telegraph (single-target, raidwide, or aimed at the
    ///     player - AIHintsBuilder skips those last because the zone tracks the
    ///     player and cannot be dodged).
    /// </summary>
    internal static Zone? BuildZone(in CastPrimitive p)
    {
        if (p.CastType is 1 or 6 or 7 or 9 or 14 or 15)
            return null;

        // Raidwide: CastType 2/5 at raidwide size (AIHintsBuilder "AutomaticConservative").
        if (p.CastType is 2 or 5 && p.EffectRange >= RaidwideSize)
            return null;

        // Cast aimed at the player: undodgeable, and dodging fights the healers.
        if (p.CastTargetId != 0 && p.CastTargetId == p.PlayerId)
            return null;

        var aim = AimRot(p.CastTargetLoc - p.CasterPos);

        switch (p.CastType)
        {
            case 2: // circle at the cast location; no caster hitbox padding
                return new Zone(ShapeKind.Circle, p.CastTargetLoc, 0f, p.EffectRange + MaxError, 0f, 0f, 0f, default, p.RemainingSec);

            case 5: // point-blank circle around the caster
                return new Zone(ShapeKind.Circle, p.CasterPos, 0f, p.EffectRange + p.HitboxRadius + MaxError, 0f, 0f, 0f, default, p.RemainingSec);

            case 3: // cone from the caster toward the target
                return new Zone(ShapeKind.Cone, p.CasterPos, aim, p.EffectRange + p.HitboxRadius, 0f, 0f,
                    p.ConeFallbackDeg * 0.5f * MathF.PI / 180f, default, p.RemainingSec);

            case 13: // cone without hitbox padding
                return new Zone(ShapeKind.Cone, p.CasterPos, aim, p.EffectRange, 0f, 0f,
                    p.ConeFallbackDeg * 0.5f * MathF.PI / 180f, default, p.RemainingSec);

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

    private static float AimRot(Vector2 v) => v.LengthSquared() < 0.0001f ? 0f : MathF.Atan2(v.Y, v.X);

    private static float AngleDiff(float a, float b)
    {
        var d = a - b;
        while (d > MathF.PI) d -= 2f * MathF.PI;
        while (d < -MathF.PI) d += 2f * MathF.PI;
        return d;
    }
}
