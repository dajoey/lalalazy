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
    private readonly HashSet<uint> _openDungeons = [];
    private readonly HashSet<string> _openSections = [];

    public MainWindow(InventoryScanner scanner, CabinetObserver cabinet, ArmoireAutoStore autoStore)
        : base("Armoire Auto-Fill###ArmoireAutoFillMain")
    {
        _scanner = scanner;
        _cabinet = cabinet;
        _autoStore = autoStore;
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        Size = new Vector2(820, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
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

    // The shell lays out its own gutters, so the window itself has no padding.
    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    public override void PostDraw() => ImGui.PopStyleVar();

    public override void Draw()
    {
        var c = Plugin.Configuration;
        ArmoireChrome.ResetLayout();
        Theme.PushFrame();
        try
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var gutter = ArmoireChrome.Gutter(winSize.X);
            ImGui.SetCursorPos(Vector2.Zero);
            var (state, action) = OverallState();
            var y = ArmoireChrome.TopBar(winPos, winSize, gutter, Navigation, (int)_section, index => _section = (Section)index, state, () => IsOpen = false);

            var showStop = c.AutoStoreOnOpen;
            var footerHeight = ArmoireChrome.FooterHeight(winSize, showStop);
            ImGui.SetCursorPos(new Vector2(0, y));
            ArmoireChrome.BeginBody("##armoire_body", new Vector2(winSize.X, MathF.Max(40, winSize.Y - footerHeight - y)), gutter);
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
                ArmoireChrome.EndBody();
            }

            ArmoireChrome.Footer(winPos, winSize, gutter, state, action, FooterDetail(), showStop, out var stop);
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
        if (_section == Section.Shopping && !KnightshopperCatalogBuilder.IsLoaded)
        {
            if (KnightshopperCatalogBuilder.SheetsUnavailable)
                return (UiState.Blocked, "game data unavailable — the shop catalog cannot be built");
            if (KnightshopperCatalogBuilder.BuildFailed)
                return (UiState.Failed, "the shop catalog build failed — see /xllog");
            return (UiState.Running, "building the shop catalog");
        }
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
        if (KnightshopperCatalogBuilder.SheetsUnavailable)
            return UiState.Blocked;
        if (KnightshopperCatalogBuilder.BuildFailed)
            return UiState.Failed;
        return KnightshopperCatalogBuilder.IsLoaded ? UiState.Ready : UiState.Running;
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    // ---- Collection ------------------------------------------------------------------------------------------------

    private void DrawCollection(int total, int inInv, int inArm, int owned, int missing, float progress)
    {
        var loaded = ArmoireGearDatabase.IsLoaded;
        ArmoireChrome.PageHeader("Collection",
            "Every armoire-eligible dungeon drop (level 31+) and whether it is owned.",
            loaded ? $"{owned} of {total} owned" : "Gear database not loaded", loaded ? CabinetLine() : string.Empty);

        if (!loaded)
            ArmoireChrome.Notice("Needs setup — the gear database is not available. Check /xllog, then reopen the plugin window.", UiState.NeedsSetup);

        const string unavailable = "Unavailable until the gear database loads";
        ArmoireChrome.StatCards(
            new ArmoireStat("Owned", loaded ? owned.ToString() : "—", loaded ? total.ToString() : null,
                loaded ? $"{progress * 100:F1}% of all dungeon pieces" : unavailable, loaded ? progress : null),
            new ArmoireStat("In inventory", loaded ? inInv.ToString() : "—", null,
                loaded ? "Bags, equipment, and armoury chest" : unavailable, null),
            new ArmoireStat("In armoire", loaded ? inArm.ToString() : "—", null,
                loaded ? "Items stored in the armoire" : unavailable, null),
            new ArmoireStat("Missing", loaded ? missing.ToString() : "—", null,
                loaded ? "Still to collect" : unavailable, null));

        if (loaded)
            ArmoireChrome.Card("collection_snapshot", 0, () =>
            {
                ArmoireChrome.CardHeader("Live snapshot");
                ArmoireChrome.Paragraph(ScanLine(), gap: 0);
            });

        var showOwned = Plugin.Configuration.ShowOwnedItems;
        var hideComplete = Plugin.Configuration.HideCompleteDungeons;
        ArmoireChrome.Card("collection_filters", 0, () =>
        {
            ArmoireChrome.CardHeader("Collection filters");
            if (ArmoireChrome.Toggle("Show owned items", ref showOwned,
                    "Includes pieces already found in inventory or the armoire in each expanded dungeon."))
            {
                Plugin.Configuration.ShowOwnedItems = showOwned;
                Plugin.Configuration.Save();
            }
            if (ArmoireChrome.Toggle("Hide completed dungeons", ref hideComplete,
                    "Removes dungeons with no missing pieces from the checklist.", separator: true))
            {
                Plugin.Configuration.HideCompleteDungeons = hideComplete;
                Plugin.Configuration.Save();
            }
        });

        if (!loaded)
            return;
        if (ArmoireGearDatabase.DungeonSets.Count == 0)
        {
            ArmoireChrome.Notice("Empty — no armoire-eligible dungeon drops were loaded. Check /xllog for data-load errors.", UiState.Empty);
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
            ArmoireChrome.Notice("Empty — every loaded dungeon is complete. Turn off Hide completed dungeons to review the full collection.", UiState.Empty);
            return;
        }

        ArmoireChrome.Card("dungeon_table", 0, () =>
        {
            var draw = ImGui.GetWindowDrawList();
            var inner = ArmoireChrome.Avail();
            var compact = inner < 520;
            const float rowPad = ArmoireChrome.Gap;
            const float colGap = ArmoireChrome.Gap;
            var lineHeight = ArmoireChrome.LineHeight();
            var secondaryHeight = ArmoireChrome.LineHeight(Theme.SecondaryScale);
            var captionHeight = ArmoireChrome.LineHeight(Theme.CaptionScale);
            string LevelText(DungeonInfo d) => d.Level > 0 ? $"Lv {d.Level}" : "—";
            string StateText(DungeonInfo d) => d.IsComplete ? Copy.Word(UiState.Ready) : $"{d.MissingCount} left";

            var levelWidth = MathF.Max(ArmoireChrome.TextWidth("Level", Theme.CaptionScale), ordered.Max(d => ArmoireChrome.TextWidth(LevelText(d))));
            var itemsWidth = MathF.Max(ArmoireChrome.TextWidth("Items", Theme.CaptionScale), ordered.Max(d => ArmoireChrome.TextWidth(d.Items.Count.ToString())));
            var missingWidth = MathF.Max(ArmoireChrome.TextWidth("Missing", Theme.CaptionScale), ordered.Max(d => ArmoireChrome.TextWidth(d.MissingCount.ToString())));
            var stateWidth = (2 * Theme.DotRadius) + (2 * Theme.Unit)
                + MathF.Max(ArmoireChrome.TextWidth("Status", Theme.CaptionScale), ordered.Max(d => ArmoireChrome.TextWidth(StateText(d))));
            var nameLeft = rowPad + 20;
            var nameWidth = inner - nameLeft - rowPad - levelWidth - itemsWidth - missingWidth - stateWidth - (4 * colGap);
            var levelX = nameLeft + nameWidth + colGap;
            var itemsRight = levelX + levelWidth + colGap + itemsWidth;
            var missingRight = itemsRight + colGap + missingWidth;
            var stateX = missingRight + colGap;

            if (!compact)
            {
                var headerPos = ImGui.GetCursorScreenPos();
                var captionY = headerPos.Y;
                ArmoireChrome.DrawText(draw, new Vector2(headerPos.X + nameLeft, captionY), "Dungeon", Theme.TextSecondary, Theme.CaptionScale);
                ArmoireChrome.DrawText(draw, new Vector2(headerPos.X + levelX, captionY), "Level", Theme.TextSecondary, Theme.CaptionScale);
                ArmoireChrome.DrawTextRight(draw, headerPos.X + itemsRight, captionY, "Items", Theme.TextSecondary, Theme.CaptionScale);
                ArmoireChrome.DrawTextRight(draw, headerPos.X + missingRight, captionY, "Missing", Theme.TextSecondary, Theme.CaptionScale);
                ArmoireChrome.DrawTextRight(draw, headerPos.X + stateX + stateWidth, captionY, "Status", Theme.TextSecondary, Theme.CaptionScale);
                ArmoireChrome.Advance(headerPos, inner, captionHeight, 2 * Theme.Unit);
            }

            foreach (var dungeon in ordered)
            {
                var pos = ImGui.GetCursorScreenPos();
                var open = _openDungeons.Contains(dungeon.ContentFinderConditionId);
                var rowHeight = compact
                    ? (2 * 3 * Theme.Unit) + lineHeight + (Theme.Unit / 2f) + secondaryHeight
                    : (2 * 3 * Theme.Unit) + lineHeight;
                if (ImGui.InvisibleButton($"##dungeon_{dungeon.ContentFinderConditionId}", new Vector2(inner, rowHeight)))
                {
                    if (open)
                        _openDungeons.Remove(dungeon.ContentFinderConditionId);
                    else
                        _openDungeons.Add(dungeon.ContentFinderConditionId);
                    open = !open;
                }
                var hovered = ImGui.IsItemHovered();
                draw.AddRectFilled(pos, pos + new Vector2(inner, rowHeight), Theme.Pack(Theme.ShellBar), ArmoireChrome.Radius);
                if (hovered)
                    draw.AddRectFilled(pos, pos + new Vector2(inner, rowHeight), Theme.Pack(new Vector4(1f, 1f, 1f, 0.05f)), ArmoireChrome.Radius);

                var stateColor = dungeon.IsComplete ? Copy.MarkColor(UiState.Ready) : Copy.MarkColor(UiState.Missing);
                var padY = 3 * Theme.Unit;
                ArmoireChrome.Chevron(draw, pos + new Vector2(rowPad + 4, padY + (lineHeight / 2f)), open || hovered ? Theme.Accent : Theme.TextSecondary, right: !open);
                if (!compact)
                {
                    var textY = pos.Y + padY;
                    ArmoireChrome.DrawFitted(draw, new Vector2(pos.X + nameLeft, textY), dungeon.Name, nameWidth, Theme.TextPrimary);
                    ArmoireChrome.DrawText(draw, new Vector2(pos.X + levelX, textY), LevelText(dungeon), Theme.TextSecondary);
                    ArmoireChrome.DrawTextRight(draw, pos.X + itemsRight, textY, dungeon.Items.Count.ToString(), Theme.TextSecondary);
                    ArmoireChrome.DrawTextRight(draw, pos.X + missingRight, textY, dungeon.MissingCount.ToString(),
                        dungeon.MissingCount > 0 ? Copy.MarkColor(UiState.Missing) : Theme.TextSecondary);
                    var stateRight = pos.X + stateX + stateWidth;
                    var stateLeft = MathF.Round(stateRight - ArmoireChrome.TextWidth(StateText(dungeon)));
                    draw.AddCircleFilled(new Vector2(stateLeft - (2 * Theme.Unit) - Theme.DotRadius, textY + (lineHeight / 2f)), Theme.DotRadius, Theme.Pack(stateColor));
                    ArmoireChrome.DrawTextRight(draw, stateRight, textY, StateText(dungeon), stateColor);
                }
                else
                {
                    var stateTextWidth = ArmoireChrome.TextWidth(StateText(dungeon));
                    var stateRight = pos.X + inner - rowPad;
                    ArmoireChrome.DrawFitted(draw, new Vector2(pos.X + nameLeft, pos.Y + padY), dungeon.Name,
                        inner - nameLeft - rowPad - stateTextWidth - (2 * Theme.DotRadius) - colGap, Theme.TextPrimary);
                    ArmoireChrome.DrawTextRight(draw, stateRight, pos.Y + padY, StateText(dungeon), stateColor);
                    draw.AddCircleFilled(new Vector2(stateRight - stateTextWidth - (2 * Theme.Unit) - Theme.DotRadius, pos.Y + padY + (lineHeight / 2f)),
                        Theme.DotRadius, Theme.Pack(stateColor));
                    var detail = $"{LevelText(dungeon)} · {Plural(dungeon.Items.Count, "item")} · {dungeon.MissingCount} missing";
                    ArmoireChrome.DrawFitted(draw, new Vector2(pos.X + nameLeft, pos.Y + padY + lineHeight + (Theme.Unit / 2f)), detail,
                        inner - nameLeft - rowPad, Theme.TextSecondary, Theme.SecondaryScale);
                }
                ArmoireChrome.Advance(pos, inner, rowHeight, 2 * Theme.Unit);

                if (open)
                    DrawDungeonPieces(dungeon, showOwned, inner);
            }
        });
    }

    /// <summary>The pieces of one expanded dungeon, laid out in a full-width panel under its row.</summary>
    private static void DrawDungeonPieces(DungeonInfo dungeon, bool showOwned, float width)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        const float pad = 3 * Theme.Unit;
        var itemsToShow = (showOwned ? dungeon.Items : dungeon.Items.Where(i => i.Owned == OwnershipStatus.NotOwned))
            .OrderBy(i => i.Slot).ThenBy(i => i.Name).ToList();
        var lineHeight = ArmoireChrome.LineHeight();
        var panelWidth = width;
        var innerWidth = panelWidth - (2 * pad);

        if (itemsToShow.Count == 0)
        {
            const string none = "No missing pieces. Enable Show owned items to review this dungeon.";
            var textHeight = ArmoireChrome.WrappedHeight(none, innerWidth, Theme.SecondaryScale);
            var emptyHeight = (2 * pad) + textHeight;
            draw.AddRectFilled(pos, pos + new Vector2(panelWidth, emptyHeight), Theme.Pack(Theme.ShellBar with { W = 0.6f }), ArmoireChrome.Radius);
            ArmoireChrome.DrawWrapped(draw, pos + new Vector2(pad, pad), none, innerWidth, Theme.TextSecondary, Theme.SecondaryScale);
            ArmoireChrome.Advance(pos, panelWidth, emptyHeight, 2 * Theme.Unit);
            return;
        }

        var badges = new[] { "Inventory", "Armoire", "Missing" };
        var badgeWidth = badges.Max(b => ArmoireChrome.TextWidth(b, Theme.SecondaryScale));
        var slotWidth = itemsToShow.Max(i => i.Slot == GearSlot.Unknown ? 0 : ArmoireChrome.TextWidth(i.Slot.ToString(), Theme.SecondaryScale));
        var slotGap = 3 * Theme.Unit;
        var nameNeed = itemsToShow.Max(i => ArmoireChrome.TextWidth(i.Name));
        var cellNeed = (5 * Theme.Unit) + slotWidth + slotGap + nameNeed + (3 * Theme.Unit) + badgeWidth + Theme.Unit;
        var columns = Math.Clamp((int)((innerWidth + pad) / (cellNeed + pad)), 1, 3);
        var cellWidth = (innerWidth - ((columns - 1) * pad)) / columns;
        var pitch = lineHeight + (2 * Theme.Unit);
        var rows = (itemsToShow.Count + columns - 1) / columns;
        var panelHeight = (2 * pad) + (rows * pitch);
        draw.AddRectFilled(pos, pos + new Vector2(panelWidth, panelHeight), Theme.Pack(Theme.ShellBar with { W = 0.6f }), ArmoireChrome.Radius);

        for (var i = 0; i < itemsToShow.Count; i++)
        {
            var item = itemsToShow[i];
            var cellMin = pos + new Vector2(pad + ((i % columns) * (cellWidth + pad)), pad + ((i / columns) * pitch));
            var cellMax = cellMin + new Vector2(cellWidth, pitch);
            var (state, badge) = item.Owned switch
            {
                OwnershipStatus.InInventory => (UiState.Ready, "Inventory"),
                OwnershipStatus.InArmoire => (UiState.Ready, "Armoire"),
                _ => (UiState.Missing, "Missing"),
            };
            var hovered = ImGui.IsMouseHoveringRect(cellMin, cellMax);
            if (hovered)
                draw.AddRectFilled(cellMin, cellMax, Theme.Pack(new Vector4(1f, 1f, 1f, 0.05f)), Theme.Unit);
            var textY = cellMin.Y + Theme.Unit;
            var color = Copy.MarkColor(state);
            draw.AddCircleFilled(new Vector2(cellMin.X + (2 * Theme.Unit), textY + (lineHeight / 2f)), 2.5f, Theme.Pack(color));
            var slotX = cellMin.X + (5 * Theme.Unit);
            if (item.Slot != GearSlot.Unknown)
                ArmoireChrome.DrawText(draw, new Vector2(slotX, textY + ((lineHeight - ArmoireChrome.LineHeight(Theme.SecondaryScale)) / 2f)),
                    item.Slot.ToString(), Theme.TextSecondary, Theme.SecondaryScale);
            var nameX = slotX + slotWidth + slotGap;
            var nameWidth = cellMax.X - nameX - badgeWidth - (3 * Theme.Unit);
            ArmoireChrome.DrawFitted(draw, new Vector2(nameX, textY), item.Name, nameWidth, Theme.TextPrimary);
            ArmoireChrome.DrawTextRight(draw, cellMax.X - Theme.Unit, textY + ((lineHeight - ArmoireChrome.LineHeight(Theme.SecondaryScale)) / 2f),
                badge, color, Theme.SecondaryScale);

            if (hovered)
            {
                var tipHeight = (4 * lineHeight) + (4 * Theme.Unit) + 8;
                var below = cellMax.Y + Theme.Unit + tipHeight <= ImGui.GetIO().DisplaySize.Y;
                ArmoireChrome.BeginTooltip(new Vector2(cellMin.X + (5 * Theme.Unit), below ? cellMax.Y + Theme.Unit : cellMin.Y - Theme.Unit),
                    below ? Vector2.Zero : new Vector2(0f, 1f));
                ImGui.Text($"Item ID: {item.ItemId}");
                ImGui.Text($"Slot: {item.Slot}");
                ImGui.Text($"Source: {item.DungeonName ?? "Unknown"}");
                ImGui.Text($"Status: {badge}");
                ArmoireChrome.EndTooltip();
            }
        }
        ArmoireChrome.Advance(pos, panelWidth, panelHeight, 2 * Theme.Unit);
    }

    // ---- Shopping --------------------------------------------------------------------------------------------------

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
                ArmoireChrome.Notice(
                    "Game data is not available right now, so the shop catalog cannot be built. "
                    + "Reopen this window after the game has fully loaded, or press Refresh.",
                    UiState.Blocked);
                if (ArmoireChrome.Button("Refresh", "Refresh", true))
                    KnightshopperCatalogBuilder.RequestRefresh();
            }
            else if (KnightshopperCatalogBuilder.BuildFailed)
            {
                ArmoireChrome.Notice(
                    "The shop catalog build failed. It is not retried automatically — see /xllog for details, or press Refresh.",
                    UiState.Failed, KnightshopperCatalogBuilder.BuildFailureDetail);
                if (ArmoireChrome.Button("Refresh", "Refresh", true))
                    KnightshopperCatalogBuilder.RequestRefresh();
            }
            else
            {
                ArmoireChrome.Notice("Running — building the shop catalog. Shopping lists will appear when the real vendor data is ready.", UiState.Running);
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
            ArmoireChrome.Notice("Empty — no inventory scan is available. Open Collection to scan automatically, then return to Shopping.", UiState.Empty);
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
            headerCount, Plural(result.Currencies.Count, "currency list"));

        ArmoireChrome.StatCards(
            new ArmoireStat("Vendor-buyable", buyable.ToString(), totalMissing.ToString(),
                "Missing pieces with a usable vendor", totalMissing > 0 ? buyable / (float)totalMissing : null),
            new ArmoireStat("Not vendor-buyable", result.MissingNotBuyable.ToString(), null,
                "Dropped, crafted, or unresolved shops", null),
            new ArmoireStat("Excluded", excluded.ToString(), null,
                $"{inBags} in inventory · {progressLocked} locked", null));

        if (totalMissing == 0)
        {
            ArmoireChrome.Notice(
                "Nothing missing — every armoire-eligible piece is either already in the armoire "
                + "or in the inventory/armoury chest. Nothing to shop for.",
                UiState.Ready);
            return;
        }

        if (buyable == 0)
        {
            ArmoireChrome.Notice(
                $"None of the {Plural(totalMissing, "missing piece")} is sold by a vendor Knightshopper can reach "
                + "(dropped, crafted, or handler-linked shop). Nothing to add to the shopping list.",
                UiState.Empty);
        }

        var snapshot = KnightshopperCatalogBuilder.Snapshot;
        var notes = new List<string>();
        if (excluded > 0)
            notes.Add($"{Plural(inBags, "missing piece")} {(inBags == 1 ? "is" : "are")} already in the inventory/armoury chest — Auto-store handles those, no purchase needed.");
        if (progressLocked > 0)
            notes.Add($"{Plural(progressLocked, "missing piece")} {(progressLocked == 1 ? "needs" : "need")} quest or achievement progress not yet unlocked — left out of the "
                + "import because Knightshopper aborts the whole buy on the first item it cannot unlock.");
        if (result.MissingNotBuyable > 0)
            notes.Add($"{Plural(result.MissingNotBuyable, "missing piece")} {(result.MissingNotBuyable == 1 ? "is" : "are")} not sold by any vendor Knightshopper can reach (dropped, crafted, or handler-linked shop).");
        if (snapshot.UnderlistedItemCount > 0)
            notes.Add($"Not counted: {Plural(snapshot.UnderlistedItemCount, "more armoire piece")} sold only in shops this list "
                + "cannot safely name (scripted vendors and gil-priced SpecialShop entries) — the real buyable total is higher.");
        if (snapshot.LeftOutItemCount > 0)
            notes.Add($"{Plural(snapshot.LeftOutItemCount, "item")} left out: Knightshopper cannot buy {(snapshot.LeftOutItemCount == 1 ? "it" : "them")}.");

        ArmoireChrome.Card("shopping_coverage", 0, () =>
        {
            ArmoireChrome.CardHeader("Coverage and exclusions");
            DrawNotes(notes);
            var leftOutPieces = snapshot.LeftOutPieces;
            if (leftOutPieces.Count > 0)
            {
                var open = _openSections.Contains("leftout");
                if (ArmoireChrome.Disclosure("leftout", $"Pieces no code can name ({leftOutPieces.Count})", open))
                    Toggle(_openSections, "leftout");
                if (open)
                    foreach (var piece in leftOutPieces)
                        ArmoireChrome.Paragraph($"{input.ItemNames.GetValueOrDefault(piece.ItemId, $"item {piece.ItemId}")} — {piece.Reason}", gap: Theme.Unit);
            }
            ArmoireChrome.Paragraph(KnightshopperInstructions.CatalogCheckNote(), gap: 0);
        });

        if (!IsKnightshopperLoaded())
            ArmoireChrome.Notice(KnightshopperInstructions.NotLoadedMessage(), UiState.NeedsSetup);

        // One price column width for every table so the columns line up from card to card.
        var priceWidth = MathF.Max(ArmoireChrome.TextWidth("Price", Theme.CaptionScale),
            result.Currencies.SelectMany(g => g.Items).Select(PriceText).DefaultIfEmpty("n/a").Max(t => ArmoireChrome.TextWidth(t)));
        foreach (var group in result.Currencies)
        {
            ArmoireChrome.Card($"currency_{group.CurrencyId}", 0, () =>
            {
                var priceLabel = group.TotalPrice > 0 ? $" · {group.TotalPrice:N0} total" : string.Empty;
                ArmoireChrome.CardHeader($"{group.CurrencyName} · {Plural(group.Items.Count, "item")}", $"Quantity 1 per piece{priceLabel}");
                if (ArmoireChrome.Button($"Copy import code##ks{group.CurrencyId}", "Copy import code", true))
                    CopyImportCode(group);

                if (group.Truncated)
                    ArmoireChrome.Paragraph(
                        $"Blocked — only the first {KnightshopperShare.MaxItems} items fit in one Knightshopper list; the rest are skipped this time.");
                if (group.CurrencyId == 2)
                    ArmoireChrome.Paragraph(
                        "Gil prices are the standard vendor price; some vendors charge less with reputation.");

                if (group.Items.Count > 0)
                {
                    var key = $"pieces{group.CurrencyId}";
                    var open = _openSections.Contains(key);
                    if (ArmoireChrome.Disclosure(key, "Pieces", open))
                        Toggle(_openSections, key);
                    if (open)
                        DrawShoppingItemsTable(group, priceWidth);
                }
            });
        }

        if (!string.IsNullOrEmpty(_ksStatus))
        {
            ArmoireChrome.Card("shopping_import_status", 0, () =>
            {
                ArmoireChrome.CardHeader("Import instructions");
                var lines = _ksStatus.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < lines.Length; i++)
                    ArmoireChrome.Paragraph(CurlyQuotes(lines[i]), color: i == 0 ? Theme.TextPrimary : null,
                        scale: i == 0 ? 1f : Theme.SecondaryScale, gap: i == lines.Length - 1 ? 0 : 2 * Theme.Unit);
            });
        }
    }

    private static void Toggle(HashSet<string> set, string key)
    {
        if (!set.Remove(key))
            set.Add(key);
    }

    private static readonly System.Text.RegularExpressions.Regex QuotePair =
        new(@"(?<!\p{L})'([^']+?)'(?!\p{L})", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex LeadingCount =
        new(@"^(\d+) (.*)$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>Quoted control names read as typographic quotes instead of raw single quotes.</summary>
    private static string CurlyQuotes(string text) => QuotePair.Replace(text, "“$1”");

    /// <summary>
    ///     Separated notes: a count column (the leading number in the primary colour, or a dot) and the wrapped
    ///     sentence beside it, with a hairline between notes.
    /// </summary>
    private static void DrawNotes(IReadOnlyList<string> notes)
    {
        if (notes.Count == 0)
            return;
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ArmoireChrome.Avail();
        var parsed = notes.Select(n => LeadingCount.Match(n) is { Success: true } m ? (Count: m.Groups[1].Value, Text: m.Groups[2].Value) : (Count: string.Empty, Text: n)).ToArray();
        var countWidth = MathF.Max(3 * Theme.Unit, parsed.Max(p => ArmoireChrome.TextWidth(p.Count)));
        var textLeft = countWidth + (3 * Theme.Unit);
        var textWidth = MathF.Max(40, width - textLeft);
        var lineHeight = ArmoireChrome.LineHeight();
        var secondaryHeight = ArmoireChrome.LineHeight(Theme.SecondaryScale);
        var y = 0f;
        for (var i = 0; i < parsed.Length; i++)
        {
            var (count, text) = parsed[i];
            if (i > 0)
            {
                draw.AddLine(origin + new Vector2(0, y - (1.5f * Theme.Unit)), origin + new Vector2(width, y - (1.5f * Theme.Unit)), Theme.Pack(Theme.SurfaceBorder));
            }
            if (count.Length > 0)
                ArmoireChrome.DrawText(draw, origin + new Vector2(0, y + ((secondaryHeight - lineHeight) / 2f)), count, Theme.TextPrimary);
            else
                draw.AddCircleFilled(origin + new Vector2(Theme.Unit, y + (secondaryHeight / 2f)), 2f, Theme.Pack(Theme.TextSecondary));
            y += ArmoireChrome.DrawWrapped(draw, origin + new Vector2(textLeft, y), text, textWidth, Theme.TextSecondary, Theme.SecondaryScale) + (3 * Theme.Unit);
        }
        ArmoireChrome.Advance(origin, width, y - (3 * Theme.Unit), 3 * Theme.Unit);
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

    private static string PriceText(ShoppingCandidate cand) => cand.Entry.Price is { } price ? price.ToString("N0") : "n/a";

    /// <summary>One row per piece in the same four columns for every currency; numbers are right aligned.</summary>
    private static void DrawShoppingItemsTable(CurrencyCandidates group, float priceWidth)
    {
        var draw = ImGui.GetWindowDrawList();
        var inner = ArmoireChrome.Avail();
        var compact = inner < 520;
        const float rowPad = 3 * Theme.Unit;
        const float colGap = ArmoireChrome.Gap;
        var lineHeight = ArmoireChrome.LineHeight();
        var secondaryHeight = ArmoireChrome.LineHeight(Theme.SecondaryScale);
        var captionHeight = ArmoireChrome.LineHeight(Theme.CaptionScale);
        var qtyWidth = MathF.Max(ArmoireChrome.TextWidth("Qty", Theme.CaptionScale), ArmoireChrome.TextWidth("1"));
        var vendorWidth = Math.Clamp(0.30f * inner, 140, 260);
        var itemWidth = inner - (2 * rowPad) - priceWidth - vendorWidth - qtyWidth - (3 * colGap);
        var vendorX = rowPad + itemWidth + colGap;
        var priceRight = vendorX + vendorWidth + colGap + priceWidth;
        var qtyRight = priceRight + colGap + qtyWidth;

        var top = ImGui.GetCursorScreenPos();
        if (!compact)
        {
            ArmoireChrome.DrawText(draw, top + new Vector2(rowPad, 0), "Item", Theme.TextSecondary, Theme.CaptionScale);
            ArmoireChrome.DrawText(draw, top + new Vector2(vendorX, 0), "Vendor", Theme.TextSecondary, Theme.CaptionScale);
            ArmoireChrome.DrawTextRight(draw, top.X + priceRight, top.Y, "Price", Theme.TextSecondary, Theme.CaptionScale);
            ArmoireChrome.DrawTextRight(draw, top.X + qtyRight, top.Y, "Qty", Theme.TextSecondary, Theme.CaptionScale);
            ArmoireChrome.Advance(top, inner, captionHeight, 2 * Theme.Unit);
        }

        foreach (var cand in group.Items)
        {
            var pos = ImGui.GetCursorScreenPos();
            var rowHeight = compact
                ? (2 * 2.5f * Theme.Unit) + lineHeight + (Theme.Unit / 2f) + secondaryHeight
                : (2 * 2.5f * Theme.Unit) + lineHeight;
            draw.AddRectFilled(pos, pos + new Vector2(inner, rowHeight), Theme.Pack(Theme.ShellBar), ArmoireChrome.Radius);
            var textY = pos.Y + (2.5f * Theme.Unit);
            if (!compact)
            {
                ArmoireChrome.DrawFitted(draw, new Vector2(pos.X + rowPad, textY), cand.Name, itemWidth, Theme.TextPrimary);
                ArmoireChrome.DrawFitted(draw, new Vector2(pos.X + vendorX, textY), VendorName(cand.Entry.VendorId), vendorWidth, Theme.TextSecondary);
                ArmoireChrome.DrawTextRight(draw, pos.X + priceRight, textY, PriceText(cand), Theme.TextPrimary);
                ArmoireChrome.DrawTextRight(draw, pos.X + qtyRight, textY, "1", Theme.TextSecondary);
            }
            else
            {
                var price = PriceText(cand);
                var priceTextWidth = ArmoireChrome.TextWidth(price);
                ArmoireChrome.DrawFitted(draw, new Vector2(pos.X + rowPad, textY), cand.Name, inner - (2 * rowPad) - priceTextWidth - colGap, Theme.TextPrimary);
                ArmoireChrome.DrawTextRight(draw, pos.X + inner - rowPad, textY, price, Theme.TextPrimary);
                var sub = textY + lineHeight + (Theme.Unit / 2f);
                ArmoireChrome.DrawFitted(draw, new Vector2(pos.X + rowPad, sub), VendorName(cand.Entry.VendorId), inner - (2 * rowPad) - ArmoireChrome.TextWidth("Qty 1", Theme.SecondaryScale) - colGap, Theme.TextSecondary, Theme.SecondaryScale);
                ArmoireChrome.DrawTextRight(draw, pos.X + inner - rowPad, sub, "Qty 1", Theme.TextSecondary, Theme.SecondaryScale);
            }
            ArmoireChrome.Advance(pos, inner, rowHeight, Theme.Unit);
        }
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

    // ---- Auto-store ------------------------------------------------------------------------------------------------

    private void DrawStore()
    {
        var c = Plugin.Configuration;
        ArmoireChrome.PageHeader("Auto-store",
            "Moves eligible dungeon gear from bags into the armoire — automatically on open, or on demand.",
            c.AutoStoreOnOpen ? "Auto-store on" : "Auto-store off", CabinetLine());

        ArmoireChrome.Columns("store",
            () =>
            {
                ArmoireChrome.Card("store_action", 0, () =>
                {
                    ArmoireChrome.CardHeader("Store eligible gear");
                    ArmoireChrome.Paragraph("The armoire UI must be open at an inn room. Eligibility, gearset protection, and inventory scope use the existing automation rules.",
                        gap: 3 * Theme.Unit);
                    if (_autoStore.IsStoring)
                    {
                        ArmoireChrome.StateLine(UiState.Running, "Running");
                        ArmoireChrome.Paragraph("Storing eligible items to the armoire.", gap: 3 * Theme.Unit);
                    }
                    if (ArmoireChrome.Button("Store all to armoire", "Store all to armoire", true, !_autoStore.IsStoring))
                        _autoStore.StoreAll();

                    if (!string.IsNullOrEmpty(_autoStore.LastResultMessage))
                    {
                        ArmoireChrome.StateLine(_autoStore.LastStoredCount > 0 ? UiState.Ready : UiState.Idle,
                            _autoStore.LastStoredCount > 0 ? "Ready" : "Idle");
                        ArmoireChrome.Paragraph(_autoStore.LastResultMessage, gap: 0);
                    }
                });

                ArmoireChrome.Card("store_requirements", 0, () =>
                {
                    ArmoireChrome.CardHeader("Requirements");
                    ArmoireChrome.Requirement("Armoire UI", _cabinet.CabinetDataAvailable ? "Live" : "Open at an inn",
                        _cabinet.CabinetDataAvailable ? UiState.Ready : UiState.Missing, _cabinet.CabinetDataAvailable);
                    var scanned = _scanner.LastScan != DateTime.MinValue;
                    ArmoireChrome.Requirement("Inventory scan", scanned ? "Ready" : "Needs setup",
                        scanned ? UiState.Ready : UiState.Missing, scanned);
                    ArmoireChrome.Requirement("Armoury chest", c.AutoStoreIncludeArmory ? "Included" : "Optional",
                        c.AutoStoreIncludeArmory ? UiState.Ready : UiState.Idle, c.AutoStoreIncludeArmory);
                    var sinceScan = DateTime.UtcNow - _scanner.LastScan;
                    ArmoireChrome.Advance(ImGui.GetCursorScreenPos(), 1, 0, 3 * Theme.Unit);
                    ArmoireChrome.Paragraph(_scanner.LastScan != DateTime.MinValue && sinceScan < ScanCooldown
                        ? $"Scan cooldown: {ScanCooldown.TotalSeconds - sinceScan.TotalSeconds:F0}s left."
                        : "Scanner idle.", gap: 0);
                }, fill: true);
            },
            () =>
            {
                ArmoireChrome.Card("store_settings", 0, () =>
                {
                    ArmoireChrome.CardHeader("Settings");
                    var autoStore = c.AutoStoreOnOpen;
                    if (ArmoireChrome.Toggle("Auto-store when armoire opens", ref autoStore,
                            "When enabled, eligible items are automatically stored to the armoire the moment the armoire UI opens at an inn room."))
                    {
                        c.AutoStoreOnOpen = autoStore;
                        c.Save();
                    }

                    var includeArmory = c.AutoStoreIncludeArmory;
                    if (ArmoireChrome.Toggle("Also store from armoury chest", ref includeArmory,
                            "When enabled, storing also pulls eligible gear from the armoury chest. Off by default: only the regular inventory (bags) is scanned.",
                            separator: true))
                    {
                        c.AutoStoreIncludeArmory = includeArmory;
                        c.Save();
                    }

                    var skipGearset = c.SkipGearsetItems;
                    if (ArmoireChrome.Toggle("Skip gear that is in a gearset", ref skipGearset,
                            "When enabled, any item that belongs to a saved gearset is left alone and never stored to the armoire.",
                            separator: true))
                    {
                        c.SkipGearsetItems = skipGearset;
                        c.Save();
                    }
                }, fill: true);
            },
            fillPage: true);
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
