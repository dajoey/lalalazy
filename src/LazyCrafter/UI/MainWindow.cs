using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using LalaUiKit;
using LazyCrafter.Adapters;
using LazyCrafter.Catalog;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.UI;

/// <summary>
/// The LazyCrafter window (Plan §Phase 4, Scope §4). Draws only from immutable snapshots the
/// <see cref="CatalogService"/> publishes; every filter/sort change is a <see cref="ViewRequest"/> poke and the
/// heavy lifting happens on its worker.
/// Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13). The Dalamud Window base
/// auto-begins/ends this window (Draw() draws content only), so the top bar is drawn with the same kit
/// primitives Layout.Shell uses and the suite frame comes from Theme.PushFrame/PopFrame. Navigation is one
/// section row (Run, Catalog, Settings — R1 layer 2, no group tabs) plus the catalog's effort buckets as the
/// one selector row on the Catalog page. Colours come only from the kit (R4); live state uses the R5
/// vocabulary; the footer is R3 place 3 with Stop gated to a running dispatch.
/// </summary>
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

    // R1 layer 2: three functional areas, one selected at a time (the accent outline marks exactly the
    // selected section). Three sections, so no group tabs (R1).
    private enum Section
    {
        Run,
        Catalog,
        Settings,
    }

    private Section _section = Section.Catalog;

    // View state (UI thread only).
    private CatalogTab _tab = CatalogTab.Now;
    private uint _jobFilter;
    private bool _hqOnly;
    private float _minVelocity;
    private bool _hideUntradeable;
    private string _search = "";
    private SortKey _sort = SortKey.PerDay;
    private bool _descending = true;
    private CatalogTab _sortTab = CatalogTab.Now;
    private uint _levelingJob;
    private uint _selectedRecipe;
    private float _treeWidth = 340f;

    public MainWindow(Plugin plugin) : base("LazyCrafter##lcraft")
    {
        _plugin = plugin;
        _table = new CatalogTable(plugin);
        _tree = new IngredientTree(plugin);
        _cart = new CartPanel(plugin, this);
        _settings = new SettingsTab(plugin);
        _run = new RunTab(plugin);
        Size = new Vector2(1180, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(760, 420), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
    }

    public uint SelectedRecipe => _selectedRecipe;

    public override void Draw()
    {
        var svc = _plugin.Catalog;
        var snap = svc.Snapshot;

        Theme.PushFrame();
        try
        {
            var (state, action, reason) = OverallState(snap);
            DrawTopBar(state, $"{snap.Cart.Count} in cart");
            DrawStatusAnnouncements(snap);

            // Run auto-select (old tab behaviour kept): when a dispatch switches on (Idle -> Running/Blocked)
            // it grabs the Run section ONCE; OpenRunTab() re-arms it from the cart panel.
            var snapRun = _plugin.Dispatch.Snapshot;
            var runActive = snapRun.State is RunState.Running or RunState.Blocked;
            if (runActive && !_wasRunActive) _selectRunNext = true;
            _wasRunActive = runActive;
            if (_selectRunNext)
            {
                _section = Section.Run;
                _selectRunNext = false;
            }

            DrawSectionNav(snap, snapRun);

            switch (_section)
            {
                case Section.Run:
                    DrawRunSection(snapRun);
                    break;
                case Section.Catalog:
                    DrawCatalogSection(svc, snap);
                    break;
                case Section.Settings:
                    DrawSettingsSection();
                    break;
            }

            // Footer (R3 place 3): live dot, state word, current action, Stop pinned right. Stop is the
            // existing dispatch stop; gated to a running dispatch so an idle click stays a no-op
            // (Dispatch.Stop() prints a chat line when nothing is running).
            Layout.Footer(state, action, reason, "Stop", out var stop);
            if (stop && _plugin.Dispatch.Running)
                _plugin.Dispatch.Stop();
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    /// <summary>Cart panel's "Run tab" button: select the Run section on the next draw.</summary>
    internal void OpenRunTab() => _selectRunNext = true;

    private void PushRequest(CatalogService svc)
    {
        svc.Request(new ViewRequest(_tab, _jobFilter, _hqOnly, _minVelocity, _hideUntradeable, _search, _sort, _descending,
            _levelingJob, _plugin.Config.UndersuppliedMinVelocity, _plugin.Config.UndersuppliedMaxListings, _plugin.Config.ShowAboveLevel));
    }

    private void OnTabChanged(CatalogTab t)
    {
        _tab = t;
        if (_sortTab != t)
        {
            _sort = ViewRequest.DefaultSort(t);
            _descending = ViewRequest.DefaultDescending(t);
            _sortTab = t;
        }
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
            return (UiState.Blocked, "run needs you", string.IsNullOrEmpty(run.Status) ? null : run.Status);
        if (run.State == RunState.Done)
            return (UiState.Ready, "run finished", null);
        if (snap.InventoryDegraded)
            return (UiState.Missing, "AllaganTools", "counting the current character's bags only");
        if (!snap.LoggedIn)
            return (UiState.Empty, "not logged in", "showing every recipe");
        if (_plugin.Catalog.Busy)
            return (UiState.Running, "computing the catalog", null);
        return (UiState.Ready, "catalog current", null);
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
        ImGui.TextUnformatted("LazyCrafter");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(state, "LazyCrafter", countDetail, alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);
    }

    private void DrawStatusAnnouncements(CatalogSnapshot snap)
    {
        // Finished, failed and degraded data are announced once, here (R3) — the conditions the old
        // coloured banner lines covered, in the kit's banner/info-box shapes.
        var svc = _plugin.Catalog;
        if (_plugin.GameData is null)
        {
            if (_plugin.GameDataLoad.IsCompleted)
                Widgets.InfoBox("Game data failed to load — see /xllog.", UiState.Failed);
            else
                Widgets.Banner(UiState.Running, "Loading game data…");
        }
        if (!snap.LoggedIn)
            Widgets.Secondary("Not logged in — showing every recipe; job levels, inventory and prices need a character.");
        if (snap.InventoryDegraded)
            Widgets.InfoBox(
                "AllaganTools is not available — counting the current character's bags only. "
                + "Install/enable AllaganTools to count retainers, saddlebag, armoury chest and alts.",
                UiState.Missing);
        if (svc.LastError is { } err)
            Widgets.InfoBox($"Catalog error: {err}", UiState.Failed);
    }

    private void DrawSectionNav(CatalogSnapshot snap, RunSnapshot run)
    {
        var runDot = run.State switch
        {
            RunState.Running => UiState.Running,
            RunState.Blocked => UiState.Blocked,
            RunState.Failed => UiState.Failed,
            RunState.Done => UiState.Ready,
            _ => UiState.Idle,
        };
        var catalogDot = _plugin.GameData is null
            ? (_plugin.GameDataLoad.IsCompleted ? UiState.Failed : UiState.Running)
            : _plugin.Catalog.Busy ? UiState.Running
            : UiState.Ready;

        if (Layout.NavSection("lc_nav_run", "Run", null, FontAwesomeIcon.Play.ToIconString(), runDot, _section == Section.Run))
            _section = Section.Run;
        ImGui.SameLine();
        if (Layout.NavSection("lc_nav_catalog", "Catalog", null, FontAwesomeIcon.BoxOpen.ToIconString(), catalogDot, _section == Section.Catalog))
            _section = Section.Catalog;
        ImGui.SameLine();
        if (Layout.NavSection("lc_nav_settings", "Settings", null, FontAwesomeIcon.Cog.ToIconString(), UiState.Ready, _section == Section.Settings))
            _section = Section.Settings;

        ImGui.Dummy(new Vector2(0, Theme.Unit));
    }

    private void DrawRunSection(RunSnapshot snapRun)
    {
        Layout.PageHeader(null, FontAwesomeIcon.Play.ToIconString(), "Run",
            "Watch the current dispatch, stop or resume it, and copy the full report.",
            snapRun.State == RunState.Idle
                ? "no run"
                : $"{snapRun.PhaseLabel} — {RunReport.Elapsed(RunTab.ElapsedFor(snapRun))} elapsed");
        _run.Draw();
    }

    private void DrawCatalogSection(CatalogService svc, CatalogSnapshot snap)
    {
        Layout.PageHeader(null, FontAwesomeIcon.BoxOpen.ToIconString(), "Catalog",
            "Every recipe you can craft, bucketed by the effort its missing materials take, priced at your market scope.",
            $"{svc.View.Rows.Count} recipes shown");

        // Effort buckets: the one selector row on the Catalog page (R1); the accent outline marks exactly
        // the selected bucket. Counts are the live numbers the old tab labels carried.
        foreach (var t in Enum.GetValues<CatalogTab>())
        {
            if (Layout.NavSection($"lc_bucket_{t}", BucketLabel(t, snap), null, BucketGlyph(t), BucketDot(t, snap), _tab == t))
                OnTabChanged(t);
            ImGui.SameLine();
        }
        ImGui.Dummy(new Vector2(0, 0));
        ImGui.NewLine();

        DrawFilterBar(snap);
        PushRequest(svc);

        var view = svc.View;
        var avail = ImGui.GetContentRegionAvail();
        var cartHeight = _cart.DesiredHeight(snap);
        var bodyHeight = Math.Max(120f, avail.Y - cartHeight - ImGui.GetStyle().ItemSpacing.Y);
        var treeWidth = Math.Clamp(_treeWidth, 220f, Math.Max(220f, avail.X * 0.5f));

        if (ImGui.BeginChild("##lcraft-table", new Vector2(avail.X - treeWidth - ImGui.GetStyle().ItemSpacing.X, bodyHeight), true))
        {
            var (sort, desc) = _table.Draw(view, snap, _tab, _hqOnly, ref _selectedRecipe, _sort, _descending);
            if (sort != _sort || desc != _descending) { _sort = sort; _descending = desc; }
        }
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##lcraft-tree", new Vector2(treeWidth, bodyHeight), true))
        {
            if (_selectedRecipe != 0 && snap.ByRecipe.TryGetValue(_selectedRecipe, out var row))
            {
                svc.Pin(_selectedRecipe);
                _tree.Draw(row, snap, view, _hqOnly);
            }
            else Widgets.Secondary("Select a recipe in the table to see its ingredients and fulfil buttons.");
        }
        ImGui.EndChild();

        _cart.Draw(snap);
    }

    private void DrawSettingsSection()
    {
        var sources = Enum.GetValues<InventorySource>();
        var on = sources.Count(s => _plugin.Config.IsSourceEnabled(s));
        Layout.PageHeader(null, FontAwesomeIcon.Cog.ToIconString(), "Settings",
            "Inventory sources, price basis, catalog thresholds and the dispatch hand-offs.",
            $"{on} of {sources.Length} inventory sources on");
        _settings.Draw();
    }

    private static string BucketLabel(CatalogTab t, CatalogSnapshot snap) => t switch
    {
        CatalogTab.Now => $"Now ({snap.Count(EffortTier.Now)})",
        CatalogTab.Easy => $"Easy ({snap.Count(EffortTier.Easy)})",
        CatalogTab.SomeEffort => $"Some effort ({snap.Count(EffortTier.SomeEffort)})",
        CatalogTab.RealEffort => $"Real effort ({snap.RealEffortCount})",
        CatalogTab.Leveling => "Leveling",
        CatalogTab.LogCompletion => $"Log completion ({snap.NotYetCrafted})",
        CatalogTab.Undersupplied => "Undersupplied",
        _ => t.ToString(),
    };

    private static string BucketGlyph(CatalogTab t) => t switch
    {
        CatalogTab.Now => FontAwesomeIcon.Clock.ToIconString(),
        CatalogTab.Easy => FontAwesomeIcon.Feather.ToIconString(),
        CatalogTab.SomeEffort => FontAwesomeIcon.Bolt.ToIconString(),
        CatalogTab.RealEffort => FontAwesomeIcon.Hammer.ToIconString(),
        CatalogTab.Leveling => FontAwesomeIcon.ChartBar.ToIconString(),
        CatalogTab.LogCompletion => FontAwesomeIcon.Book.ToIconString(),
        CatalogTab.Undersupplied => FontAwesomeIcon.Search.ToIconString(),
        _ => "",
    };

    private static UiState BucketDot(CatalogTab t, CatalogSnapshot snap) => t switch
    {
        CatalogTab.Now => snap.Count(EffortTier.Now) > 0 ? UiState.Ready : UiState.Empty,
        CatalogTab.Easy => snap.Count(EffortTier.Easy) > 0 ? UiState.Ready : UiState.Empty,
        CatalogTab.SomeEffort => snap.Count(EffortTier.SomeEffort) > 0 ? UiState.Ready : UiState.Empty,
        CatalogTab.RealEffort => snap.RealEffortCount > 0 ? UiState.Ready : UiState.Empty,
        CatalogTab.LogCompletion => snap.NotYetCrafted > 0 ? UiState.Ready : UiState.Empty,
        _ => UiState.Idle,
    };

    private void DrawFilterBar(CatalogSnapshot snap)
    {
        var svc = _plugin.Catalog;
        ImGui.SetNextItemWidth(220f);
        ImGui.InputTextWithHint("##search", "Search item name...", ref _search, 64);
        ImGui.SameLine();

        // Job filter: only the crafter jobs, "All" first. On the Leveling tab this is the job being leveled.
        var jobs = Adapters.PlayerState.CrafterJobs;
        var isLeveling = _tab == CatalogTab.Leveling;
        var current = isLeveling ? _levelingJob : _jobFilter;
        if (isLeveling && current == 0)
        {
            // Default to the first unlocked crafter (lowest level first - that is what you level).
            current = snap.Jobs.Where(kv => jobs.Contains(kv.Key)).OrderBy(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault(jobs[0]);
            _levelingJob = current;
        }
        ImGui.SetNextItemWidth(isLeveling ? 150f : 110f);
        var preview = current == 0 ? "All jobs" : JobLabel(current, snap);
        if (ImGui.BeginCombo("##job", preview))
        {
            if (!isLeveling && ImGui.Selectable("All jobs", current == 0)) _jobFilter = 0;
            foreach (var j in jobs)
            {
                if (ImGui.Selectable(JobLabel(j, snap), current == j))
                {
                    if (isLeveling) _levelingJob = j; else _jobFilter = j;
                }
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        Widgets.Checkbox("HQ", ref _hqOnly);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Price and rank by the HQ row; hide recipes whose result cannot be HQ.");
        ImGui.SameLine();
        Widgets.Checkbox("Hide untradeable", ref _hideUntradeable);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        ImGui.InputFloat("min /day##vel", ref _minVelocity, 0, 0, "%.1f");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hide results that sell fewer than this many units per day at your scope.");
        if (_minVelocity < 0) _minVelocity = 0;
        ImGui.SameLine();
        var showAbove = _plugin.Config.ShowAboveLevel;
        if (Widgets.Checkbox("Above level", ref showAbove)) { _plugin.Config.ShowAboveLevel = showAbove; _plugin.SaveConfig(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Also show recipes above your job level and for jobs you have not unlocked.");
        ImGui.SameLine();
        if (ImGui.Button("Refresh")) { _plugin.Inventory.Invalidate(); svc.RefreshPrices(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Recount inventory and re-fetch stale prices for what is on screen.");
        ImGui.SameLine();
        Widgets.StatusDot(svc.Busy ? UiState.Running : UiState.Idle);
        ImGui.SameLine();
        Widgets.Secondary(svc.Busy ? $"{Copy.Word(UiState.Running)} — {svc.Status}" : svc.Status);
        if (ImGui.IsItemHovered() && snap.ComputedAt != DateTime.MinValue)
            ImGui.SetTooltip($"Computed {snap.ComputedAt:HH:mm:ss}; scope {_plugin.Prices.Scope}; {_plugin.Prices.CacheSize} quotes cached; {_plugin.Prices.RequestsMade} Universalis requests ({_plugin.Prices.Failures} failed).");
    }

    private string JobLabel(uint jobId, CatalogSnapshot snap)
    {
        var abbr = _plugin.GameData?.JobAbbr(jobId) ?? jobId.ToString();
        return snap.Jobs.TryGetValue(jobId, out var lvl) ? $"{abbr} {lvl}" : abbr;
    }
}
