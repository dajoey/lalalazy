using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using LalaUiKit;
using Lumina.Excel.Sheets;

namespace LazyFoodBuff;

internal class ConfigWindow : Window
{
    private const float WideFooterHeight = 68f;
    private const float NarrowFooterHeight = 100f;

    private readonly Plugin _plugin;
    private string _foodSearch = string.Empty;
    private uint _selectedJobId;
    private string _jobSearch = string.Empty;

    public ConfigWindow(Plugin plugin) : base("LazyFoodBuff##cfg")
    {
        _plugin = plugin;
        Flags = ImGuiWindowFlags.NoTitleBar;
        Size = new Vector2(1080, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 480),
            MaximumSize = new Vector2(1600, 1200),
        };
    }

    private sealed record Live(
        UiState State,
        string Action,
        string? Reason,
        string? PillDetail,
        bool Fed,
        TimeSpan Remaining,
        uint ActiveFoodRowId);

    private readonly record struct Stat(string Label, string Value, string? Cap, float? Fraction, string Note);

    public override void Draw()
    {
        var c = _plugin.Config;
        var changed = false;

        Theme.PushFrame();
        FoodUiChrome.PushInputStyle();
        try
        {
            var live = DeriveLive(c);
            DrawTopBar(live);
            DrawPageHeader(c);

            var footerHeight = FooterHeight(ImGui.GetContentRegionAvail().X);
            var bodyHeight = MathF.Max(40, ImGui.GetContentRegionAvail().Y - footerHeight);
            if (ImGui.BeginChild("##lfbBody", new Vector2(0, bodyHeight), false))
            {
                DrawStatCards(c, live);
                DrawContent(c, live, ref changed);
            }
            ImGui.EndChild();

            if (DrawFooter(live, c.MasterEnable) && c.MasterEnable)
            {
                c.MasterEnable = false;
                changed = true;
            }

            if (changed)
                _plugin.SaveConfig();
        }
        finally
        {
            FoodUiChrome.PopInputStyle();
            Theme.PopFrame();
        }
    }

    private static Live DeriveLive(Configuration c)
    {
        var fed = FoodService.TryGetWellFedStatus(out var rowId, out var left);
        var pillDetail = fed ? $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}m" : null;

        if (!c.MasterEnable)
            return new Live(UiState.Idle, "automation off", null, pillDetail, fed, left, rowId);

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

    private static void DrawTopBar(Live live)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var stateLabel = Copy.Word(live.State);
        var statusSize = FoodUiChrome.StatusPillSize(stateLabel, live.PillDetail);
        const float brandWidth = 190;
        var singleRow = width >= brandWidth + statusSize.X + 24;
        var barHeight = singleRow ? 66f : 108f;

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));

        var brandPos = origin + new Vector2(0, 12);
        FoodUiChrome.Diamond(draw, brandPos + new Vector2(26, 21), 15, Theme.Pack(Theme.Accent));
        var brand = "LazyFoodBuff";
        var brandSize = ImGui.CalcTextSize(brand) * Theme.TitleScale;
        FoodUiChrome.DrawText(draw, brandPos + new Vector2(50, (42 - brandSize.Y) / 2f), brand, Theme.TextPrimary, Theme.TitleScale);

        var statusPos = singleRow
            ? origin + new Vector2(width - statusSize.X, 16)
            : origin + new Vector2(0, 62);
        FoodUiChrome.StatusPill(draw, statusPos, stateLabel, live.PillDetail, live.State);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private static void DrawPageHeader(Configuration c)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var tileSize = new Vector2(42);
        var glyph = FontAwesomeIcon.Utensils.ToIconString();
        var count = c.Jobs.Count == 0 ? "Default profile only" : $"{c.Jobs.Count} job overrides";
        const string title = "Food automation";
        const string description = "Keeps Well Fed active with the food and duty rules selected below.";

        draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 2 * Theme.Unit);
        var glyphSize = ImGui.CalcTextSize(glyph);
        draw.AddText(origin + ((tileSize - glyphSize) / 2f), Theme.Pack(Theme.Accent), glyph);

        if (width >= 700)
        {
            var textX = origin.X + tileSize.X + 12;
            var countWidth = ImGui.CalcTextSize(count).X;
            FoodUiChrome.DrawText(draw, new Vector2(textX, origin.Y),
                FoodUiChrome.FitText(title, width - tileSize.X - 12 - countWidth - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            FoodUiChrome.DrawText(draw, new Vector2(textX, origin.Y + 29),
                FoodUiChrome.FitText(description, width - tileSize.X - 12 - countWidth - 16), Theme.TextSecondary);
            FoodUiChrome.DrawTextRight(draw, origin.X + width, origin.Y + 13, count, Theme.TextSecondary);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 58));
        }
        else
        {
            FoodUiChrome.DrawText(draw, origin + new Vector2(tileSize.X + 12, 4),
                FoodUiChrome.FitText(title, width - tileSize.X - 12, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, tileSize.Y + 8));
            Widgets.SecondaryWrapped(description);
            Widgets.Caption(count);
        }
        ImGui.Separator();
    }

    private void DrawStatCards(Configuration c, Live live)
    {
        string? foodName = null;
        if (live.Fed)
            foodName = _plugin._service.AllFoods.FirstOrDefault(f => f.ItemFoodRowId == live.ActiveFoodRowId)?.Name;
        DrawStatCardsCore(c, live, foodName);
    }

    private static void DrawStatCardsCore(Configuration c, Live live, string? foodName)
    {
        var minutes = Math.Max(0, (int)Math.Round(live.Remaining.TotalMinutes));
        var gate = c.OnlyInCombatDuty && !FoodService.IsInCombatDuty();
        var stats = new[]
        {
            new Stat("Well Fed", live.Fed ? $"{minutes}m" : "None", live.Fed ? "30m" : null,
                live.Fed ? Math.Clamp((float)(live.Remaining.TotalMinutes / 30.0), 0f, 1f) : null,
                live.Fed ? foodName ?? "Food active" : "No food active"),
            new Stat("Automation", c.MasterEnable ? "On" : "Off", null, null,
                !c.MasterEnable ? "Automation off" : gate ? "Gated to combat duties" : c.OnlyInCombatDuty ? "In combat duty" : "No duty gate"),
            new Stat("Low-food alert", c.WarningEnabled ? $"{c.WarningThresholdCount} left" : "Off", null, null,
                !c.WarningEnabled ? "Alerts off" : c.WarningSoundEnabled ? $"Chat + sound #{c.WarningSoundId}" : "Chat only"),
        };
        DrawStats(stats);
    }

    private static void DrawStats(Stat[] stats)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var stacked = available < 620;
        var gap = 3 * Theme.Unit;
        var width = stacked ? available : (available - (2 * gap)) / stats.Length;
        const float height = 108;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
            FoodUiChrome.Diamond(draw, pos + new Vector2(20, 20), 5, Theme.Pack(Theme.Accent));
            FoodUiChrome.DrawText(draw, pos + new Vector2(34, 12),
                FoodUiChrome.FitText(stat.Label, width - 46, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
            var value = stat.Cap is null ? stat.Value : $"{stat.Value} / {stat.Cap}";
            FoodUiChrome.DrawText(draw, pos + new Vector2(12, 34),
                FoodUiChrome.FitText(value, width - 24, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            if (stat.Fraction is { } fraction)
            {
                var min = pos + new Vector2(12, 68);
                var max = pos + new Vector2(width - 12, 72);
                draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
                if (fraction > 0)
                    draw.AddRectFilled(min, min + new Vector2((max.X - min.X) * Math.Clamp(fraction, 0, 1), 0), Theme.Pack(Theme.StateGreen), 2f);
            }
            FoodUiChrome.DrawText(draw, pos + new Vector2(12, 84),
                FoodUiChrome.FitText(stat.Note, width - 24, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
        }

        var consumed = stacked ? (stats.Length * (height + gap)) : height + gap;
        ImGui.SetCursorScreenPos(origin + new Vector2(0, consumed));
    }

    private void DrawContent(Configuration c, Live live, ref bool changed)
    {
        var available = ImGui.GetContentRegionAvail().X;
        if (available < 900)
        {
            DrawWork(c, live, ref changed);
            DrawRail(c, live, ref changed);
            return;
        }

        var gap = 3 * Theme.Unit;
        var leftWidth = (available - gap) * 0.64f;
        if (ImGui.BeginChild("##lfbWork", new Vector2(leftWidth, 0), false))
            DrawWork(c, live, ref changed);
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##lfbRail", new Vector2(0, 0), false))
            DrawRail(c, live, ref changed);
        ImGui.EndChild();
    }

    private void DrawWork(Configuration c, Live live, ref bool changed)
    {
        var workChanged = false;
        FoodCard.Draw("master", () =>
        {
            FoodUiChrome.SectionTitle("Automation", "The master switch controls eating without changing the saved food profiles.");
            var master = c.MasterEnable;
            if (FoodUiChrome.Toggle("Enabled", ref master, "Turn automation on or off. Food selection and duty rules stay saved."))
            {
                c.MasterEnable = master;
                workChanged = true;
            }
            Widgets.SecondaryWrapped(c.MasterEnable
                ? "Eating follows the live state shown in the footer."
                : "Idle — nothing is eaten while automation is off.");
        });

        var job = _selectedJobId == 0 ? c.DefaultJob : c.GetOrCreateJobSettings(_selectedJobId);
        FoodCard.Draw("food_selection", () =>
        {
            FoodUiChrome.SectionTitle("Food selection", "Choose a job profile, then keep automatic scoring or select a specific food.");
            DrawJobSelector();
            DrawJobHeader(_selectedJobId);
            DrawModeSelector(job, ref workChanged);

            if (job.Mode == FoodSelectionMode.Manual)
                DrawManualFoodPicker(job, ref workChanged);
            else
                DrawAutoRecommendation();
        });
        changed |= workChanged;
    }

    private void DrawRail(Configuration c, Live live, ref bool changed)
    {
        var railChanged = false;
        FoodCard.Draw("requirements", () =>
        {
            FoodUiChrome.SectionTitle("Requirements");
            var local = Plugin.Objects.LocalPlayer;
            FoodUiChrome.Requirement("Character", local == null ? Copy.Missing("character") : local.IsDead ? "Blocked" : "Ready",
                local == null ? UiState.Missing : local.IsDead ? UiState.Blocked : UiState.Ready);
            var inDuty = FoodService.IsInCombatDuty();
            FoodUiChrome.Requirement("Combat duty", !c.OnlyInCombatDuty ? "Optional" : inDuty ? "Ready" : "Blocked",
                !c.OnlyInCombatDuty ? UiState.Idle : inDuty ? UiState.Ready : UiState.Blocked);
            FoodUiChrome.Requirement("Well Fed", live.Fed ? $"{Math.Max(1, (int)Math.Ceiling(live.Remaining.TotalMinutes))} min left" : "Empty",
                live.Fed ? UiState.Ready : UiState.Empty);
            FoodUiChrome.Requirement("Food profile", _selectedJobId == 0 ? "Default" : ResolveJobName(_selectedJobId), UiState.Idle);
        });

        FoodCard.Draw("duty_refresh", () =>
        {
            FoodUiChrome.SectionTitle("Duty & refresh");
            var onlyDuty = c.OnlyInCombatDuty;
            if (FoodUiChrome.Toggle("Only eat in combat duties", ref onlyDuty,
                    "Includes dungeons, raids, trials, variant and criterion duties, deep dungeons, and the Crucible. Excludes Diadem, field operations, and the overworld."))
            {
                c.OnlyInCombatDuty = onlyDuty;
                railChanged = true;
            }

            Widgets.Secondary("Refresh threshold");
            ImGui.SetNextItemWidth(-1);
            var refresh = c.RefreshThresholdMinutes;
            if (ImGui.SliderFloat("##refreshThreshold", ref refresh, 0f, 29f, "%.0f min"))
            {
                c.RefreshThresholdMinutes = refresh;
                railChanged = true;
            }
            Widgets.SecondaryWrapped("Re-eats when the remaining time reaches this threshold. Food duration caps at 30 minutes.");
        });

        FoodCard.Draw("warnings", () =>
        {
            FoodUiChrome.SectionTitle("Low-food warning");
            var warning = c.WarningEnabled;
            if (FoodUiChrome.Toggle("Enable warning", ref warning, "Warn once in chat when the active or selected food runs low; restocking re-arms the alert."))
            {
                c.WarningEnabled = warning;
                railChanged = true;
            }

            if (!c.WarningEnabled)
                return;

            Widgets.Secondary("Warn when food left is at or below");
            ImGui.SetNextItemWidth(-1);
            var count = c.WarningThresholdCount;
            if (ImGui.SliderInt("##warningThreshold", ref count, 1, 20))
            {
                c.WarningThresholdCount = count;
                railChanged = true;
            }

            var sound = c.WarningSoundEnabled;
            if (FoodUiChrome.Toggle("Play sound", ref sound))
            {
                c.WarningSoundEnabled = sound;
                railChanged = true;
            }

            if (c.WarningSoundEnabled)
                DrawSoundSettings(c, ref railChanged);
        });

        FoodCard.Draw("diagnostics", () =>
        {
            FoodUiChrome.SectionTitle("Diagnostics", "Optional decision logging does not change food selection or automation.");
            var telemetry = c.DecisionTelemetry;
            if (FoodUiChrome.Toggle("Log food decisions", ref telemetry,
                    $"Writes local {FoodTelemetry.Prefix} lines with the selected food, score, and runner-ups. Nothing is sent anywhere."))
            {
                c.DecisionTelemetry = telemetry;
                if (telemetry)
                    FoodTelemetry.Reset();
                railChanged = true;
            }
            Widgets.SecondaryWrapped("Off by default. The /lazyfoodbuff telemetry command controls the same setting; enabling it reports the current recommendation immediately.");
        });
        changed |= railChanged;
    }

    private static void DrawSoundSettings(Configuration c, ref bool changed)
    {
        Widgets.Secondary("Sound ID");
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var sideBySide = width >= 230;
        var inputWidth = sideBySide ? width - 92 : width;
        ImGui.SetNextItemWidth(inputWidth);
        var soundId = (int)c.WarningSoundId;
        if (ImGui.InputInt("##warningSoundId", ref soundId))
        {
            c.WarningSoundId = (uint)Math.Clamp(soundId, 1, 100);
            changed = true;
        }

        var buttonPos = sideBySide ? origin + new Vector2(width - 80, 0) : ImGui.GetCursorScreenPos();
        if (FoodUiChrome.OutlineButton(ImGui.GetWindowDrawList(), "sound_test", "Test", buttonPos, new Vector2(80, 32)))
        {
            // Sound playback is unavailable in the current API; the chat alert remains primary.
        }
        ImGui.SetCursorScreenPos(sideBySide
            ? origin + new Vector2(0, 36)
            : buttonPos + new Vector2(0, 36));
    }

    private void DrawJobSelector()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var sideBySide = width >= 430;
        var currentName = _selectedJobId == 0 ? "Default (all jobs)" : ResolveJobName(_selectedJobId);
        ImGui.SetNextItemWidth(sideBySide ? 220 : width);
        if (ImGui.BeginCombo("##jobselector", currentName))
        {
            if (ImGui.Selectable("Default (all jobs)", _selectedJobId == 0))
                _selectedJobId = 0;

            ImGui.Separator();
            var jobSheet = Plugin.Data.GetExcelSheet<ClassJob>();
            if (jobSheet != null)
            {
                foreach (var jobRow in jobSheet)
                {
                    if (jobRow.Role == 0)
                        continue;
                    var name = jobRow.Name.ExtractText();
                    if (string.IsNullOrEmpty(name))
                        continue;
                    name = char.ToUpper(name[0]) + name[1..];
                    if (!string.IsNullOrEmpty(_jobSearch) && !name.Contains(_jobSearch, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (ImGui.Selectable($"{name}##{jobRow.RowId}", _selectedJobId == jobRow.RowId))
                        _selectedJobId = jobRow.RowId;
                }
            }
            ImGui.EndCombo();
        }

        if (sideBySide)
            ImGui.SameLine();
        ImGui.SetNextItemWidth(sideBySide ? MathF.Max(120, width - 228) : width);
        ImGui.InputTextWithHint("##jobsearch", "Search jobs...", ref _jobSearch, 32);
        ImGui.Dummy(new Vector2(0, Theme.Unit));
    }

    private static void DrawJobHeader(uint jobId)
    {
        var icon = ResolveJobIcon(jobId);
        Widgets.IconTile(icon, icon is null ? FontAwesomeIcon.Utensils.ToIconString() : null, new Vector2(42), true);
        Widgets.Title(jobId == 0 ? "Default profile" : $"Settings — {ResolveJobName(jobId)}");
        Widgets.SecondaryWrapped(jobId == 0
            ? "Used by jobs without a specific override."
            : "This override starts as a copy of the default profile.");
        ImGui.Dummy(new Vector2(0, Theme.Unit));
    }

    private static void DrawModeSelector(JobFoodSettings job, ref bool changed)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var stacked = width < 340;
        var gap = 2 * Theme.Unit;
        var buttonWidth = stacked ? width : (width - gap) / 2f;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var modes = new[] { FoodSelectionMode.AutoSelect, FoodSelectionMode.Manual };
        var labels = new[] { "Auto-select", "Manual" };
        for (var i = 0; i < modes.Length; i++)
        {
            var pos = origin + (stacked ? new Vector2(0, i * 42) : new Vector2(i * (buttonWidth + gap), 0));
            if (FoodUiChrome.ChoiceButton(draw, modes[i].ToString(), labels[i], pos, new Vector2(buttonWidth, 34), job.Mode == modes[i])
                && job.Mode != modes[i])
            {
                job.Mode = modes[i];
                changed = true;
            }
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(0, stacked ? 84 : 42));
    }

    private void DrawAutoRecommendation()
    {
        Widgets.SecondaryWrapped("Scores owned food against the selected job's stat priorities; the best available NQ or HQ food is chosen.");
        var recommended = FoodRecommender.RecommendBest(_plugin._service.AllFoods, _selectedJobId);
        if (recommended != null)
        {
            Widgets.StatusDot(UiState.Ready);
            ImGui.SameLine();
            Widgets.Body($"Recommended: {recommended.Name}");
            Widgets.SecondaryWrapped($"In inventory: {InventoryText(recommended)}");
            return;
        }

        Widgets.StatusDot(UiState.Empty);
        ImGui.SameLine();
        Widgets.Body("Empty — no suitable food found");
        Widgets.SecondaryWrapped("Add food to inventory, choose another job profile, or switch to Manual selection.");
    }

    private void DrawManualFoodPicker(JobFoodSettings job, ref bool changed)
    {
        Widgets.Secondary("Select food");
        ImGui.SetNextItemWidth(-1);
        var selectedFood = _plugin._service.AllFoods.FirstOrDefault(f => f.Id == job.ManualFoodItemId);
        var currentName = job.ManualFoodItemId == 0 ? "- None -" : selectedFood?.Name ?? $"Item #{job.ManualFoodItemId}";

        if (ImGui.BeginCombo("##foodpicker", currentName))
        {
            if (ImGui.Selectable("- None -", job.ManualFoodItemId == 0))
            {
                job.ManualFoodItemId = 0;
                changed = true;
            }
            ImGui.Separator();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##foodsearch", "Search food...", ref _foodSearch, 64);
            ImGui.Separator();

            var filtered = _plugin._service.AllFoods
                .Where(f => string.IsNullOrEmpty(_foodSearch) || f.Name.Contains(_foodSearch, StringComparison.OrdinalIgnoreCase))
                .Take(201)
                .ToList();
            var hasMore = filtered.Count > 200;
            foreach (var food in filtered.Take(200))
            {
                var owned = food.InventoryCount(true) > 0 || food.InventoryCount(false) > 0;
                var label = owned ? $"{food.Name} ({InventoryText(food)})" : food.Name;
                if (!owned)
                    ImGui.BeginDisabled();
                if (ImGui.Selectable($"{label}##{food.Id}", job.ManualFoodItemId == food.Id))
                {
                    job.ManualFoodItemId = food.Id;
                    changed = true;
                }
                if (!owned)
                    ImGui.EndDisabled();
            }
            if (filtered.Count == 0)
                Widgets.SecondaryWrapped("No food matches this search.");
            else if (hasMore)
                Widgets.Caption("First 200 matches shown; narrow the search for more.");
            ImGui.EndCombo();
        }

        if (job.ManualFoodItemId != 0)
        {
            var hq = job.ManualFoodIsHQ;
            if (FoodUiChrome.Toggle("Prefer HQ", ref hq, "Uses NQ automatically when HQ is not in inventory."))
            {
                job.ManualFoodIsHQ = hq;
                changed = true;
            }
        }

        var fallback = job.FallbackToAutoSelect;
        if (FoodUiChrome.Toggle("Fall back to auto-select if unavailable", ref fallback,
                "Keeps the same manual-first automation behavior when the selected food is missing."))
        {
            job.FallbackToAutoSelect = fallback;
            changed = true;
        }

        if (job.ManualFoodItemId == 0)
        {
            Widgets.StatusDot(UiState.NeedsSetup);
            ImGui.SameLine();
            Widgets.Body("Needs setup — choose a food");
            Widgets.SecondaryWrapped("With no manual food selected, automatic selection is used until a food is chosen.");
        }
        else if (selectedFood == null || (selectedFood.InventoryCount(true) == 0 && selectedFood.InventoryCount(false) == 0))
        {
            Widgets.StatusDot(UiState.Missing);
            ImGui.SameLine();
            Widgets.Body(Copy.Missing("selected food"));
            Widgets.SecondaryWrapped(job.FallbackToAutoSelect
                ? "Automatic selection will be used until this food is restocked."
                : "No food will be eaten from this profile until the selected food is restocked.");
        }
        else
        {
            Widgets.SecondaryWrapped($"{selectedFood.Name}: {InventoryText(selectedFood)} in inventory.");
        }
    }

    private bool DrawFooter(Live live, bool masterEnabled)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = FooterHeight(width);
        var narrow = width < 600;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));

        var primary = $"{Copy.Word(live.State)} · {live.Action}";
        var secondary = live.Reason ?? (live.Fed
            ? $"Well Fed · {live.PillDetail} remaining"
            : "Food profiles and settings save on change.");
        var actionSize = new Vector2(96, 36);
        var actionPos = origin + new Vector2(width - actionSize.X, narrow ? 54 : 16);
        var textRight = narrow || !masterEnabled ? origin.X + width : actionPos.X - 16;
        var textPos = origin + new Vector2(24, 9);
        draw.AddCircleFilled(origin + new Vector2(10, 19), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(live.State)));
        FoodUiChrome.DrawText(draw, textPos, FoodUiChrome.FitText(primary, textRight - textPos.X), Theme.TextPrimary);
        FoodUiChrome.DrawText(draw, textPos + new Vector2(0, 27), FoodUiChrome.FitText(secondary, textRight - textPos.X), Theme.TextSecondary);

        var stop = masterEnabled && FoodUiChrome.StopButton(actionPos, actionSize);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
        return stop;
    }

    private static float FooterHeight(float width) => width < 600 ? NarrowFooterHeight : WideFooterHeight;

    private static string InventoryText(Food food)
    {
        var hq = food.InventoryCount(true);
        var nq = food.InventoryCount(false);
        var parts = new List<string>();
        if (hq > 0)
            parts.Add($"{hq} HQ");
        if (nq > 0)
            parts.Add($"{nq} NQ");
        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    private static string ResolveJobName(uint jobId)
    {
        if (jobId == 0)
            return "Default";
        var sheet = Plugin.Data.GetExcelSheet<ClassJob>();
        if (sheet != null && sheet.TryGetRow(jobId, out var row))
        {
            var name = row.Name.ExtractText();
            if (!string.IsNullOrEmpty(name))
                return char.ToUpper(name[0]) + name[1..];
        }
        return $"Job {jobId}";
    }

    private static ImTextureID? ResolveJobIcon(uint jobId)
    {
        if (jobId == 0)
            return null;
        var tex = Plugin.Textures.GetFromGameIcon(new GameIconLookup(62000u + jobId));
        return tex.GetWrapOrEmpty().Handle;
    }
}
