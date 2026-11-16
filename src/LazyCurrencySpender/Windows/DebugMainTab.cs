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
        ImGui.Dummy(new Vector2(1, 8)); // Fork: 16 px rhythm between the toolbar button and the first card
        var collectables = Generator.items.Where(item => item.Currency == 22 && item.Type.HasFlag(ItemType.Collectable)).ToList();
        CurrencyChrome.Card("debug_collectables", () =>
        {
            CurrencyChrome.Section("Flame Seal collectables", "Generated item and unlock data.", collectables.Count + " rows");
            if (CurrencyChrome.BeginTable("##NPC", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
            {
                // Fork: Item stretches across the card, the other columns are fixed with the shared header row.
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Item ID", ImGuiTableColumnFlags.WidthFixed, 112);
                ImGui.TableSetupColumn("Unlocked", ImGuiTableColumnFlags.WidthFixed, 112);
                CurrencyChrome.TableHeaders(false, false, false);
                foreach (var item in collectables)
                {
                    ImGui.TableNextColumn();
                    ImGui.Text($"{item.Name}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{item.Id}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{ItemHelper.IsUnlocked(item.Id)}");
                }
                CurrencyChrome.EndTable(); // Fork: pops the shared table style
            }
        });

        CurrencyChrome.Card("debug_shops", () =>
        {
            CurrencyChrome.Section("Generated shops", null, Generator.shops.Count + " rows");
            if (CurrencyChrome.BeginTable("##Shops", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
            {
                // Fork: stretch Name and NPC across the card with the shared header row.
                ImGui.TableSetupColumn("Shop ID", ImGuiTableColumnFlags.WidthFixed, 112);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("NPC", ImGuiTableColumnFlags.WidthStretch);
                CurrencyChrome.TableHeaders(false, false, false);
                foreach (var shop in Generator.shops)
                {
                    ImGui.TableNextColumn();
                    ImGui.Text($"{shop.ShopId}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{shop.ShopName}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{shop.NpcName}");
                }
                CurrencyChrome.EndTable(); // Fork: pops the shared table style
            }
        });
    }
}
