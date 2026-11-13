using System.Numerics;
using ArmoireAutoFill.Data;
using ArmoireAutoFill.Data.Shopping;
using ArmoireAutoFill.Logic;
using ArmoireAutoFill.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using LalaUiKit;

namespace ArmoireAutoFill.Windows;

// Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13).
// The Dalamud Window base auto-begins/ends this window (Draw() draws content only),
// so the top bar is drawn with the same kit primitives Layout.Shell uses, and the
// suite frame comes from Theme.PushFrame/PopFrame. Colours come only from the kit
// (one accent, reserved state colours); spacing is the kit's 4 px grid.
public class MainWindow : Window
{
    private readonly InventoryScanner _scanner;
    private readonly CabinetObserver _cabinet;
    private readonly ArmoireAutoStore _autoStore;
    private static readonly TimeSpan ScanCooldown = TimeSpan.FromSeconds(2);

    // R1 layer 2: three functional areas, one selected at a time (the accent outline
    // marks exactly the selected section). Three sections, so no group tabs (R1).
    private enum Section
    {
        Collection,
        Shopping,
        Store,
    }

    private Section _section = Section.Collection;

    public MainWindow(InventoryScanner scanner, CabinetObserver cabinet, ArmoireAutoStore autoStore)
        : base("Armoire Auto-Fill###ArmoireAutoFillMain")
    {
        _scanner = scanner;
        _cabinet = cabinet;
        _autoStore = autoStore;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620, 380),
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
            // Live state (Brief R3): derived from the plugin's own session facts.
            var (state, action) = OverallState();
            var total = ArmoireGearDatabase.TotalItems;
            var inInv = _scanner.LastInventoryHits;
            var inArm = _scanner.LastArmoireHits;
            var owned = inInv + inArm;
            var missing = Math.Max(total - owned, 0);
            var progress = total > 0 ? (float)owned / total : 0f;

            DrawTopBar(state, $"{owned}/{total} owned");

            DrawSectionNav();

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

