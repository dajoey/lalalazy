// Signed-distance functions for telegraph shapes. Structure follows
// awgil/ffxiv_bossmod BossMod/Util/ShapeDistance.cs (BSD-3-Clause, Copyright
// (c) 2022-2024 Andrew Gilewsky); see THIRD_PARTY_NOTICES.md. Angles here are
// math convention: direction(a) = (cos a, sin a) over (X, Z), matching
// DangerZoneModel. Negative = inside.
// PURE: no Dalamud types; compiled into tests/GluttonyCombo.SmartMoverHarness.

#region

using System;
using System.Numerics;

#endregion

namespace GluttonyCombo.AutoRotation.Movement;

internal static class ShapeDistance
{
    private static Vector2 Dir(float a) => new(MathF.Cos(a), MathF.Sin(a));

    public static Func<Vector2, float> Circle(Vector2 origin, float radius) =>
        radius <= 0 ? (_ => float.MaxValue) : (p => (p - origin).Length() - radius);

    public static Func<Vector2, float> Donut(Vector2 origin, float inner, float outer)
    {
        if (outer <= 0 || inner >= outer)
            return _ => float.MaxValue;
        if (inner <= 0)
            return Circle(origin, outer);
        return p =>
        {
            var d = (p - origin).Length();
            return MathF.Max(d - outer, inner - d);
        };
    }

    /// <summary>
    ///     Cone of <paramref name="halfAngle"/> radians either side of
    ///     <paramref name="centerDir"/>. For half angles up to 90 degrees the
    ///     shape is the intersection of the disc and two half-planes; beyond
    ///     that it is the disc minus the opposite wedge.
    /// </summary>
    public static Func<Vector2, float> Cone(Vector2 origin, float radius, float centerDir, float halfAngle)
    {
        if (halfAngle <= 0 || radius <= 0)
            return _ => float.MaxValue;
        if (halfAngle >= MathF.PI)
            return Circle(origin, radius);

        if (halfAngle <= MathF.PI / 2f)
        {
            // outward normals of the two edges
            var nl = Dir(centerDir + halfAngle + MathF.PI / 2f);
            var nr = Dir(centerDir - halfAngle - MathF.PI / 2f);
            return p =>
            {
                var off = p - origin;
                var distOuter = off.Length() - radius;
                var angular = MathF.Max(Vector2.Dot(off, nl), Vector2.Dot(off, nr));
                return MathF.Max(distOuter, angular);
            };
        }
        else
        {
            // disc minus the excluded wedge (half angle PI - halfAngle around centerDir + PI)
            var exHalf = MathF.PI - halfAngle;
            var exCenter = centerDir + MathF.PI;
            var nl = Dir(exCenter + exHalf + MathF.PI / 2f);
            var nr = Dir(exCenter - exHalf - MathF.PI / 2f);
            return p =>
            {
                var off = p - origin;
                var distOuter = off.Length() - radius;
                var wedge = MathF.Max(Vector2.Dot(off, nl), Vector2.Dot(off, nr)); // negative inside the excluded wedge
                return MathF.Max(distOuter, -wedge);
            };
        }
    }

    /// <summary> Rectangle from <paramref name="origin"/> along <paramref name="dir"/> (unit), <paramref name="lenFront"/> ahead, <paramref name="lenBack"/> behind, <paramref name="halfWidth"/> each side. </summary>
    public static Func<Vector2, float> Rect(Vector2 origin, Vector2 dir, float lenFront, float lenBack, float halfWidth)
    {
        var normal = new Vector2(-dir.Y, dir.X);
        return p =>
        {
            var off = p - origin;
            var along = Vector2.Dot(off, dir);
            var across = Vector2.Dot(off, normal);
            var distFront = along - lenFront;
            var distBack = -along - lenBack;
            var distSide = MathF.Abs(across) - halfWidth;
            return MathF.Max(MathF.Max(distFront, distBack), distSide);
        };
    }

    public static Func<Vector2, float> Rect(Vector2 origin, float direction, float lenFront, float lenBack, float halfWidth) =>
        Rect(origin, Dir(direction), lenFront, lenBack, halfWidth);

    public static Func<Vector2, float> Rect(Vector2 from, Vector2 to, float halfWidth)
    {
        var d = to - from;
        var l = d.Length();
        if (l < 0.01f)
            return Circle(from, halfWidth);
        return Rect(from, d / l, l, 0f, halfWidth);
    }

    public static Func<Vector2, float> Cross(Vector2 origin, float direction, float length, float halfWidth)
    {
        var a = Rect(origin, direction, length, length, halfWidth);
        var b = Rect(origin, direction + MathF.PI / 2f, length, length, halfWidth);
        return p => MathF.Min(a(p), b(p));
    }

    public static Func<Vector2, float> Union(Func<Vector2, float> a, Func<Vector2, float> b) => p => MathF.Min(a(p), b(p));
}
