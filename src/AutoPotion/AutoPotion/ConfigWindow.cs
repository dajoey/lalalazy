using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using LalaUiKit;
using Lumina.Excel.Sheets;

namespace AutoPotion;

internal sealed class ConfigWindow : Window
{
    private const float ControlHeight = 34f;
    private const float WideFooterHeight = 68f;
    private const float NarrowFooterHeight = 104f;

    private readonly Plugin _plugin;
    private bool _changed;

    public ConfigWindow(Plugin plugin) : base("AutoPotion##cfg")
    {
        _plugin = plugin;
        Flags = ImGuiWindowFlags.NoTitleBar;
        Size = new Vector2(1080f, 780f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420f, 540f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

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
        var jobIcon = ResolveJobIcon(jobId);
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

        Theme.PushFrame();
        try
        {
            DrawTopBar(state);
            DrawFeatureRow();
            DrawPageHeader(jobIcon, jobName, rulesOn, profileCount);

            var width = ImGui.GetContentRegionAvail().X;
            var footerHeight = FooterHeight(width);
            ImGui.BeginChild("##autopotionBody", new Vector2(0f, MathF.Max(40f, ImGui.GetContentRegionAvail().Y - footerHeight)), false);
            if (ImGui.GetContentRegionAvail().X >= 960f)
                DrawWideBody(config, job, jobName, rulesOn, profileCount, inCombat, inDuty);
            else
            {
                DrawRules(job, rulesOn, profileCount);
                DrawRail(config, jobName, rulesOn, inCombat, inDuty);
            }
            ImGui.EndChild();

            DrawFooter(state, action, jobName, rulesOn, config.MasterEnable, out var stop);
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

    private static float FooterHeight(float width) => width < 620f ? NarrowFooterHeight : WideFooterHeight;

    private static void DrawTopBar(UiState state)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        const float barHeight = 66f;
        var gap = 2f * Theme.Unit;
        var statusSize = AutoPotionChrome.StatusSize(state);
        var brandSize = new Vector2(MathF.Min(190f, MathF.Max(120f, width - statusSize.X - gap)), 42f);
        var controlY = (barHeight - ControlHeight) / 2f;

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0f, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        AutoPotionChrome.Brand(draw, origin + new Vector2(0f, 12f), brandSize);
        AutoPotionChrome.StatusPill(draw, origin + new Vector2(width - statusSize.X, controlY), statusSize, state);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private static void DrawFeatureRow()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        const float barHeight = 58f;
        var size = new Vector2(MathF.Min(width, 174f), ControlHeight);

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0f, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        AutoPotionChrome.FeatureButton(draw, "rules", "Potion rules", origin + new Vector2(0f, 8f), size, true);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private static void DrawPageHeader(ImTextureID? jobIcon, string jobName, int rulesOn, int profileCount)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var tileSize = new Vector2(42f);
        var title = $"Potion rules · {jobName}";
        const string description = "Switch jobs to edit another profile; uncustomized jobs inherit Default.";
        var count = $"{rulesOn}/4 rules";
        var subtext = $"{profileCount} job profiles";

        draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 2f * Theme.Unit);
        draw.AddRect(origin, origin + tileSize, Theme.Pack(Theme.SurfaceBorder), 2f * Theme.Unit);
        if (jobIcon is { } icon)
            draw.AddImage(icon, origin + new Vector2(4f), origin + tileSize - new Vector2(4f));
        else
            AutoPotionChrome.Diamond(draw, origin + (tileSize / 2f), 10f, Theme.Pack(Theme.Accent));

        if (width >= 680f)
        {
            var textX = origin.X + tileSize.X + 12f;
            var rightWidth = MathF.Max(ImGui.CalcTextSize(count).X, ImGui.CalcTextSize(subtext).X * Theme.CaptionScale);
            var textWidth = width - tileSize.X - 12f - rightWidth - 16f;
            DrawText(draw, new Vector2(textX, origin.Y), FitText(title, textWidth, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            DrawText(draw, new Vector2(textX, origin.Y + 29f), FitText(description, textWidth), Theme.TextSecondary);
            DrawTextRight(draw, origin.X + width, origin.Y + 4f, count, Theme.TextSecondary);
            DrawTextRight(draw, origin.X + width, origin.Y + 31f, subtext, Theme.TextSecondary, Theme.CaptionScale);
            ImGui.SetCursorScreenPos(origin + new Vector2(0f, 58f));
            ImGui.Separator();
            return;
        }

        DrawText(draw, origin + new Vector2(tileSize.X + 12f, 4f), FitText(title, width - tileSize.X - 12f, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        ImGui.SetCursorScreenPos(origin + new Vector2(0f, tileSize.Y + 8f));
        Widgets.SecondaryWrapped(description);
        Widgets.Caption(count + " · " + subtext);
        ImGui.Separator();
    }

    private void DrawWideBody(Configuration config, JobPotionSettings job, string jobName, int rulesOn, int profileCount, bool inCombat, bool inDuty)
    {
        var available = ImGui.GetContentRegionAvail().X;
        const float gap = 12f;
        var leftWidth = MathF.Floor((available - gap) * 0.68f);
        ImGui.BeginChild("##autopotionRules", new Vector2(leftWidth, 0f), false);
        DrawRules(job, rulesOn, profileCount);
        ImGui.EndChild();
        ImGui.SameLine(0f, gap);
        ImGui.BeginChild("##autopotionRail", new Vector2(0f, 0f), false);
        DrawRail(config, jobName, rulesOn, inCombat, inDuty);
        ImGui.EndChild();
    }

    private void DrawRules(JobPotionSettings job, int rulesOn, int profileCount)
    {
        DrawStatCards(
            new Stat("Active rules", rulesOn.ToString(), "/ 4", rulesOn / 4f, $"{profileCount} profiles including Default"),
            new Stat("HP trigger", $"{job.HpPotionThreshold:F0}%", null, job.HpPotionThreshold / 100f,
                job.HpPotionEnable ? "HP potion rule on" : "Threshold retained while off"),
            new Stat("MP trigger", $"{job.MpPotionThreshold:F0}%", null, job.MpPotionThreshold / 100f,
                job.MpPotionEnable ? "MP potion rule on" : "Threshold retained while off"));

        AutoPotionChrome.Card("hp_rule", () =>
        {
            var enabled = job.HpPotionEnable;
            if (AutoPotionChrome.Toggle("hp", "Auto-use HP potions", ref enabled,
                    "Uses the best usable HP potion without exceeding missing HP."))
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
                    "Uses the first usable Ether when MP reaches this threshold."))
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
                    "Sustaining, Eurekan, Orthos, or Pilgrim's potion; only in a deep dungeon."))
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
        });
    }

    private void DrawRail(Configuration config, string jobName, int rulesOn, bool inCombat, bool inDuty)
    {
        AutoPotionChrome.Card("requirements", () =>
        {
            Section("Requirements", "Live gates for the active profile");
            Requirement("Profile", jobName, UiState.Ready);
            Requirement("Rules", rulesOn > 0 ? $"{rulesOn}/4 enabled" : "Needs setup", rulesOn > 0 ? UiState.Ready : UiState.NeedsSetup);
            Requirement("Combat", !config.OnlyInCombat ? "Not gated" : inCombat ? "In combat" : "Waiting",
                !config.OnlyInCombat ? UiState.Idle : inCombat ? UiState.Ready : UiState.Blocked);
            Requirement("Duty", !config.OnlyInDuty ? "Not gated" : inDuty ? "In a duty" : "Waiting",
                !config.OnlyInDuty ? UiState.Idle : inDuty ? UiState.Ready : UiState.Blocked);
            Requirement("Regen scope", "Deep dungeons", UiState.Idle);
        });

        AutoPotionChrome.Card("settings", () =>
        {
            Section("Settings", "Global gates apply to every job profile");
            var master = config.MasterEnable;
            if (AutoPotionChrome.Toggle("master", "Enable AutoPotion", ref master,
                    "Master switch for every potion rule."))
            {
                config.MasterEnable = master;
                _changed = true;
            }
            var combat = config.OnlyInCombat;
            if (AutoPotionChrome.Toggle("combat", "Only fire in combat", ref combat,
                    "Applies to HP, MP, regen, and Echo Drops."))
            {
                config.OnlyInCombat = combat;
                _changed = true;
            }
            var duty = config.OnlyInDuty;
            if (AutoPotionChrome.Toggle("duty", "Only fire in a duty", ref duty,
                    "Adds a duty requirement without changing thresholds."))
            {
                config.OnlyInDuty = duty;
                _changed = true;
            }
        });

        AutoPotionChrome.Card("diagnostics", () =>
        {
            Section("Diagnostics", "Optional local decision logging");
            var telemetry = config.DecisionTelemetry;
            if (AutoPotionChrome.Toggle("telemetry", "Log potion decisions", ref telemetry,
                    $"Writes \"{PotionTelemetry.Prefix}\" lines when a potion fires or a crossed threshold cannot fire. " +
                    "Near-misses are rate-limited in the plugin log; diagnostic logging does not change potion decisions. " +
                    "Same setting as /autopotion telemetry on|off."))
            {
                config.DecisionTelemetry = telemetry;
                // Match the command path: a fresh enable reports the current state immediately.
                if (telemetry)
                    PotionTelemetry.Reset();
                _changed = true;
            }
        });
    }

    private static void Section(string title, string description)
    {
        Widgets.Title(title);
        Widgets.SecondaryWrapped(description);
    }

    private static void Requirement(string label, string value, UiState state)
    {
        var start = ImGui.GetCursorPos();
        var width = ImGui.GetContentRegionAvail().X;
        Widgets.StateText(state == UiState.Ready ? "✓" : "●", state);
        ImGui.SameLine();
        Widgets.Secondary(label);
        var valueWidth = ImGui.CalcTextSize(value).X * Theme.CaptionScale;
        var labelWidth = ImGui.CalcTextSize(label).X;
        if (labelWidth + valueWidth + 40f <= width)
        {
            ImGui.SetCursorPos(new Vector2(start.X + width - valueWidth, start.Y));
            Widgets.Caption(value);
        }
        else
        {
            ImGui.SetCursorPosX(start.X + 24f);
            Widgets.Caption(value);
        }
    }

    private static void DrawFooter(UiState state, string action, string jobName, int rulesOn, bool masterEnabled, out bool stop)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = FooterHeight(width);
        var draw = ImGui.GetWindowDrawList();
        var narrow = width < 620f;
        var stopSize = new Vector2(MathF.Min(112f, width), 36f);
        var stopPos = origin + new Vector2(width - stopSize.X, narrow ? 58f : 16f);
        var textRight = narrow || !masterEnabled ? origin.X + width : stopPos.X - 16f;
        var textPos = origin + new Vector2(24f, 9f);
        var primary = $"{Copy.Word(state)} · {action}";
        var secondary = masterEnabled
            ? $"{jobName}: {rulesOn} of 4 rules enabled · changes save immediately"
            : "Automation is stopped · thresholds and job profiles are preserved";

        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin, origin + new Vector2(width, 0f), Theme.Pack(Theme.SurfaceBorder));
        draw.AddCircleFilled(origin + new Vector2(10f, 19f), Theme.DotRadius + 1f, Theme.Pack(Copy.MarkColor(state)));
        DrawText(draw, textPos, FitText(primary, textRight - textPos.X), Theme.TextPrimary);
        DrawText(draw, textPos + new Vector2(0f, 27f), FitText(secondary, textRight - textPos.X), Theme.TextSecondary, Theme.CaptionScale);
        stop = masterEnabled && AutoPotionChrome.StopButton(draw, stopPos, stopSize);
        ImGui.SetCursorScreenPos(origin + new Vector2(0f, height));
    }

    private readonly record struct Stat(string Label, string Value, string? Cap, float Fraction, string Note);

    private static void DrawStatCards(params Stat[] stats)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var stacked = available < 560f;
        var gap = 3f * Theme.Unit;
        var width = stacked ? available : (available - (2f * gap)) / 3f;
        const float height = 112f;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0f, i * (height + gap)) : new Vector2(i * (width + gap), 0f));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), 2f * Theme.Unit);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 2f * Theme.Unit);
            AutoPotionChrome.Diamond(draw, pos + new Vector2(20f, 20f), 5f, Theme.Pack(Theme.Accent));

            ImGui.SetCursorScreenPos(pos + new Vector2(38f, 11f));
            Widgets.Caption(stat.Label);
            ImGui.SetCursorScreenPos(pos + new Vector2(12f, 34f));
            Widgets.Title(stat.Cap is null ? stat.Value : $"{stat.Value} {stat.Cap}");
            var min = pos + new Vector2(12f, 70f);
            var max = pos + new Vector2(width - 12f, 74f);
            draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
            if (stat.Fraction > 0f)
                draw.AddRectFilled(min, min + new Vector2((max.X - min.X) * Math.Clamp(stat.Fraction, 0f, 1f), 0f), Theme.Pack(Theme.Accent), 2f);
            ImGui.SetCursorScreenPos(pos + new Vector2(12f, 84f));
            Widgets.Caption(FitText(stat.Note, width - 24f, Theme.CaptionScale));
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(0f, stacked ? stats.Length * (height + gap) : height + gap));
    }

    private static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1f)
    {
        if (text.Length == 0)
            return;
        if (scale == 1f)
            draw.AddText(pos, Theme.Pack(color), text);
        else
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    private static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1f)
    {
        if (text.Length == 0)
            return;
        var textWidth = ImGui.CalcTextSize(text).X * scale;
        DrawText(draw, new Vector2(right - textWidth, y), text, color, scale);
    }

    private static string FitText(string text, float width, float scale = 1f)
    {
        if (width <= 0f)
            return "";
        if (ImGui.CalcTextSize(text).X * scale <= width)
            return text;
        const string ellipsis = "…";
        var length = text.Length;
        while (length > 0 && ImGui.CalcTextSize(text[..length] + ellipsis).X * scale > width)
            length--;
        return length == 0 ? "" : text[..length].TrimEnd() + ellipsis;
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
        // ClassJob does not expose Icon in this Lumina build; framed job icons use this stable offset.
        if (jobId == 0)
            return null;
        var iconId = 62000u + jobId;
        var texture = Plugin.Textures.GetFromGameIcon(new GameIconLookup(iconId));
        return texture.GetWrapOrEmpty().Handle;
    }
}
