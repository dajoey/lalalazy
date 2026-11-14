using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace LazyFishSitter;

internal class ConfigWindow : Window
{
    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin) : base("Lazy Fish Sitter##cfg")
    {
        _plugin = plugin;
        Size = new Vector2(460, 380);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var c = _plugin.Config;
        var changed = false;

        var enabled = c.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled)) { c.Enabled = enabled; changed = true; }
        ImGui.TextDisabled("Sits you down while fishing: at the standby beat with the rod out,");
        ImGui.TextDisabled("or once the line settles in the water. Re-arms if you stand again");
        ImGui.TextDisabled("(putting the rod away or manually changing position).");

        ImGui.Separator();

        var cmd = c.SitCommand;
        if (ImGui.InputText("Sit command", ref cmd, 64))
        { c.SitCommand = cmd.Trim(); changed = true; }
        ImGui.TextDisabled("Default /sit. Must start with a slash.");

        ImGui.Separator();

        ImGui.TextUnformatted("This fishing trip");
        var s = _plugin._service;
        ImGui.TextDisabled($"At a fishing hole: {(s.TripActive ? "yes" : "no")}");
        ImGui.TextDisabled($"Sits sent this trip: {s.SendsThisTrip}");
        ImGui.TextDisabled($"Seated: {(s.SitBelieved ? "yes - leaving you alone" : "no")}");
        ImGui.TextDisabled($"Seated read works with the rod out: {(s.SeatedReadWorksWithRodOut ? "yes" : "not seen yet")}");
        var lastSit = s.LastSitSentUtc;
        ImGui.TextDisabled($"Last sit sent: {(lastSit == DateTime.MinValue ? "never" : lastSit.ToLocalTime().ToString("HH:mm:ss"))}");
        ImGui.TextDisabled($"Last check: {s.LastSkipReason}");

        if (changed) _plugin.SaveConfig();
    }
}
