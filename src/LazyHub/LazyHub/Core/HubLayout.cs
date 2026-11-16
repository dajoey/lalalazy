namespace LazyHub.Core;

public readonly record struct HubCell(float X, float Y, float Width, float Height);

/// <summary>Responsive grid measurements for the native hub. No Dalamud types.</summary>
public static class HubLayout
{
    public const float TwoColumnMinWidth = 700f;
    public const float DefaultGap = 12f;

    public static int Columns(float availableWidth, bool forceSingle = false)
        => !forceSingle && availableWidth >= TwoColumnMinWidth ? 2 : 1;

    public static float ColumnWidth(float availableWidth, int columns, float gap = DefaultGap)
    {
        columns = Math.Max(1, columns);
        var width = (MathF.Max(0, availableWidth) - ((columns - 1) * gap)) / columns;
        return MathF.Max(40, width);
    }

    public static HubCell Cell(int index, float availableWidth, int columns, float rowHeight, float gap = DefaultGap, float leading = 0)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        columns = Math.Max(1, columns);
        var width = ColumnWidth(availableWidth, columns, gap);
        var column = index % columns;
        var row = index / columns;
        return new HubCell(column * (width + gap), leading + (row * (rowHeight + gap)), width, rowHeight);
    }

    public static float RowsHeight(int itemCount, int columns, float rowHeight, float gap = DefaultGap, float leading = 0)
    {
        if (itemCount <= 0) return leading;
        columns = Math.Max(1, columns);
        var rows = (itemCount + columns - 1) / columns;
        return leading + (rows * rowHeight) + ((rows - 1) * gap);
    }
}
