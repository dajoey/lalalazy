using System.IO.Compression;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;

namespace RenderProbe;

internal static unsafe class CpuRasterizer
{
    public static RenderStats Render(
        ImDrawDataPtr drawData,
        byte[] frame,
        int frameWidth,
        int frameHeight,
        byte[] atlas,
        int atlasWidth,
        int atlasHeight,
        IntPtr fontTextureId)
    {
        if (drawData.FramebufferScale != Vector2.One)
            throw new NotSupportedException("This probe intentionally supports only a 1:1 framebuffer scale.");

        var triangles = 0;
        var displayPos = drawData.DisplayPos;
        for (var listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            var list = new ImDrawListPtr(drawData.CmdLists[listIndex]);
            for (var commandIndex = 0; commandIndex < list.CmdBuffer.Size; commandIndex++)
            {
                var command = list.CmdBuffer[commandIndex];
                if (command.UserCallback != null)
                    throw new NotSupportedException("User draw callbacks are outside this probe's scope.");
                if (command.ElemCount == 0)
                    continue;
                if (command.TextureId.Handle != (ulong)fontTextureId.ToInt64())
                    throw new NotSupportedException($"Unexpected texture ID {command.TextureId.Handle}; only the ImGui font atlas is registered.");
                if (command.ElemCount % 3 != 0)
                    throw new InvalidOperationException("Expected indexed triangles in the ImGui draw list.");

                var clip = new ClipRect(command.ClipRect, displayPos, frameWidth, frameHeight);
                for (var element = 0; element < command.ElemCount; element += 3)
                {
                    var i0 = (int)(list.IdxBuffer[(int)command.IdxOffset + element] + command.VtxOffset);
                    var i1 = (int)(list.IdxBuffer[(int)command.IdxOffset + element + 1] + command.VtxOffset);
                    var i2 = (int)(list.IdxBuffer[(int)command.IdxOffset + element + 2] + command.VtxOffset);
                    var a = ReadVertex(list.VtxBuffer[i0], displayPos);
                    var b = ReadVertex(list.VtxBuffer[i1], displayPos);
                    var c = ReadVertex(list.VtxBuffer[i2], displayPos);
                    RasterTriangle(frame, frameWidth, frameHeight, atlas, atlasWidth, atlasHeight, clip, a, b, c);
                    triangles++;
                }
            }
        }

        return new RenderStats(drawData.CmdListsCount, triangles);
    }

    private static Vertex ReadVertex(ImDrawVert value, Vector2 displayPos)
        => new(value.Pos - displayPos, value.Uv, value.Col);

