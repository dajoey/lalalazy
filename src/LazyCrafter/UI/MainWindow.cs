using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LalaUiKit;
using LazyCrafter.Adapters;
using LazyCrafter.Catalog;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.UI;

/// <summary>Integrated LazyCrafter shell. Catalog data and run state remain snapshot-driven.</summary>
public sealed class MainWindow : Window
{
    private readonly Plugin _plugin;
    private readonly CatalogTable _table;
    private readonly IngredientTree _tree;
    private readonly CartPanel _cart;
    private readonly SettingsTab _settings;
    private readonly RunTab _run;
    private bool _selectRunNext;
    private bool _wasRunActive;

    private enum Section
    {
        Catalog,
        Cart,
        Run,
        Settings,
    }

    private sealed record Destination(string Id, string Title, string Description, string SearchTerms, Section Section, CatalogTab Tab);

    private static readonly Destination[] Destinations =
    [
        new("catalog.now", "Craft now", "Recipes whose materials are already available, ranked with live market data.", "ready available ingredients craft now", Section.Catalog, CatalogTab.Now),
        new("catalog.easy", "Easy crafts", "Recipes that need a small amount of straightforward material work.", "easy simple materials craft", Section.Catalog, CatalogTab.Easy),
        new("catalog.some", "Some effort", "Recipes with a moderate material plan across gathering, vendors and sub-crafts.", "some effort medium materials", Section.Catalog, CatalogTab.SomeEffort),
        new("catalog.real", "Real effort", "Recipes with the largest material plans and the most involved prerequisites.", "real effort hard materials", Section.Catalog, CatalogTab.RealEffort),
        new("catalog.leveling", "Leveling", "Crafting-log experience for the selected job and its currently practical recipes.", "leveling experience exp job", Section.Catalog, CatalogTab.Leveling),
        new("catalog.log", "Log completion", "Recipes not yet recorded as crafted, with their current material cost.", "log completion first craft missing", Section.Catalog, CatalogTab.LogCompletion),
        new("catalog.under", "Undersupplied", "Craftable results that meet the configured sales and listing thresholds.", "undersupplied listings velocity market", Section.Catalog, CatalogTab.Undersupplied),
        new("cart", "Cart & materials", "Adjust craft counts, inspect the shared missing-material ledger, and dispatch the plan.", "cart materials missing dispatch teamcraft", Section.Cart, CatalogTab.Now),
        new("run", "Current run", "Watch each dispatch step, resolve blocked shopping, resume, or copy the full report.", "run dispatch steps blocked resume report stop", Section.Run, CatalogTab.Now),
        new("settings", "Settings", "Inventory sources, prices, catalog thresholds, dispatch behavior and integrations.", "settings inventory prices sources knightshopper artisan lifestream retainers", Section.Settings, CatalogTab.Now),
    ];

    private Section _section = Section.Catalog;

    // Catalog view state belongs to the UI thread; computation remains on CatalogService's worker.
    private CatalogTab _tab = CatalogTab.Now;
    private uint _jobFilter;
    private bool _hqOnly;
    private float _minVelocity;
    private bool _hideUntradeable;
    private string _search = "";
    private string _pageSearch = "";
    private SortKey _sort = SortKey.PerDay;
    private bool _descending = true;
    private CatalogTab _sortTab = CatalogTab.Now;
    private uint _levelingJob;
    private uint _selectedRecipe;

    public MainWindow(Plugin plugin) : base("LazyCrafter##lcraft")
    {
        _plugin = plugin;
        _table = new CatalogTable(plugin);
        _tree = new IngredientTree(plugin);
        _cart = new CartPanel(plugin, this);
        _settings = new SettingsTab(plugin);
        _run = new RunTab(plugin, this);
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        Size = new Vector2(1180, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 560),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public uint SelectedRecipe => _selectedRecipe;

    private Destination CurrentDestination => Destinations.First(d =>
        d.Section == _section && (_section != Section.Catalog || d.Tab == _tab));

    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    public override void PostDraw() => ImGui.PopStyleVar();

    public override void Draw()
    {
        var svc = _plugin.Catalog;
        var snap = svc.Snapshot;
        var snapRun = _plugin.Dispatch.Snapshot;

        // Preserve the existing one-shot transition to Run when a dispatch becomes active.
        var runActive = snapRun.State is RunState.Running or RunState.Blocked;
        if (runActive && !_wasRunActive)
            _selectRunNext = true;
        _wasRunActive = runActive;
        if (_selectRunNext)
        {
            _section = Section.Run;
            _selectRunNext = false;
        }

        CrafterChrome.PushFrame();
        try
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var gutter = CrafterChrome.Gutter(winSize.X);
            var (state, action, reason) = OverallState(snap);
            ImGui.SetCursorPos(Vector2.Zero);
            var y = DrawTopBar(winPos, winSize.X, gutter, state);
            if (_section == Section.Catalog)
            {
                y = DrawBucketBar(winPos, winSize.X, gutter, y, snap);
                PushRequest(svc);
            }

            var destination = CurrentDestination;
            y = DrawPageHeader(winPos, winSize.X, gutter, y, destination, LiveCount(snap, snapRun), LiveSubtext(snap, snapRun));

            var footerHeight = FooterHeight(winSize.X);
            ImGui.SetCursorPos(new Vector2(0, y));
            DrawBody(new Vector2(winSize.X, MathF.Max(40, winSize.Y - footerHeight - y)), gutter, svc, snap);
            DrawFooter(winPos, winSize, gutter, footerHeight, snap, snapRun, state, action, reason);
        }
        finally
        {
            CrafterChrome.PopFrame();
        }
    }

