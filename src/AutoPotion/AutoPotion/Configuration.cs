using Dalamud.Configuration;

namespace AutoPotion;

public class Configuration : IPluginConfiguration
{
    // Version 1: flat HpPotionEnable/HpPotionThreshold/RegenPotionEnable/RegenPotionThreshold.
    // Version 2: those four moved into per-job profiles + a default profile (this file).
    public int Version { get; set; } = 2;

    public bool MasterEnable { get; set; } = true;
    public bool OnlyInCombat { get; set; } = true;
    public bool OnlyInDuty { get; set; } = false;

    // V1 fields kept ONLY so a v1 config still deserializes; migrated into DefaultJob on load
    // and never read again. Nullable so we can detect "this came from a v2+ save" vs "v1 had a value".
    public bool? HpPotionEnable { get; set; }
    public float? HpPotionThreshold { get; set; }
    public bool? RegenPotionEnable { get; set; }
    public float? RegenPotionThreshold { get; set; }

    // Profile applied to any job that hasn't been customized. Also what the config window edits
    // when the player isn't on a job (title screen, between zones, etc.).
    public JobPotionSettings DefaultJob { get; set; } = new();

    // Keyed by ClassJob.RowId. Populated lazily on first edit for a job.
    public Dictionary<uint, JobPotionSettings> Jobs { get; set; } = new();

    // Returns the live profile to read from for `jobId`. Falls back to the default profile when
    // the job has never been edited; callers that want to *write* a per-job change should call
    // GetOrCreateJobSettings instead so the override is persisted.
    public JobPotionSettings GetJobSettings(uint jobId)
    {
        if (jobId != 0 && Jobs.TryGetValue(jobId, out var s)) return s;
        return DefaultJob;
    }

    public JobPotionSettings GetOrCreateJobSettings(uint jobId)
    {
        if (jobId == 0) return DefaultJob;
        if (!Jobs.TryGetValue(jobId, out var s))
        {
            s = DefaultJob.Clone();
            Jobs[jobId] = s;
        }
        return s;
    }

    // Migrate v1 → v2: copy the old flat fields into DefaultJob, then null them out so we
    // don't keep migrating. Idempotent.
    public void MigrateIfNeeded()
    {
        if (HpPotionEnable.HasValue) DefaultJob.HpPotionEnable = HpPotionEnable.Value;
        if (HpPotionThreshold.HasValue) DefaultJob.HpPotionThreshold = HpPotionThreshold.Value;
        if (RegenPotionEnable.HasValue) DefaultJob.RegenPotionEnable = RegenPotionEnable.Value;
        if (RegenPotionThreshold.HasValue) DefaultJob.RegenPotionThreshold = RegenPotionThreshold.Value;
        HpPotionEnable = null;
        HpPotionThreshold = null;
        RegenPotionEnable = null;
        RegenPotionThreshold = null;
        Version = 2;
    }
}

public class JobPotionSettings
{
    public bool HpPotionEnable { get; set; } = true;
    public float HpPotionThreshold { get; set; } = 60f;

    // Default off: most jobs don't need MP management and burning ethers is wasteful;
    // healers/casters who want it can opt in per-job.
    public bool MpPotionEnable { get; set; } = false;
    public float MpPotionThreshold { get; set; } = 30f;

    public bool RegenPotionEnable { get; set; } = true;
    public float RegenPotionThreshold { get; set; } = 80f;

    public JobPotionSettings Clone() => new()
    {
        HpPotionEnable = HpPotionEnable,
        HpPotionThreshold = HpPotionThreshold,
        MpPotionEnable = MpPotionEnable,
        MpPotionThreshold = MpPotionThreshold,
        RegenPotionEnable = RegenPotionEnable,
        RegenPotionThreshold = RegenPotionThreshold,
    };
}
