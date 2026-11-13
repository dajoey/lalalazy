// LalaUiKit — frozen file set per Projects/UI Refresh 2026-11/UI Brief ("Kit shape").
// Shared SOURCE compiled into each plugin via a csproj Compile link — never a shared DLL
// (each plugin runs in its own AssemblyLoadContext). Deps: the ImGui bindings and
// System.Numerics only, so the kit compiles into any lalalazy plugin unchanged.

namespace LalaUiKit;

using System.Numerics;
using Dalamud.Bindings.ImGui;

/// <summary>
///     Palette, type scale and spacing for the whole suite. One accent, reserved state
///     colours, 4 px grid (Brief R4, R7, R8, R9). Windows consume these constants; they
///     never set ad-hoc colours, font sizes or spacing.
/// </summary>
public static class Theme
{
    // ---- R4: ONE accent. Pink #F48FB1, frozen at the Brief's linear vec4. Used only for
    // interactive or on things: toggles, tile icons, the selected/active outline.
    public static readonly Vector4 Accent = new(0.957f, 0.560f, 0.694f, 1.0f);
    public static readonly Vector4 AccentHover = new(0.957f, 0.560f, 0.694f, 0.85f);
    public static readonly Vector4 AccentActive = new(0.957f, 0.560f, 0.694f, 0.70f);

    // ---- R4: state colours, reserved meanings. green = Ready/done, amber = in-progress or
    // waiting, red = Stop/Failed only (never progress). Neutral = no state meaning.
    public static readonly Vector4 StateGreen = Srgb(0x66, 0xBB, 0x6A);
    public static readonly Vector4 StateAmber = Srgb(0xFF, 0xB7, 0x4D);
    public static readonly Vector4 StateRed = Srgb(0xEF, 0x53, 0x50);
    public static readonly Vector4 StateNeutral = Srgb(0x9E, 0x9E, 0x9E);

    // ---- R8: neutrals. TextSecondary/TextCaption must reach 4.5:1 against the window
    // background AND the card surface — the harness measures the rendered values, so do not
    // dim these to "subtle grey". They are also pushed into ImGuiCol.TextDisabled so every
    // existing TextDisabled call in converted windows reads instead of fading out.
    public static readonly Vector4 TextPrimary = new(1f, 1f, 1f, 1f);
    public static readonly Vector4 TextSecondary = Srgb(0xC7, 0xCB, 0xD1);
    public static readonly Vector4 TextCaption = TextSecondary;

    public static readonly Vector4 Surface = Srgb(0x1B, 0x1D, 0x21);   // card / rail background
    public static readonly Vector4 SurfaceBorder = Srgb(0x33, 0x36, 0x3C);
    public static readonly Vector4 ShellBar = Srgb(0x14, 0x16, 0x1A);  // top bar / footer strip

    // ---- R7: type scale, multiples of the base font size. Four named sizes, nothing else.
    public const float TitleScale = 1.15f;
    public const float BodyScale = 1.00f;
    public const float SecondaryScale = 0.90f;
    public const float CaptionScale = 0.85f;

    // ---- R9: 4 px base unit; padding and gaps in multiples of 4. The kit sets window
    // padding, item spacing and indents — never the per-window code.
    public const float Unit = 4f;
    public static readonly Vector2 WindowPadding = new(3 * Unit, 3 * Unit);
    public static readonly Vector2 FramePadding = new(2 * Unit, Unit);
    public static readonly Vector2 ItemSpacing = new(2 * Unit, 2 * Unit);
    public static readonly Vector2 ItemInnerSpacing = new(2 * Unit, Unit);
    public const float IndentSpacing = 4 * Unit;
    public const float CardMinHeight = 21 * Unit;
    public const float BarHeight = Unit;
    public const float DotRadius = 3f;
    public const float PillHeight = 6 * Unit;
    public const float NavTileSize = 6 * Unit;

    /// <summary>sRGB bytes → the float colour the renderer draws directly.</summary>
    public static Vector4 Srgb(byte r, byte g, byte b, byte a = 255) =>
        new(r / 255f, g / 255f, b / 255f, a / 255f);

    /// <summary>WCAG 2.x contrast ratio between two colours — R8's measure, checked on rendered pages.</summary>
    public static float ContrastRatio(Vector4 a, Vector4 b)
    {
        static float Channel(float c) => c <= 0.03928f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        static float Luma(Vector4 c) => (0.2126f * Channel(c.X)) + (0.7152f * Channel(c.Y)) + (0.0722f * Channel(c.Z));
        var la = Luma(a);
        var lb = Luma(b);
        var hi = MathF.Max(la, lb);
        var lo = MathF.Min(la, lb);
        return (hi + 0.05f) / (lo + 0.05f);
    }

    /// <summary>
    ///     Push the suite's colour + spacing frame (R9: the kit sets these, windows never do).
    ///     Pair with <see cref="PopFrame"/> — Layout.Shell does both for converted windows.
    /// </summary>
    public static void PushFrame()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, WindowPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, FramePadding);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ItemSpacing);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, ItemInnerSpacing);
        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, IndentSpacing);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, TextSecondary);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Accent);
    }

    public static void PopFrame()
    {
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(5);
    }

    /// <summary>Pack a Vector4 colour into the u32 the draw list takes.</summary>
    public static uint Pack(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);
}
