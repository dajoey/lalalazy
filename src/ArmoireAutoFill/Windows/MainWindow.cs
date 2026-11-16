using System.Numerics;
using ArmoireAutoFill.Data;
using ArmoireAutoFill.Data.Shopping;
using ArmoireAutoFill.Logic;
using ArmoireAutoFill.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using LalaUiKit;

namespace ArmoireAutoFill.Windows;

public class MainWindow : Window
{
    private readonly InventoryScanner _scanner;
    private readonly CabinetObserver _cabinet;
    private readonly ArmoireAutoStore _autoStore;
    private static readonly TimeSpan ScanCooldown = TimeSpan.FromSeconds(2);

    private enum Section
    {
        Collection,
        Shopping,
        Store,
    }

    private static readonly ArmoireNavItem[] Navigation =
    [
        new("aaf_nav_collection", "Collection"),
        new("aaf_nav_shopping", "Shopping"),
        new("aaf_nav_store", "Auto-store"),
    ];

    private Section _section = Section.Collection;
    private string? _ksStatus;

    public MainWindow(InventoryScanner scanner, CabinetObserver cabinet, ArmoireAutoStore autoStore)
        : base("Armoire Auto-Fill###ArmoireAutoFillMain")
    {
        _scanner = scanner;
        _cabinet = cabinet;
        _autoStore = autoStore;
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 380),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void OnOpen()
    {
        _scanner.Scan();
    }

