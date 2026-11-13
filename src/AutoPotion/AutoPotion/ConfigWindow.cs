using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using LalaUiKit;
using Lumina.Excel.Sheets;

namespace AutoPotion;

// Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13).
// The Dalamud Window base auto-begins/ends this window (Draw() draws content only),
// so Layout.Shell is not used here; the top bar is drawn with the same kit
// primitives Shell uses, and the suite frame comes from Theme.PushFrame/PopFrame.
internal class ConfigWindow : Window
{
    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin) : base("AutoPotion##cfg")
    {
        _plugin = plugin;
        Size = new Vector2(540, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var c = _plugin.Config;
        var changed = false;

        Theme.PushFrame();

        // Per-job section. Edits the active job's profile, or the "Default" profile when
        // the player isn't on a job. Lazy-cloned from Default the first time a non-default
        // job gets edited so we don't bloat the config with untouched profiles.
        var local = Plugin.Objects.LocalPlayer;
        var jobId = local?.ClassJob.RowId ?? 0;
        var job = jobId == 0 ? c.DefaultJob : c.GetOrCreateJobSettings(jobId);
        var jobName = ResolveJobName(jobId);
        var jobIcon = ResolveJobIcon(jobId);

        // Live state (Brief R3): derived from the plugin's own config and session facts.
        var rulesOn = (job.HpPotionEnable ? 1 : 0) + (job.MpPotionEnable ? 1 : 0)
                    + (job.RegenPotionEnable ? 1 : 0) + (job.SilenceEchoDropsEnable ? 1 : 0);
        UiState state;
        string action;
        if (!c.MasterEnable)
        {
            state = UiState.NeedsSetup;
            action = "master switch off";
        }
        else if (rulesOn == 0)
        {
            state = UiState.NeedsSetup;
            action = $"no potion rules enabled for {jobName}";
        }
        else if (local == null)
        {
            state = UiState.Idle;
            action = "logged out";
        }
        else
        {
            state = UiState.Running;
            action = $"watching {jobName} thresholds";
        }

        // Top bar: title left, live-state pill right (R3 place 1) - same primitives
        // Layout.Shell draws for self-beginning windows.
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth() - (2 * Theme.Unit);
        var barH = Theme.PillHeight;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(w, barH), Theme.Pack(Theme.ShellBar), 4f);
        dl.AddRect(p, p + new Vector2(w, barH), Theme.Pack(Theme.SurfaceBorder), 4f);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(2 * Theme.Unit, (barH - ImGui.GetTextLineHeight()) / 2f));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted("AutoPotion");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(state, "AutoPotion", null, alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);

        // Section row with the live dot (R3 place 2). AutoPotion has one functional area.
        Layout.NavSection("autopotion_potions", "Potions", jobIcon, null, state, true);

        Layout.PageHeader(jobIcon, null, $"Per-job rules - {jobName}",
            "Switch jobs in-game to edit a different profile; edits clone from Default.",
            $"{rulesOn} of 4 rules on");

        using var cols = Layout.TwoColumns.Begin(0.55f);

        // Left: the potion rules for the active job.
        Widgets.Title("HP potion");
        var hpEnable = job.HpPotionEnable;
        if (Widgets.Checkbox("Auto-use HP potion", ref hpEnable))
        { job.HpPotionEnable = hpEnable; changed = true; }
        var hpThr = job.HpPotionThreshold;
        if (Widgets.SliderPercent("HP threshold", ref hpThr))
        { job.HpPotionThreshold = hpThr; changed = true; }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        Widgets.Title("MP potion (Ether)");
        var mpEnable = job.MpPotionEnable;
        if (Widgets.Checkbox("Auto-use MP potion", ref mpEnable))
        { job.MpPotionEnable = mpEnable; changed = true; }
        var mpThr = job.MpPotionThreshold;
        if (Widgets.SliderPercent("MP threshold", ref mpThr))
        { job.MpPotionThreshold = mpThr; changed = true; }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        Widgets.Title("Deep dungeon regen potion");
        Widgets.Secondary("Sustaining (PotD) / Eurekan (HoH) / Orthos (EO) / Pilgrim's. Only fires in a deep dungeon.");
        var rgEnable = job.RegenPotionEnable;
        if (Widgets.Checkbox("Auto-use regen potion", ref rgEnable))
        { job.RegenPotionEnable = rgEnable; changed = true; }
        var rgThr = job.RegenPotionThreshold;
        if (Widgets.SliderPercent("HP threshold", ref rgThr))
        { job.RegenPotionThreshold = rgThr; changed = true; }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        Widgets.Title("Silence cure");
        var silenceCure = job.SilenceEchoDropsEnable;
        if (Widgets.Checkbox("Auto-use Echo Drops when silenced", ref silenceCure))
        { job.SilenceEchoDropsEnable = silenceCure; changed = true; }
        Widgets.Secondary("Uses Echo Drops from your bags to cure Silence (status 7). Off by default.");

        cols.Split();

        // Right rail: settings and requirements (R10).
        Widgets.Title("Settings");
        var master = c.MasterEnable;
        if (Widgets.Checkbox("Enabled", ref master))
        { c.MasterEnable = master; changed = true; }

        var inCombat = c.OnlyInCombat;
        if (Widgets.Checkbox("Only fire in combat", ref inCombat))
        { c.OnlyInCombat = inCombat; changed = true; }
        var inDuty = c.OnlyInDuty;
        if (Widgets.Checkbox("Only fire in a duty", ref inDuty))
        { c.OnlyInDuty = inDuty; changed = true; }

        Widgets.InfoBox("Armed whenever the master switch is on and this job has a rule enabled. "
            + "Combat-only and duty-only gate every rule to those conditions.", UiState.Idle);

        // Diagnostics: nothing here changes what the plugin does; the one control
        // writes to the log, which deserves a plain warning.
        if (ImGui.CollapsingHeader("Diagnostics"))
        {
            var telemetry = c.DecisionTelemetry;
            if (Widgets.Checkbox("Log potion decisions", ref telemetry))
            {
                c.DecisionTelemetry = telemetry;
                // Match the /autopotion telemetry command: a fresh enable reports the
                // current state instead of deduplicating against a stale one.
                if (telemetry) PotionTelemetry.Reset();
                changed = true;
            }
            Widgets.Secondary(
                $"Writes \"{PotionTelemetry.Prefix}\" lines to the plugin log when a potion fires or a "
                + "threshold was crossed without one (no stock, on cooldown, the heal would have been "
                + "wasted). Local only - nothing leaves your PC. Same as /autopotion telemetry on|off.");
        }

        cols.Dispose();

        // Footer: live dot, state word, current action, Stop pinned right (R3 place 3).
        Layout.Footer(state, action, null, "Stop", out var stop);
        if (stop && c.MasterEnable)
        {
            c.MasterEnable = false;
            changed = true;
        }

        if (changed) _plugin.SaveConfig();
        Theme.PopFrame();
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
        // ClassJob sheet doesn't expose an Icon column in this Lumina build, but the small
        // framed job icons are reliably at 62000 + jobId for both classes and jobs.
        if (jobId == 0) return null;
        var iconId = 62000u + jobId;
        var tex = Plugin.Textures.GetFromGameIcon(new GameIconLookup(iconId));
        return tex.GetWrapOrEmpty().Handle;
    }
}
