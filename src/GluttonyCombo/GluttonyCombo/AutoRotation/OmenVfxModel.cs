#region

using System;
using System.Numerics;

#endregion

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     PURE (no Dalamud types) decoder and conservative zone builder for
///     omen-telegraph VFX (fork, t_d817728f, v1.0.4.195). Instant enemy AoEs
///     have no cast bar, so the ground omen VFX the game spawns for them is
///     the only warning; ECommons' VfxManager (hooked since plugin init via
///     Module.All) hands the Dalamud half every live VFX with its path,
///     caster, world placement and age, and this file turns the omen ones
///     into danger zones the dodge branch can consume. Compiled into
///     tests/GluttonyCombo.SmartMoverHarness.
/// </summary>
/// <remarks>
///     Shape classification follows the omen path-fragment conventions the
///     game data carries (Omen.csv: "gl_fan060_1bf", "gl_sircle_1907af",
///     "m0244donut_o0t", "general01_cline0k1") and BossMod's own parse
///     (AIHintsBuilder.DetermineConeAngle reads exactly the three digits
///     after "fan" as the FULL cone angle, halved for the half-angle). Real
///     dimensions are NOT encoded reliably in the fragment, so every unknown
///     degrades conservatively: the failure direction is over-dodging,
///     never under-dodging, and the constants below are the tuning surface
///     for the live-data round.
/// </remarks>
internal static class OmenVfxModel
{
    /// <summary> Conservative radius for a circle omen with no encoded size, yalms. </summary>
    internal const float CircleRadiusY = 10f;

    /// <summary> Conservative outer radius for a donut omen, yalms. </summary>
    internal const float DonutOuterY = 10f;

    /// <summary> Donut inner radius when the path does not encode one - BossMod's own default (AOEShapeDonut(3, ...)). </summary>
    internal const float DonutInnerY = 3f;

    /// <summary> Conservative reach for a cone omen, yalms (padded by the caster hitbox like CastType 3). </summary>
    internal const float ConeReachY = 12f;

    /// <summary> A rect omen is treated as symmetric about its placement: half span along the axis, yalms. </summary>
    internal const float RectHalfSpanY = 6f;

    /// <summary> Conservative rect half width, yalms. </summary>
    internal const float RectHalfWidthY = 4f;

    /// <summary> Cone half-angle fallback when "fan" has no readable angle, degrees. BossMod falls back to a 180-degree cone. </summary>
    internal const float DefaultConeHalfDeg = 90f;

    /// <summary> An omen zone older than this is dropped even if its destruction event was missed, seconds. </summary>
    internal const float MaxAgeSec = 12f;

    internal enum OmenKind : byte
    {
        Circle,
        Donut,
        Cone,
        Rect,
    }

