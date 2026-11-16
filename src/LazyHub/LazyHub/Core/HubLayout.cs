namespace LazyHub.Core;

public readonly record struct HubCell(float X, float Y, float Width, float Height);

public readonly record struct HubRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool Overlaps(HubRect other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
    public bool Contains(HubRect inner) => inner.X >= X && inner.Y >= Y && inner.Right <= Right && inner.Bottom <= Bottom;
}

/// <summary>Where the parts of a row sit inside its card. Unused parts are the empty rectangle.</summary>
public readonly record struct HubRowParts(HubRect Icon, HubRect Text, HubRect ButtonA, HubRect Value, HubRect ButtonB);

/// <summary>
/// Geometry of the native hub, in window-content units on a 4 unit grid. Every position and size the window uses comes
/// from here, so the harness can check alignment, overlap and fit without the game. No Dalamud types.
/// </summary>
public static class HubLayout
{
    public const float TwoColumnMinWidth = 700f;
    public const float DefaultGap = 12f;

    // ---- shell --------------------------------------------------------------------------------------
    public const float Unit = 4f;
    public const float Radius = 8f;
    public const float Control = 36f;
    public const float HeaderHeight = 68f;
    public const float NavHeight = 56f;
    public const float BrandWidth = 144f;
    public const float BrandHeight = 40f;
    public const float StatusPillWidth = 160f;
    public const float NavPillWidth = 168f;
    public const float NavPillHeight = 32f;
    public const float NavPillGap = 12f;
    public const float TileSize = 44f;
    public const float PageTop = HeaderHeight + NavHeight + 20f;
    public const float BodyTop = PageTop + TileSize + 16f;
    public const float FooterHeight = 60f;
    public const float MetaWidth = 320f;

    /// <summary>Window frame chrome (title bar and padding) around the content area; an estimate read from an in-game capture.</summary>
    public const float FrameOverheadY = 52f;

    // ---- cards and controls -------------------------------------------------------------------------
    public const float CardPad = 16f;
    public const float RowHeight = 56f;
    public const float ColumnGap = 12f;
    public const float RowGap = 8f;
    public const float TrayHeight = 212f;
    public const float TrayLeading = TrayHeight + 16f;
    public const float ButtonHeight = 32f;
    public const float ButtonMinWidth = 96f;
    public const float SmallButtonWidth = 72f;
    public const float StepButtonWidth = 40f;
    public const float ValueWidth = 104f;
    public const float ToggleWidth = 44f;
    public const float ToggleHeight = 24f;
    public const float KnobSize = 18f;
    public const float KnobInset = 3f;
    public const float IconSize = 32f;
    public const float GroupHeight = 32f;
    public const float ScrollPadBottom = 8f;

    /// <summary>Wide estimate of a label's pixel width in the button font (about 8 per character): leaves margin over the real metrics.</summary>
    public const float GlyphWidthEstimate = 8f;

    /// <summary>The native text button reserves this much of its width for its own padding (16 each side).</summary>
    public const float ButtonLabelPad = 32f;

    public static float Gutter(float windowWidth) => windowWidth >= 760f ? 24f : windowWidth >= 560f ? 16f : 12f;

    public static float InnerWidth(float windowWidth) => MathF.Max(120f, windowWidth - (2f * Gutter(windowWidth)));

    public static int Columns(float availableWidth, bool forceSingle = false)
        => !forceSingle && availableWidth >= TwoColumnMinWidth ? 2 : 1;

    public static float ColumnWidth(float availableWidth, int columns, float gap = DefaultGap)
    {
        columns = Math.Max(1, columns);
        var width = (MathF.Max(0, availableWidth) - ((columns - 1) * gap)) / columns;
        return MathF.Max(40, width);
    }

