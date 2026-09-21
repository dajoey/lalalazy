using System.Text;
using ECommons.DalamudServices;
using Lumina.Text.ReadOnly;

namespace LazyCrucible;

/// <summary>
///     Loads the Yes/No prompt templates <see cref="PromptGuard"/> checks from the running client's Addon sheet, so the
///     fixed text follows the client language (item and familiar names are still matched in English, so on another
///     language the check fails closed and the screen is left to the player). Falls back to the 7.56 English text.
/// </summary>
internal static class PromptTexts
{
    private static bool _loaded;

    public static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Addon>();
            var expected = new Dictionary<PromptGuard.Kind, IReadOnlyList<string>>();
            foreach (var (kind, row) in PromptGuard.Rows)
                if (sheet.TryGetRow(row, out var r) && Fragments(r.Text) is { Count: > 0 } f)
                    expected[kind] = f;
            var refused = new List<IReadOnlyList<string>>();
            foreach (var row in PromptGuard.RefusedRows)
                if (sheet.TryGetRow(row, out var r) && Fragments(r.Text) is { Count: > 0 } f)
                    refused.Add(f);

            if (expected.Count == PromptGuard.Rows.Count)
                PromptGuard.Expected = k => expected.TryGetValue(k, out var f) ? f : [];
            if (refused.Count > 0)
            {
                var all = refused.Concat(PromptGuard.EnglishRefused).ToList();
                PromptGuard.Refused = () => all;
            }
            CrucibleLog.Line($"SL|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|note=prompts|expected={expected.Count}|refused={refused.Count}");
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "prompt templates");
        }
    }

    /// <summary> The plain-text pieces of a sheet string (macros such as the item name or a line break split it). </summary>
    private static List<string> Fragments(ReadOnlySeString text)
    {
        var list = new List<string>();
        foreach (var p in text)
            if (p.Type == ReadOnlySePayloadType.Text)
            {
                var s = Encoding.UTF8.GetString(p.Body.Span);
                if (s.Trim().Length > 0)
                    list.Add(s);
            }
        return list;
    }
}
