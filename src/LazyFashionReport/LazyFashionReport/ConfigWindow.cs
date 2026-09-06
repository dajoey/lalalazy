using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace LazyFashionReport;

internal class ConfigWindow : Window
{
    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin) : base("LazyFashionReport Settings##cfg")
    {
        _plugin = plugin;
        Size = new Vector2(420, 260);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var c = _plugin.Config;

        var autoOpen = c.AutoOpen;
        if (ImGui.Checkbox("Open automatically when the Fashion Report opens", ref autoOpen))
        { c.AutoOpen = autoOpen; _plugin.SaveConfig(); }

        var filterOwned = c.FilterOwned;
        if (ImGui.Checkbox("Only show candidates I own (bags, dresser, armoire)", ref filterOwned))
        { c.FilterOwned = filterOwned; _plugin.SaveConfig(); }

        var max = c.MaxCandidatesPerSlot;
        if (ImGui.SliderInt("Candidates per slot", ref max, 3, 20))
        { c.MaxCandidatesPerSlot = max; _plugin.SaveConfig(); }

        ImGui.Separator();
        ImGui.TextUnformatted("Data sources: xivstats.com (crowdsourced items + dyes),");
        ImGui.TextUnformatted("fashionreportxiv.com (exact weekly dyes). Cached locally;");
        ImGui.TextUnformatted("failures degrade to whatever loaded last.");
        ImGui.Spacing();
        if (ImGui.Button("Refresh data now"))
            _plugin.Service.RequestRefresh();
        ImGui.SameLine();
        ImGui.TextUnformatted(_plugin.Service.RemoteLoaded ? "data loaded" : "no data yet");
    }
}
