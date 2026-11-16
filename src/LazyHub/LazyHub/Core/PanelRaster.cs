using System.Numerics;

namespace LazyHub.Core;

/// <summary>
/// Rasterises a rounded rectangle with an optional 1 unit border into a BGRA bitmap at its exact pixel size, with
/// anti-aliased edges. The native image node draws it 1:1, so corners keep their shape at any panel size (a small
/// texture stretched over a wide panel smears its corners). No Dalamud types.
/// </summary>
public static class PanelRaster
{
    public const int MaxSide = 2048;

    public static int Clamp(float side) => Math.Clamp((int)MathF.Round(side), 1, MaxSide);

    /// <summary>Signed distance from the point to the edge of a rounded box centred at the origin: negative inside.</summary>
    public static float Distance(float x, float y, float halfWidth, float halfHeight, float radius)
    {
        radius = MathF.Max(0f, MathF.Min(radius, MathF.Min(halfWidth, halfHeight)));
        var qx = MathF.Abs(x) - (halfWidth - radius);
        var qy = MathF.Abs(y) - (halfHeight - radius);
        var outside = MathF.Sqrt((MathF.Max(qx, 0f) * MathF.Max(qx, 0f)) + (MathF.Max(qy, 0f) * MathF.Max(qy, 0f)));
        return outside + MathF.Min(MathF.Max(qx, qy), 0f) - radius;
    }

    private static float Coverage(float distance) => Math.Clamp(0.5f - distance, 0f, 1f);

    /// <summary>Straight-alpha BGRA, width * height * 4 bytes.</summary>
    public static byte[] Render(int width, int height, Vector4 fill, Vector4? border, float radius, float borderWidth = 1f)
    {
        width = Math.Clamp(width, 1, MaxSide);
        height = Math.Clamp(height, 1, MaxSide);
        var bytes = new byte[width * height * 4];
        var hw = width / 2f;
        var hh = height / 2f;
        var innerRadius = MathF.Max(0f, radius - borderWidth);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var px = x + 0.5f - hw;
            var py = y + 0.5f - hh;
            var outer = Coverage(Distance(px, py, hw, hh, radius));
            if (outer <= 0f) continue;

            float a, r, g, b;
            if (border is { } edge)
            {
                var inner = Coverage(Distance(px, py, hw - borderWidth, hh - borderWidth, innerRadius));
                inner = MathF.Min(inner, outer);
                var ring = outer - inner;
                a = (inner * fill.W) + (ring * edge.W);
                if (a <= 0f) continue;
                r = ((inner * fill.X * fill.W) + (ring * edge.X * edge.W)) / a;
                g = ((inner * fill.Y * fill.W) + (ring * edge.Y * edge.W)) / a;
                b = ((inner * fill.Z * fill.W) + (ring * edge.Z * edge.W)) / a;
            }
            else
            {
                a = outer * fill.W;
                r = fill.X; g = fill.Y; b = fill.Z;
            }

            var i = ((y * width) + x) * 4;
            bytes[i + 0] = ToByte(b);
            bytes[i + 1] = ToByte(g);
            bytes[i + 2] = ToByte(r);
            bytes[i + 3] = ToByte(a);
        }
        return bytes;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
}
