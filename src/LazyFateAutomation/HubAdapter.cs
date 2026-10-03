using System;
using System.Linq;
using Lalalazy.Hub;

namespace LazyFateAutomation;

/// <summary>
/// Quick controls for the lalalazy hub window (src/Shared/LalaHub).
///
/// The master is the bot's RUN STATE, not a config bool: turning it on starts a task loop that teleports, mounts,
/// fights and talks to NPCs, and turning it off tears down BossMod, Gluttony and vnavmesh control. So the setter
/// calls exactly what the window's Start/Stop button calls: FateToolKit.ToggleRunning(), and a hard stop also
/// stops vnavmesh like the button does. It only toggles when the state actually differs, and starting needs the
/// character to be available (the window itself is only drawn then). Turning it on asks for a confirmation in the hub.
///
/// The grind mode can change the zone pool and equip items on the next zone swap, so it is only editable while the
/// bot is stopped. The window never saved its display settings, so they are left out, as are the zone sets and the
/// blacklist. No lease is registered with Gluttony from here: FateToolKit already manages its own.
/// </summary>
internal static class HubAdapter
{
    public static void Declare(HubEndpoint ep)
    {
        const string bot = "FATE bot";
        const string tuning = "Tuning";

        bool Running() => Plugin.FateToolKit.Running && Service.Automation.Running;
        ControlState WhileStopped() => Running()
            ? new ControlState(Enabled: false, Why: "Stop the bot first.")
            : new ControlState();

        ep.Toggle("running", "FATE bot", Running,
            v =>
            {
                if (v == Plugin.FateToolKit.Running) return SetOutcome.Success;
                if (v)
                {
                    if (!ECommons.GameHelpers.Player.Available) return SetOutcome.Refuse("Log in first.");
                    Plugin.FateToolKit.ToggleRunning();
                }
                else
                {
                    Plugin.FateToolKit.ToggleRunning();
                    Service.Navmesh.Stop();
                }
                return SetOutcome.Success;
            },
            group: bot, master: true,
            tip: "Stopping releases BossMod, Gluttony Combo and navigation.",
            confirm: "Starting the FATE bot takes over the character: it teleports, mounts, fights and talks to NPCs until it is stopped.",
            state: () => Plugin.FateToolKit.Running || ECommons.GameHelpers.Player.Available
                ? new ControlState()
                : new ControlState(Enabled: false, Why: "Available once the character is logged in."));

        ep.Button("stop_safe", "Stop when safe", () =>
            {
                if (!Plugin.FateToolKit.Running) return SetOutcome.Refuse("The bot is not running.");
                Plugin.FateToolKit.PendingStopWhenSafe = true;
                return SetOutcome.Success;
            }, group: bot, tip: "Finishes the current FATE and stops once out of combat.",
            state: () => Plugin.FateToolKit.Running ? new ControlState() : new ControlState(Enabled: false, Why: "The bot is not running."));

        ep.Toggle("forlorn", "Prioritize Forlorn Maidens", () => Plugin.Config.PrioritizeForlornMaidens,
            v => { Plugin.Config.PrioritizeForlornMaidens = v; Plugin.Config.Save(); }, group: tuning);
        ep.Toggle("swap_zones", "Swap zones when empty", () => Plugin.Config.SwapZones,
            v => { Plugin.Config.SwapZones = v; Plugin.Config.Save(); }, group: tuning,
            tip: "A running bot teleports to another zone when this one has no FATE.");
        ep.Stepper("max_duration", "Skip FATEs longer than", min: 60, max: 3600, step: 60,
            get: () => Plugin.Config.MaxDuration, set: v => { Plugin.Config.MaxDuration = (int)v; Plugin.Config.Save(); },
            unit: " s", group: tuning);
        ep.Stepper("min_time_left", "Skip FATEs with less than", min: 0, max: 600, step: 10,
            get: () => Plugin.Config.MinTimeRemaining, set: v => { Plugin.Config.MinTimeRemaining = (int)v; Plugin.Config.Save(); },
            unit: " s left", group: tuning);
        ep.Stepper("max_progress", "Skip FATEs past", min: 0, max: 100, step: 5,
            get: () => Plugin.Config.MaxProgress, set: v => { Plugin.Config.MaxProgress = (int)v; Plugin.Config.Save(); },
            unit: "%", group: tuning);

        // The mode list is fixed once the plugin has loaded, so it is read here, once.
        var modes = FateGrindModes.All.Select(m => m.DisplayName).ToArray();
        if (modes.Length > 0 && modes.Length <= HubProtocol.MaxChoices)
            ep.Choice("mode", "Grind mode", modes,
                () => Math.Max(0, Array.IndexOf(modes, Plugin.FateToolKit.GetCurrentMode().DisplayName)),
                i => { Plugin.FateToolKit.SelectedModeId = modes[Math.Max(0, Math.Min(modes.Length - 1, i))]; },
                group: tuning, tip: "Changes the zone pool. Editable only while the bot is stopped.", state: WhileStopped);
    }
}
