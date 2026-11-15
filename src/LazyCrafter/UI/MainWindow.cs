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
            var (state, action, reason) = OverallState(snap);
            DrawTopBar(state);
            DrawPageSearchResults();
            if (_section == Section.Catalog)
            {
                DrawFeatureRow(snap);
                PushRequest(svc);
            }

            var destination = CurrentDestination;
            CrafterChrome.PageHeader(destination.Title, destination.Description, LiveCount(snap, snapRun), LiveSubtext(snap, snapRun));

            var footerHeight = ImGui.GetContentRegionAvail().X < 620
                ? CrafterChrome.NarrowFooterHeight
                : CrafterChrome.WideFooterHeight;
            var bodyHeight = MathF.Max(1, ImGui.GetContentRegionAvail().Y - footerHeight);
            ImGui.BeginChild("##crafter_body", new Vector2(0, bodyHeight), false);
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
            ImGui.EndChild();
            DrawFooter(snap, snapRun, state, action, reason);
        }
        finally
        {
            CrafterChrome.PopFrame();
        }
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
            return (UiState.Missing, "inventory counts are limited", Copy.Missing("AllaganTools"));
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
            Section.Catalog => _plugin.Catalog.Busy ? Copy.Word(UiState.Running) : scope,
            Section.Cart => snap.Cart.Count == 0 ? "add from the Catalog" : $"worst tier {Fmt.TierName(snap.CartTotals.Tier)}",
            Section.Run => run.State == RunState.Idle ? "Idle" : run.PhaseLabel,
            Section.Settings => scope,
            _ => "",
        };
    }

    private void DrawTopBar(UiState state)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        const float gap = 2 * Theme.Unit;
        var brandSize = new Vector2(188, 42);
        var groups = Enum.GetValues<Section>();
        var groupSizes = groups.Select(group => new Vector2(MathF.Max(76, ImGui.CalcTextSize(group.ToString()).X + 30), CrafterChrome.ControlHeight)).ToArray();
        var stateLabel = Copy.Word(state);
        var statusSize = new Vector2(MathF.Max(80, ImGui.CalcTextSize(stateLabel).X + 43), CrafterChrome.ControlHeight);
        var leftWidth = brandSize.X + gap + groupSizes.Sum(size => size.X) + ((groupSizes.Length - 1) * gap);
        const float minSearchWidth = 180;
        var singleRow = width >= leftWidth + minSearchWidth + statusSize.X + (3 * gap);
        var groupPositions = new Vector2[groups.Length];
        var brandPos = new Vector2(0, 12);
        Vector2 searchPos;
        Vector2 statusPos;
        float searchWidth;
        float barHeight;

        if (singleRow)
        {
            barHeight = 66;
            var x = brandSize.X + gap;
            var y = (barHeight - CrafterChrome.ControlHeight) / 2f;
            for (var i = 0; i < groups.Length; i++)
            {
                groupPositions[i] = new Vector2(x, y);
                x += groupSizes[i].X + gap;
            }
            searchWidth = MathF.Min(280, width - leftWidth - statusSize.X - (3 * gap));
            statusPos = new Vector2(width - statusSize.X, y);
            searchPos = new Vector2(statusPos.X - gap - searchWidth, y);
        }
        else
        {
            statusPos = new Vector2(width - statusSize.X, 16);
            var x = 0f;
            var y = 62f;
            for (var i = 0; i < groups.Length; i++)
            {
                var buttonWidth = MathF.Min(groupSizes[i].X, width);
                if (x > 0 && x + buttonWidth > width)
                {
                    x = 0;
                    y += CrafterChrome.ControlHeight + gap;
                }
                groupPositions[i] = new Vector2(x, y);
                x += buttonWidth + gap;
            }
            var searchY = y + CrafterChrome.ControlHeight + gap;
            searchWidth = width;
            searchPos = new Vector2(0, searchY);
            barHeight = searchY + CrafterChrome.ControlHeight + 12;
        }

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        if (CrafterChrome.BrandButton(draw, origin + brandPos, brandSize))
            Navigate(Destinations[0]);
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            if (CrafterChrome.OutlineButton(draw, "group_" + group, group.ToString(), origin + groupPositions[i], groupSizes[i], _section == group))
                _section = group;
        }
        DrawSearchInput(origin + searchPos, searchWidth);
        CrafterChrome.StatusPill(draw, origin + statusPos, statusSize, stateLabel, state);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private void DrawSearchInput(Vector2 pos, float width)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.SetNextItemWidth(width);
        var padding = Theme.FramePadding;
        padding.Y = MathF.Max(padding.Y, (CrafterChrome.ControlHeight - ImGui.GetTextLineHeight()) / 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, padding);
        ImGui.InputTextWithHint("##crafter_page_search", "Search pages and settings...", ref _pageSearch, 128);
        ImGui.PopStyleVar();
    }

    private void DrawPageSearchResults()
    {
        var query = _pageSearch.Trim();
        if (query.Length == 0)
            return;
        var matches = Destinations.Where(destination =>
                (destination.Title + " " + destination.Description + " " + destination.SearchTerms + " " + destination.Section)
                .Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToArray();
        CrafterChrome.Card("page_search_results", () =>
        {
            Widgets.Caption($"{matches.Length} destinations for ‘{query}’");
            if (matches.Length == 0)
            {
                Widgets.SecondaryWrapped("No page or setting matches this search. Recipe-name search remains inside the Catalog.");
                return;
            }
            foreach (var match in matches)
                if (ImGui.Selectable($"{match.Section} · {match.Title}##destination_{match.Id}"))
                    Navigate(match);
        });
    }

    private void DrawFeatureRow(CatalogSnapshot snap)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        const float gap = 3 * Theme.Unit;
        var tabs = Enum.GetValues<CatalogTab>();
        var labels = tabs.Select(tab => BucketLabel(tab, snap)).ToArray();
        var sizes = labels.Select(label => new Vector2(MathF.Min(width, MathF.Max(116, ImGui.CalcTextSize(label).X + 50)), CrafterChrome.ControlHeight)).ToArray();
        var positions = new Vector2[tabs.Length];
        var x = 0f;
        var y = 8f;
        for (var i = 0; i < tabs.Length; i++)
        {
            if (x > 0 && x + sizes[i].X > width)
            {
                x = 0;
                y += CrafterChrome.ControlHeight + (2 * Theme.Unit);
            }
            positions[i] = new Vector2(x, y);
            x += sizes[i].X + gap;
        }
        var barHeight = y + CrafterChrome.ControlHeight + 8;
        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        for (var i = 0; i < tabs.Length; i++)
            if (CrafterChrome.FeatureButton(draw, tabs[i].ToString(), labels[i], origin + positions[i], sizes[i], _tab == tabs[i]))
                OnTabChanged(tabs[i]);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private void DrawStatusAnnouncements(CatalogSnapshot snap)
    {
        if (_plugin.GameData is null)
        {
            if (_plugin.GameDataLoad.IsCompleted)
                Widgets.InfoBox("Failed — game data could not load. See /xllog for the loader error.", UiState.Failed);
            else
                Widgets.InfoBox("Running — loading game data. Catalog rows appear when indexing finishes.", UiState.Running);
        }
        if (!snap.LoggedIn)
            Widgets.InfoBox("Idle — showing every recipe. Log in to include job levels, inventory and market prices.", UiState.Idle);
        if (snap.InventoryDegraded)
            Widgets.InfoBox(Copy.Missing("AllaganTools") + " — counting the current character's bags only. Enable it to include retainers, saddlebags, armoury chests and other characters.", UiState.Missing);
        if (_plugin.Catalog.LastError is { } error)
            Widgets.InfoBox("Catalog error: " + error, UiState.Failed);
    }

    private void DrawCatalogSection(CatalogService svc, CatalogSnapshot snap)
    {
        DrawFilterBar(snap);
        var view = svc.View;
        CrafterChrome.TwoColumns("catalog", 0.68f,
            () =>
            {
                var (sort, descending) = _table.Draw(view, snap, _tab, _hqOnly, ref _selectedRecipe, _sort, _descending);
                if (sort == _sort && descending == _descending)
                    return;
                _sort = sort;
                _descending = descending;
            },
            () =>
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
                        Widgets.InfoBox("Empty — select a recipe to inspect its ingredients, costs and fulfil actions.", UiState.Empty);
                    });
                }
            });
    }

    private void DrawFilterBar(CatalogSnapshot snap)
    {
        var svc = _plugin.Catalog;
        ImGui.SetNextItemWidth(MathF.Min(240, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##crafter_recipe_search", "Search recipe names...", ref _search, 64);

        var jobs = PlayerState.CrafterJobs;
        var isLeveling = _tab == CatalogTab.Leveling;
        var current = isLeveling ? _levelingJob : _jobFilter;
        if (isLeveling && current == 0)
        {
            current = snap.Jobs.Where(kv => jobs.Contains(kv.Key)).OrderBy(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault(jobs[0]);
            _levelingJob = current;
        }
        CrafterChrome.SameLineWrapped(isLeveling ? 150 : 124);
        ImGui.SetNextItemWidth(isLeveling ? 150 : 124);
        var preview = current == 0 ? "All jobs" : JobLabel(current, snap);
        if (ImGui.BeginCombo("##crafter_job", preview))
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
            ImGui.EndCombo();
        }

        CrafterChrome.SameLineWrapped(54);
        Widgets.Checkbox("HQ", ref _hqOnly);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Price and rank by the HQ row; hide recipes whose result cannot be HQ.");
        CrafterChrome.SameLineWrapped(126);
        Widgets.Checkbox("Hide untradeable", ref _hideUntradeable);
        CrafterChrome.SameLineWrapped(90);
        ImGui.SetNextItemWidth(90);
        ImGui.InputFloat("min /day##crafter_velocity", ref _minVelocity, 0, 0, "%.1f");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hide results that sell fewer than this many units per day at the current scope.");
        if (_minVelocity < 0)
            _minVelocity = 0;
        CrafterChrome.SameLineWrapped(100);
        var showAbove = _plugin.Config.ShowAboveLevel;
        if (Widgets.Checkbox("Above level", ref showAbove))
        {
            _plugin.Config.ShowAboveLevel = showAbove;
            _plugin.SaveConfig();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Also show recipes above the current job level and for jobs not unlocked.");
        CrafterChrome.SameLineWrapped(CrafterChrome.ButtonWidth("Refresh"));
        if (ImGui.Button("Refresh"))
        {
            _plugin.Inventory.Invalidate();
            svc.RefreshPrices();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Recount inventory and re-fetch stale prices for the visible catalog.");
        CrafterChrome.SameLineWrapped(14);
        Widgets.StatusDot(svc.Busy ? UiState.Running : UiState.Idle);
        ImGui.SameLine();
        Widgets.SecondaryWrapped(svc.Busy ? $"{Copy.Word(UiState.Running)} — {svc.Status}" : svc.Status);
        if (ImGui.IsItemHovered() && snap.ComputedAt != DateTime.MinValue)
            ImGui.SetTooltip($"Computed {snap.ComputedAt:HH:mm:ss}; scope {_plugin.Prices.Scope}; {_plugin.Prices.CacheSize} quotes cached; {_plugin.Prices.RequestsMade} Universalis requests ({_plugin.Prices.Failures} failed).");
        ImGui.Dummy(new Vector2(0, Theme.Unit));
    }

    private void DrawFooter(CatalogSnapshot snap, RunSnapshot run, UiState state, string action, string? reason)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = width < 620 ? CrafterChrome.NarrowFooterHeight : CrafterChrome.WideFooterHeight;
        var narrow = width < 620;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));

        var running = _plugin.Dispatch.Running;
        var actionLabel = running
            ? "Stop"
            : _section == Section.Run && run.CanResume
                ? "Resume"
                : _section == Section.Cart
                    ? snap.Cart.Count > 0 ? "Dispatch cart" : "Browse catalog"
                    : "Open cart";
        var actionWidth = MathF.Min(width, MathF.Max(96, ImGui.CalcTextSize(actionLabel).X + 40));
        var actionPos = origin + new Vector2(width - actionWidth, narrow ? 52 : 14);
        var textRight = narrow ? origin.X + width : actionPos.X - 16;
        var textPos = origin + new Vector2(24, 8);
        draw.AddCircleFilled(origin + new Vector2(10, 17), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        draw.AddText(textPos, Theme.Pack(Theme.TextPrimary), CrafterChrome.FitText($"{Copy.Word(state)} · {action}", textRight - textPos.X));
        draw.AddText(textPos + new Vector2(0, 25), Theme.Pack(Theme.TextSecondary),
            CrafterChrome.FitText(reason ?? LiveSubtext(snap, run), textRight - textPos.X));

        if (running)
        {
            if (CrafterChrome.StopButton(draw, actionPos, new Vector2(actionWidth, 36)))
                _plugin.Dispatch.Stop();
        }
        else if (CrafterChrome.OutlineButton(draw, "footer_action", actionLabel, actionPos, new Vector2(actionWidth, 36)))
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
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
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
