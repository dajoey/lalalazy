using Lumina.Excel.Sheets;
using Lumina.Extensions;

namespace LazyFateAutomation.Helpers.Extensions;

public static class FateExtensions {
    extension(Fate fate) {
        public bool HasFollowUp => fate.GetFollowUp() is { };

        /// <remarks>This is a very basic heuristic. I know there's some that don't follow this (the two at the top of Outer La Noscea).</summary>
        public Fate? GetFollowUp()
            => Svc.Data.GetSheet<Fate>().FirstOrNull(row => row.RowId > fate.RowId && row.Location == fate.Location);
    }
}
