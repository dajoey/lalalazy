using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace LazyFishSitter;

// Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13).
// The Dalamud Window base auto-begins/ends this window (Draw() draws content only),
// so Layout.Shell is not used here; the top bar is drawn with the same kit
// primitives Shell uses, and the suite frame comes from Theme.PushFrame/PopFrame.
internal class ConfigWindow : Window
{
    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin) : base("Lazy Fish Sitter##cfg")
    {
        _plugin = plugin;
        Size = new Vector2(540, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var c = _plugin.Config;
        var s = _plugin._service;
        var changed = false;

        Theme.PushFrame();

        // Live state (Brief R3/R5): derived from the plugin's own config and the
        // same game conditions the service checks before sending a sit.
        UiState state;
        string action;
        string? reason = null;
        var block = SitBlockReason();
        if (!c.Enabled)
        {
            state = UiState.NeedsSetup;
            action = "plugin is off - enable it to auto-sit";
        }
        else if (!s.TripActive)
        {
            state = UiState.Idle;
            action = "waiting for a fishing hole";
        }
        else if (s.SitBelieved)
        {
            state = UiState.Ready;
            action = "seated - leaving you alone this trip";
        }
        else if (block != null)
        {
            state = UiState.Blocked;
            action = "holding the sit";
            reason = block;
        }
        else
        {
            state = UiState.Ready;
            action = "armed - sits at the beat or when the line settles";
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
        ImGui.TextUnformatted("Lazy Fish Sitter");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(state, "Fish sitter", null, alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);

        // Section row with the live dot (R3 place 2). One functional area.
        Layout.NavSection("lazyfishsitter_fish", "Fish sitter", null, "FS", state, true);

        Layout.PageHeader(null, "FS", "Fish sitting",
            "Sits you down at the standby beat or once the line settles, and re-arms if you stand.",
            $"{s.SendsThisTrip} of {FishSitService.MaxSends} sits sent");

        using var cols = Layout.TwoColumns.Begin(0.55f);

        // Left: this trip's numbers and live facts (R6, R11).
        Widgets.StatCard("Sits sent this trip", s.SendsThisTrip.ToString(),
            FishSitService.MaxSends.ToString(), s.SendsThisTrip / (float)FishSitService.MaxSends,
            s.LastSitSentUtc == DateTime.MinValue
                ? "none sent yet"
                : $"last sent {s.LastSitSentUtc.ToLocalTime():HH:mm:ss}",
            240);

        ImGui.NewLine();
        Widgets.Title("This trip");
        Widgets.Secondary($"At a fishing hole: {(s.TripActive ? "yes" : "no")}");
        Widgets.Secondary(s.SitBelieved ? "Seated: yes - leaving you alone" : "Seated: no");
        Widgets.Secondary($"Seated read works with the rod out: {(s.SeatedReadWorksWithRodOut ? "yes" : "not seen yet")}");
        Widgets.Secondary($"Last check: {s.LastSkipReason}");

        cols.Split();

        // Right rail: settings (R10).
        Widgets.Title("Settings");
        var enabled = c.Enabled;
        if (Widgets.Checkbox("Enabled", ref enabled))
        { c.Enabled = enabled; changed = true; }
        Widgets.SecondaryWrapped("Sits you down while fishing: at the standby beat with the rod out, or once the line settles in the water. Re-arms if you stand again (putting the rod away or manually changing position).");

        var cmd = c.SitCommand;
        if (ImGui.InputText("Sit command", ref cmd, 64))
        { c.SitCommand = cmd.Trim(); changed = true; }
        Widgets.Secondary("Default /sit. Must start with a slash.");

        Widgets.InfoBox("Runs every frame while you fish; the policy rate-limits sends (10 s apart, 3 a trip). The Stop button below is the same as unticking Enabled.", UiState.Idle);

        cols.Dispose();

        // Footer: live dot, state word, current action, Stop pinned right (R3 place 3).
        Layout.Footer(state, action, reason, "Stop", out var stop);
        if (stop && c.Enabled)
        {
            c.Enabled = false;
            changed = true;
        }

        if (changed) _plugin.SaveConfig();
        Theme.PopFrame();
    }

    /// <summary>Game conditions where injecting a command would land somewhere unexpected
    /// (the display-side read of the same list FishSitService checks; UI only).</summary>
    private static string? SitBlockReason()
    {
        if (Plugin.Condition[ConditionFlag.BetweenAreas]) return "between areas";
        if (Plugin.Condition[ConditionFlag.OccupiedInEvent]) return "occupied in event";
        if (Plugin.Condition[ConditionFlag.OccupiedInQuestEvent]) return "occupied in quest event";
        if (Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]) return "occupied in cutscene event";
        if (Plugin.Condition[ConditionFlag.WatchingCutscene]) return "watching cutscene";
        if (Plugin.Condition[ConditionFlag.InCombat]) return "in combat";
        if (Plugin.Condition[ConditionFlag.Emoting]) return "mid-emote";
        if (Plugin.Condition[ConditionFlag.Jumping]) return "jumping";
        return null;
    }
}
