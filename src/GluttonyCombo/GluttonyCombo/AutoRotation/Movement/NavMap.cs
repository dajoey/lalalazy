// Ported from awgil/ffxiv_bossmod BossMod/Pathfinding/Map.cs (BSD-3-Clause,
// Copyright (c) 2022-2024 Andrew Gilewsky). See THIRD_PARTY_NOTICES.md.
// PURE: no Dalamud types; compiled into tests/GluttonyCombo.SmartMoverHarness.

#region

using System;
using System.Numerics;

#endregion

namespace GluttonyCombo.AutoRotation.Movement;

/// <summary>
///     Axis-aligned square grid over the character's neighbourhood. Each pixel is
///     either safe (MaxG = float.MaxValue), dangerous after MaxG seconds of travel
///     (MaxG >= 0), or impassable (MaxG &lt; 0). Goal pixels carry a priority;
///     danger overrides goal. World coordinates are XZ as (X, Z).
/// </summary>
internal sealed class NavMap
{
    public float Resolution { get; private set; }
    public int Width { get; private set; }  // always even
    public int Height { get; private set; } // always even
    public float[] PixelMaxG = Array.Empty<float>();
    public float[] PixelPriority = Array.Empty<float>();
    public bool[] PixelAvoid = Array.Empty<bool>();

    public Vector2 Center { get; private set; }
    public float MaxG;        // max MaxG over all blocked pixels
    public float MaxPriority; // max priority over all goal pixels

    public int MinX, MinY, MaxX, MaxY;

    public void Init(float resolution, Vector2 center, float worldHalfWidth, float worldHalfHeight)
    {
        Resolution = resolution;
        Width = 2 * (int)MathF.Ceiling(worldHalfWidth / resolution);
        Height = 2 * (int)MathF.Ceiling(worldHalfHeight / resolution);

        var numPixels = Width * Height;
        if (PixelMaxG.Length < numPixels)
            PixelMaxG = new float[numPixels];
        Array.Fill(PixelMaxG, float.MaxValue, 0, numPixels);
        if (PixelPriority.Length < numPixels)
            PixelPriority = new float[numPixels];
        else
            Array.Fill(PixelPriority, 0f, 0, numPixels);
        if (PixelAvoid.Length < numPixels)
            PixelAvoid = new bool[numPixels];
        else
            Array.Fill(PixelAvoid, false, 0, numPixels);

        Center = center;
        MaxG = 0;
        MaxPriority = 0;
        MinX = MinY = 0;
        MaxX = Width - 1;
        MaxY = Height - 1;
    }

    public Vector2 WorldToGridFrac(Vector2 world)
    {
        var off = world - Center;
        return new Vector2(Width / 2 + off.X / Resolution, Height / 2 + off.Y / Resolution);
    }

    public int GridToIndex(int x, int y) => y * Width + x;
    public (int x, int y) IndexToGrid(int index) => (index % Width, index / Width);
    public (int x, int y) FracToGrid(Vector2 frac) => ((int)MathF.Floor(frac.X), (int)MathF.Floor(frac.Y));
    public (int x, int y) WorldToGrid(Vector2 world) => FracToGrid(WorldToGridFrac(world));
    public (int x, int y) ClampToGrid((int x, int y) pos) => (Math.Clamp(pos.x, 0, Width - 1), Math.Clamp(pos.y, 0, Height - 1));
    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
    public bool ContainsWorld(Vector2 world)
    {
        var f = WorldToGridFrac(world);
        return f.X >= 0 && f.X < Width && f.Y >= 0 && f.Y < Height;
    }

    public Vector2 GridToWorld(int gx, int gy, float fx, float fy) =>
        Center + new Vector2((gx - Width / 2 + fx) * Resolution, (gy - Height / 2 + fy) * Resolution);

    public Vector2 CellCenter(int index)
    {
        var (x, y) = IndexToGrid(index);
        return GridToWorld(x, y, 0.5f, 0.5f);
    }

    /// <summary> World position of grid corner (x, y), x in 0..Width, y in 0..Height. </summary>
    public Vector2 CornerToWorld(int x, int y) => GridToWorld(x, y, 0f, 0f);

    /// <summary> Marks every pixel whose centre satisfies <paramref name="inside"/> with at most <paramref name="maxG"/> (negative = impassable). </summary>
    public void BlockPixelsInside(Func<Vector2, bool> inside, float maxG)
    {
        MaxG = MathF.Max(MaxG, maxG);
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                if (!inside(GridToWorld(x, y, 0.5f, 0.5f)))
                    continue;
                ref var pixel = ref PixelMaxG[y * Width + x];
                pixel = MathF.Min(pixel, maxG);
            }
    }

    /// <summary> Blocks one pixel outright (used to exclude an off-mesh waypoint and re-plan). </summary>
    public void BlockPixel(int index)
    {
        if (index < 0 || index >= Width * Height)
            return;
        PixelMaxG[index] = -1000f;
        PixelPriority[index] = float.MinValue;
    }
}
