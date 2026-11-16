using System.Reflection;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Style;
using Dalamud.Interface.Utility;

namespace Lala.UiRender;

/// <summary>
/// Makes an ImGui context look like a live Dalamud one for the default configuration of a player: the default font
/// (Noto Sans CJK Medium, 17 px) built the way Dalamud's font atlas builds it, the FontAwesome icon font, the global
/// UI scale, and the "Dalamud Standard" style. The steps follow the Dalamud sources they cite; see the README next to
/// this file for the member-by-member list and for what is not reproduced.
/// </summary>
public static unsafe class GameLook
{
    /// <summary>Font files that must exist in the asset folder (Dalamud asset 2000 and asset 2003).</summary>
    public static readonly string[] RequiredFontFiles = ["NotoSansCJK-Medium.ttc", "FontAwesome710FreeSolid.otf"];

    /// <summary>Default font size in pixels (<c>DefaultFontSpec.SizePx</c> of a stock install).</summary>
    public const float DefaultFontSizePx = 17f;

    /// <summary>Icon font min/max glyph advance (<c>InterfaceManager.DefaultFontSizePx</c>, 12pt * 4/3), distinct from the 17px default font size.</summary>
    public const float IconAdvancePx = 16f;

    // Dalamud: FontAtlasFactory.FallbackCodepoints / EllipsisCodepoints.
    private const string FallbackCodepoints = "〓�?-";
    private const string EllipsisCodepoints = "…\u0085";

    public static RenderLook Apply(RenderOptions options)
    {
        if (options.AssetDirectory is null)
            throw new InvalidOperationException("Game look needs an asset directory.");
        var scale = options.Scale;
        var io = ImGui.GetIO();

        // InterfaceManager: style.Apply(), then FontGlobalScale = GlobalUiScale. Dalamud does not scale style metrics
        // with the global scale; only the font size follows it. Docking is off in a stock configuration.
        StyleModelV1.DalamudStandard.Apply();
        io.FontGlobalScale = scale;
        io.ConfigFlags &= ~ImGuiConfigFlags.DockingEnable;
        io.ConfigFlags &= ~ImGuiConfigFlags.ViewportsEnable;

        var atlas = io.Fonts;
        var rangeMemory = new List<IntPtr>();
        ImFontPtr defaultFont, iconFont;
        try
        {
            // Dalamud passes ranges [1, 0xFFFE] when the spec supplies none (BuildToolkit.AddFontFromImGuiHeapAllocatedMemory).
            defaultFont = AddFont(atlas, Path.Combine(options.AssetDirectory, RequiredFontFiles[0]), DefaultFontSizePx, scale,
                [1, 0xFFFE, 0], float.MaxValue, 0f, rangeMemory);

            // InterfaceManager.IconFontHandle: AddFontAwesomeIconFont with min/max advance pinned to the default size.
            var iconMin = (ushort)Enum.GetValues<FontAwesomeIcon>().Where(x => x > 0).Min();
            var iconMax = (ushort)Enum.GetValues<FontAwesomeIcon>().Where(x => x > 0).Max();
            iconFont = AddFont(atlas, Path.Combine(options.AssetDirectory, RequiredFontFiles[1]), DefaultFontSizePx, scale,
                [iconMin, iconMax, 0], IconAdvancePx, IconAdvancePx, rangeMemory);

            if (!atlas.Build())
                throw new InvalidOperationException("ImFontAtlas.Build failed for the game fonts.");
        }
        finally
        {
            foreach (var memory in rangeMemory)
                Marshal.FreeHGlobal(memory);
        }

        PostBuild(defaultFont, scale);
        PostBuild(iconFont, scale);
        ApplyDefaultSpecPostBuild(defaultFont, scale);
        BuildLookupTable(defaultFont);
        BuildLookupTable(iconFont);
        io.Handle->FontDefault = defaultFont.Handle;

        var textures = UiLook.CaptureTextures(atlas);
        var description = $"game: Dalamud Standard style, NotoSansCJK-Medium {DefaultFontSizePx:0.##}px x{scale:0.###} scale, FontAwesome icon font, " +
                          $"{textures.Count} atlas texture(s) {textures[0].Width}x{textures[0].Height}, kerning pairs {(KerningApplied ? "applied" : "not available")}, " +
                          $"effective font size {defaultFont.FontSize * io.FontGlobalScale:0.##}px, ImGuiHelpers.GlobalScale {ImGuiHelpers.GlobalScale:0.###}";
        return new RenderLook(options, defaultFont, iconFont, textures, description);
    }

    private static bool KerningApplied;

    // BuildToolkit.AddFontFromImGuiHeapAllocatedMemory + PreBuild (size, advance limits and offset are multiplied by the atlas scale).
    private static ImFontPtr AddFont(ImFontAtlasPtr atlas, string path, float sizePx, float scale, ushort[] ranges,
        float advanceMaxPx, float advanceMinPx, List<IntPtr> rangeMemory)
    {
        var bytes = File.ReadAllBytes(path);
        var memory = ImGuiHelpers.AllocateMemory(checked((nuint)bytes.Length));
        bytes.CopyTo(new Span<byte>(memory, bytes.Length));

        var config = new SafeFontConfig { SizePx = sizePx, GlyphMinAdvanceX = advanceMinPx, GlyphMaxAdvanceX = advanceMaxPx, FontNo = 0 };
        var raw = config.Raw;
        raw.FontData = memory;
        raw.FontDataOwnedByAtlas = true;
        raw.FontDataSize = bytes.Length;

        var rangeBlock = Marshal.AllocHGlobal(ranges.Length * sizeof(ushort));
        rangeMemory.Add(rangeBlock);
        ranges.AsSpan().CopyTo(new Span<ushort>((void*)rangeBlock, ranges.Length));
        raw.GlyphRanges = (ushort*)rangeBlock;

        raw.SizePixels *= scale;
        raw.GlyphMaxAdvanceX = ScaleLimit(raw.GlyphMaxAdvanceX, scale);
        raw.GlyphMinAdvanceX = ScaleLimit(raw.GlyphMinAdvanceX, scale);
        raw.GlyphOffset *= scale;

        var font = atlas.AddFont(raw.Handle);
        AddKerning(font, raw, rangeBlock, sizePx);
        return font;
    }

