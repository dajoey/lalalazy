namespace LazyHub;

/// <summary>
/// The lalalazy colours (docs/site.css and LalaImages/STYLE.md) as native-node values, plus the numbers a look pass
/// is likely to tune. Text colours are RGBA 0..1; panel colours are the same.
/// </summary>
internal static class HubTheme
{
    // Text
    public static readonly Vector4 White = new(0.95f, 0.97f, 0.95f, 1f);
    public static readonly Vector4 Green = new(0.435f, 0.851f, 0.498f, 1f);   // #6fd97f, on
    public static readonly Vector4 Amber = new(0.910f, 0.647f, 0.282f, 1f);   // #e8a548, testing / confirm
    public static readonly Vector4 Grey = new(0.62f, 0.70f, 0.66f, 1f);
    public static readonly Vector4 Dim = new(0.42f, 0.48f, 0.45f, 1f);
    public static readonly Vector4 Gold = new(0.910f, 0.757f, 0.282f, 1f);    // #E8C148, the brand gold
    public static readonly Vector4 Red = new(0.91f, 0.54f, 0.54f, 1f);

    // Flat panels (drawn as stretched solid textures)
    public static readonly Vector4 Backdrop = new(0.059f, 0.137f, 0.094f, 1f);   // #0f2318 forest
    public static readonly Vector4 Tray = new(0.086f, 0.188f, 0.122f, 1f);       // #16301f
    public static readonly Vector4 GoldLine = Gold;

    /// <summary>
    /// Added to the stock window's background and border nodes (each channel 0..1, may be negative) to pull the stock blue
    /// toward forest green. A first guess, not previewable outside the game: tune here. Zero it for the stock look.
    /// </summary>
    public static readonly Vector3 ChromeTint = new(0.02f, 0.10f, -0.15f);

    public static Vector4 ForState(Lalalazy.Hub.ParsedState? st, bool usable)
        => st == null ? Dim : st.Locked ? Amber : !usable ? Dim : Grey;
}
