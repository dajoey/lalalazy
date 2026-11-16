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
                    // Fork: centred empty block instead of a bare bullet and sentence.
                    CurrencyChrome.EmptyBlock("Empty", "Search for an item and select it to add the first tracked item.");
                    return;
                }

                if (CurrencyChrome.BeginTable("##itemsofinterest", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
                {
                    // Fork: shared header row; the Remove button column is fixed and right aligned.
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, CurrencyChrome.SmallButtonWidth("Remove"));
                    CurrencyChrome.TableHeaders(false, true);
                    foreach (var item in C.ItemsOfInterest)
                    {
                        ImGui.TableNextColumn();
                        var currentItem = Service.DataManager.GetExcelSheet<Item>().GetRow(item);
                        UiHelper.LeftAlign(currentItem.Name.ToString());
                        if (ImGui.IsItemHovered() && C.Debug)
                        {
                            ImGui.BeginTooltip(); // Fork: plain text; the table cell helpers would draw nothing in a tooltip window
                            ImGui.TextUnformatted($"ID: {item}");
                            ImGui.EndTooltip();
                        }

                        ImGui.TableNextColumn();
                        var slot = CurrencyChrome.MapButtonsBegin(CurrencyChrome.SmallButtonWidth("Remove")); // Fork: small button centred in the row
                        if (CurrencyChrome.SmallButton("remove_item_" + item, "Remove"))
                            itemToRemove = item;
                        CurrencyChrome.MapButtonsEnd(slot);
                    }
                    CurrencyChrome.EndTable(); // Fork: pops the shared table style
                }
            }),
            () => CurrencyChrome.Card("item_discovery", () =>
            {
                CurrencyChrome.Section("Find & display");
                CurrencyChrome.Toggle("Show items of interest", ref C.ShowItemsOfInterest,
                    "Include tracked items in applicable spending guides.");
                ImGui.SetNextItemWidth(CurrencyChrome.Avail()); // Fork: fill the card's inner width
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
                    if (CurrencyChrome.BeginResultList("##FilteredDropdown", FilteredItems.Count)) // Fork: framed result list inside the card padding
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
                        CurrencyChrome.EndResultList(); // Fork: pairs with BeginResultList
                    }
                }
                else if (!string.IsNullOrWhiteSpace(SearchQuery))
                {
                    CurrencyChrome.Muted("No untracked items match this search."); // Fork: secondary size
                }
            }));

        if (itemToRemove.HasValue)
        {
            SearchQuery = "";
            C.ItemsOfInterest.Remove(itemToRemove.Value);
        }
    }
}
