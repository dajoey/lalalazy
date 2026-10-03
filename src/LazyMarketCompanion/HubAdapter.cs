using System;
using Lalalazy.Hub;

namespace LazyMarketCompanion;

/// <summary>
/// Quick controls for the lalalazy hub window (src/Shared/LalaHub).
///
/// SAFETY (repo CLAUDE.md, "Auto-Market safety doctrine"): this adapter exposes the master switch and settings
/// that only change what is shown or logged. Nothing here changes what Auto-Market lists, prices, vends or pulls:
/// the value gate, pricing, routing, stack and keep-floor settings, the AutoRetainer hook, the Pinch keys and the
/// per-item list are all deliberately NOT exposed. Do not add one without reading that section first.
///
/// Every setter does what the settings window does: set the field, then Configuration.Save().
///
/// The master is locked while a run is in progress (review 2026-10-03): a sweep reads the switch per retainer, and a
/// switch turned off part-way skips the listing and pulls but still runs the vendor leg from the verdicts already made.
/// The settings window allows it; one click in another window while the game is automated is too easy.
/// </summary>
internal static class HubAdapter
{
    public static void Declare(HubEndpoint ep, Func<bool> runInProgress)
    {
        const string run = "Auto-Market";
        const string show = "Messages and display";

        ep.Toggle("auto_market", "Auto-Market", () => Plugin.Configuration.AutoMarketEnabled,
            v => { Plugin.Configuration.AutoMarketEnabled = v; Plugin.Configuration.Save(); },
            group: run, master: true,
            state: () => runInProgress()
                ? new ControlState(Enabled: false, Why: "A run is in progress. Cancel it first.")
                : new ControlState(),
            tip: "Master switch. Locked while a run is in progress.",
            confirm: "Turning Auto-Market on lets it run the next time it is triggered, including during AutoRetainer cycles if that is set up in its settings.");

        ep.Toggle("bag_markers", "Bag markers", () => Plugin.Configuration.AutoMarketMarkersEnabled,
            v => { Plugin.Configuration.AutoMarketMarkersEnabled = v; Plugin.Configuration.Save(); }, group: show);
        ep.Toggle("chat_messages", "Auto-Market chat messages", () => Plugin.Configuration.ShowAutoMarketMessages,
            v => { Plugin.Configuration.ShowAutoMarketMessages = v; Plugin.Configuration.Save(); }, group: show);
        ep.Toggle("errors_in_chat", "Errors in chat", () => Plugin.Configuration.ShowErrorsInChat,
            v => { Plugin.Configuration.ShowErrorsInChat = v; Plugin.Configuration.Save(); }, group: show);
        ep.Toggle("price_msgs", "Price adjustment messages", () => Plugin.Configuration.ShowPriceAdjustmentsMessages,
            v => { Plugin.Configuration.ShowPriceAdjustmentsMessages = v; Plugin.Configuration.Save(); }, group: show);
        ep.Toggle("retainer_names", "Show retainer names", () => Plugin.Configuration.ShowRetainerNames,
            v => { Plugin.Configuration.ShowRetainerNames = v; Plugin.Configuration.Save(); }, group: show);
        ep.Toggle("owner_tooltip", "Inventory owner tooltip", () => Plugin.Configuration.InventoryOwnerTooltip,
            v => { Plugin.Configuration.InventoryOwnerTooltip = v; Plugin.Configuration.Save(); }, group: show);
        ep.Toggle("ctx_menu", "Inventory right-click entry", () => Plugin.Configuration.ShowInventoryContextMenuEntry,
            v => { Plugin.Configuration.ShowInventoryContextMenuEntry = v; Plugin.Configuration.Save(); }, group: show);
        ep.Toggle("decision_log", "Log price decisions", () => Plugin.Configuration.DecisionTelemetry,
            v => { Plugin.Configuration.DecisionTelemetry = v; Plugin.Configuration.Save(); }, group: "Diagnostics",
            tip: "Writes price decisions to the plugin log. Changes no behaviour.");
    }
}
