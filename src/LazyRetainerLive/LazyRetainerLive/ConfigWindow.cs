using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace LazyRetainerLive;

internal class ConfigWindow : Window
{
    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin) : base("LazyRetainerLive##cfg")
    {
        _plugin = plugin;
        Size = new Vector2(420, 150);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var c = _plugin.Config;
        var changed = false;

        var enabled = c.Enabled;
        if (ImGui.Checkbox("Enabled (serve live retainer data on loopback)", ref enabled))
        { c.Enabled = enabled; changed = true; }

        var port = c.Port;
        if (ImGui.InputInt("Loopback port", ref port))
        {
            c.Port = Math.Clamp(port, 1024, 65535);
            changed = true;
        }
        ImGui.TextDisabled("Serves GET http://127.0.0.1:<port>/retainers for the ffxiv dashboard relay.");
        ImGui.TextDisabled("Loopback only - never reachable from other machines. Port changes");
        ImGui.TextDisabled("apply after the plugin reloads (or /xlreload).");

        ImGui.Spacing();
        ImGui.Separator();
        // "Have a snapshot" and "that snapshot is current" are different things:
        // the service deliberately keeps the last good snapshot published across
        // logout/zone transitions, so Current != null stays true at the title
        // screen. Distinguish them from LastTickOk instead of claiming "live".
        var snap = _plugin.State?.Current;
        var fresh = _plugin.State?.LastTickOk == true;
        var status = snap == null
            ? "no snapshot yet (not logged in since load)"
            : fresh
                ? "serving live snapshot"
                : "serving LAST-KNOWN snapshot (game in transition / logged out)";
        ImGui.TextUnformatted($"Status: {status}");
        if (_plugin.State != null && !string.IsNullOrEmpty(_plugin.HttpError))
            ImGui.TextDisabled($"Listener error: {_plugin.HttpError}");

        if (changed)
            _plugin.SaveConfig();
    }
}
