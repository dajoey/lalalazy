using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using LalaUiKit;

namespace CurrencySpender.Windows;

internal class DebugTab
{
    internal static unsafe void Draw()
    {
        var agent = AgentMap.Instance();
        CurrencyChrome.Card("debug_map", () =>
        {
            CurrencyChrome.Section("Map state");
            Widgets.SecondaryWrapped($"CurrentMapId: {agent->CurrentMapId}\nCurrentTerritoryId: {agent->CurrentTerritoryId}");
        });
        CurrencyChrome.TwoColumns("debug_ranks",
            () => CurrencyChrome.Card("debug_gc_ranks", () =>
            {
                CurrencyChrome.Section("GC rankings");
                foreach (var rank in PlayerHelper.GCRanks)
                    Widgets.Secondary($"{rank.Key} — {rank.Value}");
            }),
            () => CurrencyChrome.Card("debug_fate_ranks", () =>
            {
                CurrencyChrome.Section("FATE ranks");
                foreach (var rank in PlayerHelper.SharedFateRanks)
                    Widgets.Secondary($"{rank.Key} — {rank.Value}");
            }));
        CurrencyChrome.Card("debug_tools", () =>
        {
            CurrencyChrome.Section("Generated data");
            Widgets.SecondaryWrapped("Open the separate diagnostic window for generated collectable and shop rows.");
            if (CurrencyChrome.Button("open_debug_window", "Open Debug Window"))
                P.debugTabWindow.IsOpen = true;
        });
    }
}
