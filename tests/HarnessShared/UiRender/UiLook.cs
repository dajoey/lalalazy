using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;

namespace Lala.UiRender;

/// <summary>
/// Everything a harness needs after <see cref="UiLook.Apply"/>: the fonts that were built, the atlas textures the
/// rasterizer must be given, and a glyph-coverage helper.
/// </summary>
public sealed class RenderLook
{
    internal RenderLook(RenderOptions options, ImFontPtr defaultFont, ImFontPtr iconFont, IReadOnlyList<AtlasTexture> textures, string description)
    {
        Options = options;
        DefaultFont = defaultFont;
        IconFont = iconFont;
        Textures = textures;
        Description = description;
    }

    public RenderOptions Options { get; }

    /// <summary>The font ImGui draws with when nothing is pushed.</summary>
    public ImFontPtr DefaultFont { get; }

    /// <summary>FontAwesome as Dalamud exposes it to plugins (null handle in built-in mode).</summary>
    public ImFontPtr IconFont { get; }

    /// <summary>Every atlas texture, with the id its draw commands carry. Pass to <see cref="CpuRasterizer.Render(ImDrawDataPtr, byte[], int, int, IReadOnlyList{AtlasTexture})"/>.</summary>
    public IReadOnlyList<AtlasTexture> Textures { get; }

    /// <summary>One line describing what was set up, for the harness log.</summary>
    public string Description { get; }

    /// <summary>
    /// Distinct non-whitespace characters of <paramref name="text"/> that have no glyph in the default font, i.e. that
    /// ImGui would draw as the fallback glyph. In game mode the Dalamud game-symbol glyphs (private use area) count as
    /// missing because the harness has no access to the game's own font files.
    /// </summary>
    public IReadOnlyList<string> MissingGlyphs(string text) => GlyphCoverage.Missing(DefaultFont, text);
}

public static class GlyphCoverage
{
    /// <summary>Distinct characters of <paramref name="text"/> without a glyph in <paramref name="font"/>, as "U+XXXX 'c'" strings.</summary>
    public static unsafe IReadOnlyList<string> Missing(ImFontPtr font, string text)
    {
        var missing = new SortedDictionary<int, string>();
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value <= 0x20 || (value >= 0x7F && value <= 0xA0) || missing.ContainsKey(value))
                continue;
            if (value <= ushort.MaxValue && font.FindGlyphNoFallback((ushort)value) != null)
                continue;
            missing[value] = $"U+{value:X4} '{rune}'";
        }
        return missing.Values.ToList();
    }
}

/// <summary>Sets up an ImGui context's fonts and style for the selected <see cref="RenderFidelity"/>.</summary>
public static class UiLook
{
    /// <summary>First atlas texture id handed out; further textures use consecutive ids.</summary>
    public const long FirstTextureId = 1;

    /// <summary>
    /// Call once per context, after <c>ImGui.CreateContext()</c> and before the first <c>NewFrame()</c>. Built-in mode
    /// applies ImGui's dark style and its embedded font; game mode applies <see cref="GameLook"/>.
    /// </summary>
    public static RenderLook Apply(RenderOptions options)
    {
        if (options.IsGame)
            return GameLook.Apply(options);

        ImGui.StyleColorsDark();
        var fonts = ImGui.GetIO().Fonts;
        var font = fonts.AddFontDefault();
        var textures = CaptureTextures(fonts);
        return new RenderLook(options, font, default, textures, "built-in: ImGui embedded font and default dark style");
    }

    /// <summary>
    /// Copies every atlas texture out as RGBA32 and registers consecutive texture ids on the atlas. Whatever pixel
    /// format the atlas holds is converted by ImGui; a texture that does not come back as 4 bytes per pixel is rejected.
    /// </summary>
    internal static unsafe IReadOnlyList<AtlasTexture> CaptureTextures(ImFontAtlasPtr fonts)
    {
        // The first call builds the atlas when it has not been built yet.
        byte* pixels = null;
        int width = 0, height = 0, bytesPerPixel = 0;
        fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height, &bytesPerPixel);
        var count = Math.Max(1, fonts.Textures.Size);
        var result = new List<AtlasTexture>(count);
        for (var index = 0; index < count; index++)
        {
            pixels = null;
            width = height = bytesPerPixel = 0;
            fonts.GetTexDataAsRGBA32(index, &pixels, &width, &height, &bytesPerPixel);
            if (pixels == null || bytesPerPixel != 4 || width <= 0 || height <= 0)
                throw new InvalidOperationException($"Font atlas texture {index} is not a valid RGBA32 image ({width}x{height}, {bytesPerPixel} bytes per pixel).");
            var copy = new byte[checked(width * height * 4)];
            Marshal.Copy((IntPtr)pixels, copy, 0, copy.Length);
            var id = new IntPtr(FirstTextureId + index);
            fonts.SetTexID(index, new ImTextureID(id));
            result.Add(new AtlasTexture(id, copy, width, height));
        }
        return result;
    }
}
