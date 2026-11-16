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
    private const float BodyPadY = FoodUiChrome.Gap;

    private readonly Plugin _plugin;
    private string _foodSearch = string.Empty;
    private uint _selectedJobId;
    private string _jobSearch = string.Empty;
    private bool _changed;

    public ConfigWindow(Plugin plugin) : base("LazyFoodBuff##cfg")
    {
        _plugin = plugin;
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        Size = new Vector2(1080, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 480),
            MaximumSize = new Vector2(1600, 1200),
        };
    }

    // The shell lays out its own gutters from the window edge, so the window itself carries no padding.
    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    public override void PostDraw() => ImGui.PopStyleVar();

    private sealed record Live(
        UiState State,
        string Action,
        string? Reason,
        string? PillDetail,
        bool Fed,
        TimeSpan Remaining,
        uint ActiveFoodRowId);

    private readonly record struct Stat(string Label, string Value, string? Cap, float? Fraction, string Note, bool Dim = false);

    public override void Draw()
    {
        var c = _plugin.Config;
        _changed = false;
        FoodUiChrome.ResetLayout();
        FoodCard.FillBottom = float.NaN;

        Theme.PushFrame();
        try
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var gutter = FoodUiChrome.Gutter(winSize.X);
            var live = DeriveLive(c);
            ImGui.SetCursorPos(Vector2.Zero);
            var y = DrawTopBar(live, winPos, winSize.X, gutter);
            y = DrawPageHeader(c, winPos, winSize.X, gutter, y);

            var footerHeight = FooterHeight(winSize.X);
            FoodUiChrome.PopupBounds = (winPos.Y + y, winPos.Y + winSize.Y - footerHeight - (2 * Theme.Unit));
            ImGui.SetCursorPos(new Vector2(0, y));
            DrawBody(c, live, new Vector2(winSize.X, MathF.Max(40, winSize.Y - footerHeight - y)), gutter);

            if (DrawFooter(live, c.MasterEnable, winPos, winSize, gutter, footerHeight) && c.MasterEnable)
            {
                c.MasterEnable = false;
                _changed = true;
            }
        }
        finally
        {
            Theme.PopFrame();
            if (_changed)
                _plugin.SaveConfig();
        }
    }

    private static string Sentence(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static Live DeriveLive(Configuration c)
    {
        var fed = FoodService.TryGetWellFedStatus(out var rowId, out var left);
        var pillDetail = fed ? $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}m" : null;

        if (!c.MasterEnable)
            return new Live(UiState.Idle, "Automation off", null, pillDetail, fed, left, rowId);

        var local = Plugin.Objects.LocalPlayer;
        if (local == null)
            return new Live(UiState.Blocked, "Waiting for you to log in", "No character", pillDetail, fed, left, rowId);
        if (local.IsDead)
            return new Live(UiState.Blocked, "Waiting", "You are dead", pillDetail, fed, left, rowId);
        if (Plugin.Condition[ConditionFlag.BetweenAreas])
            return new Live(UiState.Blocked, "Waiting", "Between areas", pillDetail, fed, left, rowId);
        if (Plugin.Condition[ConditionFlag.OccupiedInEvent] || Plugin.Condition[ConditionFlag.OccupiedInQuestEvent])
            return new Live(UiState.Blocked, "Waiting", "Event or cutscene", pillDetail, fed, left, rowId);

        var gated = c.OnlyInCombatDuty && !FoodService.IsInCombatDuty();
        if (fed)
        {
            if (left <= TimeSpan.FromMinutes(c.RefreshThresholdMinutes))
                return new Live(UiState.Running, "Refresh due", gated ? "Duty gate holds the refresh" : null, pillDetail, fed, left, rowId);
            return new Live(UiState.Ready, "Food active", gated ? "Refresh waits for a combat duty" : null, pillDetail, fed, left, rowId);
        }

        if (gated)
            return new Live(UiState.Blocked, "Waiting for a combat duty", "Only eat in combat duties", pillDetail, fed, left, rowId);
        return new Live(UiState.Running, "Choosing food", null, pillDetail, fed, left, rowId);
    }

    // ---- title bar ----

    private float DrawTopBar(Live live, Vector2 winPos, float winWidth, float gutter)
    {
        var draw = ImGui.GetWindowDrawList();
        var inner = winWidth - (2 * gutter);
        const float padY = 3.5f * Theme.Unit;
        const float brandHeight = 10 * Theme.Unit;
        var control = FoodUiChrome.ControlHeight;
        var pillSize = new Vector2(FoodUiChrome.PillWidth(), control);
        var closeSize = new Vector2(control);
        var brandWidth = FoodUiChrome.BrandWidth();
        var oneRow = inner >= brandWidth + (4 * Theme.Unit) + pillSize.X + (2 * Theme.Unit) + closeSize.X;
        var controlY = padY + ((brandHeight - control) / 2f);
        var closePos = new Vector2(winWidth - gutter - closeSize.X, controlY);
        Vector2 pillPos;
        float barHeight;
        if (oneRow)
        {
            pillPos = new Vector2(closePos.X - (2 * Theme.Unit) - pillSize.X, controlY);
            barHeight = padY + brandHeight + padY;
        }
        else
        {
            pillPos = new Vector2(gutter, padY + brandHeight + (2 * Theme.Unit));
            barHeight = pillPos.Y + control + padY;
        }

        draw.PushClipRect(winPos, winPos + new Vector2(winWidth, barHeight + 1), false);
        draw.AddRectFilled(winPos, winPos + new Vector2(winWidth, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, barHeight), winPos + new Vector2(winWidth, barHeight), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
        FoodUiChrome.Brand(draw, winPos + new Vector2(gutter, padY), brandHeight);
        FoodUiChrome.StatusPill(draw, winPos + pillPos, pillSize, Copy.Word(live.State), live.PillDetail, live.State);
        if (FoodUiChrome.OutlineButton(draw, "close", "×", winPos + closePos, closeSize))
            IsOpen = false;
        ImGui.SetCursorScreenPos(winPos + new Vector2(0, barHeight + 1));
        return barHeight + 1;
    }

    // ---- page header ----

    private static float DrawPageHeader(Configuration c, Vector2 winPos, float winWidth, float gutter, float top)
    {
        var draw = ImGui.GetWindowDrawList();
        var count = c.Jobs.Count == 0 ? "Default profile only" : $"{c.Jobs.Count} job overrides";
        const string title = "Food automation";
        const string description = "Keeps Well Fed active with the food and duty rules selected below.";
        const float tile = 11 * Theme.Unit;
        const float padTop = 5 * Theme.Unit;
        var wide = winWidth >= 1000;
        var textX = gutter + tile + (4 * Theme.Unit);
        var metaWidth = wide ? MathF.Min(winWidth * 0.40f, FoodUiChrome.TextWidth(count, Theme.SecondaryScale)) : 0;
        var textWidth = winWidth - textX - gutter - (wide ? metaWidth + (6 * Theme.Unit) : 0);
        var origin = winPos + new Vector2(gutter, top + padTop);
        draw.AddRectFilled(origin, origin + new Vector2(tile), Theme.Pack(Theme.Accent with { W = 0.14f }), FoodUiChrome.Radius);
        FoodUiChrome.Diamond(draw, origin + new Vector2(tile / 2f), 11, Theme.Pack(Theme.Accent));

        var shownTitle = FoodUiChrome.ClampLines(title, textWidth, FoodUiChrome.PageTitleScale, wide ? 1 : 2);
        var titleHeight = FoodUiChrome.WrappedHeight(shownTitle, textWidth, FoodUiChrome.PageTitleScale);
        var textOrigin = new Vector2(winPos.X + textX, origin.Y);
        FoodUiChrome.DrawWrapped(draw, textOrigin, shownTitle, textWidth, Theme.TextPrimary, FoodUiChrome.PageTitleScale);
        var shownDescription = FoodUiChrome.ClampLines(description, textWidth, Theme.SecondaryScale, wide ? 1 : 3);
        var descriptionTop = textOrigin.Y + titleHeight + Theme.Unit;
        var descriptionHeight = FoodUiChrome.DrawWrapped(draw, new Vector2(textOrigin.X, descriptionTop), shownDescription, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        var blockBottom = descriptionTop + descriptionHeight;
        if ((shownTitle != title || shownDescription != description)
            && ImGui.IsMouseHoveringRect(textOrigin, new Vector2(textOrigin.X + textWidth, blockBottom)))
            ImGui.SetTooltip(shownTitle != title ? title : description);

        if (wide)
        {
            var line = textOrigin.Y + ((titleHeight - FoodUiChrome.LineHeight(Theme.SecondaryScale)) / 2f);
            FoodUiChrome.DrawFittedRight(draw, winPos.X + winWidth - gutter, line, count, metaWidth, Theme.TextSecondary, Theme.SecondaryScale);
        }
        else
        {
            FoodUiChrome.DrawFitted(draw, new Vector2(textOrigin.X, blockBottom + Theme.Unit), count, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
            blockBottom += Theme.Unit + FoodUiChrome.LineHeight(Theme.SecondaryScale);
        }
        var height = MathF.Max(tile, blockBottom - origin.Y);
        return top + padTop + height + (2 * Theme.Unit);
    }

    // ---- body ----

    private void DrawBody(Configuration c, Live live, Vector2 size, float gutter)
    {
        // A one pixel transparent border makes the child honour its window padding, which gives every page the same
        // left and right gutter and a bottom margin; the scrollbar then sits in a narrow themed track on the window edge.
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(gutter, BodyPadY));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 2.5f * Theme.Unit);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 2 * Theme.Unit);
        ImGui.PushStyleColor(ImGuiCol.Border, 0u);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Theme.TextSecondary with { W = 0.45f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Theme.Accent with { W = 0.6f });
        ImGui.BeginChild("##lfbBody", size, true);
        DrawStatCards(c, live);
        FoodCard.Columns("body", () => DrawWork(c, live), () => DrawRail(c, live), breakpoint: 840, fillPage: true);

        // Rows are never sliced by the footer without a hint: a soft fade marks content that continues below, and a
        // fade with a hairline marks content that has scrolled away above.
        {
            var draw = ImGui.GetWindowDrawList();
            var bg = ImGui.GetColorU32(ImGuiCol.WindowBg);
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            if (ImGui.GetScrollMaxY() > 0 && ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1)
                draw.AddRectFilledMultiColor(winPos + new Vector2(1, winSize.Y - (7 * Theme.Unit)), winPos + new Vector2(winSize.X - 1, winSize.Y - 1),
                    bg & 0x00FFFFFFu, bg & 0x00FFFFFFu, bg, bg);
            if (ImGui.GetScrollY() > 1)
            {
                draw.AddRectFilledMultiColor(winPos + new Vector2(1, 1), winPos + new Vector2(winSize.X - 1, 1 + (5 * Theme.Unit)),
                    bg, bg, bg & 0x00FFFFFFu, bg & 0x00FFFFFFu);
                draw.AddLine(winPos, winPos + new Vector2(winSize.X, 0), Theme.Pack(Theme.SurfaceBorder));
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(6);
        ImGui.PopStyleVar(5);
    }

    // ---- stat cards ----

    private void DrawStatCards(Configuration c, Live live)
    {
        string? foodName = null;
        if (live.Fed)
            foodName = _plugin._service.AllFoods.FirstOrDefault(f => f.ItemFoodRowId == live.ActiveFoodRowId)?.Name;
        DrawStatCardsCore(c, live, foodName);
    }

    private static void DrawStatCardsCore(Configuration c, Live live, string? foodName)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(live.Remaining.TotalMinutes));
        var gate = c.OnlyInCombatDuty && !FoodService.IsInCombatDuty();
        var stats = new[]
        {
            new Stat("Well Fed", live.Fed ? $"{minutes}m" : "None", live.Fed ? "30m" : null,
                live.Fed ? Math.Clamp((float)(live.Remaining.TotalMinutes / 30.0), 0f, 1f) : null,
                live.Fed ? foodName ?? "Food active" : "No food active", Dim: !live.Fed || !c.MasterEnable),
            new Stat("Automation", c.MasterEnable ? "On" : "Off", null, null,
                !c.MasterEnable ? "Automation off" : gate ? "Gated to combat duties" : c.OnlyInCombatDuty ? "In combat duty" : "No duty gate",
                Dim: !c.MasterEnable),
            new Stat("Low-food alert", c.WarningEnabled ? $"{c.WarningThresholdCount} left" : "Off", null, null,
                !c.WarningEnabled ? "Alerts off" : c.WarningSoundEnabled ? $"Chat + sound #{c.WarningSoundId}" : "Chat only",
                Dim: !c.WarningEnabled),
        };
        DrawStats(stats);
    }

    private static void DrawStats(Stat[] stats)
    {
        var draw = ImGui.GetWindowDrawList();
        var available = FoodUiChrome.Avail();
        var gap = FoodUiChrome.Gap;
        var stacked = available < (3 * 220) + (2 * gap);
        var width = stacked ? available : (available - (2 * gap)) / stats.Length;
        const float pad = FoodUiChrome.Gap;
        var labelHeight = FoodUiChrome.LineHeight(Theme.SecondaryScale);
        var valueHeight = FoodUiChrome.LineHeight(FoodUiChrome.StatValueScale);
        var noteHeight = labelHeight;
        var height = stacked
            ? (3 * Theme.Unit) + labelHeight + Theme.Unit + valueHeight + (2 * Theme.Unit) + Theme.BarHeight + (3 * Theme.Unit)
            : pad + labelHeight + (2 * Theme.Unit) + valueHeight + (2 * Theme.Unit) + Theme.BarHeight + (2 * Theme.Unit) + noteHeight + pad;
        var origin = ImGui.GetCursorScreenPos();
        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), FoodUiChrome.Radius);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), FoodUiChrome.Radius);

            var y = pos.Y + (stacked ? 3 * Theme.Unit : pad);
            FoodUiChrome.Diamond(draw, new Vector2(pos.X + pad + 5, y + (labelHeight / 2f)), 5, Theme.Pack(Theme.Accent));
            FoodUiChrome.DrawFitted(draw, new Vector2(pos.X + pad + 16, y), stat.Label, width - (2 * pad) - 16, Theme.TextSecondary, Theme.SecondaryScale);
            y += labelHeight + (stacked ? Theme.Unit : 2 * Theme.Unit);

            var valueWidth = FoodUiChrome.TextWidth(stat.Value, FoodUiChrome.StatValueScale);
            FoodUiChrome.DrawText(draw, new Vector2(pos.X + pad, y), stat.Value, stat.Dim ? Theme.TextSecondary : Theme.TextPrimary, FoodUiChrome.StatValueScale);
            var after = pos.X + pad + valueWidth + (2 * Theme.Unit);
            if (stat.Cap is { } cap)
            {
                var capText = cap.Length > 0 && char.IsDigit(cap[0]) ? "/ " + cap : cap;
                var capY = y + valueHeight - labelHeight - (valueHeight * 0.08f);
                FoodUiChrome.DrawText(draw, new Vector2(after, capY), capText, Theme.TextSecondary, Theme.SecondaryScale);
                after += FoodUiChrome.TextWidth(capText, Theme.SecondaryScale) + Gap2;
            }
            if (stacked)
            {
                // Compact: the note sits on the value's baseline, right aligned, so the card stays short.
                var noteRoom = pos.X + width - pad - after;
                if (noteRoom > 40)
                    FoodUiChrome.DrawFittedRight(draw, pos.X + width - pad, y + valueHeight - noteHeight - (valueHeight * 0.08f), stat.Note, noteRoom, Theme.TextSecondary, Theme.SecondaryScale);
            }
            y += valueHeight + (2 * Theme.Unit);

            var min = new Vector2(pos.X + pad, y);
            var max = new Vector2(pos.X + width - pad, y + Theme.BarHeight);
            draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), Theme.BarHeight / 2f);
            if (stat.Fraction is { } fraction && fraction > 0)
                draw.AddRectFilled(min, new Vector2(min.X + ((max.X - min.X) * Math.Clamp(fraction, 0, 1)), max.Y),
                    Theme.Pack(stat.Dim ? Theme.StateNeutral : Theme.StateGreen), Theme.BarHeight / 2f);
            y += Theme.BarHeight + (2 * Theme.Unit);

            if (!stacked)
            {
                var noteColor = stat.Fraction is >= 1f ? Theme.StateGreen : Theme.TextSecondary;
                FoodUiChrome.DrawFitted(draw, new Vector2(pos.X + pad, y), stat.Note, width - (2 * pad), noteColor, Theme.SecondaryScale);
            }
        }
        var total = stacked ? (stats.Length * height) + ((stats.Length - 1) * gap) : height;
        FoodUiChrome.Advance(origin, available, total);
    }

    private const float Gap2 = 2 * Theme.Unit;

    // ---- work column ----

    private void DrawWork(Configuration c, Live live)
    {
        FoodCard.Draw("master", () =>
        {
            FoodUiChrome.CardHeader("Automation", "The master switch controls eating without changing the saved food profiles.", Theme.Unit);
            var master = c.MasterEnable;
            if (FoodUiChrome.Toggle("Enabled", ref master, "Turn automation on or off. Food selection and duty rules stay saved."))
            {
                c.MasterEnable = master;
                _changed = true;
            }
            if (!c.MasterEnable)
                FoodUiChrome.Notice("Idle", "Nothing is eaten while automation is off.", UiState.Idle, 0);
        });

        var job = _selectedJobId == 0 ? c.DefaultJob : c.GetOrCreateJobSettings(_selectedJobId);
        FoodCard.Draw("food_selection", () =>
        {
            FoodUiChrome.CardHeader("Food selection", "Choose a job profile, then keep automatic scoring or select a specific food.");
            DrawJobSelector();
            DrawJobHeader(_selectedJobId);
            DrawModeSelector(job);

            if (job.Mode == FoodSelectionMode.Manual)
                DrawManualFoodPicker(job);
            else
                DrawAutoRecommendation();
        }, fill: true);
    }

    // ---- rail column ----

    private void DrawRail(Configuration c, Live live)
    {
        FoodCard.Draw("requirements", () =>
        {
            FoodUiChrome.CardHeader("Requirements", null, Theme.Unit);
            var local = Plugin.Objects.LocalPlayer;
            FoodUiChrome.StatusRow("Character", local == null ? Copy.Missing("character") : local.IsDead ? "Blocked" : "Ready",
                local == null ? UiState.Missing : local.IsDead ? UiState.Blocked : UiState.Ready);
            var inDuty = FoodService.IsInCombatDuty();
            FoodUiChrome.StatusRow("Combat duty", !c.OnlyInCombatDuty ? "Optional" : inDuty ? "Ready" : "Blocked",
                !c.OnlyInCombatDuty ? UiState.Idle : inDuty ? UiState.Ready : UiState.Blocked);
            FoodUiChrome.StatusRow("Well Fed", live.Fed ? $"{Math.Max(1, (int)Math.Ceiling(live.Remaining.TotalMinutes))} min left" : "Empty",
                live.Fed ? UiState.Ready : UiState.Empty);
            FoodUiChrome.StatusRow("Food profile", _selectedJobId == 0 ? "Default" : ResolveJobName(_selectedJobId), UiState.Idle);
        });

        FoodCard.Draw("duty_refresh", () =>
        {
            FoodUiChrome.CardHeader("Duty & refresh", null, Theme.Unit);
            var onlyDuty = c.OnlyInCombatDuty;
            if (FoodUiChrome.Toggle("Only eat in combat duties", ref onlyDuty,
                    "Includes dungeons, raids, trials, variant and criterion duties, deep dungeons, and the Crucible. Excludes Diadem, field operations, and the overworld."))
            {
                c.OnlyInCombatDuty = onlyDuty;
                _changed = true;
            }

            FoodUiChrome.BeginRow(separator: true);
            var refresh = c.RefreshThresholdMinutes;
            if (FoodUiChrome.Slider("refreshThreshold", "Refresh threshold", ref refresh, 0f, 29f, $"{refresh:0} min",
                    "Re-eats when the remaining time reaches this threshold. Food duration caps at 30 minutes."))
            {
                c.RefreshThresholdMinutes = refresh;
                _changed = true;
            }
            FoodUiChrome.EndRow();
        });

        FoodCard.Draw("warnings", () =>
        {
            FoodUiChrome.CardHeader("Low-food warning", null, Theme.Unit);
            var warning = c.WarningEnabled;
            if (FoodUiChrome.Toggle("Enable warning", ref warning, "Warn once in chat when the active or selected food runs low; restocking re-arms the alert."))
            {
                c.WarningEnabled = warning;
                _changed = true;
            }

            if (!c.WarningEnabled)
                return;

            FoodUiChrome.BeginRow(separator: true);
            var count = (float)c.WarningThresholdCount;
            if (FoodUiChrome.Slider("warningThreshold", "Warn when food left is at or below", ref count, 1f, 20f, $"{count:0}"))
            {
                c.WarningThresholdCount = (int)count;
                _changed = true;
            }
            FoodUiChrome.EndRow();

            var sound = c.WarningSoundEnabled;
            if (FoodUiChrome.Toggle("Play sound", ref sound, null, separator: true))
            {
                c.WarningSoundEnabled = sound;
                _changed = true;
            }

            if (c.WarningSoundEnabled)
                DrawSoundSettings(c);
        }, fill: false);

        FoodCard.Draw("diagnostics", () =>
        {
            FoodUiChrome.CardHeader("Diagnostics", "Optional decision logging does not change food selection or automation.", Theme.Unit);
            var telemetry = c.DecisionTelemetry;
            if (FoodUiChrome.Toggle("Log food decisions", ref telemetry,
                    "Writes local log lines with the selected food, score, and runner-ups. Nothing is sent anywhere."))
            {
                c.DecisionTelemetry = telemetry;
                if (telemetry)
                    FoodTelemetry.Reset();
                _changed = true;
            }
            FoodUiChrome.Paragraph("Off by default. The /lazyfoodbuff telemetry command controls the same setting; enabling it reports the current recommendation immediately.", gap: 0);
        }, fill: true);
    }

    private void DrawSoundSettings(Configuration c)
    {
        var draw = ImGui.GetWindowDrawList();
        FoodUiChrome.BeginRow(separator: true);
        var origin = ImGui.GetCursorScreenPos();
        var width = FoodUiChrome.Avail();
        var lineHeight = FoodUiChrome.LineHeight();
        FoodUiChrome.DrawText(draw, origin, "Sound ID", Theme.TextPrimary);
        var fieldTop = origin + new Vector2(0, lineHeight + (2 * Theme.Unit));
        var buttonSize = new Vector2(80, FoodUiChrome.ControlHeight);
        var sideBySide = width >= 230;
        var inputWidth = sideBySide ? width - buttonSize.X - (2 * Theme.Unit) : width;
        ImGui.SetCursorScreenPos(fieldTop);
        ImGui.SetNextItemWidth(inputWidth);
        var soundId = (int)c.WarningSoundId;
        FoodUiChrome.PushField();
        if (ImGui.InputInt("##warningSoundId", ref soundId))
        {
            c.WarningSoundId = (uint)Math.Clamp(soundId, 1, 100);
            _changed = true;
        }
        FoodUiChrome.PopField();

        var buttonPos = sideBySide
            ? fieldTop + new Vector2(width - buttonSize.X, 0)
            : fieldTop + new Vector2(0, FoodUiChrome.ControlHeight + (2 * Theme.Unit));
        if (FoodUiChrome.OutlineButton(draw, "sound_test", "Test", buttonPos, buttonSize, onCard: true))
        {
            // Sound playback is unavailable in the current API; the chat alert remains primary.
        }
        var bottom = sideBySide ? FoodUiChrome.ControlHeight : (2 * FoodUiChrome.ControlHeight) + (2 * Theme.Unit);
        FoodUiChrome.Advance(origin, width, lineHeight + (2 * Theme.Unit) + bottom, 0);
        FoodUiChrome.EndRow();
    }

    // ---- food selection card ----

    private void DrawJobSelector()
    {
        var width = FoodUiChrome.Avail();
        var origin = ImGui.GetCursorScreenPos();
        var currentName = _selectedJobId == 0 ? "Default (all jobs)" : ResolveJobName(_selectedJobId);
        var open = FoodUiChrome.BeginCombo("##jobselector", currentName, width);
        if (open)
        {
            if (ImGui.Selectable("Default (all jobs)", _selectedJobId == 0))
                _selectedJobId = 0;

            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##jobsearch", "Search jobs...", ref _jobSearch, 32);
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
        }
        FoodUiChrome.EndCombo(open);
        FoodUiChrome.Advance(origin, width, FoodUiChrome.ControlHeight, FoodUiChrome.Gap - Theme.Unit);
    }

    private static void DrawJobHeader(uint jobId)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = FoodUiChrome.Avail();
        const float tile = 10 * Theme.Unit;
        var textX = tile + (3 * Theme.Unit);
        var textWidth = MathF.Max(40, width - textX);
        var title = jobId == 0 ? "Default profile" : $"Settings — {ResolveJobName(jobId)}";
        var subtitle = jobId == 0 ? "Used by jobs without a specific override." : "This override starts as a copy of the default profile.";
        var titleHeight = FoodUiChrome.WrappedHeight(title, textWidth);
        var subtitleHeight = FoodUiChrome.WrappedHeight(subtitle, textWidth, Theme.SecondaryScale);
        var block = titleHeight + Theme.Unit + subtitleHeight;
        var height = MathF.Max(tile, block);

        var tilePos = pos + new Vector2(0, (height - tile) / 2f);
        draw.AddRectFilled(tilePos, tilePos + new Vector2(tile), Theme.Pack(Theme.ShellBar), FoodUiChrome.Radius);
        var icon = ResolveJobIcon(jobId);
        if (icon is { } texture)
            draw.AddImage(texture, tilePos + new Vector2(Theme.Unit), tilePos + new Vector2(tile - Theme.Unit));
        else
            FoodUiChrome.Diamond(draw, tilePos + new Vector2(tile / 2f), 9, Theme.Pack(Theme.Accent));
        draw.AddRect(tilePos, tilePos + new Vector2(tile), Theme.Pack(Theme.SurfaceBorder), FoodUiChrome.Radius);

        var textTop = pos.Y + ((height - block) / 2f);
        FoodUiChrome.DrawWrapped(draw, new Vector2(pos.X + textX, textTop), title, textWidth, Theme.TextPrimary);
        FoodUiChrome.DrawWrapped(draw, new Vector2(pos.X + textX, textTop + titleHeight + Theme.Unit), subtitle, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        FoodUiChrome.Advance(pos, width, height, FoodUiChrome.Gap - Theme.Unit);
    }

    private void DrawModeSelector(JobFoodSettings job)
    {
        var width = FoodUiChrome.Avail();
        var stacked = width < 340;
        var gap = 2 * Theme.Unit;
        var buttonSize = new Vector2(stacked ? width : (width - gap) / 2f, FoodUiChrome.ControlHeight);
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var modes = new[] { FoodSelectionMode.AutoSelect, FoodSelectionMode.Manual };
        var labels = new[] { "Auto-select", "Manual" };
        for (var i = 0; i < modes.Length; i++)
        {
            var pos = origin + (stacked ? new Vector2(0, i * (buttonSize.Y + gap)) : new Vector2(i * (buttonSize.X + gap), 0));
            if (FoodUiChrome.ChoiceButton(draw, modes[i].ToString(), labels[i], pos, buttonSize, job.Mode == modes[i])
                && job.Mode != modes[i])
            {
                job.Mode = modes[i];
                _changed = true;
            }
        }
        var total = stacked ? (2 * buttonSize.Y) + gap : buttonSize.Y;
        FoodUiChrome.Advance(origin, width, total, FoodUiChrome.Gap);
    }

    private void DrawAutoRecommendation()
    {
        FoodUiChrome.Paragraph("Scores owned food against the selected job's stat priorities; the best available NQ or HQ food is chosen.", gap: FoodUiChrome.Gap);
        var recommended = FoodRecommender.RecommendBest(_plugin._service.AllFoods, _selectedJobId);
        if (recommended != null)
        {
            FoodUiChrome.Notice($"Recommended: {recommended.Name}", $"In inventory: {InventoryText(recommended)}", UiState.Ready, 0);
            return;
        }

        FoodUiChrome.Notice("Empty", "No suitable food found. Add food to inventory, choose another job profile, or switch to Manual selection.", UiState.Empty, 0);
    }

    private void DrawManualFoodPicker(JobFoodSettings job)
    {
        var draw = ImGui.GetWindowDrawList();
        var labelPos = ImGui.GetCursorScreenPos();
        var width = FoodUiChrome.Avail();
        FoodUiChrome.DrawText(draw, labelPos, "Select food", Theme.TextSecondary, Theme.SecondaryScale);
        FoodUiChrome.Advance(labelPos, width, FoodUiChrome.LineHeight(Theme.SecondaryScale), Theme.Unit);

        var comboOrigin = ImGui.GetCursorScreenPos();
        var selectedFood = _plugin._service.AllFoods.FirstOrDefault(f => f.Id == job.ManualFoodItemId);
        var currentName = job.ManualFoodItemId == 0 ? "- None -" : selectedFood?.Name ?? $"Item #{job.ManualFoodItemId}";

        var open = FoodUiChrome.BeginCombo("##foodpicker", currentName, width);
        if (open)
        {
            if (ImGui.Selectable("- None -", job.ManualFoodItemId == 0))
            {
                job.ManualFoodItemId = 0;
                _changed = true;
            }
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
                    _changed = true;
                }
                if (!owned)
                    ImGui.EndDisabled();
            }
            if (filtered.Count == 0)
                ImGui.TextDisabled("No food matches this search.");
            else if (hasMore)
                ImGui.TextDisabled("First 200 matches shown; narrow the search for more.");
        }
        FoodUiChrome.EndCombo(open);
        FoodUiChrome.Advance(comboOrigin, width, FoodUiChrome.ControlHeight, 0);

        if (job.ManualFoodItemId != 0)
        {
            var hq = job.ManualFoodIsHQ;
            if (FoodUiChrome.Toggle("Prefer HQ", ref hq, "Uses NQ automatically when HQ is not in inventory.", separator: true))
            {
                job.ManualFoodIsHQ = hq;
                _changed = true;
            }
        }

        var fallback = job.FallbackToAutoSelect;
        if (FoodUiChrome.Toggle("Fall back to auto-select if unavailable", ref fallback,
                "Keeps the same manual-first automation behavior when the selected food is missing.", separator: true))
        {
            job.FallbackToAutoSelect = fallback;
            _changed = true;
        }

        var gapTop = ImGui.GetCursorScreenPos();
        FoodUiChrome.Advance(gapTop, width, 0, Theme.Unit);
        if (job.ManualFoodItemId == 0)
        {
            FoodUiChrome.Notice("Needs setup", "Choose a food. With no manual food selected, automatic selection is used until a food is chosen.", UiState.NeedsSetup, 0);
        }
        else if (selectedFood == null || (selectedFood.InventoryCount(true) == 0 && selectedFood.InventoryCount(false) == 0))
        {
            FoodUiChrome.Notice(Copy.Missing("selected food"), job.FallbackToAutoSelect
                ? "Automatic selection will be used until this food is restocked."
                : "No food will be eaten from this profile until the selected food is restocked.", UiState.Missing, 0);
        }
        else
        {
            FoodUiChrome.Notice(selectedFood.Name, $"{InventoryText(selectedFood)} in inventory.", UiState.Ready, 0);
        }
    }

    // ---- footer ----

    private static float FooterHeight(float width)
    {
        var textBlock = FoodUiChrome.LineHeight() + Theme.Unit + FoodUiChrome.LineHeight(Theme.SecondaryScale);
        const float pad = 3 * Theme.Unit;
        return pad + MathF.Max(textBlock, FoodUiChrome.ControlHeight) + pad;
    }

    private bool DrawFooter(Live live, bool masterEnabled, Vector2 winPos, Vector2 winSize, float gutter, float height)
    {
        var top = winPos.Y + winSize.Y - height;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(new Vector2(winPos.X, top), winPos + winSize, false);
        draw.AddRectFilled(new Vector2(winPos.X, top), winPos + winSize, Theme.Pack(Theme.ShellBar));
        draw.AddLine(new Vector2(winPos.X, top), new Vector2(winPos.X + winSize.X, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        var primary = $"{Copy.Word(live.State)} · {live.Action}";
        var secondary = live.Reason ?? (live.Fed
            ? $"Well Fed · {live.PillDetail} remaining"
            : "Food profiles and settings save on change.");
        var actionSize = new Vector2(MathF.Max(12 * Theme.Unit, FoodUiChrome.TextWidth("Stop") + (8 * Theme.Unit)), FoodUiChrome.ControlHeight);
        var textBlock = FoodUiChrome.LineHeight() + Theme.Unit + FoodUiChrome.LineHeight(Theme.SecondaryScale);
        var actionPos = new Vector2(winPos.X + winSize.X - gutter - actionSize.X, top + ((height - actionSize.Y) / 2f));
        var textTop = top + ((height - textBlock) / 2f);
        var textLeft = winPos.X + gutter + FoodUiChrome.Gap;
        var textRight = actionPos.X - FoodUiChrome.Gap;
        draw.AddCircleFilled(new Vector2(winPos.X + gutter + Theme.Unit, textTop + (FoodUiChrome.LineHeight() / 2f)), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(live.State)));
        var shown = FoodUiChrome.Fit(primary, textRight - textLeft);
        var shownSecondary = FoodUiChrome.Fit(secondary, textRight - textLeft, Theme.SecondaryScale);
        FoodUiChrome.DrawText(draw, new Vector2(textLeft, textTop), shown, Theme.TextPrimary);
        FoodUiChrome.DrawText(draw, new Vector2(textLeft, textTop + FoodUiChrome.LineHeight() + Theme.Unit), shownSecondary, Theme.TextSecondary, Theme.SecondaryScale);
        if ((shown != primary || shownSecondary != secondary)
            && ImGui.IsMouseHoveringRect(new Vector2(textLeft, textTop), new Vector2(textRight, textTop + textBlock)))
            ImGui.SetTooltip(primary + "\n" + secondary);

        return masterEnabled && FoodUiChrome.StopButton(draw, actionPos, actionSize);
    }

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
