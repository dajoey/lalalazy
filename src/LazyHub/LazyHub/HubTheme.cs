namespace LazyHub;

using LalaUiKit;
using LazyHub.Core;

/// <summary>
/// The LazyHub bridge to the shared LalaUiKit (Projects/UI Refresh 2026-11/UI Brief): every colour and font size the
/// native window draws resolves here, and here only to kit values - no ad-hoc colours, no local palette. Text colours
/// are RGBA 0..1; panel colours are the same. Contrast (R8): TextSecondary and TextCaption clear 4.5:1 on the kit
/// surfaces; the old green-grey Dim (about 3.7:1) is gone.
/// </summary>
internal static class HubTheme
{
    // Text (kit neutrals, R8). TextCaption is TextSecondary in the kit: caption text keeps full contrast.
    public static readonly Vector4 White = Theme.TextPrimary;
    public static readonly Vector4 Grey = Theme.TextSecondary;
    public static readonly Vector4 Dim = Theme.TextCaption;

    // States (kit reserved colours, R4): green = ready, amber = waiting/testing, red = stop/failed only.
    public static readonly Vector4 Green = Theme.StateGreen;
    public static readonly Vector4 Amber = Theme.StateAmber;
    public static readonly Vector4 Red = Theme.StateRed;

    // Panels (kit surfaces).
    public static readonly Vector4 Backdrop = Theme.ShellBar;
    public static readonly Vector4 Tray = Theme.Surface;

    // The one accent (R4): the selected/active tab underline.
    public static readonly Vector4 Accent = Theme.Accent;

    /// <summary>
    /// Zeroed: the stock window chrome keeps its own look while the content nodes carry the kit colours. (Each
    /// channel 0..1, may be negative.) Only touch this if the chrome must be tinted again.
    /// </summary>
    public static readonly Vector3 ChromeTint = new(0f, 0f, 0f);

    // R7 type scale at the native 14 px base: the four kit scales rounded to whole pixels.
    public const uint FontTitle = 16;      // round(1.15 x 14)
    public const uint FontBody = 14;       // 1.00 x 14
    public const uint FontSecondary = 13;  // round(0.90 x 14)
    public const uint FontCaption = 12;    // round(0.85 x 14)

    /// <summary>The status word for a plugin state (R5, via the kit Copy vocabulary).</summary>
    public static string StatusText(PluginState state) => state switch
    {
        PluginState.Loaded => Copy.Word(UiState.Ready),
        PluginState.LoadedTesting => Copy.WithReason(Copy.Word(UiState.Ready), "testing"),
        PluginState.InstalledNotLoaded => Copy.Word(UiState.NeedsSetup),
        _ => Copy.Word(UiState.Missing),
    };

    /// <summary>The kit mark colour for a plugin state (R4/R5).</summary>
    public static Vector4 StateColor(PluginState state) => state switch
    {
        PluginState.Loaded => Copy.MarkColor(UiState.Ready),
        PluginState.LoadedTesting => Theme.StateAmber,
        PluginState.InstalledNotLoaded => Copy.MarkColor(UiState.NeedsSetup),
        _ => Copy.MarkColor(UiState.Missing),
    };

    /// <summary>
    /// Word + mark colour for a Gluttony auto-rotation reading (R4/R5), via the kit Copy mapping: on shows Running
    /// (the kit marks running amber - green is only for Ready), off shows Idle (neutral), unreadable shows Blocked
    /// with a reason (amber - the fix for the old red, which R4 reserves for stop/failed), and a not-loaded Gluttony
    /// Combo is Missing.
    /// </summary>
    public static (string Text, Vector4 Color) GluttonyStatus(GluttonyProbe.Reading reading) => reading switch
    {
        GluttonyProbe.Reading.On => (Copy.Word(UiState.Running), Copy.MarkColor(UiState.Running)),
        GluttonyProbe.Reading.Off => (Copy.Word(UiState.Idle), Copy.MarkColor(UiState.Idle)),
        GluttonyProbe.Reading.Unavailable => (Copy.WithReason(Copy.Word(UiState.Blocked), "state not readable"), Copy.MarkColor(UiState.Blocked)),
        _ => (Copy.Missing("Gluttony Combo"), Copy.MarkColor(UiState.Missing)),
    };
}
