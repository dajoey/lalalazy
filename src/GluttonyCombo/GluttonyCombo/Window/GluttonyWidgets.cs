using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace GluttonyCombo.Window;

// Fork: the plugin's own text and notice widgets. A type in this namespace takes precedence over the shared kit's
// Widgets for every file under GluttonyCombo.Window, so the upstream-owned pages that call Widgets.Title,
// Widgets.Banner and friends pick up the approved section headings, notices and card-aware wrapping without a line
// of those files changing. Members the plugin does not restyle forward to the kit unchanged; a new kit member an
// upstream merge starts to use fails to compile here until it is forwarded.
internal static class Widgets
{
    /// <summary>Section heading with the right space above and below.</summary>
    public static void Title(string text) => GluttonyChrome.Heading(text);

    public static void Body(string text) => LalaUiKit.Widgets.Body(text);

    public static void Secondary(string text) => LalaUiKit.Widgets.Secondary(text);

    public static void Caption(string text) => LalaUiKit.Widgets.Caption(text);

    /// <summary>Secondary text that wraps at the current card's inner edge.</summary>
    public static void SecondaryWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        GluttonyChrome.WrappedText(text);
        ImGui.PopStyleColor();
    }

    public static void StatusDot(UiState state, float radius = Theme.DotRadius) => LalaUiKit.Widgets.StatusDot(state, radius);

    public static void StatusPill(UiState state, string feature, string? detail = null, bool alignRight = false) =>
        LalaUiKit.Widgets.StatusPill(state, feature, detail, alignRight);

    /// <summary>Notice with the suite's rounded, tinted look.</summary>
    public static void InfoBox(string text, UiState tone = UiState.Idle) => GluttonyChrome.Notice(tone, text);

    /// <summary>Notice with the suite's rounded, tinted look.</summary>
    public static void Banner(UiState state, string headline, string? detail = null) => GluttonyChrome.Notice(state, headline, detail);

    public static void IconTile(ImTextureID? icon, string? glyph, Vector2 size, bool selected) =>
        LalaUiKit.Widgets.IconTile(icon, glyph, size, selected);

    public static bool Checkbox(string label, ref bool value) => LalaUiKit.Widgets.Checkbox(label, ref value);

    public static void DragGhost(ImTextureID? icon, Vector2 screenPos, float size) =>
        LalaUiKit.Widgets.DragGhost(icon, screenPos, size);

    public static void StateText(string text, UiState state) => LalaUiKit.Widgets.StateText(text, state);
}
