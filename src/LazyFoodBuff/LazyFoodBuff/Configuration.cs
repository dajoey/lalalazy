using Dalamud.Configuration;

namespace LazyFoodBuff;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool MasterEnable { get; set; } = true;

    /// <summary>
    /// Only auto-eat inside combat duties (dungeons, raids, trials, alliance raids,
    /// criterion, variant). Excludes Diadem, field operations, deep dungeons,
    /// Gold Saucer, overworld, etc.
    /// </summary>
    public bool OnlyInCombatDuty { get; set; } = true;

    /// <summary>
    /// Minutes of food buff remaining before auto-refresh eating triggers.
    /// Food can be re-eaten to extend up to a 30-minute cap.
    /// </summary>
    public float RefreshThresholdMinutes { get; set; } = 5f;

    // Warning system
    public bool WarningEnabled { get; set; } = true;
    public float WarningThresholdMinutes { get; set; } = 3f;
    public bool WarningSoundEnabled { get; set; } = true;
    public uint WarningSoundId { get; set; } = 23;

    // Per-job settings
    public JobFoodSettings DefaultJob { get; set; } = new();
    public Dictionary<uint, JobFoodSettings> Jobs { get; set; } = new();

    public JobFoodSettings GetJobSettings(uint jobId)
    {
        if (jobId != 0 && Jobs.TryGetValue(jobId, out var s)) return s;
        return DefaultJob;
    }

    public JobFoodSettings GetOrCreateJobSettings(uint jobId)
    {
        if (jobId == 0) return DefaultJob;
        if (!Jobs.TryGetValue(jobId, out var s))
        {
            s = DefaultJob.Clone();
            Jobs[jobId] = s;
        }
        return s;
    }
}

public class JobFoodSettings
{
    /// <summary>
    /// AutoSelect = score food by job-relevant stats.
    /// Manual = use the specific item chosen below.
    /// </summary>
    public FoodSelectionMode Mode { get; set; } = FoodSelectionMode.AutoSelect;

    /// <summary>
    /// Item RowId for manual food selection. 0 = none selected.
    /// </summary>
    public uint ManualFoodItemId { get; set; } = 0;

    /// <summary>
    /// Whether the manual food preference is HQ.
    /// </summary>
    public bool ManualFoodIsHQ { get; set; } = true;

    /// <summary>
    /// If true and the manual food is not in inventory, fall back to AutoSelect.
    /// </summary>
    public bool FallbackToAutoSelect { get; set; } = true;

    public JobFoodSettings Clone() => new()
    {
        Mode = Mode,
        ManualFoodItemId = ManualFoodItemId,
        ManualFoodIsHQ = ManualFoodIsHQ,
        FallbackToAutoSelect = FallbackToAutoSelect,
    };
}

public enum FoodSelectionMode
{
    AutoSelect,
    Manual,
}
