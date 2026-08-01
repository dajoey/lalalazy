using System.Globalization;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Ocelot.Config.Handlers;
using ExcelMount = Lumina.Excel.Sheets.Mount;

namespace LazyOccultCrescent.Modules.Mount;

public class MountProvider : ExcelSheetItemProvider<ExcelMount>
{
    public override unsafe bool Filter(ExcelMount item)
    {
        return PlayerState.Instance()->IsMountUnlocked(item.RowId);
    }

    public override string GetLabel(ExcelMount item)
    {
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(item.Singular.ToString());
    }
}