    private static float ScaleLimit(float value, float scale)
    {
        value *= scale;
        return float.IsInfinity(value) || float.IsNaN(value) ? (value > 0 ? float.MaxValue : -float.MaxValue) : value;
    }

    // Dalamud reads GPOS/kern pair adjustments with an internal helper and registers them on the font. The helper is
    // internal, so it is reached by reflection; when it cannot be found the harness continues without kerning.
    private static void AddKerning(ImFontPtr font, ImFontConfigPtr raw, IntPtr rangeBlock, float unscaledSizePx)
    {
        try
        {
            var type = typeof(FontAwesomeIcon).Assembly.GetType("Dalamud.Interface.ManagedFontAtlas.Internals.TrueTypeUtils");
            var method = type?.GetMethod("ExtractHorizontalPairAdjustments", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method is null)
                return;
            if (method.Invoke(null, [raw]) is not IEnumerable<(char Left, char Right, float Distance)> pairs)
                return;
            var ranges = (ushort*)rangeBlock;
            foreach (var pair in pairs.ToArray())
            {
                if (!InRanges(pair.Left, ranges) || !InRanges(pair.Right, ranges))
                    continue;
                font.AddKerningPair(pair.Left, pair.Right, pair.Distance * unscaledSizePx);
            }
            KerningApplied = true;
        }
        catch
        {
            // Kerning is a small metric refinement; a harness must not fail because Dalamud moved an internal.
        }
    }

    private static bool InRanges(char c, ushort* ranges)
    {
        for (; ranges[0] != 0; ranges += 2)
            if (c >= ranges[0] && c <= ranges[1])
                return true;
        return false;
    }

    // BuildToolkit.PostBuild: undo the atlas scale on glyph metrics, then pick fallback and ellipsis characters.
    private static void PostBuild(ImFontPtr font, float scale)
    {
        font.AdjustGlyphMetrics(1 / scale, 1 / scale);
        foreach (var c in FallbackCodepoints)
        {
            if (font.FindGlyphNoFallback(c) == null)
                continue;
            // ImGuiHelpers.UpdateFallbackChar (internal in Dalamud): record the fallback character and point the hot data at it.
            font.FallbackChar = c;
            font.Handle->FallbackHotData = (ImFontGlyphHotData*)((ImGuiHelpers.ImFontGlyphHotDataReal*)font.IndexedHotData.Data + font.FallbackChar);
            break;
        }

        foreach (var c in EllipsisCodepoints)
        {
            if (font.FindGlyphNoFallback(c) == null)
                continue;
            font.EllipsisChar = c;
            break;
        }
    }

    // SingleFontSpec.AddToBuildToolkit post-build step for the default spec (LineHeight 1, no glyph offset or letter
    // spacing): snap ascent and size to whole physical pixels.
    private static void ApplyDefaultSpecPostBuild(ImFontPtr font, float scale)
    {
        const float lineHeight = 1f;
        var roundUnit = 1 / scale;
        var newAscent = MathF.Round(font.Ascent * lineHeight / roundUnit) * roundUnit;
        var newFontSize = MathF.Round(font.FontSize * lineHeight / roundUnit) * roundUnit;
        var shiftDown = MathF.Round((newFontSize - font.FontSize) / 2f / roundUnit) * roundUnit;

        font.Ascent = newAscent;
        font.FontSize = newFontSize;
        font.Descent = newFontSize - font.Ascent;
        if (shiftDown == 0f)
            return;
        var delta = new System.Numerics.Vector4(0, shiftDown, 0, shiftDown);
        foreach (ref var glyph in font.GlyphsWrapped().DataSpan)
            glyph.XY += delta;
    }

    // BuildToolkit.BuildLookupTable, including the fix for missing-glyph advances in the custom ImGui.
    private static void BuildLookupTable(ImFontPtr font)
    {
        font.Handle->FallbackGlyph = null;
        font.Handle->FallbackHotData = null;
        font.BuildLookupTable();

        var indexedHotData = font.IndexedHotDataWrapped();
        var indexLookup = font.IndexLookupWrapped();
        ref var fallbackHotData = ref *(ImGuiHelpers.ImFontGlyphHotDataReal*)font.Handle->FallbackHotData;
        for (var codepoint = 0; codepoint < indexedHotData.Length; codepoint++)
        {
            if (indexLookup[codepoint] == ushort.MaxValue)
            {
                indexedHotData[codepoint].AdvanceX = fallbackHotData.AdvanceX;
                indexedHotData[codepoint].OccupiedWidth = fallbackHotData.OccupiedWidth;
            }
        }
    }
}
