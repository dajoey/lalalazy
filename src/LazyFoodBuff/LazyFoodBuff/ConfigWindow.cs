using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using LalaUiKit;
using Lumina.Excel.Sheets;

namespace LazyFoodBuff;

// Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13).
// The Dalamud Window base auto-begins/ends this window (Draw() draws content only),
// so the top bar is drawn with the same kit primitives Layout.Shell uses, and the
// suite frame comes from Theme.PushFrame/PopFrame. FoodService ticks in the
// background, so the footer carries the live state and a guarded Stop (Brief R3):
// Stop only turns the automation off, exactly like unchecking Enabled.

internal class ConfigWindow : Window
{
    private readonly Plugin _plugin;

    // UI state for food search filter.
    private string _foodSearch = string.Empty;

    // UI state for job selection.
    private uint _selectedJobId;
    private string _jobSearch = string.Empty;

    public ConfigWindow(Plugin plugin) : base("LazyFoodBuff##cfg")
    {
        _plugin = plugin;
        Size = new Vector2(640, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    // Everything the pill, cards and footer need, derived once per frame from the
    // config plus the same player-state guards FoodService.Tick applies — read-only.
    private sealed record Live(
        UiState State,
        string Action,
        string? Reason,
        string? PillDetail,
        bool Fed,
        TimeSpan Remaining,
        uint ActiveFoodRowId);

    public override void Draw()
    {
        var c = _plugin.Config;
        var changed = false;

        Theme.PushFrame();
        try
        {
            var live = DeriveLive(c);

            DrawTopBar(live);
            Layout.NavSection("lfb_cfg_nav_settings", "Settings", null,
                FontAwesomeIcon.Utensils.ToIconString(), live.State, true);
            ImGui.Dummy(new Vector2(0, Theme.Unit));

            Layout.PageHeader(null, FontAwesomeIcon.Utensils.ToIconString(), "Food Automation",
                "Keeps the Well Fed buff up for you while you fight.",
                $"{c.Jobs.Count} jobs customized");

            DrawStatCards(c, live);

            var job = _selectedJobId == 0
                ? c.DefaultJob
                : c.GetOrCreateJobSettings(_selectedJobId);
            var jobName = ResolveJobName(_selectedJobId);
            var jobIcon = ResolveJobIcon(_selectedJobId);

            using var columns = Layout.TwoColumns.Begin(0.58f);

            // Work column: master enable, then per-job selection and mode.
            var master = c.MasterEnable;
            if (Widgets.Checkbox("Enabled", ref master))
            { c.MasterEnable = master; changed = true; }
            Widgets.Secondary("Master switch for the food automation.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            Widgets.Title("Per-Job Food Selection");

            // Job selector — lets the user browse and configure any job.
            DrawJobSelector();

            DrawJobHeader(jobName, jobIcon);

            // Mode selector.
            var mode = (int)job.Mode;
            if (ImGui.Combo("##mode", ref mode, new[] { "Auto-Select (best stats)", "Manual (pick food)" }, 2))
            { job.Mode = (FoodSelectionMode)mode; changed = true; }

            if (job.Mode == FoodSelectionMode.Manual)
            {
                DrawManualFoodPicker(job, ref changed);

                var fallback = job.FallbackToAutoSelect;
                if (ImGui.Checkbox("Fall back to auto-select if unavailable", ref fallback))
                { job.FallbackToAutoSelect = fallback; changed = true; }
            }
            else
            {
                ImGui.TextDisabled("Food is automatically selected based on your job's optimal stats.");
                ImGui.TextDisabled("The best food you have in inventory will be chosen.");

                // Show current recommendation for this job (state colour via the kit, R4).
                var recommended = FoodRecommender.RecommendBest(_plugin._service.AllFoods, _selectedJobId);
                if (recommended != null)
                {
                    ImGui.Spacing();
                    Widgets.StatusDot(UiState.Ready);
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"Recommended: {recommended.Name}");
                    var hq = recommended.InventoryCount(true);
                    var nq = recommended.InventoryCount(false);
                    ImGui.TextDisabled($"In inventory: {(hq > 0 ? $"{hq} HQ" : "")}{(hq > 0 && nq > 0 ? ", " : "")}{(nq > 0 ? $"{nq} NQ" : "")}");
                }
                else if (_selectedJobId != 0)
                {
                    ImGui.TextDisabled("No suitable food found in inventory.");
                }
            }

            columns.Split();

            // Rail: grouped settings.
            DrawRail(c, ref changed);

            if (changed) _plugin.SaveConfig();

            Layout.Footer(live.State, live.Action, live.Reason, "Stop", out var stop);
            if (stop && c.MasterEnable)
            {
                // Guarded Stop (R3): the only action is turning the automation off,
                // identical to unchecking Enabled. No-op when it is already off.
                c.MasterEnable = false;
                _plugin.SaveConfig();
            }
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private Live DeriveLive(Configuration c)
    {
        var fed = FoodService.TryGetWellFedStatus(out var rowId, out var left);
        var pillDetail = fed ? $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}m" : null;

        if (!c.MasterEnable)
            return new Live(UiState.Idle, "automation off", null, pillDetail, fed, left, rowId);

        // The same player-state guards FoodService.Tick applies, read-only.
        var local = Plugin.Objects.LocalPlayer;
        if (local == null)
            return new Live(UiState.Blocked, "waiting for you to log in", "no character", pillDetail, fed, left, rowId);
        if (local.IsDead)
            return new Live(UiState.Blocked, "waiting", "you are dead", pillDetail, fed, left, rowId);
        if (Plugin.Condition[ConditionFlag.BetweenAreas])
            return new Live(UiState.Blocked, "waiting", "between areas", pillDetail, fed, left, rowId);
        if (Plugin.Condition[ConditionFlag.OccupiedInEvent] || Plugin.Condition[ConditionFlag.OccupiedInQuestEvent])
            return new Live(UiState.Blocked, "waiting", "event or cutscene", pillDetail, fed, left, rowId);

        var gated = c.OnlyInCombatDuty && !FoodService.IsInCombatDuty();
        if (fed)
        {
            if (left <= TimeSpan.FromMinutes(c.RefreshThresholdMinutes))
                return new Live(UiState.Running, "refresh due", gated ? "duty gate holds the refresh" : null, pillDetail, fed, left, rowId);
            return new Live(UiState.Ready, "food active", gated ? "refresh waits for a combat duty" : null, pillDetail, fed, left, rowId);
        }

        if (gated)
            return new Live(UiState.Blocked, "waiting for a combat duty", "only eat in combat duties", pillDetail, fed, left, rowId);
        return new Live(UiState.Running, "choosing food", null, pillDetail, fed, left, rowId);
    }

    private void DrawTopBar(Live live)
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
        ImGui.TextUnformatted("LazyFoodBuff");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(live.State, "Food", live.PillDetail, alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);
    }

    private void DrawStatCards(Configuration c, Live live)
    {
        var width = (ImGui.GetContentRegionAvail().X - (2 * Theme.ItemSpacing.X)) / 3f;

        string? foodName = null;
        if (live.Fed)
            foodName = _plugin._service.AllFoods.FirstOrDefault(f => f.ItemFoodRowId == live.ActiveFoodRowId)?.Name;
        var mins = Math.Max(0, (int)Math.Round(live.Remaining.TotalMinutes));
        Widgets.StatCard("Well Fed", live.Fed ? $"{mins}m" : "None", live.Fed ? "30m" : null,
            live.Fed ? (float?)Math.Clamp((float)(live.Remaining.TotalMinutes / 30.0), 0f, 1f) : null,
            live.Fed ? foodName ?? "food active" : "no food active", width);

        var gate = c.OnlyInCombatDuty && !FoodService.IsInCombatDuty();
        Widgets.StatCard("Automation", c.MasterEnable ? "On" : "Off", null, null,
            !c.MasterEnable ? "automation off"
            : gate ? "gated to combat duties"
            : c.OnlyInCombatDuty ? "in combat duty"
            : "no duty gate", width);

        Widgets.StatCard("Low-food alert", c.WarningEnabled ? $"{c.WarningThresholdCount} left" : "Off", null, null,
            !c.WarningEnabled ? "alerts off"
            : c.WarningSoundEnabled ? $"chat + sound #{c.WarningSoundId}"
            : "chat only", width);
    }

    private void DrawRail(Configuration c, ref bool changed)
    {
        // Duty automation.
        Widgets.Title("Automation");
        var onlyDuty = c.OnlyInCombatDuty;
        if (ImGui.Checkbox("Only eat in combat duties", ref onlyDuty))
        { c.OnlyInCombatDuty = onlyDuty; changed = true; }
        ImGui.TextDisabled("Dungeons, raids, trials, alliance raids, criterion, variant, deep dungeons,");
        ImGui.TextDisabled("Crucible of the Unbroken (and its Bentbranch Meadows entry, on Beastmaster).");
        ImGui.TextDisabled("Excludes Diadem, field operations (Eureka/Bozja), overworld.");

        ImGui.Spacing();

        var refreshThr = c.RefreshThresholdMinutes;
        if (ImGui.SliderFloat("Refresh threshold (min)", ref refreshThr, 0f, 29f, "%.0f"))
        { c.RefreshThresholdMinutes = refreshThr; changed = true; }
        ImGui.TextDisabled("Re-eats food when remaining time drops below this. Food caps at 30 min.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Low-food warning.
        Widgets.Title("Low-Food Warning");
        var warnEnable = c.WarningEnabled;
        if (ImGui.Checkbox("Enable warning", ref warnEnable))
        { c.WarningEnabled = warnEnable; changed = true; }

        if (c.WarningEnabled)
        {
            var warnCount = c.WarningThresholdCount;
            if (ImGui.SliderInt("Warn when food left is at or below", ref warnCount, 1, 20))
            { c.WarningThresholdCount = warnCount; changed = true; }
            ImGui.TextDisabled("Alerts in chat once when the food you're eating drops to this");
            ImGui.TextDisabled("many in your inventory. Re-arms after you restock.");

            var warnSound = c.WarningSoundEnabled;
            if (ImGui.Checkbox("Play sound", ref warnSound))
            { c.WarningSoundEnabled = warnSound; changed = true; }

            if (c.WarningSoundEnabled)
            {
                var soundId = (int)c.WarningSoundId;
                if (ImGui.InputInt("Sound ID", ref soundId))
                { c.WarningSoundId = (uint)Math.Clamp(soundId, 1, 100); changed = true; }
                ImGui.SameLine();
                if (ImGui.Button("Test##soundtest"))
                {
                    // Sound playback removed — IGameGui.PlaySoundEffect does not exist in API 15.
                    // Chat error notification below is the primary alert mechanism.
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Advanced / Diagnostics. Collapsed by default: nothing in here changes what the
        // plugin does, and the one control writes to the log, which deserves a plain warning.
        if (ImGui.CollapsingHeader("Advanced / Diagnostics"))
        {
            var telemetry = c.DecisionTelemetry;
            if (ImGui.Checkbox("Log food decisions", ref telemetry))
            {
                c.DecisionTelemetry = telemetry;
                // Match the /lazyfoodbuff telemetry command: a fresh enable reports the
                // current recommendation instead of deduplicating against a stale one.
                if (telemetry) FoodTelemetry.Reset();
                changed = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Off by default. When on, LazyFoodBuff writes a diagnostic line to the Dalamud\n" +
                    "plugin log every time it settles on a food for your job — the food it picked,\n" +
                    "its score, and the runner-ups it beat. Lines start with \"" + FoodTelemetry.Prefix + "\".\n\n" +
                    "It changes no food behaviour and sends nothing anywhere — the lines only go\n" +
                    "to your own local plugin log, so the stat weights behind auto-select can be\n" +
                    "checked against how the food actually performed.\n\n" +
                    "Same as /lazyfoodbuff telemetry on|off.");
            }
            ImGui.TextDisabled($"Writes \"{FoodTelemetry.Prefix}\" lines to the plugin log. Nothing leaves your PC.");
        }
    }

    private void DrawJobSelector()
    {
        // Compact combo for selecting which job to configure.
        var currentName = _selectedJobId == 0 ? "Default (all jobs)" : ResolveJobName(_selectedJobId);
        ImGui.SetNextItemWidth(200f);
        if (ImGui.BeginCombo("##jobselector", currentName))
        {
            // Default option.
            if (ImGui.Selectable("Default (all jobs)", _selectedJobId == 0))
                _selectedJobId = 0;

            ImGui.Separator();

            // All combat jobs.
            var jobSheet = Plugin.Data.GetExcelSheet<ClassJob>();
            if (jobSheet != null)
            {
                foreach (var jobRow in jobSheet)
                {
                    // Skip non-combat classes (gatherers/crafters skip for food).
                    if (jobRow.Role == 0) continue;

                    var name = jobRow.Name.ExtractText();
                    if (string.IsNullOrEmpty(name)) continue;
                    name = char.ToUpper(name[0]) + name[1..];

                    if (!string.IsNullOrEmpty(_jobSearch) &&
                        !name.Contains(_jobSearch, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (ImGui.Selectable($"{name}##{jobRow.RowId}", _selectedJobId == jobRow.RowId))
                        _selectedJobId = jobRow.RowId;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##jobsearch", "Search jobs...", ref _jobSearch, 32);
    }

    private void DrawManualFoodPicker(JobFoodSettings job, ref bool changed)
    {
        ImGui.TextUnformatted("Select food:");

        // Searchable food picker (avoid 500+ item combo freezing).
        ImGui.SetNextItemWidth(-1);
        var currentName = job.ManualFoodItemId == 0
            ? "- None -"
            : _plugin._service.AllFoods.FirstOrDefault(f => f.Id == job.ManualFoodItemId)?.Name ?? $"Item #{job.ManualFoodItemId}";

        if (ImGui.BeginCombo("##foodpicker", currentName))
        {
            // Clear option.
            if (ImGui.Selectable("- None -", job.ManualFoodItemId == 0))
            { job.ManualFoodItemId = 0; changed = true; }
            ImGui.Separator();

            // Search filter.
            ImGui.InputTextWithHint("##foodsearch", "Search food...", ref _foodSearch, 64);
            ImGui.Separator();

            // Filtered list, capped at 200 for safety.
            var filtered = _plugin._service.AllFoods
                .Where(f => string.IsNullOrEmpty(_foodSearch) ||
                            f.Name.Contains(_foodSearch, StringComparison.OrdinalIgnoreCase))
                .Take(200);

            foreach (var food in filtered)
            {
                var hq = food.InventoryCount(true);
                var nq = food.InventoryCount(false);
                var owned = hq > 0 || nq > 0;
                var label = food.Name;
                if (owned)
                    label += $" ({(hq > 0 ? $"{hq}HQ" : "")}{(hq > 0 && nq > 0 ? "," : "")}{(nq > 0 ? $"{nq}NQ" : "")})";

                if (!owned)
                    ImGui.BeginDisabled();

                if (ImGui.Selectable($"{label}##{food.Id}", job.ManualFoodItemId == food.Id))
                { job.ManualFoodItemId = food.Id; changed = true; }

                if (!owned)
                    ImGui.EndDisabled();
            }

            ImGui.EndCombo();
        }

        if (job.ManualFoodItemId != 0)
        {
            var hq = job.ManualFoodIsHQ;
            if (ImGui.Checkbox("Prefer HQ", ref hq))
            { job.ManualFoodIsHQ = hq; changed = true; }
            ImGui.TextDisabled("If HQ is not in inventory, NQ will be used automatically.");
        }
    }

    private static void DrawJobHeader(string jobName, ImTextureID? icon)
    {
        Widgets.IconTile(icon, null, new Vector2(Theme.NavTileSize, Theme.NavTileSize), true);
        Widgets.Title($"Settings — {jobName}");
    }

    private static string ResolveJobName(uint jobId)
    {
        if (jobId == 0) return "Default";
        var sheet = Plugin.Data.GetExcelSheet<ClassJob>();
        if (sheet != null && sheet.TryGetRow(jobId, out var row))
        {
            var name = row.Name.ExtractText();
            if (!string.IsNullOrEmpty(name)) return char.ToUpper(name[0]) + name[1..];
        }
        return $"Job {jobId}";
    }

    private static ImTextureID? ResolveJobIcon(uint jobId)
    {
        if (jobId == 0) return null;
        var iconId = 62000u + jobId;
        var tex = Plugin.Textures.GetFromGameIcon(new GameIconLookup(iconId));
        return tex.GetWrapOrEmpty().Handle;
    }
}
