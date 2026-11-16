using CurrencySpender.Classes;
using CurrencySpender.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using LalaUiKit;

namespace CurrencySpender.Windows;

internal class SpendingWindow : Window
{
    public static TrackedCurrency? Currency;
    internal static List<ShopItem>? CollectableItems;
    internal static List<ShopItem>? Ventures;
    internal static List<ShopItem>? SellableItems;
    internal static List<ShopItem>? ItemsOfInterest;
    internal static List<ShopItem>? GeneralItems;
    private readonly CurrencyChrome.ShellControls _controls;

    public SpendingWindow() : base("SpendingWindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _controls = new(this, new Vector2(720, 360), new Vector2(float.MaxValue, float.MaxValue));
        P.ws.AddWindow(this);
    }

    public override void PreDraw()
    {
        WindowName = $"{P.Name} Spending Guide###SpendingWindow";
    }

    public unsafe override void Draw()
    {
        if (Currency == null)
            return;
        var minimized = _controls.PrepareDraw();
        CurrencyChrome.PushFrame();
        try
        {
            var sectionCount = VisibleSectionCount();
            CurrencyChrome.TopBar("Spending guide", sectionCount > 0 ? UiState.Ready : UiState.Empty,
                controls: _controls, utilityLabel: C.Debug ? "Refresh" : null,
                utilityAction: C.Debug ? RefreshPrices : null, utilityTooltip: "Force refresh Universalis");
            if (minimized)
                return;
            var footerHeight = CurrencyChrome.FooterHeight(); // Fork: suite footer status line
            CurrencyChrome.BeginBody("##currency_spending_body", footerHeight); // Fork: shared scrolling body child; the page header scrolls with it
            CurrencyChrome.PageHeader(Currency.Name,
                "Compare available purchases and open vendor travel actions.",
                $"{StringHelper.FormatString(Currency.CurrentCount.ToString())} available");
            DrawSummary(sectionCount);

            if (C.Debug)
                CurrencyChrome.Card("spending_debug", () =>
                {
                    CurrencyChrome.Section("Diagnostic data");
                    Widgets.SecondaryWrapped($"CurrencyId: {Currency.ItemId}\nCollectableItems: {CollectableItems?.Count} | SellableItems: {SellableItems?.Count} | ItemsOfInterest: {ItemsOfInterest?.Count}\nStorm: {PlayerHelper.GCRanks[1]} Serpent: {PlayerHelper.GCRanks[2]} Flame: {PlayerHelper.GCRanks[3]}");
                });

            List<uint> grandCompanyCurrencies = [20, 21, 22];
            if (grandCompanyCurrencies.Contains(Currency.ItemId))
            {
                if (!PlayerHelper.GCRanksCreated)
                    PlayerHelper.init();
                if (PlayerHelper.GCRanks[Currency.ItemId - 19] < 10)
                    UiHelper.WarningText("Some items cannot be purchased yet due to GC rankings, so they are not displayed here.");
            }
            if (Currency.ItemId == 26807 && !PlayerHelper.SharedFateRanksMax)
                UiHelper.WarningText("Some items cannot be purchased yet due to shared FATE rankings, so they are not displayed here.");

            try
            {
                if (sectionCount == 0)
                {
                    CurrencyChrome.EmptyState("Empty",
                        "No tables match the current settings and availability data. Enable additional table types or choose another currency.",
                        "Open settings", () => P.configTabWindow.IsOpen = true);
                }
                if (C.ShowItemsOfInterest && ItemsOfInterest is { Count: > 0 })
                    DrawSection("items_of_interest", "Items of interest",
                        "Tracked items available for this currency.", ItemsOfInterest.Count,
                        () => DrawSimpleTable("##itemsofinterest", ref ItemsOfInterest));
                if (C.ShowCollectables && CollectableItems is { Count: > 0 })
                    DrawSection("collectables", "Selected collectables not yet registered",
                        "Missing collectables of the selected types.", CollectableItems.Count,
                        DrawCollectableTable);
                if (C.ShowVentures && Ventures is { Count: > 0 })
                    DrawSection("ventures", "Ventures",
                        "Ventures available for this currency.", Ventures.Count,
                        () => DrawSimpleTable("##ventures", ref Ventures));
                if (C.ShowSellables && SellableItems is { Count: > 0 })
                    DrawSection("market_items", "Items eligible for sale on the market board",
                        "Sales history and estimated value for the current balance.", SellableItems.Count,
                        DrawSellableTable);
                if (GeneralItems is { Count: > 0 })
                    DrawSection("general_items", "Equipment and gear exchange",
                        "Equipment and other exchange items available for this currency.", GeneralItems.Count,
                        DrawGeneralTable);
            }
            catch (Exception e)
            {
                Service.Log.Error(e, "ImGuiTable");
            }
            CurrencyChrome.EndBody(); // Fork: pairs with BeginBody
            CurrencyChrome.Footer(footerHeight, Currency.Name, $"{sectionCount} {(sectionCount == 1 ? "table" : "tables")} shown", sectionCount > 0 ? UiState.Ready : UiState.Empty);
        }
        finally
        {
            CurrencyChrome.PopFrame();
        }
    }