    /// <summary> Whether a spawned VFX path is an omen telegraph at all ("vfx/omen/..." or a bare omen fragment). </summary>
    internal static bool IsOmenPath(string path) =>
        !string.IsNullOrEmpty(path) && path.IndexOf("omen", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    ///     Classifies an omen path into a shape and, for cones, reads the
    ///     full angle (degrees) out of the "fan%03d" fragment the same way
    ///     BossMod does (three chars starting right after "fan"), halved
    ///     into the half-angle.
    /// </summary>
    internal static OmenKind Classify(string path, out float coneHalfDeg)
    {
        coneHalfDeg = DefaultConeHalfDeg;
        if (string.IsNullOrEmpty(path))
            return OmenKind.Circle;

        var fan = path.IndexOf("fan", StringComparison.Ordinal);
        if (fan >= 0)
        {
            coneHalfDeg = TryReadThreeDigits(path, fan + 3, out var full) ? full * 0.5f : DefaultConeHalfDeg;
            return OmenKind.Cone;
        }

        if (path.IndexOf("don", StringComparison.Ordinal) >= 0)
            return OmenKind.Donut;

        if (path.IndexOf("line", StringComparison.Ordinal) >= 0 || path.IndexOf("rect", StringComparison.Ordinal) >= 0)
            return OmenKind.Rect;

        // "sircle" (the game's own misspelling), "general_*", and unknown
        // fragments are all treated as circles - the conservative superset.
        return OmenKind.Circle;
    }

    /// <summary>
    ///     Builds the conservative danger zone for one decoded omen VFX, or
    ///     null when its age already expired. Cones without a usable aim
    ///     and rects without a meaningful placement rotation degrade to
    ///     circles: a directional shape with the wrong direction
    ///     under-dodges, the circle superset never does.
    /// </summary>
    /// <param name="kind"> Decoded shape. </param>
    /// <param name="coneHalfDeg"> Cone half-angle in degrees (from the fan fragment). </param>
    /// <param name="center"> World XZ anchor: VFX placement, or the caster position for cones. </param>
    /// <param name="aimYaw"> Math-convention radians (atan2(z, x)); used by cone/rect only. </param>
    /// <param name="haveAim"> Whether <paramref name="aimYaw"/> is trustworthy. </param>
    /// <param name="casterHitboxRadius"> Cone reach padding, yalms. </param>
    /// <param name="ageSec"> VFX age in seconds. </param>
    /// <param name="buffer"> Configured danger buffer, yalms. </param>
    internal static DangerZoneModel.Zone? BuildZone(
        OmenKind kind,
        float coneHalfDeg,
        Vector2 center,
        float aimYaw,
        bool haveAim,
        float casterHitboxRadius,
        float ageSec,
        float buffer)
    {
        if (ageSec < 0f || ageSec >= MaxAgeSec)
            return null;

        var rem = MaxAgeSec - ageSec;
        switch (kind)
        {
            case OmenKind.Circle:
                return new DangerZoneModel.Zone(
                    DangerZoneModel.ShapeKind.Circle, center, 0f, CircleRadiusY + buffer,
                    0f, 0f, 0f, default, rem);

            case OmenKind.Donut:
                return new DangerZoneModel.Zone(
                    DangerZoneModel.ShapeKind.Donut, center, 0f, DonutOuterY + buffer,
                    MathF.Max(0f, DonutInnerY - buffer), 0f, 0f, default, rem);

            case OmenKind.Cone:
                if (!haveAim)
                    return new DangerZoneModel.Zone(
                        DangerZoneModel.ShapeKind.Circle, center, 0f, ConeReachY + casterHitboxRadius + buffer,
                        0f, 0f, 0f, default, rem);
                return new DangerZoneModel.Zone(
                    DangerZoneModel.ShapeKind.Cone, center, aimYaw, ConeReachY + casterHitboxRadius,
                    0f, 0f, coneHalfDeg * MathF.PI / 180f + buffer / MathF.Max(ConeReachY, 1f), default, rem);

            case OmenKind.Rect:
                if (!haveAim)
                    return new DangerZoneModel.Zone(
                        DangerZoneModel.ShapeKind.Circle, center, 0f, RectHalfSpanY + buffer,
                        0f, 0f, 0f, default, rem);
                // The zone model's rect is one-sided (origin + direction +
                // length); anchoring at center - halfSpan makes the omen rect
                // symmetric about its placement, which also makes the zone
                // invariant to a 180-degree error in the rotation source.
                var dir = new Vector2(MathF.Cos(aimYaw), MathF.Sin(aimYaw));
                return new DangerZoneModel.Zone(
                    DangerZoneModel.ShapeKind.Rect, center - dir * RectHalfSpanY, aimYaw,
                    2f * RectHalfSpanY + DangerZoneModel.MaxError, 0f, RectHalfWidthY + buffer, 0f, default, rem);

            default:
                return null;
        }
    }

    /// <summary>
    ///     Game-convention rotation (facing = (sin r, cos r), radians CCW
    ///     from north/+Z) to math convention (atan2(z, x)) - the convention
    ///     every DangerZoneModel rotation uses.
    /// </summary>
    internal static float FacingYaw(float gameRotation) =>
        MathF.Atan2(MathF.Cos(gameRotation), MathF.Sin(gameRotation));

    /// <summary>
    ///     Yaw (math convention) from a VFX placement quaternion. The
    ///     ECommons <c>Quat</c> struct exposes X, Z, Y, W fields; this takes
    ///     them as plain floats so the math stays in this harness-compiled
    ///     pure file. Returns null when the quaternion is degenerate,
    ///     identity, or near-identity: an unrotated omen does not tell
    ///     which default axis its model uses, so the caller degrades to a
    ///     circle instead of committing a possibly-perpendicular rect.
    /// </summary>
    internal static float? QuatYaw(float qx, float qz, float qy, float qw)
    {
        var lenSq = qx * qx + qy * qy + qz * qz + qw * qw;
        if (lenSq < 0.5f || lenSq > 1.5f)
            return null; // not a unit quaternion - placement junk
        var yaw = MathF.Atan2(2f * (qw * qy + qz * qx), 1f - 2f * (qy * qy + qz * qz));
        if (MathF.Abs(yaw) < 5f * MathF.PI / 180f)
            return null; // effectively unrotated - no trustworthy axis
        return yaw;
    }

    private static bool TryReadThreeDigits(string s, int start, out float value)
    {
        value = 0f;
        if (start < 0 || start + 3 > s.Length)
            return false;
        var d0 = s[start] - '0';
        var d1 = s[start + 1] - '0';
        var d2 = s[start + 2] - '0';
        if (d0 is < 0 or > 9 || d1 is < 0 or > 9 || d2 is < 0 or > 9)
            return false;
        value = d0 * 100 + d1 * 10 + d2;
        return true;
    }
}
