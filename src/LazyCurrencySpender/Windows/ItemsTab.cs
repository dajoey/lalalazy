using CurrencySpender.Classes;
using CurrencySpender.Data;
using Dalamud.Bindings.ImGui;
using LalaUiKit;
using Lumina.Excel.Sheets;

namespace CurrencySpender.Windows;

internal class ItemsTab
{
    private static string SearchQuery = "";
    private static List<ShopItem> FilteredItems = [];

    internal static void Draw()
    {
        uint? itemToRemove = null;
        CurrencyChrome.TwoColumns("items_of_interest",
            () => CurrencyChrome.Card("tracked_items", () =>
            {
                CurrencyChrome.Section("Tracked items",
                    "These items appear when the selected currency can buy them.",
                    C.ItemsOfInterest.Count + " items");
                if (C.ItemsOfInterest.Count == 0)
                {
                    Widgets.StateText("● Empty", UiState.Empty);
                    Widgets.SecondaryWrapped("Search for an item and select it to add the first tracked item.");
                    return;
                }

                if (CurrencyChrome.BeginTable("##itemsofinterest", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
                {
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Actions");
                    ImGui.TableHeadersRow();
                    foreach (var item in C.ItemsOfInterest)
                    {
                        ImGui.TableNextColumn();
                        var currentItem = Service.DataManager.GetExcelSheet<Item>().GetRow(item);
                        UiHelper.LeftAlign(currentItem.Name.ToString());
                        if (ImGui.IsItemHovered() && C.Debug)
                        {
                            ImGui.BeginTooltip();
                            UiHelper.LeftAlign($"ID: {item}");
                            ImGui.EndTooltip();
                        }

                        ImGui.TableNextColumn();
                        if (CurrencyChrome.Button("remove_item_" + item, "Remove"))
                            itemToRemove = item;
                    }
                    ImGui.EndTable();
                }
            }),
            () => CurrencyChrome.Card("item_discovery", () =>
            {
                CurrencyChrome.Section("Find & display");
                CurrencyChrome.Toggle("Show items of interest", ref C.ShowItemsOfInterest,
                    "Include tracked items in applicable spending guides.");
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputTextWithHint("##Search", "Search item names...", ref SearchQuery, 100))
                {
                    if (!string.IsNullOrWhiteSpace(SearchQuery))
                        FilteredItems = Generator.items
                            .Where(item => item.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                            .GroupBy(item => item.Name)
                            .Select(group => group.First())
                            .Where(item => !C.ItemsOfInterest.Contains(item.Id))
                            .ToList();
                    else
                        FilteredItems.Clear();
                }

                if (!string.IsNullOrWhiteSpace(SearchQuery) && FilteredItems.Count > 0)
                {
                    if (ImGui.BeginListBox("##FilteredDropdown", new Vector2(0, Math.Min(FilteredItems.Count * 24.0f, 160.0f))))
                    {
                        foreach (var item in FilteredItems)
                            if (ImGui.Selectable(item.Name))
                            {
                                if (!C.ItemsOfInterest.Contains(item.Id))
                                    C.ItemsOfInterest.Add(item.Id);
                                SearchQuery = "";
                                FilteredItems.Clear();
                                break;
                            }
                        ImGui.EndListBox();
                    }
                }
                else if (!string.IsNullOrWhiteSpace(SearchQuery))
                {
                    Widgets.SecondaryWrapped("No untracked items match this search.");
                }
            }));

        if (itemToRemove.HasValue)
        {
            SearchQuery = "";
            C.ItemsOfInterest.Remove(itemToRemove.Value);
        }
    }
}