    private static void RasterTriangle(
        byte[] frame,
        int frameWidth,
        int frameHeight,
        byte[] atlas,
        int atlasWidth,
        int atlasHeight,
        ClipRect clip,
        Vertex a,
        Vertex b,
        Vertex c)
    {
        var area = Edge(a.Position, b.Position, c.Position);
        if (MathF.Abs(area) < 0.000001f)
            return;

        // Normalize winding so the top-left rule can avoid blending shared edges twice.
        if (area < 0)
        {
            (b, c) = (c, b);
            area = -area;
        }

        var minX = Math.Max(clip.MinX, FloorToInt(MathF.Min(a.Position.X, MathF.Min(b.Position.X, c.Position.X))));
        var minY = Math.Max(clip.MinY, FloorToInt(MathF.Min(a.Position.Y, MathF.Min(b.Position.Y, c.Position.Y))));
        var maxX = Math.Min(clip.MaxX - 1, CeilingToInt(MathF.Max(a.Position.X, MathF.Max(b.Position.X, c.Position.X))));
        var maxY = Math.Min(clip.MaxY - 1, CeilingToInt(MathF.Max(a.Position.Y, MathF.Max(b.Position.Y, c.Position.Y))));
        if (minX > maxX || minY > maxY)
            return;

        var topLeft0 = IsTopLeft(b.Position, c.Position);
        var topLeft1 = IsTopLeft(c.Position, a.Position);
        var topLeft2 = IsTopLeft(a.Position, b.Position);
        var inverseArea = 1f / area;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var w0 = Edge(b.Position, c.Position, p);
                var w1 = Edge(c.Position, a.Position, p);
                var w2 = Edge(a.Position, b.Position, p);
                if (!(w0 > 0 || (w0 == 0 && topLeft0)) ||
                    !(w1 > 0 || (w1 == 0 && topLeft1)) ||
                    !(w2 > 0 || (w2 == 0 && topLeft2)))
                    continue;

                var bary0 = w0 * inverseArea;
                var bary1 = w1 * inverseArea;
                var bary2 = w2 * inverseArea;
                var uv = a.Uv * bary0 + b.Uv * bary1 + c.Uv * bary2;
                var vertexColor = InterpolateColor(a.Color, b.Color, c.Color, bary0, bary1, bary2);
                var texel = SampleBilinear(atlas, atlasWidth, atlasHeight, uv);

                var srcA = (vertexColor.A * texel.A + 127) / 255;
                if (srcA == 0)
                    continue;
                var srcR = (vertexColor.R * texel.R + 127) / 255;
                var srcG = (vertexColor.G * texel.G + 127) / 255;
                var srcB = (vertexColor.B * texel.B + 127) / 255;
                Blend(frame, (y * frameWidth + x) * 4, srcR, srcG, srcB, srcA);
            }
        }
    }

    private static (int R, int G, int B, int A) InterpolateColor(
        uint packedA,
        uint packedB,
        uint packedC,
        float wA,
        float wB,
        float wC)
    {
        var a = Unpack(packedA);
        var b = Unpack(packedB);
        var c = Unpack(packedC);
        return (
            ClampByte(a.R * wA + b.R * wB + c.R * wC),
            ClampByte(a.G * wA + b.G * wB + c.G * wC),
            ClampByte(a.B * wA + b.B * wB + c.B * wC),
            ClampByte(a.A * wA + b.A * wB + c.A * wC));
    }

    private static (int R, int G, int B, int A) SampleBilinear(byte[] atlas, int width, int height, Vector2 uv)
    {
        var x = uv.X * width - 0.5f;
        var y = uv.Y * height - 0.5f;
        var x0 = FloorToInt(x);
        var y0 = FloorToInt(y);
        var fx = x - x0;
        var fy = y - y0;
        var c00 = Texel(atlas, width, height, x0, y0);
        var c10 = Texel(atlas, width, height, x0 + 1, y0);
        var c01 = Texel(atlas, width, height, x0, y0 + 1);
        var c11 = Texel(atlas, width, height, x0 + 1, y0 + 1);

        return (
            ClampByte(Bilinear(c00.R, c10.R, c01.R, c11.R, fx, fy)),
            ClampByte(Bilinear(c00.G, c10.G, c01.G, c11.G, fx, fy)),
            ClampByte(Bilinear(c00.B, c10.B, c01.B, c11.B, fx, fy)),
            ClampByte(Bilinear(c00.A, c10.A, c01.A, c11.A, fx, fy)));
    }

    private static float Bilinear(float c00, float c10, float c01, float c11, float x, float y)
    {
        var top = c00 + (c10 - c00) * x;
        var bottom = c01 + (c11 - c01) * x;
        return top + (bottom - top) * y;
    }

    private static (int R, int G, int B, int A) Texel(byte[] atlas, int width, int height, int x, int y)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        var offset = (y * width + x) * 4;
        return (atlas[offset], atlas[offset + 1], atlas[offset + 2], atlas[offset + 3]);
    }

    private static (int R, int G, int B, int A) Unpack(uint color)
        => ((int)(color & 0xff), (int)((color >> 8) & 0xff), (int)((color >> 16) & 0xff), (int)((color >> 24) & 0xff));

    private static void Blend(byte[] frame, int offset, int r, int g, int b, int a)
    {
        var inverse = 255 - a;
        frame[offset] = (byte)((r * a + frame[offset] * inverse + 127) / 255);
        frame[offset + 1] = (byte)((g * a + frame[offset + 1] * inverse + 127) / 255);
        frame[offset + 2] = (byte)((b * a + frame[offset + 2] * inverse + 127) / 255);
        frame[offset + 3] = 255;
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 p)
        => (p.X - a.X) * (b.Y - a.Y) - (p.Y - a.Y) * (b.X - a.X);

    private static bool IsTopLeft(Vector2 a, Vector2 b)
        => a.Y == b.Y ? b.X > a.X : b.Y > a.Y;

    private static int ClampByte(float value) => (int)MathF.Round(Math.Clamp(value, 0, 255));
    private static int FloorToInt(float value) => (int)MathF.Floor(value);
    private static int CeilingToInt(float value) => (int)MathF.Ceiling(value);

    private readonly record struct Vertex(Vector2 Position, Vector2 Uv, uint Color);

    private readonly struct ClipRect
    {
        public ClipRect(Vector4 value, Vector2 displayPos, int frameWidth, int frameHeight)
        {
            MinX = Math.Clamp(CeilingToInt(value.X - displayPos.X), 0, frameWidth);
            MinY = Math.Clamp(CeilingToInt(value.Y - displayPos.Y), 0, frameHeight);
            MaxX = Math.Clamp(CeilingToInt(value.Z - displayPos.X), 0, frameWidth);
            MaxY = Math.Clamp(CeilingToInt(value.W - displayPos.Y), 0, frameHeight);
        }

        public int MinX { get; }
        public int MinY { get; }
        public int MaxX { get; }
        public int MaxY { get; }
    }
}

internal readonly record struct RenderStats(int DrawLists, int Triangles);

internal static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static void Write(string path, byte[] rgba, int width, int height)
    {
        if (rgba.Length != width * height * 4)
            throw new ArgumentException("RGBA buffer dimensions do not match.", nameof(rgba));

        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 4 + 1);
            raw[row] = 0; // PNG filter: None.
            Buffer.BlockCopy(rgba, y * width * 4, raw, row + 1, width * 4);
        }

        byte[] compressed;
        using (var input = new MemoryStream(raw))
        using (var output = new MemoryStream())
        {
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                input.CopyTo(zlib);
            compressed = output.ToArray();
        }

        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, (uint)width);
        WriteBigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8; // Bit depth.
        ihdr[9] = 6; // RGBA truecolor.
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;

        using var file = File.Create(path);
        file.Write(Signature);
        WriteChunk(file, "IHDR", ihdr);
        WriteChunk(file, "IDAT", compressed);
        WriteChunk(file, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        Span<byte> length = stackalloc byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        stream.Write(length);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xffffffffu;
        foreach (var value in type)
            crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        foreach (var value in data)
            crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        return crc ^ 0xffffffffu;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static void WriteBigEndian(Span<byte> target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }
}
