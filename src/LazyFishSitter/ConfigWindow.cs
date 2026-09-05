using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace LazyFishSitter;

internal class ConfigWindow : Window
{
    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin) : base("Lazy Fish Sitter##cfg")
    {
        _plugin = plugin;
        Size = new Vector2(430, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var c = _plugin.Config;
        var changed = false;

        var enabled = c.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled)) { c.Enabled = enabled; changed = true; }
        ImGui.TextDisabled("Only acts while the game reports you as fishing.");

        ImGui.Separator();

        var interval = c.CheckIntervalSeconds;
        if (ImGui.SliderInt("Check interval (seconds)", ref interval, 1, 10))
        { c.CheckIntervalSeconds = interval; changed = true; }

        var cmd = c.SitCommand;
        if (ImGui.InputText("Sit command", ref cmd, 64))
        { c.SitCommand = cmd.Trim(); changed = true; }
        ImGui.TextDisabled("Default /sit. Must start with a slash.");

        ImGui.Separator();

        ImGui.TextUnformatted("Status");
        ImGui.TextDisabled($"Last check skipped: {_plugin._service.LastSkipReason}");
        var lastSit = _plugin._service.LastSitSentUtc;
        ImGui.TextDisabled($"Last sit sent: {(lastSit == DateTime.MinValue ? "never" : lastSit.ToLocalTime().ToString("HH:mm:ss"))}");

        if (changed) _plugin.SaveConfig();
    }
}
