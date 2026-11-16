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
            CurrencyChrome.Muted($"CurrentMapId: {agent->CurrentMapId}\nCurrentTerritoryId: {agent->CurrentTerritoryId}");
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
            }), 0.5f); // Fork: balanced columns
        CurrencyChrome.Card("debug_tools", () =>
        {
            CurrencyChrome.Section("Generated data");
            CurrencyChrome.Muted("Open the separate diagnostic window for generated collectable and shop rows."); // Fork: secondary size
            ImGui.Dummy(new Vector2(1, 4));
            if (CurrencyChrome.Button("open_debug_window", "Open Debug Window"))
                P.debugTabWindow.IsOpen = true;
        });
    }
}
