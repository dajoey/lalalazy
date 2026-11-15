using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace ArmoireAutoFill.Windows;

public class ConfigWindow : Window
{
    private static readonly ArmoireNavItem[] Navigation =
    [
        new("aaf_cfg_nav_settings", "Settings"),
    ];

    public ConfigWindow()
        : base("Armoire Auto-Fill Settings###ArmoireAutoFillConfig")
    {
        Flags = ImGuiWindowFlags.NoTitleBar;
        Size = new Vector2(600, 360);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        Theme.PushFrame();
        try
        {
            ArmoireChrome.TopBar(Navigation, 0, _ => { }, UiState.Ready);
            ArmoireChrome.PageHeader("Settings",
                "Controls when the inventory scanner runs and how collection data stays current.",
                "1 option", "Scanner behavior");

            ArmoireChrome.Card("config_scan_settings", 0, () =>
            {
                Widgets.Title("Inventory scanning");
                var scanOnLoad = Plugin.Configuration.ScanOnLoad;
                if (ArmoireChrome.Toggle("Auto-scan on login", ref scanOnLoad,
                        "Automatically scans inventory at login so Collection is current without a manual rescan."))
                {
                    Plugin.Configuration.ScanOnLoad = scanOnLoad;
                    Plugin.Configuration.Save();
                }
            });

            ArmoireChrome.Card("config_about", 0, () =>
            {
                Widgets.Title("About Armoire Auto-Fill");
                Widgets.SecondaryWrapped(
                    "Armoire Auto-Fill helps collect armoire-compatible dungeon gear (level 31+). "
                    + "It scans inventory and shows which pieces are missing from each dungeon. "
                    + "Open the armoire UI at an inn once per session to populate the in-armoire state; "
                    + "in-inventory and equipped state is detected automatically.");
            });
        }
        finally
        {
            Theme.PopFrame();
        }
    }
}