    private static void RefreshPrices()
    {
        if (Currency != null)
            P.TaskManager.Enqueue(() => WebHelper.CheckAll(Currency.ItemId, true));
    }

    private static int VisibleSectionCount()
    {
        var count = 0;
        if (C.ShowItemsOfInterest && ItemsOfInterest is { Count: > 0 }) count++;
        if (C.ShowCollectables && CollectableItems is { Count: > 0 }) count++;
        if (C.ShowVentures && Ventures is { Count: > 0 }) count++;
        if (C.ShowSellables && SellableItems is { Count: > 0 }) count++;
        if (GeneralItems is { Count: > 0 }) count++;
        return count;
    }

    private static void DrawSummary(int sectionCount)
    {
        if (Currency is null)
            return;
        var optionCount = 0;
        if (C.ShowItemsOfInterest) optionCount += ItemsOfInterest?.Count ?? 0;
        if (C.ShowCollectables) optionCount += CollectableItems?.Count ?? 0;
        if (C.ShowVentures) optionCount += Ventures?.Count ?? 0;
        if (C.ShowSellables) optionCount += SellableItems?.Count ?? 0;
        optionCount += GeneralItems?.Count ?? 0;
        CurrencyChrome.StatCards(
            new CurrencyChrome.Stat("Current balance", StringHelper.FormatString(Currency.CurrentCount.ToString()),
                StringHelper.FormatString(Currency.MaxCount.ToString()), Currency.Name, Currency.MaxCount == 0 ? null : Currency.Percentage / 100f),
            new CurrencyChrome.Stat("Listed options", optionCount.ToString(), null,
                $"Across {sectionCount} visible tables", null),
            new CurrencyChrome.Stat("Missing collectables", C.ShowCollectables ? (CollectableItems?.Count ?? 0).ToString() : "—", null,
                C.ShowCollectables ? "Selected types not registered" : "Collectables hidden", null));
    }

    private static void DrawSection(string id, string title, string description, int count, Action content)
    {
        CurrencyChrome.Card(id, () =>
        {
            CurrencyChrome.Section(title, description, count + (count == 1 ? " item" : " items")); // Fork: singular for one
            content();
        });
    }

    private static void SortStandard(ref List<ShopItem>? items, ImGuiTableSortSpecsPtr sortSpecs)
    {
        if (items is null || sortSpecs.IsNull || sortSpecs.SpecsCount == 0)
            return;
        ImGuiTableColumnSortSpecsPtr spec = sortSpecs.Specs;
        var ascending = spec.SortDirection == ImGuiSortDirection.Ascending;
        switch (spec.ColumnIndex)
        {
            case 0:
                items = ascending ? items.OrderBy(item => item.Name).ToList() : items.OrderByDescending(item => item.Name).ToList();
                break;
            case 1:
                items = ascending ? items.OrderBy(item => item.Price).ToList() : items.OrderByDescending(item => item.Price).ToList();
                break;
            case 2:
                items = ascending ? items.OrderBy(item => item.Shop.Location.Zone).ToList() : items.OrderByDescending(item => item.Shop.Location.Zone).ToList();
                break;
        }
    }

    // Fork: shared width of the Price column (number, gap and currency icon).
    private const float PriceColumnWidth = 136;

