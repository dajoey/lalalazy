using System;
using Lalalazy.Hub;

namespace AutoPotion;

/// <summary>
/// Quick controls for the lalalazy hub window (src/Shared/LalaHub). Every setter does exactly what the
/// settings window does: set the field, then SaveConfig. The per-job controls act on the job the player
/// is on now (the settings window edits the same profile): read with GetJobSettings, write with
/// GetOrCreateJobSettings.
/// </summary>
internal static class HubAdapter
{
    private static uint ActiveJobId() => Plugin.Objects.LocalPlayer?.ClassJob.RowId ?? 0;

    public static void Declare(HubEndpoint ep, Plugin plugin)
    {
        var c = plugin.Config;
        JobPotionSettings Job() => c.GetJobSettings(ActiveJobId());
        JobPotionSettings JobForWrite() => c.GetOrCreateJobSettings(ActiveJobId());
        void Save() => plugin.SaveConfig();

        const string general = "General";
        const string job = "Current job";
        const string jobTip = "Applies to the job you are playing now.";

        ep.Toggle("master_enable", "Auto-use potions", () => c.MasterEnable, v => { c.MasterEnable = v; Save(); },
            group: general, tip: "Master switch for the whole plugin.", master: true);
        ep.Toggle("only_in_combat", "Only in combat", () => c.OnlyInCombat, v => { c.OnlyInCombat = v; Save(); }, group: general);
        ep.Toggle("only_in_duty", "Only in a duty", () => c.OnlyInDuty, v => { c.OnlyInDuty = v; Save(); }, group: general);

        ep.Toggle("hp_potion", "HP potion", () => Job().HpPotionEnable, v => { JobForWrite().HpPotionEnable = v; Save(); }, group: job, tip: jobTip);
        ep.Stepper("hp_threshold", "HP below", min: 1, max: 99, step: 5, get: () => Job().HpPotionThreshold,
            set: v => { JobForWrite().HpPotionThreshold = (float)v; Save(); }, unit: "%", group: job, tip: jobTip);
        ep.Toggle("mp_potion", "MP potion", () => Job().MpPotionEnable, v => { JobForWrite().MpPotionEnable = v; Save(); }, group: job, tip: jobTip);
        ep.Stepper("mp_threshold", "MP below", min: 1, max: 99, step: 5, get: () => Job().MpPotionThreshold,
            set: v => { JobForWrite().MpPotionThreshold = (float)v; Save(); }, unit: "%", group: job, tip: jobTip);
        ep.Toggle("regen_potion", "Regen potion", () => Job().RegenPotionEnable, v => { JobForWrite().RegenPotionEnable = v; Save(); }, group: job, tip: jobTip);
        ep.Stepper("regen_threshold", "Regen below", min: 1, max: 99, step: 5, get: () => Job().RegenPotionThreshold,
            set: v => { JobForWrite().RegenPotionThreshold = (float)v; Save(); }, unit: "%", group: job, tip: jobTip);
        ep.Toggle("silence_echo", "Cure Silence (Echo Drops)", () => Job().SilenceEchoDropsEnable,
            v => { JobForWrite().SilenceEchoDropsEnable = v; Save(); }, group: job, tip: jobTip);
    }
}
