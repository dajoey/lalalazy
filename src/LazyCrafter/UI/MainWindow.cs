using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using LazyCrafter.Catalog;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.UI;

/// <summary>
/// The LazyCrafter window (Plan §Phase 4, Scope §4). Draws only from immutable snapshots the
/// <see cref="CatalogService"/> publishes; every filter/sort change is a <see cref="ViewRequest"/> poke and the
/// heavy lifting happens on its worker. Layout: bucket tabs -> filter bar -> [catalog table | ingredient tree]
/// -> cart panel; a Settings tab replaces the catalog area.
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

    // View state (UI thread only).
    private CatalogTab _tab = CatalogTab.Now;
    private bool _settingsOpen;
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

        DrawBanner(snap);
        if (!ImGui.BeginTabBar("##lcraft-tabs", ImGuiTabBarFlags.NoCloseWithMiddleMouseButton)) return;

        // Run tab (t_c360953f): always first. When a dispatch switches on (Idle -> Running/Blocked) it grabs
        // selection ONCE via ImGuiTabItemFlags.SetSelected - same trick LazyMarketCompanion's ConfigWindow uses -
        // and OpenRunTab() re-arms it from the cart panel. Opening it turns the catalog area off, like Settings.
        var snapRun = _plugin.Dispatch.Snapshot;
        var runActive = snapRun.State is RunState.Running or RunState.Blocked;
        if (runActive && !_wasRunActive) _selectRunNext = true;
        _wasRunActive = runActive;
        if (ImGui.BeginTabItem(RunTab.TabLabel(snapRun), _selectRunNext ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            _selectRunNext = false;
            _run.Draw();
            ImGui.EndTabItem();
        }
        var anyCatalogTab = false;
        foreach (var t in Enum.GetValues<CatalogTab>())
        {
            var label = TabLabel(t, snap);
            if (ImGui.BeginTabItem(label))
            {
                anyCatalogTab = true;
                if (_tab != t) OnTabChanged(t);
                ImGui.EndTabItem();
            }
        }
        if (ImGui.BeginTabItem("Settings"))
        {
            _settingsOpen = true;
            _settings.Draw();
            ImGui.EndTabItem();
        }
        else _settingsOpen = false;
        ImGui.EndTabBar();

        if (_settingsOpen || !anyCatalogTab) return;

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
            else ImGui.TextDisabled("Select a recipe to see its ingredients.");
        }
        ImGui.EndChild();

        _cart.Draw(snap);
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

    /// <summary>Cart panel's "Run tab" button: select the Run tab on the next draw.</summary>
    internal void OpenRunTab() => _selectRunNext = true;

    private void PushRequest(CatalogService svc)
    {
        svc.Request(new ViewRequest(_tab, _jobFilter, _hqOnly, _minVelocity, _hideUntradeable, _search, _sort, _descending,
            _levelingJob, _plugin.Config.UndersuppliedMinVelocity, _plugin.Config.UndersuppliedMaxListings, _plugin.Config.ShowAboveLevel));
    }

    private static string TabLabel(CatalogTab t, CatalogSnapshot snap) => t switch
    {
        CatalogTab.Now => $"Now ({snap.Count(EffortTier.Now)})###tab-now",
        CatalogTab.Easy => $"Easy ({snap.Count(EffortTier.Easy)})###tab-easy",
        CatalogTab.SomeEffort => $"Some effort ({snap.Count(EffortTier.SomeEffort)})###tab-some",
        CatalogTab.RealEffort => $"Real effort ({snap.RealEffortCount})###tab-real",
        CatalogTab.Leveling => "Leveling###tab-leveling",
        CatalogTab.LogCompletion => $"Log completion ({snap.NotYetCrafted})###tab-log",
        CatalogTab.Undersupplied => "Undersupplied###tab-under",
        _ => t.ToString(),
    };

    private void DrawBanner(CatalogSnapshot snap)
    {
        var svc = _plugin.Catalog;
        if (_plugin.GameData is null)
        {
            ImGui.TextColored(ImGuiColors.DalamudOrange, _plugin.GameDataLoad.IsCompleted ? "Game data failed to load - see /xllog." : "Loading game data...");
            return;
        }
        if (!snap.LoggedIn)
            ImGui.TextColored(ImGuiColors.DalamudGrey, "Not logged in - showing every recipe; job levels, inventory and prices need a character.");
        if (snap.InventoryDegraded)
        {
            ImGui.TextColored(ImGuiColors.DalamudOrange, "AllaganTools not available - counting the current character's bags only.");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Install/enable AllaganTools to count retainers, saddlebag, armoury chest and alts.");
        }
        if (svc.LastError is { } err) ImGui.TextColored(ImGuiColors.DalamudRed, "Catalog error: " + err);
    }

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
        ImGui.Checkbox("HQ", ref _hqOnly);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Price and rank by the HQ row; hide recipes whose result cannot be HQ.");
        ImGui.SameLine();
        ImGui.Checkbox("Hide untradeable", ref _hideUntradeable);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        ImGui.InputFloat("min /day##vel", ref _minVelocity, 0, 0, "%.1f");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hide results that sell fewer than this many units per day at your scope.");
        if (_minVelocity < 0) _minVelocity = 0;
        ImGui.SameLine();
        var showAbove = _plugin.Config.ShowAboveLevel;
        if (ImGui.Checkbox("Above level", ref showAbove)) { _plugin.Config.ShowAboveLevel = showAbove; _plugin.SaveConfig(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Also show recipes above your job level and for jobs you have not unlocked.");
        ImGui.SameLine();
        if (ImGui.Button("Refresh")) { _plugin.Inventory.Invalidate(); svc.RefreshPrices(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Recount inventory and re-fetch stale prices for what is on screen.");
        ImGui.SameLine();
        ImGui.TextDisabled(svc.Busy ? "working..." : svc.Status);
        if (ImGui.IsItemHovered() && snap.ComputedAt != DateTime.MinValue)
            ImGui.SetTooltip($"Computed {snap.ComputedAt:HH:mm:ss}; scope {_plugin.Prices.Scope}; {_plugin.Prices.CacheSize} quotes cached; {_plugin.Prices.RequestsMade} Universalis requests ({_plugin.Prices.Failures} failed).");
    }

    private string JobLabel(uint jobId, CatalogSnapshot snap)
    {
        var abbr = _plugin.GameData?.JobAbbr(jobId) ?? jobId.ToString();
        return snap.Jobs.TryGetValue(jobId, out var lvl) ? $"{abbr} {lvl}" : abbr;
    }
}
