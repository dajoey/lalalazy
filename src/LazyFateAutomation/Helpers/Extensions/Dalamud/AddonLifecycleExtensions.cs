using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LazyFateAutomation.Helpers.Extensions;

// https://github.com/MidoriKami/VanillaPlus/blob/5f5be8496f3562ec4ffc8f9425a68467d3853070/VanillaPlus/Extensions/AddonLifecycleExtensions.cs
public static class AddonLifecycleExtensions {
    public static void LogAddonNonTicks(this IAddonLifecycle addonLifecycle, string addonName)
        => LogAddon(addonLifecycle, addonName, [.. AddonEvent.Values.Where(e => e is not (AddonEvent.PreDraw or AddonEvent.PostDraw or AddonEvent.PreUpdate or AddonEvent.PostUpdate))]);

    public static void LogAddon(this IAddonLifecycle addonLifecycle, string addonName, AddonEvent[]? events = null) {
        if (events is not null) {
            foreach (var evt in events)
                addonLifecycle.RegisterListener(evt, addonName, Logger);
        }
        else {
            foreach (var evt in AddonEvent.Values)
                addonLifecycle.RegisterListener(evt, addonName, Logger);
        }
    }

    private static void Logger(AddonEvent type, AddonArgs args) {
        switch (args) {
            case AddonReceiveEventArgs receiveEventArgs:
                Svc.Log.Print($"[{args.AddonName}] {(AtkEventType)receiveEventArgs.AtkEventType}: {receiveEventArgs.EventParam}");
                break;

            default:
                Svc.Log.Print($"{args.AddonName} called {type}");
                break;
        }
    }

    public static void UnLogAddon(this IAddonLifecycle addonLifecycle, string addonName) {
        foreach (var evt in AddonEvent.Values)
            addonLifecycle.UnregisterListener(evt, addonName, Logger);
    }
}
