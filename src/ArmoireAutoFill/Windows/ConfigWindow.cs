using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ArmoireAutoFill.Windows;

public class ConfigWindow : Window
{
    public ConfigWindow()
        : base("Armoire Auto-Fill Settings###ArmoireAutoFillConfig")
    { }

    public override void Draw()
    {
        var scanOnLoad = Plugin.Configuration.ScanOnLoad;
        if (ImGui.Checkbox("Auto-scan on login", ref scanOnLoad))
        {
            Plugin.Configuration.ScanOnLoad = scanOnLoad;
            Plugin.Configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Automatically scan your inventory when you log in");
        }

        ImGui.Separator();

        ImGui.Text("About");
        ImGui.TextWrapped("Armoire Auto-Fill helps you collect all armoire-compatible dungeon gear (level 31+). It scans your inventory and shows what pieces you're missing from each dungeon.");
    }
}
