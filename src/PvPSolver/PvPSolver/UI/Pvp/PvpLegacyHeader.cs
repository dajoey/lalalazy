using System.Numerics;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace RotationSolver.UI.Pvp;

/// <summary>
///     Fork-only header band of the legacy (full) settings window: the brand chip, a "Legacy settings" label, the reset
///     control that used to sit in the native title bar, and the drawn close button, all from the shared popup chrome.
///     The window's own two-column content is drawn unchanged below it.
/// </summary>
internal static class PvpLegacyHeader
{
    internal const string Brand = "PvP Solver";
    internal const string Label = "Legacy settings";
    internal const string ResetLabel = "Reset";
    internal const string ResetTip = "Click to reset plugin configs";

    /// <summary>
    ///     Draws the band across the top of the current window, closes <paramref name="window"/> when the close button is
    ///     pressed, sets <paramref name="resetRequested"/> when Reset is pressed, and puts the cursor where the window's
    ///     content starts, below the band. Returns the band height.
    /// </summary>
    internal static float Draw(Window window, ref bool resetRequested)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 start = ImGui.GetCursorPos();
        Vector2 winPos = ImGui.GetWindowPos();
        float width = ImGui.GetWindowWidth();
        float gutter = PopupChrome.Gutter(width);
        const float control = PopupChrome.Control;
        const float gap = PopupChrome.Gap;
        float resetWidth = PopupChrome.ActionWidth(72f, ResetLabel);
        float labelWidth = PopupChrome.TextWidth(Label) + 32f;
        float chipWidth = PopupChrome.BrandWidth(Brand);

        // Right cluster, from the right edge: close, Reset, then the label while the chip, label and buttons all fit.
        float closeX = width - gutter - control;
        float resetX = closeX - gap - resetWidth;
        float labelX = resetX - gap - labelWidth;
        bool showLabel = gutter + chipWidth + 16f <= labelX;

        // The chip gives way to the controls on a very narrow window (it ellipsises its name).
        float nameRoom = resetX - gap - gutter - 48f - 16f;
        string name = PopupChrome.FitText(Brand, nameRoom, Theme.TitleScale);
        float bottom = PopupChrome.HeaderBand(draw, winPos, width, gutter, name, "##legacyClose", out bool close);

        float top = (14f + ((40f - control) / 2f));
        if (showLabel)
        {
            Vector2 pos = winPos + new Vector2(labelX, top);
            draw.AddRectFilled(pos, pos + new Vector2(labelWidth, control), Theme.Pack(Theme.Surface), control / 2f);
            draw.AddRect(pos, pos + new Vector2(labelWidth, control), Theme.Pack(Theme.SurfaceBorder), control / 2f);
            PopupChrome.Text(draw, pos + new Vector2(16f, (control - PopupChrome.LineHeight()) / 2f), Label, Theme.TextSecondary);
        }

        if (PopupChrome.ActionButton(draw, "##legacyReset", ResetLabel, winPos + new Vector2(resetX, top), new Vector2(resetWidth, control)))
        {
            resetRequested = true;
        }

        if (ImGui.IsItemHovered())
        {
            PopupChrome.Tip(ResetTip);
        }

        if (close)
        {
            window.IsOpen = false;
        }

        ImGui.SetCursorPos(new Vector2(start.X, bottom + start.Y));
        return bottom;
    }
}
