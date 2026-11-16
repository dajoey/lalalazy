using CurrencySpender.Classes;
using CurrencySpender.Data;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace CurrencySpender.Windows;

internal class MainTab
{
    public static bool colored = false;
    internal static bool NotUpdated = true;
    internal static Dictionary<uint, int> MissingCollectables = new();

    internal static unsafe void Draw()
    {
        update();
        if (Service.Objects.LocalPlayer == null)
        {
            CurrencyChrome.Notice("Needs setup — log in before using Lazy Currency Spender.", UiState.Blocked); // Fork: tinted attention notice instead of the thin kit strip
            return;
        }
        if (P.Problem)
            CurrencyChrome.Card("shared_fate_warning", () =>
            {
                CurrencyChrome.Section("Shared FATE ranks needed");
                Widgets.SecondaryWrapped("The current shared FATE ranks could not be fetched. Open the shared FATE window so the plugin can read them.");
                if (CurrencyChrome.Button("open_shared_fate", "Open shared FATE window", true))
                    PlayerHelper.openSharedFate();
            }, CurrencyCardTone.Attention);

        var visibleCurrencies = P.Currencies.Where(currency =>
            !currency.Child && C.SelectedCurrencies.Contains(currency.ItemId)
            && (!C.HideEmptyCurrencies || currency.CurrentCount > 0)
            && !(currency.ItemId == 26807 && P.Problem)).ToList();
        var availableCount = P.Currencies.Count(currency => currency.Enabled && !currency.Child);
        var missingCount = visibleCurrencies.Sum(currency => MissingCollectables.TryGetValue(currency.ItemId, out var value) ? Math.Max(0, value) : 0);
        CurrencyChrome.StatCards(
            new CurrencyChrome.Stat("Tracked currencies", C.SelectedCurrencies.Count.ToString(), availableCount.ToString(),
                "Selected in settings", availableCount == 0 ? null : C.SelectedCurrencies.Count / (float)availableCount),
            new CurrencyChrome.Stat("Visible now", visibleCurrencies.Count.ToString(), C.SelectedCurrencies.Count.ToString(),
                "After balance and rank filters", C.SelectedCurrencies.Count == 0 ? null : visibleCurrencies.Count / (float)C.SelectedCurrencies.Count),
            new CurrencyChrome.Stat("Missing collectables", missingCount.ToString(), null,
                C.ShowMissingCollectables ? "Across visible currencies" : "Enable missing counts in settings", null));

        if (visibleCurrencies.Count == 0)
        {
            CurrencyChrome.EmptyState("Empty",
                "No selected currency matches the current balance and availability filters. Select a currency or change the zero-balance filter.",
                "Open settings", () => P.configTabWindow.IsOpen = true);
            return;
        }

        CurrencyChrome.Card("currency_balances", () =>
        {
            CurrencyChrome.Section("Currency balances", "Select Spend it to open the full spending guide.",
                visibleCurrencies.Count + " visible");
            if (CurrencyChrome.BeginTable("##currencies", C.ShowMissingCollectables ? 4 : 3,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.Sortable))
            {
                // Fork: Currency stretches across the card, numeric columns are fixed and right aligned, one shared header row.
                ImGui.TableSetupColumn("Currency", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthFixed, 184);
                if (C.ShowMissingCollectables)
                    ImGui.TableSetupColumn("Missing", ImGuiTableColumnFlags.WidthFixed, 88);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.WidthFixed, 104);
                CurrencyChrome.SetHeaderIndent(1, 14); // Fork: header lines up with the amounts after the dot slot
                if (C.ShowMissingCollectables)
                    CurrencyChrome.TableHeaders(false, false, true, true);
                else
                    CurrencyChrome.TableHeaders(false, false, true);
                var lineHeight = ImGui.GetTextLineHeight();
                var area = MathF.Max(30, 2 * lineHeight);

                var sortedCurrencies = P.Currencies;
                ImGuiTableSortSpecsPtr sortSpecs = ImGui.TableGetSortSpecs();
                if (!sortSpecs.IsNull && sortSpecs.SpecsCount > 0)
                {
                    ImGuiTableColumnSortSpecsPtr spec = sortSpecs.Specs;
                    var columnIndex = spec.ColumnIndex;
                    var ascending = spec.SortDirection == ImGuiSortDirection.Ascending;
                    switch (columnIndex)
                    {
                        case 0:
                            sortedCurrencies = ascending
                                ? P.Currencies.ToList()
                                : P.Currencies.ToList().AsEnumerable().Reverse().ToList();
                            break;
                        case 1:
                            sortedCurrencies = ascending
                                ? sortedCurrencies.OrderBy(c => c.Percentage).ToList()
                                : sortedCurrencies.OrderByDescending(c => c.Percentage).ToList();
                            break;
                        case 2:
                            sortedCurrencies = ascending
                                ? sortedCurrencies.OrderBy(c => MissingCollectables.TryGetValue(c.ItemId, out var value) ? value : int.MaxValue).ToList()
                                : sortedCurrencies.OrderByDescending(c => MissingCollectables.TryGetValue(c.ItemId, out var value) ? value : int.MinValue).ToList();
                            break;
                        default:
                            sortedCurrencies = P.Currencies;
                            break;
                    }
                }

                foreach (var currency in sortedCurrencies)
                {
                    if (currency.Child || !C.SelectedCurrencies.Contains(currency.ItemId)
                        || (C.HideEmptyCurrencies && currency.CurrentCount <= 0)
                        || (currency.ItemId == 26807 && P.Problem))
                        continue;

                    // Fork: every cell is centred in one common row area; the near-cap dot sits inline in a fixed lead slot.
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var rowTop = CurrencyChrome.CellBegin(30, area);
                    ImGui.Image(currency.Icon.Handle, new Vector2(30, 30));
                    ImGui.SameLine();
                    ImGui.SetCursorPosY(rowTop + ((area - lineHeight) / 2f));
                    var nameRoom = ImGui.GetContentRegionAvail().X;
                    var shownName = CurrencyChrome.FitText(currency.Name, nameRoom);
                    ImGui.TextUnformatted(shownName);
                    if (shownName != currency.Name && ImGui.IsItemHovered())
                        ImGui.SetTooltip(currency.Name);
                    CurrencyChrome.CellEnd(rowTop, area);

                    ImGui.TableNextColumn();
                    rowTop = CurrencyChrome.CellBegin(2 * lineHeight, area);
                    var amountPos = ImGui.GetCursorScreenPos();
                    if (currency.Percentage > 50)
                        ImGui.GetWindowDrawList().AddCircleFilled(amountPos + new Vector2(4, lineHeight / 2f), Theme.DotRadius,
                            Theme.Pack(Copy.MarkColor(UiState.Running)));
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 14);
                    ImGui.TextUnformatted($"{StringHelper.FormatString(currency.CurrentCount.ToString())}/{StringHelper.FormatString(currency.MaxCount.ToString())}");
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 14);
                    Widgets.Secondary($"~{currency.Percentage}% full");
                    CurrencyChrome.CellEnd(rowTop, area);

                    if (C.ShowMissingCollectables)
                    {
                        ImGui.TableNextColumn();
                        rowTop = CurrencyChrome.CellBegin(lineHeight, area);
                        var missingText = MissingCollectables.TryGetValue(currency.ItemId, out var value) && value > 0 ? value.ToString() : "-";
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(missingText).X));
                        ImGui.TextUnformatted(missingText);
                        CurrencyChrome.CellEnd(rowTop, area);
                    }