    // Fork: narrowest card the tables lay out in before they scroll sideways (fixed columns plus a readable floor for the stretch ones).
    private static float StandardMinWidth => 200 + PriceColumnWidth + 110 + CurrencyChrome.MapButtonsWidth();
    private static float MarketMinWidth => 250 + 72 + PriceColumnWidth + 64 + 96 + 104 + CurrencyChrome.MapButtonsWidth();

    private static void SetupStandardColumns()
    {
        // Fork: stretch Name and Zone across the card, fixed measured Price and Actions columns, shared header row.
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, PriceColumnWidth);
        ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.WidthFixed, CurrencyChrome.MapButtonsWidth());
        CurrencyChrome.TableHeaders(false, true, false, true);
    }

    private static void DrawSimpleTable(string id, ref List<ShopItem>? items)
    {
        if (items is null || Currency is null)
            return;
        if (!CurrencyChrome.BeginTable(id, 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Sortable, horizontalScroll: true, minInnerWidth: StandardMinWidth))
            return;
        SetupStandardColumns();
        SortStandard(ref items, ImGui.TableGetSortSpecs());
        foreach (var item in items!)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            UiHelper.LeftAlign(item.Name);
            DrawDebugTooltip(item);
            DrawItemContext(item);
            if (item.Currency != Currency.ItemId)
            {
                var childCurrency = P.Currencies.First(cur => cur.ItemId == item.Currency);
                UiHelper.LeftAlign(childCurrency.Name);
                if (ImGui.IsItemHovered() && C.Debug)
                {
                    ImGui.BeginTooltip();
                    UiHelper.LeftAlign($"ID: {childCurrency.ItemId}");
                    ImGui.EndTooltip();
                }
            }
            ImGui.TableSetColumnIndex(1);
            UiHelper.RightAlignWithIcon(item.Price.ToString(), Currency.Icon.Handle, true);
            ImGui.TableSetColumnIndex(2);
            UiHelper.LeftAlign(item.Shop.Location != null ? item.Shop.Location.Zone : "");
            ImGui.TableSetColumnIndex(3);
            UiHelper.BuildMapButtons(item);
        }
        CurrencyChrome.EndTable(); // Fork: pops the shared table style
    }

    private static void DrawCollectableTable()
    {
        if (CollectableItems is null || Currency is null)
            return;
        if (!CurrencyChrome.BeginTable("##collectables", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Sortable, horizontalScroll: true, minInnerWidth: StandardMinWidth))
            return;
        SetupStandardColumns();
        SortStandard(ref CollectableItems, ImGui.TableGetSortSpecs());
        foreach (var item in CollectableItems!)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            if (ItemHelper.ContainerUnlocked.ContainsKey(item.Id)
                && ItemHelper.ContainerUnlocked.TryGetValue(item.Id, out var tuple))
                UiHelper.LeftAlign(item.Name + " (" + tuple.Item1 + "/" + tuple.Item2 + ")");
            else
                UiHelper.LeftAlign(item.Name);
            DrawItemContext(item);
            if (ImGui.IsItemHovered() && C.Debug)
            {
                ImGui.BeginTooltip();
                UiHelper.LeftAlign($"ID: {item.Id}\nCollectableType: {item.CollectableType}\nIsUnlocked: {ItemHelper.IsUnlocked(item.Id)}\nCat: {item.Category}\nShopId: {item.Shop.ShopId}\nNPCName: {item.Shop.NpcName}\nNPCID: {item.Shop.NpcId}\nShopType: {item.Shop.Type}");
                ImGui.EndTooltip();
            }
            if (item.Currency != Currency.ItemId)
            {
                var childCurrency = P.Currencies.First(cur => cur.ItemId == item.Currency);
                var childItems = Generator.items.Where(cur => cur.Id == item.Currency).ToList();
                foreach (var unused in childItems)
                {
                    // Fork: child rows sit in their own line slot with an elbow marker instead of a hand-tuned offset.
                    CurrencyChrome.ChildRowBegin();
                    UiHelper.LeftAlign(childCurrency.Name);
                    CurrencyChrome.ChildRowEnd();
                }
            }

            ImGui.TableSetColumnIndex(1);
            if (item.Currency == Currency.ItemId)
                UiHelper.RightAlignWithIcon(item.Price.ToString(), Currency.Icon.Handle, true);
            else
            {
                var childCurrency = P.Currencies.First(cur => cur.ItemId == item.Currency);
                UiHelper.RightAlignWithIcon(item.Price.ToString(), childCurrency.Icon.Handle, true);
                var childItems = Generator.items.Where(cur => cur.Id == item.Currency).ToList();
                foreach (var childItem in childItems)
                    UiHelper.RightAlignWithIcon((item.Price * childItem.Price).ToString(), Currency.Icon.Handle, true);
            }

            ImGui.TableSetColumnIndex(2);
            UiHelper.LeftAlign(item.Shop.Location != null ? item.Shop.Location.Zone : "Unknown");
            if (item.Currency != Currency.ItemId)
            {
                var childItems = Generator.items.Where(cur => cur.Id == item.Currency).ToList();
                foreach (var childItem in childItems)
                {
                    UiHelper.LeftAlign(childItem.Shop.Location != null ? childItem.Shop.Location.Zone : "Unknown"); // Fork: line slots replace the manual 3 px offset
                }
            }

            ImGui.TableSetColumnIndex(3);
            UiHelper.BuildMapButtons(item);
            if (item.Currency != Currency.ItemId)
            {
                var childItems = Generator.items.Where(cur => cur.Id == item.Currency).ToList();
                foreach (var childItem in childItems)
                    UiHelper.BuildMapButtons(childItem);
            }
        }
        CurrencyChrome.EndTable(); // Fork: pops the shared table style
    }

    private static void DrawSellableTable()
    {
        if (SellableItems is null || Currency is null)
            return;
        if (!CurrencyChrome.BeginTable("##markettable", 7,
                ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Sortable, horizontalScroll: true, minInnerWidth: MarketMinWidth))
            return;
        // Fork: stretch Name, fixed right-aligned numeric columns with shared header row.
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Sales", ImGuiTableColumnFlags.WidthFixed, 72);
        ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, PriceColumnWidth);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("Sells for", ImGuiTableColumnFlags.WidthFixed, 96);
        ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 104);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.WidthFixed, CurrencyChrome.MapButtonsWidth());
        CurrencyChrome.TableHeaders(false, true, true, true, true, true, true);

        var sortSpecs = ImGui.TableGetSortSpecs();
        if (!sortSpecs.IsNull && sortSpecs.SpecsCount > 0 && SellableItems.Count > 0)
        {
            ImGuiTableColumnSortSpecsPtr spec = sortSpecs.Specs;
            var ascending = spec.SortDirection == ImGuiSortDirection.Ascending;
            switch (spec.ColumnIndex)
            {
                case 0: SellableItems = ascending ? SellableItems.OrderBy(item => item.Name).ToList() : SellableItems.OrderByDescending(item => item.Name).ToList(); break;
                case 1: SellableItems = ascending ? SellableItems.OrderBy(item => item.HasSoldWeek).ToList() : SellableItems.OrderByDescending(item => item.HasSoldWeek).ToList(); break;
                case 2: SellableItems = ascending ? SellableItems.OrderBy(item => item.Price).ToList() : SellableItems.OrderByDescending(item => item.Price).ToList(); break;
                case 3: SellableItems = ascending ? SellableItems.OrderBy(item => item.AmountCanBuy).ToList() : SellableItems.OrderByDescending(item => item.AmountCanBuy).ToList(); break;
                case 4: SellableItems = ascending ? SellableItems.OrderBy(item => item.CurrentPrice).ToList() : SellableItems.OrderByDescending(item => item.CurrentPrice).ToList(); break;
                case 5: SellableItems = ascending ? SellableItems.OrderBy(item => item.Profit).ToList() : SellableItems.OrderByDescending(item => item.Profit).ToList(); break;
            }
        }

        foreach (var item in SellableItems)
        {
            item.Profit = item.CurrentPrice * item.AmountCanBuy;
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            UiHelper.LeftAlign(item.Name);
            if (ImGui.IsItemHovered() && C.Debug)
            {
                ImGui.BeginTooltip();
                UiHelper.LeftAlign($"ID: {item.Id}\nName: {item.Name}\nCat: {item.Category}\nNPC:{item.Shop.NpcName}\nShop:{item.Shop.ShopId}\nNpcName: {item.Shop.NpcName}\nNpcId: {item.Shop.NpcId}");
                ImGui.EndTooltip();
            }
            DrawItemContext(item);
            ImGui.TableSetColumnIndex(1);
            UiHelper.RightAlign(item.HasSoldWeek.ToString(), true);
            ImGui.TableSetColumnIndex(2);
            if (item.Currency == Currency.ItemId)
                UiHelper.RightAlignWithIcon(item.Price.ToString(), Currency.Icon.Handle, true);
            else
            {
                var childCurrency = P.Currencies.First(cur => cur.ItemId == item.Currency);
                UiHelper.RightAlignWithIcon(item.Price.ToString(), childCurrency.Icon.Handle, true);
                UiHelper.RightAlignWithIcon((item.Price * childCurrency.Price)?.ToString() ?? "", Currency.Icon.Handle, true);
            }
            ImGui.TableSetColumnIndex(3);
            UiHelper.RightAlign(item.Currency == Currency.ItemId ? item.AmountCanBuy.ToString() : $"-\n{item.AmountCanBuy}", true);
            ImGui.TableSetColumnIndex(4);
            UiHelper.RightAlign(item.CurrentPrice == 0 ? "-" : item.CurrentPrice.ToString(), true);
            ImGui.TableSetColumnIndex(5);
            UiHelper.RightAlign(item.Currency == Currency.ItemId
                ? item.Profit == 0 ? "-" : item.Profit.ToString()
                : "-\n-", true);
            ImGui.TableSetColumnIndex(6);
            UiHelper.BuildMapButtons(item);
        }
        CurrencyChrome.EndTable(); // Fork: pops the shared table style
    }

    private static void DrawGeneralTable()
    {
        if (GeneralItems is null || Currency is null)
            return;
        if (!CurrencyChrome.BeginTable("##generalgear", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Sortable, horizontalScroll: true, minInnerWidth: StandardMinWidth))
            return;
        SetupStandardColumns();
        SortStandard(ref GeneralItems, ImGui.TableGetSortSpecs());
        foreach (var item in GeneralItems!)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            UiHelper.LeftAlign(item.Name);
            DrawItemContext(item);
            DrawDebugTooltip(item);
            ImGui.TableSetColumnIndex(1);
            UiHelper.RightAlignWithIcon(item.Price.ToString(), Currency.Icon.Handle, true);
            ImGui.TableSetColumnIndex(2);
            UiHelper.LeftAlign(item.Shop.Location != null ? item.Shop.Location.Zone : "Unknown");
            ImGui.TableSetColumnIndex(3);
            UiHelper.BuildMapButtons(item);
        }
        CurrencyChrome.EndTable(); // Fork: pops the shared table style
    }

    private static void DrawDebugTooltip(ShopItem item)
    {
        if (!ImGui.IsItemHovered() || !C.Debug)
            return;
        ImGui.BeginTooltip();
        UiHelper.LeftAlign($"ID: {item.Id}\nCat: {item.Category}\nShopId: {item.Shop.ShopId}\nNPCName: {item.Shop.NpcName}\nNPCID: {item.Shop.NpcId}");
        ImGui.EndTooltip();
    }

    private static void DrawItemContext(ShopItem item)
    {
        CurrencyChrome.ContextPopup($"context##{item.Id}-{item.ShopId}-{item.Shop.NpcId}", () =>
        {
            if (ImGui.Selectable("Copy item name"))
            {
                ImGui.SetClipboardText(item.Name);
                UiHelper.Notification("Copied item name to clipboard");
            }
            if (ImGui.Selectable("Create item link"))
            {
                UiHelper.LinkItem(item.Id);
                UiHelper.Notification("Item link created");
            }
        });
    }

    public void GetData(TrackedCurrency cur)
    {
        Currency = cur;
        CollectableItems = ShopHelper.GetCollectableItems(Currency);
        Ventures = ShopHelper.GetVentures(Currency);
        SellableItems = ShopHelper.GetSellableItems(Currency);
        ItemsOfInterest = ShopHelper.GetItemsOfInterest(Currency);
        GeneralItems = ShopHelper.GetGeneralItems(Currency);
    }

    public void UpdateData()
    {
        if (Currency == null)
            return;
        CollectableItems = ShopHelper.GetCollectableItems(Currency);
        Ventures = ShopHelper.GetVentures(Currency);
        SellableItems = ShopHelper.GetSellableItems(Currency);
        ItemsOfInterest = ShopHelper.GetItemsOfInterest(Currency);
        GeneralItems = ShopHelper.GetGeneralItems(Currency);
    }
}
