// Shared source (NOT a shared DLL) - Dalamud / FFXIVClientStructs side of a problem report.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Lalalazy.Telemetry;

/// <summary>
/// What was on screen when a report was filed, in lieu of a screenshot: the name of every visible addon,
/// plus a bounded AtkValue dump of the visible WINDOWS (names starting with '_' are HUD parts - listed by
/// name only; chat log panels are skipped entirely). Read-only, framework thread only.
/// </summary>
internal static unsafe class AddonDump
{
    public const int MaxVisibleNames = 150;
    public const int MaxDetailed = 12;
    public const int MaxValues = 24;
    public const int MaxStringChars = 48;

    public sealed record Result(IReadOnlyList<string> Visible, IReadOnlyList<AddonCapture> Details)
    {
        public static Result Empty { get; } = new(Array.Empty<string>(), Array.Empty<AddonCapture>());
    }

    public static Result Capture()
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager is null)
            return Result.Empty;

        var visible = new List<string>();
        var details = new List<AddonCapture>();
        var list = manager->AtkUnitManager.AllLoadedUnitsList;
        var count = Math.Min((int)list.Count, list.Entries.Length);
        for (var i = 0; i < count && visible.Count < MaxVisibleNames; i++)
        {
            var unit = list.Entries[i].Value;
            if (unit is null || !unit->IsVisible)
                continue;

            var name = unit->NameString;
            if (string.IsNullOrEmpty(name))
                continue;
            visible.Add(name);

            if (details.Count >= MaxDetailed || name.StartsWith('_') || name.StartsWith("ChatLog", StringComparison.Ordinal))
                continue;
            details.Add(new AddonCapture(name, unit->AtkValuesCount, DescribeValues(unit)));
        }

        visible.Sort(StringComparer.Ordinal);
        return new Result(visible, details);
    }

    /// <summary><c>index:type=value;</c> for the first <see cref="MaxValues"/> defined values (same shape as the XB| lines).</summary>
    private static string DescribeValues(AtkUnitBase* unit)
    {
        var values = unit->AtkValues;
        if (values is null)
            return string.Empty;

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(256);
        var n = Math.Min((int)unit->AtkValuesCount, 400);
        var written = 0;
        for (var i = 0; i < n && written < MaxValues; i++)
        {
            var v = values[i];
            switch (v.Type)
            {
                case AtkValueType.Undefined:
                    continue;
                case AtkValueType.Bool:
                    sb.Append(i).Append(":b=").Append(v.Byte != 0 ? '1' : '0');
                    break;
                case AtkValueType.Int:
                    sb.Append(i).Append(":i=").Append(v.Int.ToString(inv));
                    break;
                case AtkValueType.UInt:
                    sb.Append(i).Append(":u=").Append(v.UInt.ToString(inv));
                    break;
                case AtkValueType.Float:
                    sb.Append(i).Append(":f=").Append(v.Float.ToString("0.###", inv));
                    break;
                case AtkValueType.String:
                case AtkValueType.ConstString:
                case AtkValueType.ManagedString:
                    var s = v.Pointer is null ? string.Empty : Marshal.PtrToStringUTF8((nint)v.Pointer) ?? string.Empty;
                    if (s.Length > MaxStringChars)
                        s = s[..MaxStringChars];
                    // ';' separates values here; the whole string is escaped again as an RP| field.
                    sb.Append(i).Append(":s=").Append(s.Replace(';', ','));
                    break;
                default:
                    sb.Append(i).Append(":t").Append(((int)v.Type).ToString(inv));
                    break;
            }
            sb.Append(';');
            written++;
        }

        return sb.ToString();
    }
}
