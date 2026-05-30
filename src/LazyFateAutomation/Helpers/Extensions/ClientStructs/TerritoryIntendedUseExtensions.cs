using Lumina.Excel.Sheets;

namespace LazyFateAutomation.Helpers.Extensions;

public static class TerritoryIntendedUseExtensions {
    extension(TerritoryIntendedUse row) {
        public FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse StructsEnum => (FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse)row.RowId;
    }
}