    public override void Draw()
    {
        var c = Plugin.Configuration;
        Theme.PushFrame();
        try
        {
            var (state, action) = OverallState();
            ArmoireChrome.TopBar(Navigation, (int)_section, index => _section = (Section)index, state, () => IsOpen = false);

            var showStop = c.AutoStoreOnOpen;
            var footerHeight = ArmoireChrome.FooterHeight(ImGui.GetContentRegionAvail().X, showStop);
            var bodyHeight = MathF.Max(100, ImGui.GetContentRegionAvail().Y - footerHeight);
            ImGui.BeginChild("##armoire_body", new Vector2(0, bodyHeight), false);
            try
            {
                var total = ArmoireGearDatabase.TotalItems;
                var inInv = _scanner.LastInventoryHits;
                var inArm = _scanner.LastArmoireHits;
                var owned = inInv + inArm;
                var missing = Math.Max(total - owned, 0);
                var progress = total > 0 ? (float)owned / total : 0f;

                switch (_section)
                {
                    case Section.Collection:
                        DrawCollection(total, inInv, inArm, owned, missing, progress);
                        break;
                    case Section.Shopping:
                        DrawShopping();
                        break;
                    case Section.Store:
                        DrawStore();
                        break;
                }
            }
            finally
            {
                ImGui.EndChild();
            }

            ArmoireChrome.Footer(state, action, FooterDetail(), showStop, out var stop);
            if (stop && c.AutoStoreOnOpen)
            {
                c.AutoStoreOnOpen = false;
                c.Save();
            }
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private (UiState State, string Action) OverallState()
    {
        if (!ArmoireGearDatabase.IsLoaded)
            return (UiState.NeedsSetup, "gear database still loading — check /xllog if it persists");
        if (_autoStore.IsStoring)
            return (UiState.Running, "storing eligible items to the armoire");
        if (_scanner.LastScan == DateTime.MinValue)
            return (UiState.Empty, "inventory not yet scanned");
        if (!_cabinet.CabinetDataAvailable && _cabinet.CachedArmoireItemIds.Count == 0)
            return (UiState.Blocked, "waiting for the server to send cabinet data");
        return Plugin.Configuration.AutoStoreOnOpen
            ? (UiState.Ready, "auto-store armed on armoire open")
            : (UiState.Idle, "auto-store off — watching inventory only");
    }

    private string FooterDetail()
    {
        if (_autoStore.IsStoring)
            return Plugin.Configuration.AutoStoreOnOpen
                ? "Stop disables future automatic storage; it does not undo the current game action."
                : "Manual storage is running; automatic storage is off.";
        if (Plugin.Configuration.AutoStoreOnOpen)
            return "Stop disables automatic storage when the armoire opens.";
        if (_scanner.LastScan == DateTime.MinValue)
            return "Open Collection to scan; automatic storage is off.";
        return "Automatic storage is off; collection, shopping, and manual storage remain available.";
    }

    private UiState ShoppingNavState()
    {
        if (!KnightshopperCatalogBuilder.IsLoaded)
            return UiState.Idle;
        if (KnightshopperCatalogBuilder.SheetsUnavailable)
            return UiState.Blocked;
        if (KnightshopperCatalogBuilder.BuildFailed)
            return UiState.Failed;
        return UiState.Ready;
    }

    private void DrawCollection(int total, int inInv, int inArm, int owned, int missing, float progress)
    {
        ArmoireChrome.PageHeader("Collection",
            "Every armoire-eligible dungeon drop (level 31+) and whether it is owned.",
            $"{owned} of {total} owned", CabinetLine());

        ArmoireChrome.StatCards(
            new ArmoireStat("Owned", owned.ToString(), total.ToString(), $"{progress * 100:F1}% of all dungeon pieces", progress),
            new ArmoireStat("In inventory", inInv.ToString(), null, "Bags, equipment, and armoury chest", null),
            new ArmoireStat("In armoire", inArm.ToString(), null, CabinetLine(), null),
            new ArmoireStat("Missing", missing.ToString(), null, "Still to collect", null));

        ArmoireChrome.Card("collection_snapshot", 0, () =>
        {
            Widgets.Title("Live snapshot");
            Widgets.SecondaryWrapped(ScanLine());
        });

        var showOwned = Plugin.Configuration.ShowOwnedItems;
        var hideComplete = Plugin.Configuration.HideCompleteDungeons;
        ArmoireChrome.Card("collection_filters", 0, () =>
        {
            Widgets.Title("Collection filters");
            if (ArmoireChrome.Toggle("Show owned items", ref showOwned,
                    "Includes pieces already found in inventory or the armoire in each expanded dungeon."))
            {
                Plugin.Configuration.ShowOwnedItems = showOwned;
                Plugin.Configuration.Save();
            }
            if (ArmoireChrome.Toggle("Hide completed dungeons", ref hideComplete,
                    "Removes dungeons with no missing pieces from the checklist."))
            {
                Plugin.Configuration.HideCompleteDungeons = hideComplete;
                Plugin.Configuration.Save();
            }
        });

        if (!ArmoireGearDatabase.IsLoaded)
        {
            Widgets.InfoBox("Needs setup — the gear database is not available. Check /xllog, then reopen the plugin window.", UiState.NeedsSetup);
            return;
        }
        if (ArmoireGearDatabase.DungeonSets.Count == 0)
        {
            Widgets.InfoBox("Empty — no armoire-eligible dungeon drops were loaded. Check /xllog for data-load errors.", UiState.Empty);
            return;
        }

        DrawDungeonTable(showOwned, hideComplete);
    }

    private string ScanLine()
    {
        if (_scanner.LastScan == DateTime.MinValue)
            return "Inventory not yet scanned — open this window or log in with auto-scan on.";

        var line = $"Inventory scanned {Ago(_scanner.LastScan)} ago — found {_scanner.LastInventoryItemsSeen} items.";
        if (!_cabinet.CabinetDataAvailable && _cabinet.CachedArmoireItemIds.Count == 0)
            line += " Armoire: still waiting for the server to send cabinet data (usually a few seconds after login).";
        else
        {
            var snapshotText = _cabinet.LastSnapshot == DateTime.MinValue
                ? "cached from previous session"
                : $"last update {Ago(_cabinet.LastSnapshot)} ago";
            var availability = _cabinet.CabinetDataAvailable ? "live" : "cached";
            line += $" Armoire snapshot ({availability}): {_cabinet.CachedArmoireItemIds.Count} items currently stored — {snapshotText}.";
        }

        return line;
    }

    private string CabinetLine()
    {
        if (!_cabinet.CabinetDataAvailable && _cabinet.CachedArmoireItemIds.Count == 0)
            return "waiting for cabinet data";
        var availability = _cabinet.CabinetDataAvailable ? "live" : "cached";
        return $"{availability} snapshot";
    }

    private void DrawDungeonTable(bool showOwned, bool hideComplete)
    {
        var ordered = ArmoireGearDatabase.DungeonSets
            .Where(d => !hideComplete || !d.IsComplete)
            .OrderBy(d => d.Level == 0 ? 999 : d.Level)
            .ThenBy(d => d.Name)
            .ToList();
        if (ordered.Count == 0)
        {
            Widgets.InfoBox("Empty — every loaded dungeon is complete. Turn off Hide completed dungeons to review the full collection.", UiState.Empty);
            return;
        }

        var tableHeight = MathF.Max(220, ImGui.GetContentRegionAvail().Y - Theme.Unit);
        if (!ImGui.BeginTable("dungeon_table", 5,
                ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY,
                new Vector2(0, tableHeight)))
            return;

        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, Theme.Surface with { W = 0.32f });
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, Theme.ShellBar with { W = 0.45f });
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, Theme.SurfaceBorder);
        ImGui.TableSetupColumn("Dungeon", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Level", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Items", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Missing", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableHeadersRow();

        foreach (var dungeon in ordered)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            var open = ImGui.TreeNodeEx($"{dungeon.Name}###cfc{dungeon.ContentFinderConditionId}", ImGuiTreeNodeFlags.SpanFullWidth);

            ImGui.TableSetColumnIndex(1);
            ImGui.Text(dungeon.Level > 0 ? $"Lv {dungeon.Level}" : "—");

            ImGui.TableSetColumnIndex(2);
            ImGui.Text(dungeon.Items.Count.ToString());

            ImGui.TableSetColumnIndex(3);
            if (dungeon.MissingCount > 0)
                ImGui.TextColored(Copy.MarkColor(UiState.Missing), dungeon.MissingCount.ToString());
            else
                ImGui.Text("0");

            ImGui.TableSetColumnIndex(4);
            if (dungeon.IsComplete)
                ImGui.TextColored(Copy.MarkColor(UiState.Ready), Copy.Word(UiState.Ready));
            else
                ImGui.TextColored(Copy.MarkColor(UiState.Missing), $"{dungeon.MissingCount} left");

            if (open)
            {
                var itemsToShow = showOwned
                    ? dungeon.Items
                    : dungeon.Items.Where(i => i.Owned == OwnershipStatus.NotOwned);
                var shown = 0;
                foreach (var item in itemsToShow.OrderBy(i => i.Slot).ThenBy(i => i.Name))
                {
                    shown++;
                    ImGui.Bullet();
                    ImGui.SameLine();

                    var (state, badge) = item.Owned switch
                    {
                        OwnershipStatus.InInventory => (UiState.Ready, "Inventory"),
                        OwnershipStatus.InArmoire => (UiState.Ready, "Armoire"),
                        _ => (UiState.Missing, "Missing"),
                    };

                    var slotLabel = item.Slot == GearSlot.Unknown ? "" : $"[{item.Slot}] ";
                    ImGui.TextColored(Copy.MarkColor(state), $"{slotLabel}{item.Name} — {badge}");

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"Item ID: {item.ItemId}");
                        ImGui.Text($"Slot: {item.Slot}");
                        ImGui.Text($"Source: {item.DungeonName ?? "Unknown"}");
                        ImGui.Text($"Status: {badge}");
                        ImGui.EndTooltip();
                    }
                }
                if (shown == 0)
                    Widgets.Caption("No missing pieces. Enable Show owned items to review this dungeon.");
                ImGui.TreePop();
            }
        }

        ImGui.EndTable();
        ImGui.PopStyleColor(4);
    }

    private void DrawShopping()
    {
        if (!KnightshopperCatalogBuilder.IsLoaded)
        {
            ArmoireChrome.PageHeader("Knightshopper shopping list",
                "Turns missing armoire pieces into shopping lists Knightshopper can import.",
                "Catalog not ready", Copy.Word(ShoppingNavState()));

            KnightshopperCatalogBuilder.RequestBuild();
            if (KnightshopperCatalogBuilder.SheetsUnavailable)
            {
                Widgets.InfoBox(
                    "Game data is not available right now, so the shop catalog cannot be built. "
                    + "Reopen this window after the game has fully loaded, or press Refresh.",
                    UiState.Blocked);
                if (ArmoireChrome.Button("Refresh", "Refresh"))
                    KnightshopperCatalogBuilder.RequestRefresh();
            }
            else if (KnightshopperCatalogBuilder.BuildFailed)
            {
                Widgets.InfoBox(
                    $"The shop catalog build failed ({KnightshopperCatalogBuilder.BuildFailureDetail}). "
                    + "It is not retried automatically — see /xllog for details, or press Refresh.",
                    UiState.Failed);
                if (ArmoireChrome.Button("Refresh", "Refresh"))
                    KnightshopperCatalogBuilder.RequestRefresh();
            }
            else
            {
                Widgets.InfoBox("Running — building the shop catalog. Shopping lists will appear when the real vendor data is ready.", UiState.Running);
            }

            return;
        }

        DrawKnightshopperShoppingBody();
    }

    private void DrawKnightshopperShoppingBody()
    {
        if (!ArmoireShoppingInput.TryBuild(_scanner, _cabinet, out var input))
        {
            ArmoireChrome.PageHeader("Knightshopper shopping list",
                "Turns missing armoire pieces into shopping lists Knightshopper can import.",
                "Scan needed", Copy.Word(UiState.Empty));
            Widgets.InfoBox("Empty — no inventory scan is available. Open Collection to scan automatically, then return to Shopping.", UiState.Empty);
            return;
        }

        var result = ShoppingListBuilder.Build(input);
        var buyable = result.Currencies.Sum(c => c.Items.Count);
        var excluded = result.Excluded.Count;
        var inBags = excluded == 0 ? 0 : result.Excluded.Count(e => e.Reason.Contains("inventory"));
        var progressLocked = result.Excluded.Count(e => e.Reason.StartsWith("locked"));
        var totalMissing = result.MissingNotBuyable + buyable;
        var headerCount = totalMissing == 0
            ? "nothing to shop for"
            : $"{buyable} of {totalMissing} missing buyable";

        ArmoireChrome.PageHeader("Knightshopper shopping list",
            "Turns missing armoire pieces into shopping lists Knightshopper can import.",
            headerCount, $"{result.Currencies.Count} currency lists");

        ArmoireChrome.StatCards(
            new ArmoireStat("Vendor-buyable", buyable.ToString(), totalMissing.ToString(),
                "Missing pieces with a usable vendor", totalMissing > 0 ? buyable / (float)totalMissing : null),
            new ArmoireStat("Not vendor-buyable", result.MissingNotBuyable.ToString(), null,
                "Dropped, crafted, or unresolved shops", null),
            new ArmoireStat("Excluded", excluded.ToString(), null,
                $"{inBags} in inventory · {progressLocked} locked", null));

        if (totalMissing == 0)
        {
            Widgets.InfoBox(
                "Nothing missing: every armoire-eligible piece is either already in the armoire "
                + "or in the inventory/armoury chest. Nothing to shop for.",
                UiState.Ready);
            return;
        }

        if (buyable == 0)
        {
            Widgets.InfoBox(
                $"None of the {totalMissing} missing piece(s) is sold by a vendor Knightshopper can reach "
                + "(dropped, crafted, or handler-linked shop). Nothing to add to the shopping list.",
                UiState.Empty);
        }

        ArmoireChrome.Card("shopping_coverage", 0, () =>
        {
            Widgets.Title("Coverage and exclusions");
            if (excluded > 0)
                Widgets.SecondaryWrapped(
                    $"{inBags} missing piece(s) are already in the inventory/armoury chest — Auto-store handles those, no purchase needed.");
            if (progressLocked > 0)
                Widgets.SecondaryWrapped(
                    $"{progressLocked} missing piece(s) need quest or achievement progress not yet unlocked — left out of the "
                    + "import because Knightshopper aborts the whole buy on the first item it cannot unlock.");
            if (result.MissingNotBuyable > 0)
                Widgets.SecondaryWrapped(
                    $"{result.MissingNotBuyable} missing piece(s) are not sold by any vendor Knightshopper can reach (dropped, crafted, or handler-linked shop).");
            if (KnightshopperCatalogBuilder.Snapshot.UnderlistedItemCount > 0)
                Widgets.SecondaryWrapped(
                    $"Not counted: {KnightshopperCatalogBuilder.Snapshot.UnderlistedItemCount} more armoire piece(s) sold only in shops this list "
                    + "cannot safely name (scripted vendors and gil-priced SpecialShop entries) — the real buyable total is higher.");
            if (KnightshopperCatalogBuilder.Snapshot.LeftOutItemCount > 0)
                Widgets.SecondaryWrapped(
                    $"{KnightshopperCatalogBuilder.Snapshot.LeftOutItemCount} items left out: Knightshopper cannot buy them.");
            var leftOutPieces = KnightshopperCatalogBuilder.Snapshot.LeftOutPieces;
            if (leftOutPieces.Count > 0 && ImGui.TreeNode($"Pieces no code can name ({leftOutPieces.Count})"))
            {
                foreach (var piece in leftOutPieces)
                    ImGui.TextWrapped($"{input.ItemNames.GetValueOrDefault(piece.ItemId, $"item {piece.ItemId}")} — {piece.Reason}");
                ImGui.TreePop();
            }
            Widgets.SecondaryWrapped(KnightshopperInstructions.CatalogCheckNote());
        });

        if (!IsKnightshopperLoaded())
            Widgets.InfoBox(KnightshopperInstructions.NotLoadedMessage(), UiState.NeedsSetup);

        foreach (var group in result.Currencies)
        {
            ArmoireChrome.Card($"currency_{group.CurrencyId}", 0, () =>
            {
                var priceLabel = group.TotalPrice > 0 ? $" · {group.TotalPrice:N0} total" : string.Empty;
                Widgets.Title($"{group.CurrencyName} · {group.Items.Count} item(s)");
                Widgets.Secondary($"Quantity 1 per piece{priceLabel}");
                if (ArmoireChrome.Button($"Copy import code##ks{group.CurrencyId}", "Copy import code", true))
                    CopyImportCode(group);

                if (group.Truncated)
                    Widgets.SecondaryWrapped(
                        $"Blocked — only the first {KnightshopperShare.MaxItems} items fit in one Knightshopper list; the rest are skipped this time.");
                if (group.CurrencyId == 2)
                    Widgets.SecondaryWrapped(
                        "Gil prices are the standard vendor price; some vendors charge less with reputation.");

                if (group.Items.Count > 0 && ImGui.TreeNode($"Pieces##ksItems{group.CurrencyId}"))
                {
                    DrawShoppingItemsTable(group);
                    ImGui.TreePop();
                }
            });
        }

        if (!string.IsNullOrEmpty(_ksStatus))
        {
            ArmoireChrome.Card("shopping_import_status", 0, () =>
            {
                Widgets.Title("Import instructions");
                Widgets.SecondaryWrapped(_ksStatus);
            });
        }
    }

    private void CopyImportCode(CurrencyCandidates group)
    {
        try
        {
            var listName = $"Armoire fill ({group.CurrencyName})";
            var items = group.Items
                .Select(c => new Ks1Item(c.Entry.ItemId, c.Entry.VendorId, c.Entry.ShopId, 1, c.Entry.SubCurrency))
                .ToList();
            var share = KnightshopperShare.Encode(group.CurrencyId, listName, items);
            ImGui.SetClipboardText(share);
            _ksStatus = KnightshopperInstructions.CopiedMessage(group.CurrencyId, items.Count, listName);
        }
        catch (Exception ex)
        {
            _ksStatus = $"Encoding failed: {ex.Message}";
        }
    }

    private static void DrawShoppingItemsTable(CurrencyCandidates group)
    {
        if (!ImGui.BeginTable($"ksTable{group.CurrencyId}", 4,
                ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, 280)))
            return;

        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, Theme.ShellBar with { W = 0.35f });
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, Theme.SurfaceBorder);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 3f);
        ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Vendor", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthStretch, 0.5f);
        ImGui.TableHeadersRow();
        foreach (var cand in group.Items)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(cand.Name);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(cand.Entry.Price is { } price ? price.ToString("N0") : "n/a");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(VendorName(cand.Entry.VendorId));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted("1");
        }
        ImGui.EndTable();
        ImGui.PopStyleColor(3);
    }

    private static string VendorName(uint vendorId)
    {
        if (vendorId == 0)
            return "unknown vendor";
        return KnightshopperCatalogBuilder.Snapshot.NpcNames.GetValueOrDefault(vendorId) ?? $"npc {vendorId}";
    }

    private static bool IsKnightshopperLoaded()
    {
        try
        {
            var sub = Svc.PluginInterface.GetIpcSubscriber<int>("Knightshopper.ApiVersion");
            return sub.HasFunction;
        }
        catch
        {
            return false;
        }
    }

    private void DrawStore()
    {
        var c = Plugin.Configuration;
        ArmoireChrome.PageHeader("Auto-store",
            "Moves eligible dungeon gear from bags into the armoire — automatically on open, or on demand.",
            c.AutoStoreOnOpen ? "Auto-store on" : "Auto-store off", CabinetLine());

        ArmoireChrome.TwoColumns(
            () =>
            {
                ArmoireChrome.Card("store_action", 0, () =>
                {
                    Widgets.Title("Store eligible gear");
                    Widgets.SecondaryWrapped("The armoire UI must be open at an inn room. Eligibility, gearset protection, and inventory scope use the existing automation rules.");
                    if (_autoStore.IsStoring)
                    {
                        Widgets.StateText("● Running", UiState.Running);
                        Widgets.SecondaryWrapped("Storing eligible items to the armoire.");
                    }
                    if (ArmoireChrome.Button("Store all to armoire", "Store all to armoire", true, !_autoStore.IsStoring))
                        _autoStore.StoreAll();

                    if (!string.IsNullOrEmpty(_autoStore.LastResultMessage))
                    {
                        Widgets.StateText(_autoStore.LastStoredCount > 0 ? "● Ready" : "● Idle",
                            _autoStore.LastStoredCount > 0 ? UiState.Ready : UiState.Idle);
                        Widgets.SecondaryWrapped(_autoStore.LastResultMessage);
                    }
                });

                ArmoireChrome.Card("store_requirements", 0, () =>
                {
                    Widgets.Title("Requirements");
                    ArmoireChrome.Requirement("Armoire UI", _cabinet.CabinetDataAvailable ? "Live" : "Open at an inn", _cabinet.CabinetDataAvailable);
                    ArmoireChrome.Requirement("Inventory scan", _scanner.LastScan == DateTime.MinValue ? "Needs setup" : "Ready", _scanner.LastScan != DateTime.MinValue);
                    ArmoireChrome.Requirement("Armoury chest", c.AutoStoreIncludeArmory ? "Included" : "Optional", c.AutoStoreIncludeArmory);
                    var sinceScan = DateTime.UtcNow - _scanner.LastScan;
                    if (_scanner.LastScan != DateTime.MinValue && sinceScan < ScanCooldown)
                        Widgets.SecondaryWrapped($"Scan cooldown: {ScanCooldown.TotalSeconds - sinceScan.TotalSeconds:F0}s left.");
                    else
                        Widgets.SecondaryWrapped("Scanner idle.");
                });
            },
            () =>
            {
                ArmoireChrome.Card("store_settings", 0, () =>
                {
                    Widgets.Title("Settings");
                    var autoStore = c.AutoStoreOnOpen;
                    if (ArmoireChrome.Toggle("Auto-store when armoire opens", ref autoStore,
                            "When enabled, eligible items are automatically stored to the armoire the moment the armoire UI opens at an inn room."))
                    {
                        c.AutoStoreOnOpen = autoStore;
                        c.Save();
                    }

                    var includeArmory = c.AutoStoreIncludeArmory;
                    if (ArmoireChrome.Toggle("Also store from armoury chest", ref includeArmory,
                            "When enabled, storing also pulls eligible gear from the armoury chest. Off by default: only the regular inventory (bags) is scanned."))
                    {
                        c.AutoStoreIncludeArmory = includeArmory;
                        c.Save();
                    }

                    var skipGearset = c.SkipGearsetItems;
                    if (ArmoireChrome.Toggle("Skip gear that is in a gearset", ref skipGearset,
                            "When enabled, any item that belongs to a saved gearset is left alone and never stored to the armoire."))
                    {
                        c.SkipGearsetItems = skipGearset;
                        c.Save();
                    }
                });
            });
    }

    private static string Ago(DateTime utc)
    {
        var elapsed = DateTime.UtcNow - utc;
        if (elapsed.TotalSeconds < 60)
            return $"{elapsed.TotalSeconds:F0}s";
        if (elapsed.TotalMinutes < 60)
            return $"{elapsed.TotalMinutes:F0}m";
        return $"{elapsed.TotalHours:F1}h";
    }
}