    private void DrawBody(Vector2 size, float gutter, CatalogService svc, CatalogSnapshot snap)
    {
        // A one pixel transparent border makes the child honour its window padding, which gives every page the same
        // left and right gutter and a bottom margin; the scrollbar then sits on the window edge.
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(gutter, CrafterChrome.Gap));
        ImGui.PushStyleColor(ImGuiCol.Border, 0u);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
        ImGui.BeginChild("##crafter_body", size, true);
        // The child has applied its own padding; popups and tooltips opened from page content keep the theme padding.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.WindowPadding);
        // Content ends at the same right edge as the header controls whether or not a scrollbar is showing; the
        // scrollbar sits in the margin beside it, so the draw clip is widened to the whole child.
        var childPos = ImGui.GetWindowPos();
        ImGui.PushClipRect(childPos, childPos + ImGui.GetWindowSize(), false);
        CrafterChrome.PushRight(childPos.X + ImGui.GetWindowWidth() - gutter);
        DrawStatusAnnouncements(snap);
        switch (_section)
        {
            case Section.Catalog:
                DrawCatalogSection(svc, snap);
                break;
            case Section.Cart:
                _cart.Draw(snap);
                break;
            case Section.Run:
                _run.Draw();
                break;
            case Section.Settings:
                _settings.Draw();
                break;
        }
        CrafterChrome.PopRight();
        ImGui.PopClipRect();
        ImGui.PopStyleVar();
        // Rows are never sliced by the footer without a hint: a soft fade marks content that continues below.
        if (ImGui.GetScrollMaxY() > 0 && ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1)
        {
            var draw = ImGui.GetWindowDrawList();
            var bg = ImGui.GetColorU32(ImGuiCol.WindowBg);
            var min = ImGui.GetWindowPos() + new Vector2(1, ImGui.GetWindowHeight() - (7 * Theme.Unit));
            draw.AddRectFilledMultiColor(min, ImGui.GetWindowPos() + new Vector2(ImGui.GetWindowWidth() - 1, ImGui.GetWindowHeight() - 1),
                bg & 0x00FFFFFFu, bg & 0x00FFFFFFu, bg, bg);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }

    /// <summary>Selects Run on the next draw, preserving the cart panel's existing navigation action.</summary>
    internal void OpenRunTab() => _selectRunNext = true;

    internal void OpenCart() => _section = Section.Cart;

    internal void OpenCatalog() => _section = Section.Catalog;

    private void Navigate(Destination destination)
    {
        _section = destination.Section;
        if (destination.Section == Section.Catalog)
            OnTabChanged(destination.Tab);
        _pageSearch = "";
    }

    private void PushRequest(CatalogService svc)
    {
        svc.Request(new ViewRequest(_tab, _jobFilter, _hqOnly, _minVelocity, _hideUntradeable, _search, _sort, _descending,
            _levelingJob, _plugin.Config.UndersuppliedMinVelocity, _plugin.Config.UndersuppliedMaxListings, _plugin.Config.ShowAboveLevel));
    }

    private void OnTabChanged(CatalogTab tab)
    {
        _tab = tab;
        if (_sortTab == tab)
            return;
        _sort = ViewRequest.DefaultSort(tab);
        _descending = ViewRequest.DefaultDescending(tab);
        _sortTab = tab;
    }

    private (UiState State, string Action, string? Reason) OverallState(CatalogSnapshot snap)
    {
        var run = _plugin.Dispatch.Snapshot;
        if (_plugin.GameData is null)
            return _plugin.GameDataLoad.IsCompleted
                ? (UiState.Failed, "game data failed to load", "see /xllog")
                : (UiState.Running, "loading game data", null);
        if (run.State == RunState.Failed)
            return (UiState.Failed, "run stopped", string.IsNullOrEmpty(run.Status) ? null : run.Status);
        if (run.State == RunState.Running)
            return (UiState.Running, string.IsNullOrEmpty(run.Status) ? run.PhaseLabel : run.Status, null);
        if (run.State == RunState.Blocked)
            return (UiState.Blocked, "run needs attention", string.IsNullOrEmpty(run.Status) ? null : run.Status);
        if (run.State == RunState.Done)
            return (UiState.Ready, "run finished", null);
        if (snap.InventoryDegraded)
            return (UiState.Missing, "inventory counts are limited", "counting this character's bags only");
        if (!snap.LoggedIn)
            return (UiState.Idle, "waiting for a character", "showing every recipe");
        if (_plugin.Catalog.Busy)
            return (UiState.Running, "computing the catalog", null);
        if (_plugin.Catalog.LastError is { } error)
            return (UiState.Failed, "catalog failed", error);
        if (_section == Section.Cart && snap.Cart.Count == 0)
            return (UiState.Empty, "cart is empty", "add a recipe from the Catalog");
        if (_section == Section.Run && run.State == RunState.Idle)
            return (UiState.Idle, "no run yet", "dispatch a cart to begin");
        if (_section == Section.Catalog && _plugin.Catalog.View.Generation == snap.Generation && _plugin.Catalog.View.Rows.Count == 0)
            return (UiState.Empty, "no recipes match", "change the filters or effort bucket");
        return (UiState.Ready, _section switch
        {
            Section.Cart => "cart ready",
            Section.Run => "run controls ready",
            Section.Settings => "settings ready",
            _ => "catalog current",
        }, null);
    }

