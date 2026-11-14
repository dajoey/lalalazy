using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace LazyRetainerLive;

// Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13).
// The Dalamud Window base auto-begins/ends this window (Draw() draws content only),
// so Layout.Shell is not used here; the top bar is drawn with the same kit
// primitives Shell uses, and the suite frame comes from Theme.PushFrame/PopFrame.
internal class ConfigWindow : Window
{
    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin) : base("LazyRetainerLive##cfg")
    {
        _plugin = plugin;
        Size = new Vector2(540, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var c = _plugin.Config;
        var changed = false;

        Theme.PushFrame();

        // "Have a snapshot" and "that snapshot is current" are different things:
        // the service deliberately keeps the last good snapshot published across
        // logout/zone transitions, so Current != null stays true at the title
        // screen. Distinguish them from LastTickOk instead of claiming "live".
        // Live state (Brief R3), with the plugin's own three status texts kept 1:1.
        var snap = _plugin.State?.Current;
        var fresh = _plugin.State?.LastTickOk == true;
        var httpError = _plugin.State != null ? _plugin.HttpError : null;
        UiState state;
        string action;
        if (!string.IsNullOrEmpty(httpError))
        {
            state = UiState.Failed;
            action = $"Listener error: {httpError}";
        }
        else if (snap == null)
        {
            state = UiState.NeedsSetup;
            action = "no snapshot yet (not logged in since load)";
        }
        else if (fresh)
        {
            state = UiState.Ready;
            action = "serving live snapshot";
        }
        else
        {
            state = UiState.Blocked;
            action = "serving LAST-KNOWN snapshot (game in transition / logged out)";
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
        ImGui.TextUnformatted("LazyRetainerLive");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(state, "LazyRetainerLive", null, alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);

        // Section row with the live dot (R3 place 2). One functional area.
        Layout.NavSection("retainerlive_serving", "Serving", null, null, state, true);

        Layout.PageHeader(null, null, "Retainer live state",
            "Serves GET http://127.0.0.1:<port>/retainers for the ffxiv dashboard relay.",
            c.Enabled ? $"port {c.Port}" : "listener off");

        using var cols = Layout.TwoColumns.Begin(0.55f);

        // Left: what the endpoint serves right now.
        Widgets.Title("Endpoint");
        Widgets.Secondary(
            "Reads the logged-in character's LIVE retainer table (the same in-memory data AutoRetainer uses) and serves it read-only as JSON for the dashboard's retainer panel.");
        if (!string.IsNullOrEmpty(httpError))
            Widgets.InfoBox($"Listener error: {httpError}", UiState.Failed);

        cols.Split();

        // Right rail: settings (R10). Every change saves immediately, as before.
        Widgets.Title("Settings");
        var enabled = c.Enabled;
        if (Widgets.Checkbox("Enabled (serve live retainer data on loopback)", ref enabled))
        { c.Enabled = enabled; changed = true; }

        var port = c.Port;
        if (ImGui.InputInt("Loopback port", ref port))
        {
            c.Port = Math.Clamp(port, 1024, 65535);
            changed = true;
        }
        Widgets.Secondary(
            "Loopback only - never reachable from other machines. Port changes apply after the plugin reloads (or /xlreload).");

        // Deliberate freeze (v0.1.0.1): once logged in, the endpoint keeps serving
        // the character's last known retainer state - including at the title screen.
        Widgets.InfoBox(
            "Once logged in, the endpoint keeps serving the character's last known retainer state - including at the title screen - so the dashboard never falls back to stale data mid-session.",
            UiState.Idle);

        cols.Dispose();

        // Footer: live dot, state word, current action, Stop pinned right (R3 place 3).
        Layout.Footer(state, action, null, "Stop", out var stop);
        if (stop && c.Enabled)
        {
            c.Enabled = false;
            changed = true;
        }

        if (changed) _plugin.SaveConfig();
        Theme.PopFrame();
    }
}
