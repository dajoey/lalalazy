using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using LalaUiKit;
using Lumina.Excel.Sheets;

namespace AutoPotion;

internal sealed class ConfigWindow : Window
{
    private const float ControlHeight = 36f;
    private const float TopBarHeight = 68f;
    private const float ColumnsBreakpoint = 900f;

    private readonly Plugin _plugin;
    private bool _changed;

    public ConfigWindow(Plugin plugin) : base("AutoPotion##cfg")
    {
        _plugin = plugin;
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        Size = new Vector2(1080f, 780f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420f, 540f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    // The shell draws its own full-width bands from the window edge, so the window itself has no padding.
    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    public override void PostDraw() => ImGui.PopStyleVar();

    /// <summary>Side gutter shared by the bars, the page header, the cards and the footer.</summary>
    internal static float Gutter(float width) => width >= 760f ? 24f : width >= 560f ? 16f : 12f;

    /// <summary>Below this size the page header scrolls with the body so the cards keep the room.</summary>
    internal static bool IsCompact(Vector2 size) => size.X < 560f || size.Y < 620f;

    public override void Draw()
    {
        var config = _plugin.Config;
        _changed = false;

        // Read and write the same active-job profile as automation and hub quick controls.
        // Non-default profiles retain the existing lazy clone-from-Default semantics.
        var local = Plugin.Objects.LocalPlayer;
        var jobId = local?.ClassJob.RowId ?? 0;
        var job = jobId == 0 ? config.DefaultJob : config.GetOrCreateJobSettings(jobId);
        var jobName = ResolveJobName(jobId);
        var rulesOn = CountRules(job);
        var profileCount = config.Jobs.Count + 1;
        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty];

        UiState state;
        string action;
        if (!config.MasterEnable)
        {
            state = UiState.Idle;
            action = "Automation stopped";
        }
        else if (rulesOn == 0)
        {
            state = UiState.NeedsSetup;
            action = "Enable a rule for this profile";
        }
        else if (local == null)
        {
            state = UiState.Idle;
            action = "Waiting for a character";
        }
        else if (local.IsDead || local.MaxHp == 0)
        {
            state = UiState.Idle;
            action = "Waiting for a living character";
        }
        else if (config.OnlyInCombat && !inCombat)
        {
            state = UiState.Idle;
            action = "Waiting for combat";
        }
        else if (config.OnlyInDuty && !inDuty)
        {
            state = UiState.Idle;
            action = "Waiting for a duty";
        }
        else
        {
            state = UiState.Running;
            action = $"Watching {jobName} thresholds";
        }

        AutoPotionChrome.Reset();
        Theme.PushFrame();
        try
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var draw = ImGui.GetWindowDrawList();
            var gutter = Gutter(winSize.X);
            var compact = IsCompact(winSize);
            var footerHeight = FooterHeight(winSize.X, config.MasterEnable);

            var y = DrawTopBar(draw, winPos, winSize.X, gutter, state);
            if (!compact)
            {
                y += DrawPageHeader(draw, jobName, rulesOn, profileCount, winPos.X + gutter, winPos.Y + y, winSize.X - (2f * gutter), winSize.X);
            }

            ImGui.SetCursorPos(new Vector2(0f, y));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(gutter, 16f));
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 10f);
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f);
            ImGui.PushStyleColor(ImGuiCol.Border, 0u);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
            ImGui.BeginChild("##autopotionBody", new Vector2(winSize.X, MathF.Max(40f, winSize.Y - footerHeight - y)), true);
            DrawBody(config, job, jobName, rulesOn, profileCount, inCombat, inDuty, compact, gutter);
            DrawScrollFades();
            ImGui.EndChild();
            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar(5);

            DrawFooter(draw, winPos, winSize, gutter, footerHeight, state, action, jobName, rulesOn, config.MasterEnable, out var stop);
            if (stop && config.MasterEnable)
            {
                config.MasterEnable = false;
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

    private static int CountRules(JobPotionSettings job) =>
        (job.HpPotionEnable ? 1 : 0) + (job.MpPotionEnable ? 1 : 0)
        + (job.RegenPotionEnable ? 1 : 0) + (job.SilenceEchoDropsEnable ? 1 : 0);

    private const float StackedFooterWidth = 620f;

    private static float FooterHeight(float width, bool hasAction)
    {
        var block = AutoPotionChrome.LineHeight() + 4f + AutoPotionChrome.LineHeight(Theme.SecondaryScale);
        return width < StackedFooterWidth && hasAction
            ? 12f + block + 12f + ControlHeight + 12f
            : 12f + MathF.Max(block, ControlHeight) + 12f;
    }

    private float DrawTopBar(ImDrawListPtr draw, Vector2 winPos, float width, float gutter, UiState state)
    {
        draw.AddRectFilled(winPos, winPos + new Vector2(width, TopBarHeight), Theme.Pack(Theme.ShellBar));
        draw.PushClipRect(winPos, winPos + new Vector2(width, TopBarHeight + 1f), false);
        draw.AddLine(winPos + new Vector2(0f, TopBarHeight), winPos + new Vector2(width, TopBarHeight), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        var closeSize = new Vector2(ControlHeight);
        var statusSize = new Vector2(AutoPotionChrome.StatusWidth(), ControlHeight);
        var controlY = winPos.Y + ((TopBarHeight - ControlHeight) / 2f);
        var closePos = new Vector2(winPos.X + width - gutter - closeSize.X, controlY);
        var statusPos = new Vector2(closePos.X - 8f - statusSize.X, controlY);
        var brandWidth = MathF.Min(AutoPotionChrome.BrandWidth(), statusPos.X - 8f - (winPos.X + gutter));
        var brandSize = new Vector2(MathF.Max(120f, brandWidth), 40f);

        AutoPotionChrome.Brand(draw, new Vector2(winPos.X + gutter, winPos.Y + ((TopBarHeight - brandSize.Y) / 2f)), brandSize);
        AutoPotionChrome.StatusPill(draw, statusPos, statusSize, state);
        if (AutoPotionChrome.CloseButton(draw, closePos, closeSize))
            IsOpen = false;
        return TopBarHeight + 1f;
    }

    /// <summary>
    ///     Icon tile, title, description and counts. Returns the height used, including 20 px above and 8 px below.
    ///     Wide windows put the counts at the right; narrower ones put them under the description.
    /// </summary>
    private static float DrawPageHeader(ImDrawListPtr draw, string jobName, int rulesOn, int profileCount,
        float left, float top, float innerWidth, float windowWidth)
    {
        const float tile = 44f;
        var origin = new Vector2(left, top + 20f);
        var wide = windowWidth >= 760f;
        var title = $"Potion rules · {jobName}";
        const string description = "Switch jobs to edit another profile; uncustomized jobs inherit Default.";
        var count = $"{rulesOn}/4 rules";
        var profiles = profileCount == 1 ? "1 job profile" : $"{profileCount} job profiles";

        draw.AddRectFilled(origin, origin + new Vector2(tile), Theme.Pack(AutoPotionChrome.Alpha(Theme.Accent, 0.14f)), AutoPotionChrome.Radius);
        AutoPotionChrome.Diamond(draw, origin + new Vector2(tile / 2f), 10f, Theme.Pack(Theme.Accent));

        var textX = left + 60f;
        var metaWidth = wide
            ? MathF.Max(AutoPotionChrome.TextWidth(count, Theme.SecondaryScale), AutoPotionChrome.TextWidth(profiles, Theme.SecondaryScale))
            : 0f;
        metaWidth = MathF.Min(metaWidth, windowWidth * 0.4f);
        var textWidth = innerWidth - 60f - (wide ? metaWidth + 16f : 0f);

        var shownTitle = AutoPotionChrome.ClampLines(title, textWidth, 1.35f, wide ? 1 : 2);
        var titleHeight = AutoPotionChrome.DrawWrapped(draw, new Vector2(textX, origin.Y), shownTitle, textWidth, Theme.TextPrimary, 1.35f);
        var titleLine = AutoPotionChrome.LineHeight(1.35f);
        var descY = origin.Y + titleHeight + 4f;
        var shownDescription = AutoPotionChrome.ClampLines(description, textWidth, Theme.SecondaryScale, wide ? 1 : 3);
        var descHeight = AutoPotionChrome.DrawWrapped(draw, new Vector2(textX, descY), shownDescription, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        var blockHeight = titleHeight + 4f + descHeight;

        if (wide)
        {
            var right = left + innerWidth;
            var secondaryLine = AutoPotionChrome.LineHeight(Theme.SecondaryScale);
            AutoPotionChrome.DrawTextRight(draw, right, origin.Y + ((titleLine - secondaryLine) / 2f), AutoPotionChrome.Fit(count, metaWidth, Theme.SecondaryScale),
                Theme.TextSecondary, Theme.SecondaryScale);
            AutoPotionChrome.DrawTextRight(draw, right, descY, AutoPotionChrome.Fit(profiles, metaWidth, Theme.SecondaryScale),
                Theme.TextSecondary, Theme.SecondaryScale);
        }
        else
        {
            var meta = $"{count} · {profiles}";
            var metaHeight = AutoPotionChrome.DrawWrapped(draw, new Vector2(textX, origin.Y + blockHeight + 4f), meta, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
            blockHeight += 4f + metaHeight;
        }

        return 20f + MathF.Max(tile, blockHeight) + 8f;
    }

    private void DrawBody(Configuration config, JobPotionSettings job, string jobName, int rulesOn, int profileCount,
        bool inCombat, bool inDuty, bool compact, float gutter)
    {
        if (compact)
        {
            // Scrolls away with the cards: the header, then the body padding.
            var childPos = ImGui.GetWindowPos();
            var width = ImGui.GetWindowWidth();
            var top = ImGui.GetCursorScreenPos().Y - 16f;
            var draw = ImGui.GetWindowDrawList();
            // The child clips to its padded inner rect; widen the clip to the whole child so the band is full bleed.
            var childSize = ImGui.GetWindowSize();
            var scrollbar = ImGui.GetScrollMaxY() > 0f ? ImGui.GetStyle().ScrollbarSize : 0f;
            draw.PushClipRect(childPos, childPos + childSize, false);
            var used = DrawPageHeader(draw, jobName, rulesOn, profileCount, childPos.X + gutter, top, width - (2f * gutter) - scrollbar, width);
            draw.PopClipRect();
            ImGui.SetCursorScreenPos(new Vector2(childPos.X + gutter, top + used + 16f));
            ImGui.Dummy(new Vector2(1f, 0f));
        }

        AutoPotionChrome.Columns("page", () => DrawRules(job, rulesOn, profileCount, config.MasterEnable), () => DrawRail(config, jobName, rulesOn, inCombat, inDuty), ColumnsBreakpoint);
    }

    /// <summary>Soft edges where the body is cut by the bars: a fade at the bottom, and at the top once scrolled.</summary>
    private static void DrawScrollFades()
    {
        var max = ImGui.GetScrollMaxY();
        if (max <= 0f)
            return;
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var bg = ImGui.GetColorU32(ImGuiCol.WindowBg);
        var solid = bg | 0xFF000000u;
        var clear = bg & 0x00FFFFFFu;
        var scroll = ImGui.GetScrollY();
        if (scroll < max - 1f)
            draw.AddRectFilledMultiColor(pos + new Vector2(0f, size.Y - 28f), pos + size, clear, clear, solid, solid);
        if (scroll > 1f)
            draw.AddRectFilledMultiColor(pos, pos + new Vector2(size.X, 16f), solid, solid, clear, clear);
    }

    private void DrawRules(JobPotionSettings job, int rulesOn, int profileCount, bool masterEnabled)
    {
        AutoPotionChrome.Dim = !masterEnabled;
        try
        {
            DrawRuleCards(job, rulesOn, profileCount);
        }
        finally
        {
            AutoPotionChrome.Dim = false;
        }
    }

    private void DrawRuleCards(JobPotionSettings job, int rulesOn, int profileCount)
    {
        DrawStatCards(
            new Stat("Active rules", rulesOn.ToString(), "/ 4", rulesOn / 4f,
                profileCount == 1 ? "1 profile incl. Default" : $"{profileCount} profiles incl. Default"),
            new Stat("HP trigger", $"{job.HpPotionThreshold:F0}%", null, job.HpPotionThreshold / 100f,
                job.HpPotionEnable ? "HP potion rule on" : "Threshold kept while off"),
            new Stat("MP trigger", $"{job.MpPotionThreshold:F0}%", null, job.MpPotionThreshold / 100f,
                job.MpPotionEnable ? "MP potion rule on" : "Threshold kept while off"));

        AutoPotionChrome.Card("hp_rule", () =>
        {
            var enabled = job.HpPotionEnable;
            if (AutoPotionChrome.Toggle("hp", "Auto-use HP potions", ref enabled,
                    "Uses the best usable HP potion without exceeding missing HP.", more: true))
            {
                job.HpPotionEnable = enabled;
                _changed = true;
            }
            var threshold = job.HpPotionThreshold;
            if (AutoPotionChrome.ThresholdSlider("hp", "HP threshold", ref threshold))
            {
                job.HpPotionThreshold = threshold;
                _changed = true;
            }
        });

        AutoPotionChrome.Card("mp_rule", () =>
        {
            var enabled = job.MpPotionEnable;
            if (AutoPotionChrome.Toggle("mp", "Auto-use MP potions (Ether)", ref enabled,
                    "Uses the first usable Ether when MP reaches this threshold.", more: true))
            {
                job.MpPotionEnable = enabled;
                _changed = true;
            }
            var threshold = job.MpPotionThreshold;
            if (AutoPotionChrome.ThresholdSlider("mp", "MP threshold", ref threshold))
            {
                job.MpPotionThreshold = threshold;
                _changed = true;
            }
        });

        AutoPotionChrome.Card("regen_rule", () =>
        {
            var enabled = job.RegenPotionEnable;
            if (AutoPotionChrome.Toggle("regen", "Auto-use deep dungeon regen potions", ref enabled,
                    "Sustaining, Eurekan, Orthos, or Pilgrim's potion; only in a deep dungeon.", more: true))
            {
                job.RegenPotionEnable = enabled;
                _changed = true;
            }
            var threshold = job.RegenPotionThreshold;
            if (AutoPotionChrome.ThresholdSlider("regen", "HP threshold", ref threshold))
            {
                job.RegenPotionThreshold = threshold;
                _changed = true;
            }
        });

        AutoPotionChrome.Card("silence_rule", () =>
        {
            var enabled = job.SilenceEchoDropsEnable;
            if (AutoPotionChrome.Toggle("silence", "Auto-use Echo Drops when silenced", ref enabled,
                    "Uses Echo Drops from inventory to cure Silence (status 7). Off by default."))
            {
                job.SilenceEchoDropsEnable = enabled;
                _changed = true;
            }
        }, fill: true);
    }

    private void DrawRail(Configuration config, string jobName, int rulesOn, bool inCombat, bool inDuty)
    {
        AutoPotionChrome.Card("requirements", () =>
        {
            AutoPotionChrome.Section("Requirements", "Live gates for the active profile");
            AutoPotionChrome.Requirement("Profile", jobName, UiState.Ready);
            AutoPotionChrome.Requirement("Rules", rulesOn > 0 ? $"{rulesOn}/4 enabled" : "Needs setup", rulesOn > 0 ? UiState.Ready : UiState.NeedsSetup);
            AutoPotionChrome.Requirement("Combat", !config.OnlyInCombat ? "Not gated" : inCombat ? "In combat" : "Waiting",
                !config.OnlyInCombat ? UiState.Idle : inCombat ? UiState.Ready : UiState.Blocked);
            AutoPotionChrome.Requirement("Duty", !config.OnlyInDuty ? "Not gated" : inDuty ? "In a duty" : "Waiting",
                !config.OnlyInDuty ? UiState.Idle : inDuty ? UiState.Ready : UiState.Blocked);
            AutoPotionChrome.Requirement("Regen scope", "Deep dungeons", UiState.Idle);
        });

        AutoPotionChrome.Card("settings", () =>
        {
            AutoPotionChrome.Section("Settings", "Global gates apply to every job profile");
            var master = config.MasterEnable;
            if (AutoPotionChrome.Toggle("master", "Enable AutoPotion", ref master,
                    "Master switch for every potion rule.", more: true))
            {
                config.MasterEnable = master;
                _changed = true;
            }
            var combat = config.OnlyInCombat;
            if (AutoPotionChrome.Toggle("combat", "Only fire in combat", ref combat,
                    "Applies to HP, MP, regen, and Echo Drops.", separator: true, more: true))
            {
                config.OnlyInCombat = combat;
                _changed = true;
            }
            var duty = config.OnlyInDuty;
            if (AutoPotionChrome.Toggle("duty", "Only fire in a duty", ref duty,
                    "Adds a duty requirement without changing thresholds.", separator: true))
            {
                config.OnlyInDuty = duty;
                _changed = true;
            }
        });

        AutoPotionChrome.Card("diagnostics", () =>
        {
            AutoPotionChrome.Section("Diagnostics", "Optional local decision logging");
            var telemetry = config.DecisionTelemetry;
            if (AutoPotionChrome.Toggle("telemetry", "Log potion decisions", ref telemetry,
                    $"Writes \"{PotionTelemetry.Prefix}\" lines when a potion fires or a crossed threshold cannot fire. " +
                    "Near-misses are rate-limited in the plugin log; diagnostic logging does not change potion decisions. " +
                    "Same setting as the /autopotion telemetry command."))
            {
                config.DecisionTelemetry = telemetry;
                // Match the command path: a fresh enable reports the current state immediately.
                if (telemetry)
                    PotionTelemetry.Reset();
                _changed = true;
            }
        }, fill: true);
    }

    private static void DrawFooter(ImDrawListPtr draw, Vector2 winPos, Vector2 winSize, float gutter, float height, UiState state,
        string action, string jobName, int rulesOn, bool masterEnabled, out bool stop)
    {
        var top = winPos.Y + winSize.Y - height;
        var primary = $"{Copy.Word(state)} · {action}";
        var secondary = masterEnabled
            ? $"{jobName}: {rulesOn} of 4 rules enabled · changes save immediately"
            : "Automation is stopped · thresholds and job profiles are preserved";
        var secondaryShort = masterEnabled
            ? $"{jobName}: {rulesOn} of 4 rules on"
            : "Profiles and thresholds are kept";
        var stacked = masterEnabled && winSize.X < StackedFooterWidth;
        var stopSize = AutoPotionChrome.StopSize();
        if (stacked)
            stopSize.X = winSize.X - (2f * gutter);
        var right = winPos.X + winSize.X - gutter;
        var textLeft = winPos.X + gutter + 16f;
        var textRight = masterEnabled && !stacked ? right - stopSize.X - 16f : right;
        var lineHeight = AutoPotionChrome.LineHeight();
        var block = lineHeight + 4f + AutoPotionChrome.LineHeight(Theme.SecondaryScale);
        var blockTop = stacked ? top + 12f : top + ((height - block) / 2f);

        draw.AddRectFilled(new Vector2(winPos.X, top), winPos + winSize, Theme.Pack(Theme.ShellBar));
        draw.AddLine(new Vector2(winPos.X, top), new Vector2(winPos.X + winSize.X, top), Theme.Pack(Theme.SurfaceBorder));
        draw.AddCircleFilled(new Vector2(winPos.X + gutter + 4f, blockTop + (lineHeight / 2f)), 4f, Theme.Pack(Copy.MarkColor(state)));

        // Narrow windows use the short second line instead of cutting the long one.
        if (AutoPotionChrome.TextWidth(secondary, Theme.SecondaryScale) > textRight - textLeft)
            secondary = secondaryShort;
        var shownPrimary = AutoPotionChrome.Fit(primary, textRight - textLeft);
        AutoPotionChrome.DrawText(draw, new Vector2(textLeft, blockTop), shownPrimary, Theme.TextPrimary);
        var secondaryY = blockTop + lineHeight + 4f;
        var shownSecondary = AutoPotionChrome.Fit(secondary, textRight - textLeft, Theme.SecondaryScale);
        AutoPotionChrome.DrawText(draw, new Vector2(textLeft, secondaryY), shownSecondary, Theme.TextSecondary, Theme.SecondaryScale);
        if (shownPrimary != primary || shownSecondary != secondary)
            AutoPotionChrome.HoverTip(new Vector2(textLeft, blockTop), new Vector2(textRight, blockTop + block), primary + "\n" + secondary);

        stop = false;
        if (masterEnabled)
        {
            var stopPos = stacked
                ? new Vector2(winPos.X + gutter, top + 12f + block + 12f)
                : new Vector2(right - stopSize.X, top + ((height - stopSize.Y) / 2f));
            stop = AutoPotionChrome.StopButton(draw, stopPos, stopSize);
        }
        ImGui.SetCursorScreenPos(winPos + new Vector2(0f, winSize.Y - 1f));
        ImGui.Dummy(new Vector2(1f, 0f));
    }

    private readonly record struct Stat(string Label, string Value, string? Cap, float? Fraction, string Note);

    private static void DrawStatCards(params Stat[] stats)
    {
        var available = AutoPotionChrome.Avail();
        var compact = available < 560f;
        const float gap = 16f;
        var width = compact ? available : (available - (2f * gap)) / 3f;
        var noteHeight = 0f;
        foreach (var stat in stats)
            noteHeight = MathF.Max(noteHeight, AutoPotionChrome.StatNoteHeight(width, stat.Note));
        var height = AutoPotionChrome.StatHeight(compact, noteHeight);
        var rowGap = compact ? 8f : gap;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (compact ? new Vector2(0f, i * (height + rowGap)) : new Vector2(i * (width + gap), 0f));
            AutoPotionChrome.StatCard(draw, pos, width, height, compact, stat.Label, stat.Value, stat.Cap, stat.Fraction, stat.Note);
        }

        var total = compact ? (stats.Length * height) + ((stats.Length - 1) * rowGap) : height;
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(available, total + gap - Theme.ItemSpacing.Y));
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
                return TitleCase(name);
        }
        return $"Job {jobId}";
    }

    /// <summary>The game's sheet stores job names in lower case ("dark knight"); show each word capitalised.</summary>
    private static string TitleCase(string name)
    {
        var words = name.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
                words[i] = char.ToUpper(words[i][0]) + words[i][1..];
        }
        return string.Join(' ', words);
    }
}
