using CurrencySpender.Classes;
using CurrencySpender.Data;
using Dalamud.Bindings.ImGui;

namespace CurrencySpender.Windows;

internal class DebugMainTab
{
    internal static unsafe void Draw()
    {
        if (CurrencyChrome.Button("generate_debug_data", "Generate"))
            ShopGen.init();
        var collectables = Generator.items.Where(item => item.Currency == 22 && item.Type.HasFlag(ItemType.Collectable)).ToList();
        CurrencyChrome.Card("debug_collectables", () =>
        {
            CurrencyChrome.Section("Flame Seal collectables", "Generated item and unlock data.", collectables.Count + " rows");
            if (CurrencyChrome.BeginTable("##NPC", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit, horizontalScroll: true))
            {
                ImGui.TableSetupColumn("Item");
                ImGui.TableSetupColumn("Item ID");
                ImGui.TableSetupColumn("Unlocked");
                ImGui.TableHeadersRow();
                foreach (var item in collectables)
                {
                    ImGui.TableNextColumn();
                    ImGui.Text($"{item.Name}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{item.Id}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{ItemHelper.IsUnlocked(item.Id)}");
                }
                ImGui.EndTable();
            }
        });

        CurrencyChrome.Card("debug_shops", () =>
        {
            CurrencyChrome.Section("Generated shops", null, Generator.shops.Count + " rows");
            if (CurrencyChrome.BeginTable("##Shops", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit, horizontalScroll: true))
            {
                ImGui.TableSetupColumn("Shop ID");
                ImGui.TableSetupColumn("Name");
                ImGui.TableSetupColumn("NPC");
                ImGui.TableHeadersRow();
                foreach (var shop in Generator.shops)
                {
                    ImGui.TableNextColumn();
                    ImGui.Text($"{shop.ShopId}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{shop.ShopName}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{shop.NpcName}");
                }
                ImGui.EndTable();
            }
        });
    }
}
