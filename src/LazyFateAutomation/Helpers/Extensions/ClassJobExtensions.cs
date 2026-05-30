using Lumina.Excel.Sheets;
using Action = Lumina.Excel.Sheets.Action;
using System.Collections.Generic;

namespace clib.Extensions;

public static class ClassJobExtensions {
    public static bool IsTank(this ClassJob job) => job.JobType is 1;
    public static bool IsDps(this ClassJob job) => job.JobType is 3 or 4 or 5;
    public static bool IsHealer(this ClassJob job) => job.JobType is 2 or 6;

    public static bool IsDoW(this ClassJob job) => Svc.Data.GetRef<ClassJobCategory>(30).Value.ContainsJob(job);
    public static bool IsDoM(this ClassJob job) => Svc.Data.GetRef<ClassJobCategory>(31).Value.ContainsJob(job);
    public static bool IsDoL(this ClassJob job) => Svc.Data.GetRef<ClassJobCategory>(32).Value.ContainsJob(job);
    public static bool IsDoH(this ClassJob job) => Svc.Data.GetRef<ClassJobCategory>(33).Value.ContainsJob(job);
    
    /// <summary>
    /// Disciple of War or Magic
    /// </summary>
    public static bool IsCombat(this ClassJob job) => Svc.Data.GetRef<ClassJobCategory>(34).Value.ContainsJob(job);
    
    /// <summary>
    /// Disciple of Land or Hand
    /// </summary>
    public static bool IsTrade(this ClassJob job) => Svc.Data.GetRef<ClassJobCategory>(35).Value.ContainsJob(job);

    public static bool IsMelee(this ClassJob job) => job.JobType is 3;
    
    /// <summary>
    /// Physical and Magical Ranged
    /// </summary>
    public static bool IsRanged(this ClassJob job) => job.Role is 3;
    public static bool IsPhysRanged(this ClassJob job) => job.JobType is 4;
    public static bool IsMagicRanged(this ClassJob job) => job.JobType is 5;
    public static bool IsPureHealer(this ClassJob job) => job.JobType is 2;
    public static bool IsShieldHealer(this ClassJob job) => job.JobType is 6;

    public static short GetLevel(this ClassJob job) => Svc.PlayerState.GetClassJobLevel(job);
    public static IReadOnlyList<Action> GetActions(this ClassJob job) => Svc.Data.FindRows<Action>(a => a.ClassJobCategory.ValueNullable?.ContainsJob(job) ?? false);
}
