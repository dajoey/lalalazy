using System;
using Lalalazy.Hub;

namespace LazyFoodBuff;

/// <summary>
/// Quick controls for the lalalazy hub window (src/Shared/LalaHub). Every setter does what the settings
/// window does: set the field, then SaveConfig. The per-job food choice and the sound settings are left out
/// on purpose (a food picker does not fit a quick control; nothing reads the sound settings).
/// </summary>
internal static class HubAdapter
{
    public static void Declare(HubEndpoint ep, Plugin plugin)
    {
        var c = plugin.Config;
        void Save() => plugin.SaveConfig();

        ep.Toggle("master_enable", "Auto-eat food", () => c.MasterEnable, v => { c.MasterEnable = v; Save(); },
            group: "General", tip: "Master switch for the whole plugin.", master: true);
        ep.Toggle("only_combat_duty", "Only in combat duties", () => c.OnlyInCombatDuty, v => { c.OnlyInCombatDuty = v; Save(); },
            group: "General");
        ep.Stepper("refresh_minutes", "Re-eat when food has", min: 0, max: 29, step: 1, get: () => c.RefreshThresholdMinutes,
            set: v => { c.RefreshThresholdMinutes = (float)v; Save(); }, unit: " min", group: "General",
            tip: "Eat again when the food buff has this many minutes left.");
        ep.Toggle("warn_enable", "Low-food warning", () => c.WarningEnabled, v => { c.WarningEnabled = v; Save(); }, group: "Warning");
        ep.Stepper("warn_count", "Warn at", min: 1, max: 20, step: 1, get: () => c.WarningThresholdCount,
            set: v => { c.WarningThresholdCount = (int)v; Save(); }, unit: " left", group: "Warning",
            tip: "Warn when this many of the chosen food are left.");
    }
}
