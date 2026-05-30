using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace LazyFateAutomation.Helpers.Extensions;

public static class AnimaWeaponItemExtensions {
    extension(AnimaWeaponItem row) {
        public unsafe Collection<RowRef<Item>> Items => new(row.ExcelPage, parentOffset: row.RowOffset, offset: row.RowOffset, &ItemCtor, size: 14);
    }

    internal static RowRef<Item> ItemCtor(ExcelPage page, uint parentOffset, uint offset, uint i)
        => new(page.Module, page.ReadUInt32(offset + i * 4), page.Language);
}
