using Lumina.Excel.Sheets;

namespace LazyFateAutomation.Helpers.Extensions;

public static class FateExtensions {
    public static bool HasFollowUp(this Fate fate) => GetFollowUp(fate) is { };

    public static Fate? GetFollowUp(this Fate fate) {
        var sheet = Svc.Data.GetExcelSheet<Fate>();
        foreach (var row in sheet) {
            if (row.RowId > fate.RowId && row.Location == fate.Location) {
                return row;
            }
        }
        return null;
    }
}
