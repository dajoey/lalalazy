using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace LazyFateAutomation.Helpers.Extensions;

public static class RelicItemExtensions {
    extension(RelicItem) {
        public static List<RowRef<Item>> GetItemsByStep(uint step) {
            var row = RelicItem.GetRow(step - 1);
            var items = new List<RowRef<Item>> {
                row.GladiatorItem,
                row.PugilistItem,
                row.MarauderItem,
                row.LancerItem,
                row.ArcanistSCHItem,
                row.ConjurerItem,
                row.ThaumaturgeItem,
                row.ArcanistSMNItem,
                row.ArcanistSCHItem,
                row.ShieldItem,
                row.RogueItem,
            };
            return items;
        }
    }
}
