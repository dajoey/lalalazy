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
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        Size = new Vector2(600, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    // The shell lays out its own gutters, so the window itself has no padding.
    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    public override void PostDraw() => ImGui.PopStyleVar();

    public override void Draw()
    {
        ArmoireChrome.ResetLayout();
        Theme.PushFrame();
        try
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var gutter = ArmoireChrome.Gutter(winSize.X);
            ImGui.SetCursorPos(Vector2.Zero);
            var y = ArmoireChrome.TopBar(winPos, winSize, gutter, Navigation, 0, _ => { }, UiState.Ready, () => IsOpen = false);
            ImGui.SetCursorPos(new Vector2(0, y));
            ArmoireChrome.BeginBody("##armoire_config_body", new Vector2(winSize.X, MathF.Max(40, winSize.Y - y)), gutter);
            try
            {
                ArmoireChrome.PageHeader("Settings",
                    "Controls when the inventory scanner runs and how collection data stays current.",
                    "1 option", "Scanner behavior");

                ArmoireChrome.Card("config_scan_settings", 0, () =>
                {
                    ArmoireChrome.CardHeader("Inventory scanning");
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
                    ArmoireChrome.CardHeader("About Armoire Auto-Fill");
                    ArmoireChrome.Paragraph(
                        "Armoire Auto-Fill helps collect armoire-compatible dungeon gear (level 31+). "
                        + "It scans inventory and shows which pieces are missing from each dungeon. "
                        + "Open the armoire UI at an inn once per session to populate the in-armoire state; "
                        + "in-inventory and equipped state is detected automatically.", gap: 0);
                });
            }
            finally
            {
                ArmoireChrome.EndBody();
            }
        }
        finally
        {
            Theme.PopFrame();
        }
    }
}
