using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;

namespace LazyFoodBuff;

internal class ConfigWindow : Window
{
    private const float IconSize = 20f;

    private readonly Plugin _plugin;

    // UI state for food search filter.
    private string _foodSearch = string.Empty;

    // UI state for job selection.
    private uint _selectedJobId;
    private string _jobSearch = string.Empty;

    public ConfigWindow(Plugin plugin) : base("LazyFoodBuff##cfg")
    {
        _plugin = plugin;
        Size = new Vector2(520, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var c = _plugin.Config;
        var changed = false;

        // === Master Settings ===
        var master = c.MasterEnable;
        if (ImGui.Checkbox("Enabled", ref master)) { c.MasterEnable = master; changed = true; }

        ImGui.Separator();

        // === Duty Settings ===
        ImGui.TextUnformatted("Automation");
        var onlyDuty = c.OnlyInCombatDuty;
        if (ImGui.Checkbox("Only eat in combat duties", ref onlyDuty))
        { c.OnlyInCombatDuty = onlyDuty; changed = true; }
        ImGui.TextDisabled("Dungeons, raids, trials, alliance raids, criterion, variant, deep dungeons.");
        ImGui.TextDisabled("Excludes Diadem, field operations (Eureka/Bozja), overworld.");

        ImGui.Spacing();

        var refreshThr = c.RefreshThresholdMinutes;
        if (ImGui.SliderFloat("Refresh threshold (min)", ref refreshThr, 0f, 29f, "%.0f"))
        { c.RefreshThresholdMinutes = refreshThr; changed = true; }
        ImGui.TextDisabled("Re-eats food when remaining time drops below this. Food caps at 30 min.");

        ImGui.Separator();

        // === Warning Settings ===
        ImGui.TextUnformatted("Low-Food Warning");
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

        // Advanced / Diagnostics. Collapsed by default: nothing in here changes what the
        // plugin does, and the one control writes to the log, which deserves a plain warning.
        ImGui.Spacing();
        ImGui.Separator();
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

        ImGui.Separator();

        // === Per-Job Settings ===
        ImGui.TextUnformatted("Per-Job Food Selection");

        // Job selector — lets the user browse and configure any job.
        DrawJobSelector();

        var job = _selectedJobId == 0
            ? c.DefaultJob
            : c.GetOrCreateJobSettings(_selectedJobId);

        var jobName = ResolveJobName(_selectedJobId);
        var jobIcon = ResolveJobIcon(_selectedJobId);
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

            // Show current recommendation for this job.
            var recommended = FoodRecommender.RecommendBest(_plugin._service.AllFoods, _selectedJobId);
            if (recommended != null)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), $"Recommended: {recommended.Name}");
                var hq = recommended.InventoryCount(true);
                var nq = recommended.InventoryCount(false);
                ImGui.TextDisabled($"In inventory: {(hq > 0 ? $"{hq} HQ" : "")}{(hq > 0 && nq > 0 ? ", " : "")}{(nq > 0 ? $"{nq} NQ" : "")}");
            }
            else if (_selectedJobId != 0)
            {
                ImGui.TextDisabled("No suitable food found in inventory.");
            }
        }

        if (changed) _plugin.SaveConfig();
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
        if (icon.HasValue)
        {
            ImGui.Image(icon.Value, new Vector2(IconSize, IconSize));
            ImGui.SameLine();
        }
        ImGui.TextUnformatted($"Settings - {jobName}");
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
