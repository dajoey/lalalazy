using Lumina.Excel.Sheets;

namespace LazyFateAutomation.Helpers.Extensions;

public static class ClassJobCategoryExtensions {
    /// <summary>
    /// Checks that all jobs in a given <see cref="ClassJobCategory"/> are at or above a given level
    /// </summary>
    public static bool HasJobsAtLevel(this ClassJobCategory category, int level)
        => Svc.Data.GetSheet<ClassJob>().Where(cj => category.ContainsJob(cj))
            .All(cj => cj.GetLevel() >= level);

    /// <summary>
    /// Checks that any job in a given <see cref="ClassJobCategory"/> is at or above a given level
    /// </summary>
    public static bool HasAnyJobAtLevel(this ClassJobCategory category, int level)
        => Svc.Data.GetSheet<ClassJob>().Where(cj => category.ContainsJob(cj))
            .Any(cj => cj.GetLevel() >= level);

    public static bool ContainsJob(this ClassJobCategory row, ClassJob classJob)
        => row.ExcelPage.ReadBool(row.RowOffset + classJob.RowId + 4);
}
