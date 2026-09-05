using Dalamud.Configuration;

namespace AutoPotion;

public class Configuration : IPluginConfiguration
{
    // v1: flat HpPotionEnable / HpPotionThreshold / RegenPotionEnable / RegenPotionThreshold.
    // v2: those four moved into per-job profiles, plus MP potion fields added.
    public int Version { get; set; } = 2;

    public bool MasterEnable { get; set; } = true;
    public bool OnlyInCombat { get; set; } = true;
    public bool OnlyInDuty { get; set; } = false;

    // v1 fields kept ONLY so an existing v1 save still deserializes; migrated into DefaultJob
    // on load and never read again. Nullable so we can tell "v1 had a value" apart from "v2+ save".
    public bool? HpPotionEnable { get; set; }
    public float? HpPotionThreshold { get; set; }
    public bool? RegenPotionEnable { get; set; }
    public float? RegenPotionThreshold { get; set; }

    // Profile applied to any job that hasn't been customized. Also what the config window edits
    // when the player isn't on a job (title screen, between zones, etc.).
    public JobPotionSettings DefaultJob { get; set; } = new();

    // Keyed by ClassJob.RowId. Populated lazily on first edit for a job.
    public Dictionary<uint, JobPotionSettings> Jobs { get; set; } = new();

    /// <summary>Newest CHANGELOG version the in-game "What's new" popup has shown (shared LalaChangelog gate).</summary>
    public string? LastSeenChangelogVersion { get; set; }

    /// <summary>
    ///     Off-by-default decision tap (v0.2.4.0). When on, PotionService writes one
    ///     <c>PT|</c> line to the plugin log per potion fired, plus a gated line when a
    ///     threshold was crossed and nothing fired. Purely diagnostic — it changes no
    ///     potion behaviour. A new defaulted bool needs no config Version bump: an
    ///     existing v2 save simply deserializes it as false.
    ///     See <see cref="PotionTelemetryFormat"/> for the wire format.
    /// </summary>
    public bool DecisionTelemetry { get; set; } = false;

    // Live profile to read for `jobId`. Falls back to the default profile when the job has
    // never been edited; callers that want to *write* a per-job change should call
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

    // Migrate v1 → v2: copy old flat fields into DefaultJob, then null them out so we don't
    // re-migrate. Idempotent.
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

    /// <summary>
    ///     Opt-in Silence cure (v0.2.5.0): while Silence (status 7) is on the player,
    ///     use Echo Drops (item 4566) from the bags. Item use is not blocked by Silence,
    ///     so this adds a cure the potions cannot do without changing any of them.
    ///     Off by default — curing Silence automatically is a playstyle choice; an
    ///     existing v2 save simply deserializes this as false, so no config Version bump.
    /// </summary>
    public bool SilenceEchoDropsEnable { get; set; } = false;

    public JobPotionSettings Clone() => new()
    {
        HpPotionEnable = HpPotionEnable,
        HpPotionThreshold = HpPotionThreshold,
        MpPotionEnable = MpPotionEnable,
        MpPotionThreshold = MpPotionThreshold,
        RegenPotionEnable = RegenPotionEnable,
        RegenPotionThreshold = RegenPotionThreshold,
        SilenceEchoDropsEnable = SilenceEchoDropsEnable,
    };
}