                    ImGui.TableNextColumn();
                    rowTop = CurrencyChrome.CellBegin(CurrencyChrome.SmallButtonHeight, area);
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, ImGui.GetContentRegionAvail().X - CurrencyChrome.SmallButtonWidth("Spend it")));
                    if (CurrencyChrome.SmallButton("spend_" + currency.ItemId, "Spend it", true))
                        P.ToggleSpendingUI(currency);
                    CurrencyChrome.CellEnd(rowTop, area);
                }
                CurrencyChrome.EndTable(); // Fork: pops the shared table style
            }
        });
    }

    public static void update(bool Force = false)
    {
        if (NotUpdated || Force)
        {
            MissingCollectables.Clear();
            foreach (TrackedCurrency currency in P.Currencies)
            {
                if (currency.Enabled && !currency.Child)
                {
                    var items_max = Generator.items
                        .Where(item => (item.Currency == currency.ItemId || (currency.Children != null && currency.Children.Contains(item.Currency))) && item.Type.HasFlag(ItemType.Collectable) && !item.Disabled && C.SelectedCollectableTypes.Contains((CollectableType)item.CollectableType))
                        .GroupBy(item => item.Id)
                        .Select(group => group.First())
                        .ToList().Count();
                    var items_unlocked = Generator.items
                        .Where(item => (item.Currency == currency.ItemId || (currency.Children != null && currency.Children.Contains(item.Currency))) && item.Type.HasFlag(ItemType.Collectable) && !item.Disabled && C.SelectedCollectableTypes.Contains((CollectableType)item.CollectableType) && ItemHelper.IsUnlocked(item.Id))
                        .GroupBy(item => item.Id)
                        .Select(group => group.First())
                        .ToList().Count();
                    MissingCollectables.Add(currency.ItemId, items_max - items_unlocked);
                }
            }
            NotUpdated = false;
        }
    }
}