            // Footer (R3 place 3): live dot, state word, current action, Stop pinned right.
            Layout.Footer(state, action, null, "Stop", out var stop);
            if (stop && c.AutoStoreOnOpen)
            {
                // Stop means "stop doing that": the background activity here is auto-store
                // on armoire open, so Stop turns that setting off (the same existing
                // toggle the settings rail offers). No other behaviour exists to stop.
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

    private void DrawTopBar(UiState state, string countDetail)
    {
        // Top bar: title left, live-state pill right (R3 place 1) — the same primitives
        // Layout.Shell draws for self-beginning windows.
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth() - (2 * Theme.Unit);
        var barH = Theme.PillHeight;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(w, barH), Theme.Pack(Theme.ShellBar), 4f);
        dl.AddRect(p, p + new Vector2(w, barH), Theme.Pack(Theme.SurfaceBorder), 4f);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(2 * Theme.Unit, (barH - ImGui.GetTextLineHeight()) / 2f));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted("Armoire Auto-Fill");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(state, "Armoire Auto-Fill", countDetail, alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);
    }

    private void DrawSectionNav()
    {
        var shoppingState = ShoppingNavState();
        var storeState = StoreState();

        if (Layout.NavSection("aaf_nav_collection", "Collection", null, FontAwesomeIcon.BoxOpen.ToIconString(), OverallState().State, _section == Section.Collection))
            _section = Section.Collection;
        ImGui.SameLine();
        if (Layout.NavSection("aaf_nav_shopping", "Shopping", null, FontAwesomeIcon.ShoppingCart.ToIconString(), shoppingState, _section == Section.Shopping))
            _section = Section.Shopping;
        ImGui.SameLine();
        if (Layout.NavSection("aaf_nav_store", "Store", null, FontAwesomeIcon.Archive.ToIconString(), storeState, _section == Section.Store))
            _section = Section.Store;

        ImGui.Dummy(new Vector2(0, Theme.Unit));
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

    private UiState StoreState() => _autoStore.IsStoring
        ? UiState.Running
        : Plugin.Configuration.AutoStoreOnOpen ? UiState.Ready : UiState.NeedsSetup;

    private void DrawCollection(int total, int inInv, int inArm, int owned, int missing, float progress)
    {
        Layout.PageHeader(null, FontAwesomeIcon.BoxOpen.ToIconString(), "Collection",
            "Every armoire-eligible dungeon drop (level 31+) and whether you own it.",
            $"{owned} of {total} owned");

        // Number cards (R6): equal-height kit cards, one question each, no colour legend.
        var cardW = (ImGui.GetContentRegionAvail().X - (3 * Theme.ItemSpacing.X)) / 4f;
        Widgets.StatCard("Owned", owned.ToString(), total.ToString(), progress,
            $"{progress * 100:F1}% of all dungeon pieces", cardW);
        Widgets.StatCard("In inventory", inInv.ToString(), null, null,
            "found in bags and armoury chest", cardW);
        Widgets.StatCard("In armoire", inArm.ToString(), null, null,
            CabinetLine(), cardW);
        Widgets.StatCard("Missing", missing.ToString(), null, null,
            "still to collect", cardW);
        ImGui.Dummy(new Vector2(0, 0));
        ImGui.NewLine();

        Widgets.Secondary(ScanLine());

        var showOwned = Plugin.Configuration.ShowOwnedItems;
        if (Widgets.Checkbox("Show owned items", ref showOwned))
        {
            Plugin.Configuration.ShowOwnedItems = showOwned;
            Plugin.Configuration.Save();
        }

        ImGui.SameLine();
        var hideComplete = Plugin.Configuration.HideCompleteDungeons;
        if (Widgets.Checkbox("Hide completed dungeons", ref hideComplete))
        {
            Plugin.Configuration.HideCompleteDungeons = hideComplete;
            Plugin.Configuration.Save();
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
        if (!ImGui.BeginTable("dungeon_table", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY))
            return;

        ImGui.TableSetupColumn("Dungeon", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Level", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Items", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Missing", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableHeadersRow();

        var ordered = ArmoireGearDatabase.DungeonSets
            .Where(d => !hideComplete || !d.IsComplete)
            .OrderBy(d => d.Level == 0 ? 999 : d.Level)
            .ThenBy(d => d.Name);

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

                foreach (var item in itemsToShow.OrderBy(i => i.Slot).ThenBy(i => i.Name))
                {
                    ImGui.Bullet();
                    ImGui.SameLine();

                    // State colours only (R4): owned = Ready (green), missing = Missing
                    // (amber). Inventory vs armoire is the badge text, never a colour.
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
                ImGui.TreePop();
            }
        }

        ImGui.EndTable();
    }

    private void DrawShopping()
    {
        var glyph = FontAwesomeIcon.ShoppingCart.ToIconString();

        if (!KnightshopperCatalogBuilder.IsLoaded)
        {
            Layout.PageHeader(null, glyph, "Knightshopper shopping list",
                "Turns missing armoire pieces into shopping lists Knightshopper can import.",
                "catalog not ready");

            // One build attempt per open; a failure backs off (no per-frame retry, one
            // log line) until the Refresh button is pressed.
            KnightshopperCatalogBuilder.RequestBuild();
            if (KnightshopperCatalogBuilder.SheetsUnavailable)
            {
                Widgets.InfoBox(
                    "Game data is not available right now, so the shop catalog cannot be built. "
                    + "Reopen this window after the game has fully loaded, or press Refresh.",
                    UiState.Blocked);
                if (ImGui.Button("Refresh"))
                    KnightshopperCatalogBuilder.RequestRefresh();
            }
            else if (KnightshopperCatalogBuilder.BuildFailed)
            {
                Widgets.InfoBox(
                    $"The shop catalog build failed ({KnightshopperCatalogBuilder.BuildFailureDetail}). "
                    + "It is not retried automatically — see /xllog for details, or press Refresh.",
                    UiState.Failed);
                if (ImGui.Button("Refresh"))
                    KnightshopperCatalogBuilder.RequestRefresh();
            }
            else
            {
                Widgets.Banner(UiState.Running, "Building the shop catalog…");
            }

            return;
        }

        DrawKnightshopperShoppingBody(glyph);
    }

    private void DrawKnightshopperShoppingBody(string glyph)
    {
        if (!ArmoireShoppingInput.TryBuild(_scanner, _cabinet, out var input))
        {
            Layout.PageHeader(null, glyph, "Knightshopper shopping list",
                "Turns missing armoire pieces into shopping lists Knightshopper can import.",
                "scan needed");
            Widgets.InfoBox("Rescan the inventory first — open the Collection tab (opening the window scans automatically).", UiState.Idle);
            return;
        }

        var result = ShoppingListBuilder.Build(input);
        var buyable = result.Currencies.Sum(c => c.Items.Count);
        var excluded = result.Excluded.Count;
        var inBags = excluded == 0 ? 0 : result.Excluded.Count(e => e.Reason.Contains("inventory"));
        var totalMissing = result.MissingNotBuyable + buyable;
        var headerCount = totalMissing == 0
            ? "nothing to shop for"
            : $"{buyable} of {totalMissing} missing buyable";

        Layout.PageHeader(null, glyph, "Knightshopper shopping list",
            "Turns missing armoire pieces into shopping lists Knightshopper can import.",
            headerCount);

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

        if (excluded > 0)
            Widgets.Secondary(
                $"{inBags} missing piece(s) are already in your inventory/armoury chest — AutoStore handles those, no purchase needed.");
        var progressLocked = result.Excluded.Count(e => e.Reason.StartsWith("locked"));
        if (progressLocked > 0)
            Widgets.Secondary(
                $"{progressLocked} missing piece(s) need quest or achievement progress you don't have yet — left out of the "
                + "import (Knightshopper aborts the whole buy on the first item it cannot unlock).");
        if (result.MissingNotBuyable > 0)
            Widgets.Secondary(
                $"{result.MissingNotBuyable} missing piece(s) are not sold by any vendor Knightshopper can reach (dropped, crafted, or handler-linked shop).");
        if (KnightshopperCatalogBuilder.Snapshot.UnderlistedItemCount > 0)
            Widgets.Secondary(
                $"Not counted: {KnightshopperCatalogBuilder.Snapshot.UnderlistedItemCount} more armoire piece(s) sold only in shops this list "
                + "cannot safely name (scripted vendors and gil-priced SpecialShop entries) — the real buyable total is higher.");
        if (KnightshopperCatalogBuilder.Snapshot.LeftOutItemCount > 0)
            Widgets.Secondary(
                $"{KnightshopperCatalogBuilder.Snapshot.LeftOutItemCount} items left out: Knightshopper cannot buy them");
        var leftOutPieces = KnightshopperCatalogBuilder.Snapshot.LeftOutPieces;
        if (leftOutPieces.Count > 0 && ImGui.TreeNode($"Pieces no code can name ({leftOutPieces.Count})"))
        {
            // Per-piece reasons for everything the scan had to skip, so a gap is never
            // silent (PvP currency coverage, task armoire-pvp-gear-coverage).
            foreach (var piece in leftOutPieces)
                ImGui.TextWrapped($"{input.ItemNames.GetValueOrDefault(piece.ItemId, $"item {piece.ItemId}")} — {piece.Reason}");
            ImGui.TreePop();
        }
        Widgets.Secondary(KnightshopperInstructions.CatalogCheckNote());

        var ksLoaded = IsKnightshopperLoaded();
        if (!ksLoaded)
            Widgets.InfoBox(KnightshopperInstructions.NotLoadedMessage(), UiState.NeedsSetup);

        foreach (var group in result.Currencies)
        {
            var priceLabel = group.TotalPrice > 0 ? $", {group.TotalPrice:N0} total" : string.Empty;
            ImGui.Text($"{group.CurrencyName}: {group.Items.Count} item(s){priceLabel}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Copy import code##ks{group.CurrencyId}"))
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

            if (group.Truncated)
                Widgets.InfoBox(
                    $"Only the first {KnightshopperShare.MaxItems} items fit in one Knightshopper list — the rest are skipped this time.",
                    UiState.Blocked);
            if (group.CurrencyId == 2)
                Widgets.Secondary(
                    "Gil prices are the standard vendor price; some vendors charge less with reputation.");

            if (group.Items.Count > 0 && ImGui.TreeNode($"Pieces##ksItems{group.CurrencyId}"))
            {
                if (ImGui.BeginTable($"ksTable{group.CurrencyId}", 4,
                        ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                        new Vector2(0, 280)))
                {
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
                }
                ImGui.TreePop();
            }
        }

        if (!string.IsNullOrEmpty(_ksStatus))
            Widgets.Secondary(_ksStatus);
    }

    private string? _ksStatus;

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
        Layout.PageHeader(null, FontAwesomeIcon.Archive.ToIconString(), "Auto-store",
            "Moves eligible dungeon gear from your bags into the armoire — automatically on open, or on demand.",
            c.AutoStoreOnOpen ? "auto-store on" : "auto-store off");

        using var cols = Layout.TwoColumns.Begin(0.55f);

        // Left: the work — store now, with the live result announced once (R3/R11).
        if (_autoStore.IsStoring)
        {
            Widgets.Banner(UiState.Running, "Storing eligible items to the armoire…");
            ImGui.BeginDisabled();
            ImGui.Button("Store all to armoire");
            ImGui.EndDisabled();
        }
        else
        {
            if (ImGui.Button("Store all to armoire"))
                _autoStore.StoreAll();
        }

        if (!string.IsNullOrEmpty(_autoStore.LastResultMessage))
        {
            if (_autoStore.LastStoredCount > 0)
                Widgets.Banner(UiState.Ready, _autoStore.LastResultMessage);
            else
                Widgets.Secondary(_autoStore.LastResultMessage);
        }
        else if (!_autoStore.IsStoring)
        {
            Widgets.Secondary("The armoire UI must be open at an inn room for storing to work.");
        }

        var sinceScan = DateTime.UtcNow - _scanner.LastScan;
        if (_scanner.LastScan != DateTime.MinValue && sinceScan < ScanCooldown)
            Widgets.Secondary($"Scan cooldown: {ScanCooldown.TotalSeconds - sinceScan.TotalSeconds:F0}s left.");
        else
            Widgets.Secondary("Scanner idle.");

        cols.Split();

        // Right rail: grouped settings (R10), each with the old tooltip as written copy.
        Widgets.Title("Settings");
        var autoStore = c.AutoStoreOnOpen;
        if (Widgets.Checkbox("Auto-store when armoire opens", ref autoStore))
        {
            c.AutoStoreOnOpen = autoStore;
            c.Save();
        }
        Widgets.Secondary("When enabled, eligible items are automatically stored to the armoire the moment you open the armoire UI at an inn room.");

        var includeArmory = c.AutoStoreIncludeArmory;
        if (Widgets.Checkbox("Also store from armoury chest", ref includeArmory))
        {
            c.AutoStoreIncludeArmory = includeArmory;
            c.Save();
        }
        Widgets.Secondary("When enabled, storing also pulls eligible gear from the armoury chest. Off by default: only the regular inventory (bags) is scanned.");

        var skipGearset = c.SkipGearsetItems;
        if (Widgets.Checkbox("Skip gear that is in a gearset", ref skipGearset))
        {
            c.SkipGearsetItems = skipGearset;
            c.Save();
        }
        Widgets.Secondary("When enabled, any item that belongs to one of your saved gearsets is left alone and never stored to the armoire.");

        cols.Dispose();
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
