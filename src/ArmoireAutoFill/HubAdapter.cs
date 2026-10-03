using System;
using Lalalazy.Hub;

namespace ArmoireAutoFill;

/// <summary>
/// Quick controls for the lalalazy hub window (src/Shared/LalaHub). Left out on purpose: "Skip gearset items"
/// and "Include the armoury chest" widen what an auto-store may move, and the Store All button moves items.
/// Flipping the master moves nothing by itself: items only move when the Armoire window next opens.
/// </summary>
internal static class HubAdapter
{
    public static void Declare(HubEndpoint ep)
    {
        ep.Toggle("auto_store_on_open", "Auto-store when the Armoire opens",
            () => Plugin.Configuration.AutoStoreOnOpen, v => { Plugin.Configuration.AutoStoreOnOpen = v; Plugin.Configuration.Save(); },
            group: "General", tip: "Stores eligible gear the next time the Armoire window opens.", master: true);
        ep.Toggle("scan_on_load", "Scan the Armoire on login", () => Plugin.Configuration.ScanOnLoad,
            v => { Plugin.Configuration.ScanOnLoad = v; Plugin.Configuration.Save(); }, group: "General",
            tip: "Takes effect at the next login.");
        ep.Toggle("show_owned_items", "Show owned items", () => Plugin.Configuration.ShowOwnedItems,
            v => { Plugin.Configuration.ShowOwnedItems = v; Plugin.Configuration.Save(); }, group: "Display");
        ep.Toggle("hide_complete_dungeons", "Hide finished dungeons", () => Plugin.Configuration.HideCompleteDungeons,
            v => { Plugin.Configuration.HideCompleteDungeons = v; Plugin.Configuration.Save(); }, group: "Display");
    }
}
