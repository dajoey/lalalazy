using Lumina.Excel;
using System.Data;

namespace LazyFateAutomation.Helpers.Extensions;

public static class CollectionExtensions {
    /// <summary>
    /// Checks that all given row ids are contained within the RowRef collection.
    /// </summary>
    public static bool ContainsAll<T>(this Collection<RowRef<T>> collection, IEnumerable<uint> rowIds) where T : struct, IExcelRow<T> {
        var collectionRowIds = collection.Where(r => r.RowId > 0).Select(r => r.RowId).ToHashSet();
        return rowIds.All(collectionRowIds.Contains);
    }

    /// <summary>
    /// Checks that any given row ids are contained within the RowRef collection.
    /// </summary>
    public static bool ContainsAny<T>(this Collection<RowRef<T>> collection, IEnumerable<uint> rowIds) where T : struct, IExcelRow<T> {
        var collectionRowIds = collection.Where(r => r.RowId > 0).Select(r => r.RowId).ToHashSet();
        return rowIds.Any(collectionRowIds.Contains);
    }
}
