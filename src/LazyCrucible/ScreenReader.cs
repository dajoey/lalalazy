using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static ECommons.GenericHelpers;

namespace LazyCrucible;

/// <summary> Live addon reads for the selection screens: visibility and AtkValues as <see cref="AtkCells"/>. Read-only. </summary>
internal static unsafe class ScreenReader
{
    public static bool TryGet(string name, out AtkUnitBase* addon)
    {
        addon = null;
        try
        {
            if (!TryGetAddonByName(name, out addon) || addon is null || !addon->IsVisible)
            {
                addon = null;
                return false;
            }
            return true;
        }
        catch
        {
            addon = null;
            return false;
        }
    }

    /// <summary> Visible and ready (its AtkValues populated). </summary>
    public static bool IsReady(string name) => TryGet(name, out var a) && a->IsReady;

    public static bool IsVisible(string name) => TryGet(name, out _);

    public static AtkCells Cells(AtkUnitBase* addon, int max = 1200)
    {
        var map = new Dictionary<int, AtkCell>();
        var values = addon->AtkValues;
        var n = Math.Min((int)addon->AtkValuesCount, max);
        if (values is null)
            return new AtkCells(map, 0);
        for (var i = 0; i < n; i++)
        {
            var v = values[i];
            switch (v.Type)
            {
                case AtkValueType.Bool:
                    map[i] = new AtkCell('b', v.Byte != 0 ? 1 : 0, null);
                    break;
                case AtkValueType.Int:
                    map[i] = new AtkCell('i', v.Int, null);
                    break;
                case AtkValueType.UInt:
                    map[i] = new AtkCell('u', v.UInt, null);
                    break;
                case AtkValueType.String:
                case AtkValueType.ConstString:
                case AtkValueType.ManagedString:
                    map[i] = new AtkCell('s', 0, v.Pointer is null ? "" : Text((nint)v.Pointer));
                    break;
            }
        }
        return new AtkCells(map, (int)addon->AtkValuesCount);
    }

    /// <summary> A string AtkValue as plain text (SeString payloads such as colour dropped). </summary>
    private static string Text(nint ptr)
    {
        try
        {
            return Dalamud.Memory.MemoryHelper.ReadSeStringNullTerminated(ptr).TextValue;
        }
        catch
        {
            return Marshal.PtrToStringUTF8(ptr) ?? "";
        }
    }

    public static bool TryCells(string name, out AtkCells cells)
    {
        cells = new AtkCells(new Dictionary<int, AtkCell>(), 0);
        if (!TryGet(name, out var addon) || !addon->IsReady)
            return false;
        cells = Cells(addon);
        return cells.Count > 0;
    }

    /// <summary> SelectYesno's prompt text (AtkValue 0), empty when it is not open. </summary>
    public static string YesNoText()
    {
        if (!TryGet("SelectYesno", out var addon) || addon->AtkValuesCount < 1 || addon->AtkValues is null)
            return "";
        var v = addon->AtkValues[0];
        if (v.Type is not (AtkValueType.String or AtkValueType.ConstString or AtkValueType.ManagedString) || v.Pointer is null)
            return "";
        try
        {
            // SeString: macros (item links, colour) are dropped, their text kept.
            return Dalamud.Memory.MemoryHelper.ReadSeStringNullTerminated((nint)v.Pointer).TextValue;
        }
        catch
        {
            return Marshal.PtrToStringUTF8((nint)v.Pointer) ?? "";
        }
    }
}
