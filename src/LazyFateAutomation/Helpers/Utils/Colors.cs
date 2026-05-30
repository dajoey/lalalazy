using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace LazyFateAutomation.Helpers.Utils;

public static class Colors {
    public static EzColor Gold { get; } = new(0.847f, 0.733f, 0.49f);
    public static EzColor Grey { get; } = new(0.73f, 0.73f, 0.73f);
    public static EzColor Grey2 { get; } = new(0.87f, 0.87f, 0.87f);
    public static EzColor Grey3 { get; } = new(0.6f, 0.6f, 0.6f);
    public static EzColor Grey4 { get; } = new(0.3f, 0.3f, 0.3f);
    public static EzColor Type { get; } = new(0.2f, 0.9f, 0.9f);
    public static EzColor Field { get; } = new(0.2f, 0.9f, 0.4f);

    public static EzColor Positive { get; } = new(0.22f, 0.45f, 0.24f);
    public static EzColor PositiveHover { get; } = new(0.27f, 0.53f, 0.29f);
    public static EzColor PositiveActive { get; } = new(0.19f, 0.39f, 0.21f);
    public static EzColor Negative { get; } = new(0.55f, 0.2f, 0.2f);
    public static EzColor NegativeHover { get; } = new(0.62f, 0.24f, 0.24f);
    public static EzColor NegativeActive { get; } = new(0.5f, 0.18f, 0.18f);
    public static EzColor ChipPositive { get; } = new(0.16f, 0.34f, 0.2f);
    public static EzColor ChipMuted { get; } = new(0.25f, 0.25f, 0.25f);
    public static EzColor ChipGold { get; } = new(0.42f, 0.35f, 0.2f);
    public static EzColor ChipPrimary { get; } = new(0.25f, 0.22f, 0.37f);
    public static EzColor ChipInfo { get; } = new(0.18f, 0.3f, 0.4f);

    public static unsafe bool IsLightTheme
        => RaptureAtkModule.Instance()->AtkUIColorHolder.ActiveColorThemeType == 1;
}