    private string LiveCount(CatalogSnapshot snap, RunSnapshot run)
    {
        switch (_section)
        {
            case Section.Catalog:
                return $"{_plugin.Catalog.View.Rows.Count:N0} shown · {snap.Cart.Count} in cart";
            case Section.Cart:
                return $"{snap.Cart.Count} recipes · {snap.CartTotals.Missing.Count()} missing";
            case Section.Run:
                if (run.State == RunState.Idle)
                    return "no run";
                var done = run.Steps.Count(step => step.State == StepState.Done);
                return $"{done}/{run.Steps.Count} steps · {RunReport.Elapsed(RunTab.ElapsedFor(run))}";
            case Section.Settings:
                var sources = Enum.GetValues<InventorySource>();
                return $"{sources.Count(source => _plugin.Config.IsSourceEnabled(source))}/{sources.Length} inventory sources";
            default:
                return "";
        }
    }

    private string LiveSubtext(CatalogSnapshot snap, RunSnapshot run)
    {
        var scope = _plugin.Prices.Scope.Length == 0 ? "no market scope" : _plugin.Prices.Scope;
        return _section switch
        {
            Section.Catalog => _plugin.Catalog.Busy ? "Computing" : scope,
            Section.Cart => snap.Cart.Count == 0 ? "add from the Catalog" : $"worst tier {Fmt.TierName(snap.CartTotals.Tier)}",
            Section.Run => run.State == RunState.Idle ? "" : scope,
            Section.Settings => scope,
            _ => "",
        };
    }

    private float DrawTopBar(Vector2 winPos, float winWidth, float gutter, UiState state)
    {
        var draw = ImGui.GetWindowDrawList();
        const float gap = 2 * Theme.Unit;
        const float wideGap = 4 * Theme.Unit;
        const float pad = 14f;
        const float row = 40f;
        const float minSearch = 180f;
        const float maxSearch = 320f;
        var control = CrafterChrome.ControlHeight;
        var inner = winWidth - (2 * gutter);
        var groups = Enum.GetValues<Section>();
        var brandSize = new Vector2(CrafterChrome.BrandWidth(), row);
        var groupSizes = groups.Select(group => new Vector2(MathF.Max(72, CrafterChrome.TextWidth(group.ToString()) + 32), control)).ToArray();
        var stateLabel = Copy.Word(state);
        var pillSize = new Vector2(CrafterChrome.PillWidth(), control);
        var closeSize = new Vector2(control);
        var groupsWidth = groupSizes.Sum(size => size.X) + ((groups.Length - 1) * gap);
        var rightWidth = pillSize.X + gap + closeSize.X;
        var navWidth = brandSize.X + wideGap + groupsWidth;
        var oneRow = inner >= navWidth + wideGap + minSearch + wideGap + rightWidth;
        var twoRows = !oneRow && inner >= navWidth + wideGap + rightWidth;
        var controlY = pad + ((row - control) / 2f);
        var brandPos = new Vector2(gutter, pad);
        var closePos = new Vector2(winWidth - gutter - closeSize.X, controlY);
        var pillPos = new Vector2(closePos.X - gap - pillSize.X, controlY);
        var groupPositions = new Vector2[groups.Length];
        var groupSizesUsed = (Vector2[])groupSizes.Clone();
        Vector2 searchPos;
        float searchWidth;
        float barHeight;
        var secondRowY = pad + row + gap;
        if (oneRow || twoRows)
        {
            var x = gutter + brandSize.X + wideGap;
            for (var i = 0; i < groups.Length; i++)
            {
                groupPositions[i] = new Vector2(x, controlY);
                x += groupSizes[i].X + gap;
            }
            if (oneRow)
            {
                var room = pillPos.X - wideGap - (gutter + navWidth + wideGap);
                searchWidth = MathF.Min(maxSearch, room);
                searchPos = new Vector2(pillPos.X - wideGap - searchWidth, controlY);
                barHeight = pad + row + pad;
            }
            else
            {
                searchPos = new Vector2(gutter, secondRowY);
                searchWidth = inner;
                barHeight = searchPos.Y + control + pad;
            }
        }
        else if (groupsWidth + wideGap + minSearch <= inner)
        {
            // Compact: brand and status on the first row; the groups and the search share the second.
            var x = gutter;
            for (var i = 0; i < groups.Length; i++)
            {
                groupPositions[i] = new Vector2(x, secondRowY);
                x += groupSizes[i].X + gap;
            }
            searchPos = new Vector2(gutter + groupsWidth + wideGap, secondRowY);
            searchWidth = inner - groupsWidth - wideGap;
            barHeight = secondRowY + control + pad;
        }
        else
        {
            var each = (inner - ((groups.Length - 1) * gap)) / groups.Length;
            for (var i = 0; i < groups.Length; i++)
            {
                groupSizesUsed[i] = new Vector2(each, control);
                groupPositions[i] = new Vector2(gutter + (i * (each + gap)), secondRowY);
            }
            searchPos = new Vector2(gutter, secondRowY + control + gap);
            searchWidth = inner;
            barHeight = searchPos.Y + control + pad;
        }

        draw.PushClipRect(winPos, winPos + new Vector2(winWidth, barHeight + 1), false);
        draw.AddRectFilled(winPos, winPos + new Vector2(winWidth, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, barHeight), winPos + new Vector2(winWidth, barHeight), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
        if (CrafterChrome.BrandButton(draw, winPos + brandPos, brandSize))
            Navigate(Destinations[0]);
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            if (CrafterChrome.OutlineButton(draw, "group_" + group, group.ToString(), winPos + groupPositions[i], groupSizesUsed[i], _section == group))
                _section = group;
        }
        DrawSearchInput(winPos + searchPos, searchWidth);
        CrafterChrome.StatusPill(draw, winPos + pillPos, pillSize, stateLabel, state);
        if (CrafterChrome.CloseButton(draw, winPos + closePos, closeSize))
            IsOpen = false;
        DrawSearchResults(winPos + searchPos, searchWidth);
        return barHeight + 1;
    }

