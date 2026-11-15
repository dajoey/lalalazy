using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.GameHelpers;
using RotationSolver.Decisions;
using RotationSolver.Helpers;
using LalaUiKit;

namespace RotationSolver.UI.Pvp;

/// <summary>
///     The PvP Solver settings window: a fork-only window that shows the settings that matter for PvP in plain words,
///     with a help line under each. Everything in it can be edited in any zone with or without a loaded rotation; the
///     only control that depends on the zone is turning PvP Solver on, which sits in the strip above the tabs and says
///     why it is unavailable. The full Rotation Solver window stays reachable from the Advanced tab and with
///     <c>/pvpsolver advanced</c>, and never goes through this window's drawing.
/// </summary>
internal sealed class PvpSettingsWindow : Window
{
    private const string WindowId = "###pvpSettingsWindow";

    private static readonly (string Name, Action Draw)[] Tabs =
    [
        (PvpSettingsCatalog.Match, PvpMatchTab.Draw),
        (PvpSettingsCatalog.Survival, PvpSurvivalTab.Draw),
        (PvpSettingsCatalog.Targeting, PvpTargetingTab.Draw),
        ("My Job", PvpJobTab.Draw),
        (PvpSettingsCatalog.Display, PvpDisplayTab.Draw),
        (PvpSettingsCatalog.Advanced, PvpAdvancedTab.Draw),
    ];

    private readonly PvpApprovedShell _shell = new();
    private string _switchMessage = string.Empty;

    public PvpSettingsWindow()
        : base("PvP Solver Settings v" + (typeof(PvpSettingsWindow).Assembly.GetName().Version?.ToString() ?? "?") + WindowId,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse, false)
    {
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(1080f, 760f);
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(560f, 420f),
            MaximumSize = new Vector2(5000f, 5000f),
        };
        RespectCloseHotkey = true;
    }

    public override void OnOpen()
    {
        PvpJobTab.OnOpen();
        PvpUi.ResetLog();
        _shell.Reset();
        _switchMessage = string.Empty;
        base.OnOpen();
    }

    public override void OnClose()
    {
        PvpUi.Flush();
        base.OnClose();
    }

    public override void Draw()
    {
        try
        {
            DrawShell();
        }
        catch (Exception ex)
        {
            PvpUi.LogOnce("window", ex);
            DrawFailure(ex);
        }
    }

    private void DrawShell()
    {
        SwitchView view = PvpSwitch.View;
        _shell.Draw(Tabs, view, ref _switchMessage, DrawTabBody, SetSwitch);
    }

    private static string? SetSwitch(bool on) => PvpSwitch.Set(on);

    private static void DrawTabBody(string name, Action draw)
    {
        try
        {
            draw();
        }
        catch (Exception ex)
        {
            PvpUi.LogOnce("tab " + name, ex);
            DrawFailure(ex);
        }
    }

    private static void DrawFailure(Exception ex)
    {
        PvpUi.WarningText("This part of the settings window hit an error: " + ex.Message);
        PvpUi.Paragraph("The details are in the Dalamud log (type /xllog). The full Rotation Solver settings still work.");
        if (ImGui.Button("Open the full Rotation Solver settings"))
        {
            PvPSolverPlugin.OpenAdvancedWindow();
        }
    }
}
