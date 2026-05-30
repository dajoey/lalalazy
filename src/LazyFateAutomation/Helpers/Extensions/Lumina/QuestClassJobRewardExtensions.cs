using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace LazyFateAutomation.Helpers.Extensions;

public static class QuestClassJobRewardExtensions {
    extension(QuestClassJobReward) {
        public static List<RowRef<Item>> GetRelicsByRow(int row)
            => Svc.Data.TryGetSubrows<QuestClassJobReward>((uint)row, out var subrows)
                ? [.. subrows.SelectMany(q => q.RewardItem.TakeWhile(r => r.RowId != 0).Select(r => Svc.Data.GetRef<Item>(r.RowId)))]
                : [];
    }
}