    private void DrawSearchInput(Vector2 pos, float width)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint("##crafter_page_search", "Search pages and settings...", ref _pageSearch, 128);
    }

    /// <summary>Results float under the search box as a popup; opening it never moves the page.</summary>
    private void DrawSearchResults(Vector2 searchPos, float searchWidth)
    {
        const string popupId = "##crafterSearchResults";
        var query = _pageSearch.Trim();
        if (query.Length == 0)
            return;
        var matches = Destinations.Where(destination =>
                (destination.Title + " " + destination.Description + " " + destination.SearchTerms + " " + destination.Section)
                .Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToArray();
        ImGui.SetNextWindowPos(searchPos + new Vector2(0, CrafterChrome.ControlHeight + Theme.Unit));
        ImGui.SetNextWindowSize(new Vector2(MathF.Max(searchWidth, 300), 0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3 * Theme.Unit, 3 * Theme.Unit));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2 * Theme.Unit, 2 * Theme.Unit));
        if (!ImGui.IsPopupOpen(popupId))
            ImGui.OpenPopup(popupId);
        if (ImGui.BeginPopup(popupId, ImGuiWindowFlags.NoFocusOnAppearing))
        {
            Widgets.Caption(matches.Length == 1 ? $"1 destination for ‘{query}’" : $"{matches.Length} destinations for ‘{query}’");
            if (matches.Length == 0)
                Widgets.SecondaryWrapped("No page or setting matches this search. Recipe-name search remains inside the Catalog.");
            foreach (var match in matches)
                if (ImGui.Selectable($"{DestinationLabel(match)}##destination_{match.Id}"))
                    Navigate(match);
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(2);
    }

    private static string DestinationLabel(Destination destination)
        => destination.Title.StartsWith(destination.Section.ToString(), StringComparison.OrdinalIgnoreCase)
            ? destination.Title
            : $"{destination.Section} · {destination.Title}";

    private static void DrawBar(ImDrawListPtr draw, Vector2 winPos, float winWidth, float top, float height)
    {
        draw.PushClipRect(winPos + new Vector2(0, top), winPos + new Vector2(winWidth, top + height + 1), false);
        draw.AddRectFilled(winPos + new Vector2(0, top), winPos + new Vector2(winWidth, top + height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, top + height), winPos + new Vector2(winWidth, top + height), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
    }

    /// <summary>Effort-bucket row: radio pills, or one full-width field with a menu when they do not fit.</summary>
    private float DrawBucketBar(Vector2 winPos, float winWidth, float gutter, float top, CatalogSnapshot snap)
    {
        var draw = ImGui.GetWindowDrawList();
        var inner = winWidth - (2 * gutter);
        const float gap = 3 * Theme.Unit;
        const float padY = 3 * Theme.Unit;
        const float pillHeight = 8 * Theme.Unit;
        var tabs = Enum.GetValues<CatalogTab>();
        var labels = tabs.Select(tab => BucketLabel(tab, snap)).ToArray();
        var sizes = labels.Select(label => new Vector2(CrafterChrome.FeatureWidth(label), pillHeight)).ToArray();
        var total = sizes.Sum(size => size.X) + ((tabs.Length - 1) * gap);
        float barHeight;
        if (total > inner)
        {
            barHeight = padY + CrafterChrome.ControlHeight + padY;
            DrawBar(draw, winPos, winWidth, top, barHeight);
            var fieldPos = winPos + new Vector2(gutter, top + padY);
            var fieldSize = new Vector2(inner, CrafterChrome.ControlHeight);
            ImGui.SetCursorScreenPos(fieldPos);
            var open = ImGui.InvisibleButton("##crafter_bucket_menu", fieldSize);
            var hovered = ImGui.IsItemHovered();
            draw.AddRectFilled(fieldPos, fieldPos + fieldSize, Theme.Pack(Theme.Accent with { W = 0.14f }), CrafterChrome.Radius);
            draw.AddRect(fieldPos, fieldPos + fieldSize, Theme.Pack(hovered ? Theme.Accent : Theme.Accent with { W = 0.6f }), CrafterChrome.Radius);
            var current = Array.IndexOf(tabs, _tab);
            CrafterChrome.DrawFitted(draw, fieldPos + new Vector2(CrafterChrome.Gap, (fieldSize.Y - CrafterChrome.LineHeight()) / 2f),
                "Bucket · " + labels[Math.Max(0, current)], inner - (4 * CrafterChrome.Gap), Theme.Accent);
            CrafterChrome.Chevron(draw, fieldPos + new Vector2(inner - CrafterChrome.Gap - 4, fieldSize.Y / 2f), Theme.Accent);
            if (open)
                ImGui.OpenPopup("##crafterBucketMenu");
            ImGui.SetNextWindowPos(fieldPos + new Vector2(0, fieldSize.Y + Theme.Unit));
            ImGui.SetNextWindowSize(new Vector2(inner, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2 * Theme.Unit, 2 * Theme.Unit));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2 * Theme.Unit, Theme.Unit));
            if (ImGui.BeginPopup("##crafterBucketMenu"))
            {
                for (var i = 0; i < tabs.Length; i++)
                    if (ImGui.Selectable(labels[i] + "##bucket_" + tabs[i], _tab == tabs[i], ImGuiSelectableFlags.None, new Vector2(0, CrafterChrome.ControlHeight - 8)))
                        OnTabChanged(tabs[i]);
                ImGui.EndPopup();
            }
            ImGui.PopStyleVar(2);
        }
        else
        {
            barHeight = padY + pillHeight + padY;
            DrawBar(draw, winPos, winWidth, top, barHeight);
            var x = gutter;
            for (var i = 0; i < tabs.Length; i++)
            {
                if (CrafterChrome.FeatureButton(draw, tabs[i].ToString(), labels[i], winPos + new Vector2(x, top + padY), sizes[i], _tab == tabs[i]))
                    OnTabChanged(tabs[i]);
                x += sizes[i].X + gap;
            }
        }
        return top + barHeight + 1;
    }

    private float DrawPageHeader(Vector2 winPos, float winWidth, float gutter, float top, Destination destination, string count, string subtext)
    {
        var draw = ImGui.GetWindowDrawList();
        const float tile = 44f;
        const float padTop = 20f;
        var wide = winWidth >= 760;
        // A short window keeps the body: the header shrinks to the title and its meta on one line.
        if (ImGui.GetWindowHeight() < 640)
            return DrawCompactPageHeader(draw, winPos, winWidth, gutter, top, destination, count, subtext);
        var textX = gutter + tile + (4 * Theme.Unit);
        var metaWidth = wide
            ? MathF.Min(winWidth * 0.40f, MathF.Max(CrafterChrome.TextWidth(count, Theme.SecondaryScale), CrafterChrome.TextWidth(subtext, Theme.SecondaryScale)))
            : 0;
        var textWidth = winWidth - textX - gutter - (wide ? metaWidth + CrafterChrome.Gap : 0);
        var origin = winPos + new Vector2(gutter, top + padTop);
        draw.AddRectFilled(origin, origin + new Vector2(tile), Theme.Pack(Theme.Accent with { W = 0.14f }), CrafterChrome.Radius);
        CrafterChrome.Diamond(draw, origin + new Vector2(tile / 2f), 10, Theme.Pack(Theme.Accent));

        var title = destination.Title;
        var description = destination.Description;
        var shownTitle = CrafterChrome.ClampLines(title, textWidth, CrafterChrome.PageTitleScale, wide ? 1 : 2);
        var titleHeight = CrafterChrome.WrappedHeight(shownTitle, textWidth, CrafterChrome.PageTitleScale);
        var textOrigin = new Vector2(winPos.X + textX, origin.Y);
        CrafterChrome.DrawWrapped(draw, textOrigin, shownTitle, textWidth, Theme.TextPrimary, CrafterChrome.PageTitleScale);
        var shownDescription = CrafterChrome.ClampLines(description, textWidth, Theme.SecondaryScale, wide ? 1 : 3);
        var descriptionTop = textOrigin.Y + titleHeight + Theme.Unit;
        var descriptionHeight = CrafterChrome.DrawWrapped(draw, new Vector2(textOrigin.X, descriptionTop), shownDescription, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        var blockBottom = descriptionTop + descriptionHeight;
        if ((shownTitle != title || shownDescription != description)
            && ImGui.IsMouseHoveringRect(textOrigin, new Vector2(textOrigin.X + textWidth, blockBottom)))
            ImGui.SetTooltip(shownTitle != title ? title : description);

        if (wide)
        {
            var right = winPos.X + winWidth - gutter;
            var line1 = textOrigin.Y + ((titleHeight - CrafterChrome.LineHeight(Theme.SecondaryScale)) / 2f);
            CrafterChrome.DrawFittedRight(draw, right, line1, count, metaWidth, Theme.TextSecondary, Theme.SecondaryScale);
            CrafterChrome.DrawFittedRight(draw, right, descriptionTop, subtext, metaWidth, Theme.TextSecondary, Theme.SecondaryScale);
        }
        else
        {
            var y = blockBottom + Theme.Unit;
            foreach (var meta in new[] { count, subtext })
            {
                if (meta.Length == 0)
                    continue;
                CrafterChrome.DrawFitted(draw, new Vector2(textOrigin.X, y), meta, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
                y += CrafterChrome.LineHeight(Theme.SecondaryScale) + (Theme.Unit / 2f);
            }
            blockBottom = y;
        }
        var height = MathF.Max(tile, blockBottom - origin.Y);
        return top + padTop + height + (2 * Theme.Unit);
    }

    private static float DrawCompactPageHeader(ImDrawListPtr draw, Vector2 winPos, float winWidth, float gutter, float top, Destination destination, string count, string subtext)
    {
        const float padTop = 14f;
        var meta = subtext.Length > 0 ? count + " · " + subtext : count;
        var titleHeight = CrafterChrome.LineHeight(CrafterChrome.PageTitleScale);
        var inner = winWidth - (2 * gutter);
        var metaWidth = MathF.Min(inner * 0.5f, CrafterChrome.TextWidth(meta, Theme.SecondaryScale));
        var titleWidth = inner - metaWidth - CrafterChrome.Gap;
        var origin = winPos + new Vector2(gutter, top + padTop);
        var title = CrafterChrome.Fit(destination.Title, titleWidth, CrafterChrome.PageTitleScale);
        CrafterChrome.DrawText(draw, origin, title, Theme.TextPrimary, CrafterChrome.PageTitleScale);
        if (title != destination.Title && ImGui.IsMouseHoveringRect(origin, origin + new Vector2(titleWidth, titleHeight)))
            ImGui.SetTooltip(destination.Title);
        CrafterChrome.DrawFittedRight(draw, winPos.X + winWidth - gutter, origin.Y + ((titleHeight - CrafterChrome.LineHeight(Theme.SecondaryScale)) / 2f), meta, metaWidth, Theme.TextSecondary, Theme.SecondaryScale);
        return top + padTop + titleHeight + (2 * Theme.Unit);
    }

    private void DrawStatusAnnouncements(CatalogSnapshot snap)
    {
        if (_plugin.GameData is null)
        {
            if (_plugin.GameDataLoad.IsCompleted)
                CrafterChrome.Notice("Game data could not load", "See /xllog for the loader error.", UiState.Failed);
            else
                CrafterChrome.Notice("Loading game data", "Catalog rows appear when indexing finishes.", UiState.Running);
        }
        if (!snap.LoggedIn)
            CrafterChrome.Notice("No character yet", "Showing every recipe. Log in to include job levels, inventory and market prices.", UiState.Idle);
        if (snap.InventoryDegraded)
            CrafterChrome.Notice(Copy.Missing("AllaganTools"), "Counting the current character's bags only. Enable it to include retainers, saddlebags, armoury chests and other characters.", UiState.Missing);
        if (_plugin.Catalog.LastError is { } error)
            CrafterChrome.Notice("Catalog error", error, UiState.Failed);
    }

    private const float TrailingGap = CrafterChrome.Gap - 8f;

    private bool _railScrolls;

    private void DrawCatalogSection(CatalogService svc, CatalogSnapshot snap)
    {
        DrawFilterBar(snap);
        var view = svc.View;
        var width = CrafterChrome.Avail();
        void ApplySort((SortKey Sort, bool Descending) result)
        {
            if (result.Sort == _sort && result.Descending == _descending)
                return;
            _sort = result.Sort;
            _descending = result.Descending;
        }

        void Details(float minHeight)
        {
            if (_selectedRecipe != 0 && snap.ByRecipe.TryGetValue(_selectedRecipe, out var row))
            {
                svc.Pin(_selectedRecipe);
                _tree.Draw(row, snap, view, _hqOnly);
            }
            else
            {
                CrafterChrome.Card("recipe_empty", () =>
                {
                    CrafterChrome.Section("Recipe details");
                    CrafterChrome.EmptyState("Select a recipe", "Choose a row to inspect its ingredients, costs and fulfil actions.");
                }, minHeight: minHeight > 0 ? minHeight - TrailingGap : 0);
            }
        }

        if (width < 860)
        {
            ApplySort(_table.Draw(view, snap, _tab, _hqOnly, ref _selectedRecipe, _sort, _descending, 0));
            Details(0);
            return;
        }

        // Wide: the table card fills the page height and scrolls on its own; the detail rail scrolls as one column.
        var origin = ImGui.GetCursorScreenPos();
        var height = MathF.Max(260, ImGui.GetContentRegionAvail().Y);
        var railWidth = Math.Clamp(width * 0.28f, 300, 360);
        var mainWidth = width - CrafterChrome.Gap - railWidth;
        CrafterChrome.PushRight(origin.X + mainWidth);
        ApplySort(_table.Draw(view, snap, _tab, _hqOnly, ref _selectedRecipe, _sort, _descending, height - TrailingGap));
        CrafterChrome.PopRight();

        var railOrigin = origin + new Vector2(mainWidth + CrafterChrome.Gap, 0);
        ImGui.SetCursorScreenPos(railOrigin);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
        var railHeight = height - TrailingGap;
        var populated = _selectedRecipe != 0 && snap.ByRecipe.ContainsKey(_selectedRecipe);
        // The empty card never scrolls; a populated rail scrolls as one column and ends level with the table card.
        // The child reaches into the page margin by the scrollbar width, so a scrollbar sits beside the card, not over it.
        var lane = MathF.Min(2 * Theme.Unit + 2, MathF.Max(0, CrafterChrome.Gutter(ImGui.GetWindowWidth()) - 2));
        ImGui.BeginChild("##crafter_rail", new Vector2(railWidth + lane, railHeight), false,
            populated ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.WindowPadding);
        // The scrollbar lane trims the child's clip; the card border at the card's edge is drawn inside the full child.
        ImGui.PushClipRect(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), false);
        CrafterChrome.PushRight(railOrigin.X + railWidth);
        Details(height);
        CrafterChrome.PopRight();
        ImGui.PopClipRect();
        _railScrolls = populated && ImGui.GetScrollMaxY() > 0;
        if (_railScrolls && ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1)
        {
            var railPos = ImGui.GetWindowPos();
            CrafterChrome.FadeBottom(ImGui.GetWindowDrawList(), railPos, railPos + new Vector2(railWidth, railHeight), ImGui.GetColorU32(ImGuiCol.WindowBg));
        }
        ImGui.PopStyleVar();
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
        ImGui.Dummy(new Vector2(width, 0));
    }

    private void DrawFilterBar(CatalogSnapshot snap)
    {
        var svc = _plugin.Catalog;
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = CrafterChrome.Avail();
        const float gap = 3 * Theme.Unit;
        const float rowGap = 2 * Theme.Unit;
        var h = CrafterChrome.ControlHeight;
        float x = 0, y = 0;

        void Place(float itemWidth)
        {
            if (x > 0 && x + itemWidth > width + 0.5f)
            {
                x = 0;
                y += h + rowGap;
            }
            ImGui.SetCursorScreenPos(origin + new Vector2(x, y));
            x += itemWidth + gap;
        }

        var searchWidth = MathF.Min(240, width);
        Place(searchWidth);
        ImGui.SetNextItemWidth(searchWidth);
        ImGui.InputTextWithHint("##crafter_recipe_search", "Search recipe names...", ref _search, 64);

        var jobs = PlayerState.CrafterJobs;
        var isLeveling = _tab == CatalogTab.Leveling;
        var current = isLeveling ? _levelingJob : _jobFilter;
        if (isLeveling && current == 0)
        {
            current = snap.Jobs.Where(kv => jobs.Contains(kv.Key)).OrderBy(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault(jobs[0]);
            _levelingJob = current;
        }
        const float jobWidth = 150;
        Place(jobWidth);
        var preview = current == 0 ? "All jobs" : JobLabel(current, snap);
        var comboOpen = CrafterChrome.BeginCombo("##crafter_job", preview, jobWidth);
        if (comboOpen)
        {
            if (!isLeveling && ImGui.Selectable("All jobs", current == 0))
                _jobFilter = 0;
            foreach (var job in jobs)
            {
                if (!ImGui.Selectable(JobLabel(job, snap), current == job))
                    continue;
                if (isLeveling)
                    _levelingJob = job;
                else
                    _jobFilter = job;
            }
        }
        CrafterChrome.EndCombo(comboOpen);

        Place(CrafterChrome.CheckWidth("HQ"));
        CrafterChrome.Check("hq", "HQ", ref _hqOnly);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Price and rank by the HQ row; hide recipes whose result cannot be HQ.");
        Place(CrafterChrome.CheckWidth("Hide untradeable"));
        CrafterChrome.Check("untradeable", "Hide untradeable", ref _hideUntradeable);

        const float velocityField = 88;
        const string velocityLabel = "min /day";
        Place(velocityField + 8 + CrafterChrome.TextWidth(velocityLabel));
        var fieldPos = ImGui.GetCursorScreenPos();
        ImGui.SetNextItemWidth(velocityField);
        ImGui.InputFloat("##crafter_velocity", ref _minVelocity, 0, 0, "%.1f");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hide results that sell fewer than this many units per day at the current scope.");
        if (_minVelocity < 0)
            _minVelocity = 0;
        CrafterChrome.DrawText(draw, fieldPos + new Vector2(velocityField + 8, (h - CrafterChrome.LineHeight()) / 2f), velocityLabel, Theme.TextSecondary);

        Place(CrafterChrome.CheckWidth("Above level"));
        var showAbove = _plugin.Config.ShowAboveLevel;
        if (CrafterChrome.Check("above", "Above level", ref showAbove))
        {
            _plugin.Config.ShowAboveLevel = showAbove;
            _plugin.SaveConfig();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Also show recipes above the current job level and for jobs not unlocked.");

        Place(CrafterChrome.ButtonWidth("Refresh"));
        if (CrafterChrome.Button("refresh", "Refresh"))
        {
            _plugin.Inventory.Invalidate();
            svc.RefreshPrices();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Recount inventory and re-fetch stale prices for the visible catalog.");

        // The status line takes what is left of the row, or a row of its own when it does not fit whole.
        var status = svc.Status;
        var lastRowHeight = h;
        if (status.Length > 0)
        {
            var statusScale = Theme.SecondaryScale;
            const float dot = 14f;
            var statusWidth = dot + CrafterChrome.TextWidth(status, statusScale);
            var statusHeight = h;
            if (x > 0 && x + MathF.Min(statusWidth, 240) > width)
            {
                x = 0;
                y += h + 4;
                statusHeight = CrafterChrome.LineHeight(statusScale) + (2 * Theme.Unit);
                y += 0;
            }
            lastRowHeight = statusHeight;
            var room = width - x;
            var pos = origin + new Vector2(x, y);
            var markColor = Copy.MarkColor(svc.Busy ? UiState.Running : UiState.Idle);
            draw.AddCircleFilled(pos + new Vector2(Theme.DotRadius, statusHeight / 2f), Theme.DotRadius, Theme.Pack(markColor));
            var textPos = pos + new Vector2(dot, (statusHeight - CrafterChrome.LineHeight(statusScale)) / 2f);
            var shown = CrafterChrome.Fit(status, room - dot, statusScale);
            CrafterChrome.DrawText(draw, textPos, shown, Theme.TextSecondary, statusScale);
            if (ImGui.IsMouseHoveringRect(pos, pos + new Vector2(room, statusHeight)))
            {
                if (snap.ComputedAt != DateTime.MinValue)
                    ImGui.SetTooltip($"{status}\nComputed {snap.ComputedAt:HH:mm:ss}; scope {_plugin.Prices.Scope}; {_plugin.Prices.CacheSize} quotes cached; {_plugin.Prices.RequestsMade} Universalis requests ({_plugin.Prices.Failures} failed).");
                else if (shown != status)
                    ImGui.SetTooltip(status);
            }
        }
        CrafterChrome.Advance(origin, width, y + lastRowHeight, 3 * Theme.Unit);
    }

    private static float FooterHeight(float width)
    {
        var textBlock = CrafterChrome.LineHeight() + Theme.Unit + CrafterChrome.LineHeight(Theme.SecondaryScale);
        const float pad = 3 * Theme.Unit;
        return width < 620
            ? pad + textBlock + pad + CrafterChrome.ControlHeight + pad
            : pad + MathF.Max(textBlock, CrafterChrome.ControlHeight) + pad;
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void DrawFooter(Vector2 winPos, Vector2 winSize, float gutter, float height, CatalogSnapshot snap, RunSnapshot run, UiState state, string action, string? reason)
    {
        var top = winPos.Y + winSize.Y - height;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(new Vector2(winPos.X, top), winPos + winSize, false);
        draw.AddRectFilled(new Vector2(winPos.X, top), winPos + winSize, Theme.Pack(Theme.ShellBar));
        draw.AddLine(new Vector2(winPos.X, top), new Vector2(winPos.X + winSize.X, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        var running = _plugin.Dispatch.Running;
        var actionLabel = running
            ? "Stop"
            : _section == Section.Run && run.CanResume
                ? "Resume"
                : _section == Section.Cart
                    ? snap.Cart.Count > 0 ? "Dispatch cart" : "Browse catalog"
                    : "Open cart";
        var narrow = winSize.X < 620;
        var inner = winSize.X - (2 * gutter);
        var actionSize = new Vector2(narrow ? inner : CrafterChrome.ActionWidth(running ? 72 : 96, actionLabel), CrafterChrome.ControlHeight);
        var textBlock = CrafterChrome.LineHeight() + Theme.Unit + CrafterChrome.LineHeight(Theme.SecondaryScale);
        const float pad = 3 * Theme.Unit;
        var textLeft = winPos.X + gutter + (4 * Theme.Unit);
        Vector2 actionPos;
        float textTop;
        float textRight;
        if (narrow)
        {
            textTop = top + pad;
            actionPos = new Vector2(winPos.X + gutter, textTop + textBlock + pad);
            textRight = winPos.X + winSize.X - gutter;
        }
        else
        {
            textTop = top + ((height - textBlock) / 2f);
            actionPos = new Vector2(winPos.X + winSize.X - gutter - actionSize.X, top + ((height - actionSize.Y) / 2f));
            textRight = actionPos.X - (4 * Theme.Unit);
        }

        var primary = Capitalize(action);
        var secondary = reason ?? FooterSecondary(snap, run);
        draw.AddCircleFilled(new Vector2(winPos.X + gutter + Theme.Unit, textTop + (CrafterChrome.LineHeight() / 2f)), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        var textWidth = textRight - textLeft;
        var shown = CrafterChrome.Fit(primary, textWidth);
        CrafterChrome.DrawText(draw, new Vector2(textLeft, textTop), shown, Theme.TextPrimary);
        var shownSecondary = CrafterChrome.Fit(secondary, textWidth, Theme.SecondaryScale);
        CrafterChrome.DrawText(draw, new Vector2(textLeft, textTop + CrafterChrome.LineHeight() + Theme.Unit), shownSecondary, Theme.TextSecondary, Theme.SecondaryScale);
        if ((shown != primary || shownSecondary != secondary)
            && ImGui.IsMouseHoveringRect(new Vector2(textLeft, textTop), new Vector2(textRight, textTop + textBlock)))
            ImGui.SetTooltip(primary + "\n" + secondary);

        if (running)
        {
            if (CrafterChrome.StopButton(draw, actionPos, actionSize))
                _plugin.Dispatch.Stop();
        }
        else if (CrafterChrome.OutlineButton(draw, "footer_action", actionLabel, actionPos, actionSize))
        {
            if (_section == Section.Run && run.CanResume)
                _plugin.Dispatch.Resume();
            else if (_section == Section.Cart && snap.Cart.Count > 0)
                _plugin.Dispatch.DispatchCart();
            else if (_section == Section.Cart)
                OpenCatalog();
            else
                OpenCart();
        }
    }

    private string FooterSecondary(CatalogSnapshot snap, RunSnapshot run)
    {
        if (_section == Section.Run)
            return run.State == RunState.Idle ? "No run has started" : run.PhaseLabel;
        return _plugin.Prices.Scope.Length == 0 ? "No market scope yet" : "Market scope " + _plugin.Prices.Scope;
    }

    private string JobLabel(uint jobId, CatalogSnapshot snap)
    {
        var abbreviation = _plugin.GameData?.JobAbbr(jobId) ?? jobId.ToString();
        return snap.Jobs.TryGetValue(jobId, out var level) ? $"{abbreviation} {level}" : abbreviation;
    }

    private static string BucketLabel(CatalogTab tab, CatalogSnapshot snap) => tab switch
    {
        CatalogTab.Now => $"Now ({snap.Count(EffortTier.Now)})",
        CatalogTab.Easy => $"Easy ({snap.Count(EffortTier.Easy)})",
        CatalogTab.SomeEffort => $"Some ({snap.Count(EffortTier.SomeEffort)})",
        CatalogTab.RealEffort => $"Real ({snap.RealEffortCount})",
        CatalogTab.Leveling => "Leveling",
        CatalogTab.LogCompletion => $"Log ({snap.NotYetCrafted})",
        CatalogTab.Undersupplied => "Undersupplied",
        _ => tab.ToString(),
    };
}