    /// <summary>
    /// A grid cell. Column widths are whole units and equal; the last column is pushed to the right edge, so both
    /// columns end exactly where the available width ends. <paramref name="rowGap"/> defaults to <paramref name="gap"/>.
    /// </summary>
    public static HubCell Cell(int index, float availableWidth, int columns, float rowHeight, float gap = DefaultGap, float leading = 0,
        float left = 0, float? rowGap = null)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        columns = Math.Max(1, columns);
        var width = MathF.Floor(ColumnWidth(availableWidth, columns, gap));
        var column = index % columns;
        var row = index / columns;
        var x = column == columns - 1 ? MathF.Max(0, availableWidth) - width : column * (width + gap);
        if (columns == 1) x = 0;
        return new HubCell(left + x, leading + (row * (rowHeight + (rowGap ?? gap))), width, rowHeight);
    }

    public static float RowsHeight(int itemCount, int columns, float rowHeight, float gap = DefaultGap, float leading = 0, float? rowGap = null)
    {
        if (itemCount <= 0) return leading;
        columns = Math.Max(1, columns);
        var rows = (itemCount + columns - 1) / columns;
        return leading + (rows * rowHeight) + ((rows - 1) * (rowGap ?? gap));
    }

    // ---- header, navigation, page header, footer ----------------------------------------------------

    public static HubRect Brand(float width) => new(Gutter(width), 14f, BrandWidth, BrandHeight);
    public static HubRect StatusPill(float width) => new(width - Gutter(width) - StatusPillWidth, 16f, StatusPillWidth, Control);
    public static HubRect NavPill(int index, float width) => new(Gutter(width) + (index * (NavPillWidth + NavPillGap)), HeaderHeight + 12f, NavPillWidth, NavPillHeight);
    public static HubRect NavBack(float width) => new(width - Gutter(width) - NavPillWidth, HeaderHeight + 12f, NavPillWidth, NavPillHeight);
    public static HubRect PageTile(float width) => new(Gutter(width), PageTop, TileSize, TileSize);
    public static HubRect PageTitle(float width) => new(Gutter(width) + 60f, PageTop, MathF.Max(80f, width - (2f * Gutter(width)) - 60f - MetaWidth - 16f), 24f);
    public static HubRect PageDescription(float width) => new(Gutter(width) + 60f, PageTop + 26f, MathF.Max(80f, width - (2f * Gutter(width)) - 60f), 18f);
    public static HubRect PageMeta(float width) => new(width - Gutter(width) - MetaWidth, PageTop + 3f, MetaWidth, 18f);

    /// <summary>Height of the scrolling body for a window content height.</summary>
    public static float BodyHeight(float contentHeight) => MathF.Max(120f, contentHeight - BodyTop - FooterHeight);

    public static HubRect Footer(float width, float contentHeight) => new(0f, MathF.Max(BodyTop, contentHeight - FooterHeight), width, FooterHeight);

    /// <summary>The footer's status mark and text lines; without a second line the first one is centred.</summary>
    public static (HubRect Mark, HubRect First, HubRect Second) FooterLines(HubRect footer, float width, bool hasSecond)
    {
        var g = Gutter(width);
        var firstY = footer.Y + (hasSecond ? 10f : 20f);
        var textWidth = MathF.Max(80f, width - (2f * g) - 16f);
        return (new HubRect(footer.X + g, firstY, 14f, 20f),
            new HubRect(footer.X + g + 16f, firstY, textWidth, 20f),
            new HubRect(footer.X + g + 16f, footer.Y + 32f, textWidth, 18f));
    }

    /// <summary>The window height whose body just holds the Quick controls page with the given number of plugin rows.</summary>
    public static float WindowHeightForQuick(int pluginCount, float width = 1080f)
    {
        var columns = Columns(InnerWidth(width));
        return FrameOverheadY + BodyTop + RowsHeight(pluginCount, columns, RowHeight, ColumnGap, TrayLeading, RowGap) + ScrollPadBottom + FooterHeight;
    }

    // ---- buttons ------------------------------------------------------------------------------------

    /// <summary>A text button wide enough for its label: the native button keeps 16 on each side, so the label gets width - 32.</summary>
    public static float ButtonWidth(string label, float minimum = ButtonMinWidth)
    {
        var needed = ButtonLabelPad + MathF.Ceiling((label?.Length ?? 0) * GlyphWidthEstimate);
        return MathF.Max(minimum, MathF.Ceiling(needed / Unit) * Unit);
    }

    public static bool LabelFits(string label, float buttonWidth)
        => (label?.Length ?? 0) * GlyphWidthEstimate <= buttonWidth - ButtonLabelPad;

    // ---- tray ---------------------------------------------------------------------------------------

    /// <summary>One of the hotbar buttons: four columns, equal widths, the last column ending on the card's inner edge.</summary>
    public static HubRect TrayButton(int index, float width)
    {
        var g = Gutter(width);
        var inner = InnerWidth(width) - (2f * CardPad);
        const float gap = 8f;
        var cell = Cell(index % 4, inner, 4, ButtonHeight, gap, 0f, g + CardPad);
        return new HubRect(cell.X, 124f + ((index / 4) * (ButtonHeight + gap)), cell.Width, ButtonHeight);
    }

    // ---- row interiors ------------------------------------------------------------------------------

    private static HubRect Slot(HubCell c, float x, float width, float height)
        => new(x, c.Y + MathF.Floor((c.Height - height) / 2f), width, height);

    /// <summary>The text column: a name over a note, or the name alone filling the row's height.</summary>
    public static (HubRect Label, HubRect Info) TextLines(HubRect text, bool hasInfo)
        => hasInfo
            ? (new HubRect(text.X, text.Y + 10f, text.Width, 20f), new HubRect(text.X, text.Y + 30f, text.Width, 16f))
            : (text, new HubRect(text.X, text.Y + 30f, text.Width, 16f));

    /// <summary>Quick controls row: icon, name and note, a Settings button and the switch.</summary>
    public static HubRowParts MasterRow(HubCell c)
    {
        var toggle = Slot(c, c.X + c.Width - CardPad - ToggleWidth, ToggleWidth, ToggleHeight);
        var settings = Slot(c, toggle.X - 12f - ButtonWidth("Settings"), ButtonWidth("Settings"), ButtonHeight);
        var icon = Slot(c, c.X + CardPad, IconSize, IconSize);
        var textX = icon.Right + 12f;
        return new HubRowParts(icon, new HubRect(textX, c.Y, MathF.Max(40f, settings.X - 12f - textX), c.Height), settings, default, toggle);
    }

    /// <summary>Plugin library row: icon, name and state, a Settings button and an Open button.</summary>
    public static HubRowParts LibraryRow(HubCell c)
    {
        var open = Slot(c, c.X + c.Width - CardPad - ButtonWidth("Open", SmallButtonWidth), ButtonWidth("Open", SmallButtonWidth), ButtonHeight);
        var settings = Slot(c, open.X - 8f - ButtonWidth("Settings"), ButtonWidth("Settings"), ButtonHeight);
        var icon = Slot(c, c.X + CardPad, IconSize, IconSize);
        var textX = icon.Right + 12f;
        return new HubRowParts(icon, new HubRect(textX, c.Y, MathF.Max(40f, settings.X - 12f - textX), c.Height), settings, default, open);
    }

    /// <summary>Detail page switch row: name and note, the switch on the right.</summary>
    public static HubRowParts ToggleRow(HubCell c)
    {
        var toggle = Slot(c, c.X + c.Width - CardPad - ToggleWidth, ToggleWidth, ToggleHeight);
        var textX = c.X + CardPad;
        return new HubRowParts(default, new HubRect(textX, c.Y, MathF.Max(40f, toggle.X - 12f - textX), c.Height), default, default, toggle);
    }

    /// <summary>Detail page stepper or choice row: name and note, then minus, value, plus.</summary>
    public static HubRowParts StepRow(HubCell c)
    {
        var plus = Slot(c, c.X + c.Width - CardPad - StepButtonWidth, StepButtonWidth, ButtonHeight);
        var value = Slot(c, plus.X - 8f - ValueWidth, ValueWidth, 20f);
        var minus = Slot(c, value.X - 8f - StepButtonWidth, StepButtonWidth, ButtonHeight);
        var textX = c.X + CardPad;
        return new HubRowParts(default, new HubRect(textX, c.Y, MathF.Max(40f, minus.X - 12f - textX), c.Height), minus, value, plus);
    }

    /// <summary>Detail page action row: name and note, one button on the right.</summary>
    public static HubRowParts ActionRow(HubCell c)
    {
        var width = ButtonWidth("Confirm?");
        var button = Slot(c, c.X + c.Width - CardPad - width, width, ButtonHeight);
        var textX = c.X + CardPad;
        return new HubRowParts(default, new HubRect(textX, c.Y, MathF.Max(40f, button.X - 12f - textX), c.Height), button, default, default);
    }
}
