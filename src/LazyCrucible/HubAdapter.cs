using System;
using Lalalazy.Hub;

namespace LazyCrucible;

/// <summary>
/// Quick controls for the lalalazy hub window (src/Shared/LalaHub). Every setter does what the settings window
/// does: set the field, then Config.Save().
///
/// Left out on purpose: the automations that spend or click during a run (auto shop, auto feed, auto treasure,
/// auto spoils, the score goal), and "Yield to AutoDuty", which is a safety setting. The three automations kept
/// here only write rosters and horns, pick who rests, or open a guide, and each is reversible. If the matching
/// screen is already open, the next tick acts on a changed setting (same as ticking it in the settings window).
/// There is no single master switch: Auto roster and Auto horns are co-equal.
/// </summary>
internal static class HubAdapter
{
    public static void Declare(HubEndpoint ep)
    {
        const string auto = "Automation";
        const string display = "Display";

        ep.Toggle("auto_roster", "Fill the familiar roster", () => Plugin.Config.AutoRoster,
            v => { Plugin.Config.AutoRoster = v; Plugin.Config.Save(); }, group: auto,
            tip: "Writes the familiar roster when the entry menu is open.");
        ep.Toggle("auto_horns", "Set the Battlehorns", () => Plugin.Config.AutoHorns,
            v => { Plugin.Config.AutoHorns = v; Plugin.Config.Save(); }, group: auto,
            tip: "Writes the Battlehorn slots. It never starts the fight.");
        ep.Toggle("auto_camp", "Pick who rests", () => Plugin.Config.AutoCamp,
            v => { Plugin.Config.AutoCamp = v; Plugin.Config.Save(); }, group: auto,
            tip: "Only selects who rests. Resting is still your button.");

        ep.Toggle("guide_auto_open", "Open the guide automatically", () => Plugin.Config.GuideAutoOpen,
            v => { Plugin.Config.GuideAutoOpen = v; Plugin.Config.Save(); }, group: display);
        ep.Toggle("announce_picks", "Announce picks in chat", () => Plugin.Config.AnnouncePicks,
            v => { Plugin.Config.AnnouncePicks = v; Plugin.Config.Save(); }, group: display,
            tip: "Prints to your own chat only.");
        ep.Toggle("record_screens", "Record screens to the log", () => Plugin.Config.RecordScreens,
            v => { Plugin.Config.RecordScreens = v; Plugin.Config.Save(); }, group: "Diagnostics",
            tip: "Read-only logging.");
    }
}
