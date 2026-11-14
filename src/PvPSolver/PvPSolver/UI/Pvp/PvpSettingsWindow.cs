using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.GameHelpers;
using RotationSolver.Decisions;
using RotationSolver.Helpers;

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

    private string _switchMessage = string.Empty;

    public PvpSettingsWindow()
        : base("PvP Solver Settings v" + (typeof(PvpSettingsWindow).Assembly.GetName().Version?.ToString() ?? "?") + WindowId,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse, false)
    {
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(700f, 620f);
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(420f, 360f),
            MaximumSize = new Vector2(5000f, 5000f),
        };
        RespectCloseHotkey = true;
    }

    public override void OnOpen()
    {
        PvpJobTab.OnOpen();
        PvpUi.ResetLog();
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
        DrawStrip(PvpSwitch.View);

        using var tabBar = ImRaii.TabBar("##pvptabs");
        if (!tabBar)
        {
            return;
        }

        foreach ((string name, Action draw) in Tabs)
        {
            using var item = ImRaii.TabItem(name);
            if (!item)
            {
                continue;
            }

            using var body = ImRaii.Child("##body" + name, Vector2.Zero, false);
            if (body)
            {
                DrawTabBody(name, draw);
            }
        }
    }

    private void DrawStrip(SwitchView view)
    {
        bool on = view.IsOn;
        using (var color = ImRaii.PushColor(ImGuiCol.Text, on ? ImGuiColors.HealerGreen : PvpUi.Muted))
        {
            ImGui.TextUnformatted(view.StatusText);
        }

        ImGui.SameLine();
        ImGui.TextWrapped(view.Reason);

        string job = Player.Available ? Player.Job.ToString() : "no character";
        ImGui.TextUnformatted("Job: " + job);
        ImGui.SameLine();
        if (PvpUi.Button(view.ButtonLabel, !view.ButtonEnabled, view.ButtonWhy))
        {
            _switchMessage = PvpSwitch.Set(!on) ?? string.Empty;
        }

        if (_switchMessage.Length > 0)
        {
            PvpUi.WarningText(_switchMessage);
        }

        ImGui.Separator();
    }

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
