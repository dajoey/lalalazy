using Dalamud.Interface.Utility.Raii;
using LalaUiKit;
using ECommons.DalamudServices;
using RotationSolver.Basic.Configuration;
using RotationSolver.Decisions;
using System.Diagnostics;

namespace RotationSolver.UI.Pvp;

/// <summary>
///     Tab 6, Advanced: the way to the full (legacy) Rotation Solver settings, backup and restore, reset and the
///     debug switch. Restore and reset ask first.
/// </summary>
internal static class PvpAdvancedTab
{
    private const string ResetPopup = "Reset all PvP Solver settings";
    private const string RestorePopup = "Restore settings from the backup";

    private static bool _openReset;
    private static bool _openRestore;
    private static string _message = string.Empty;

    private static readonly (string Label, string Tab, string What)[] Links =
    [
        ("Actions", "Actions", "Enable or disable single actions and change how each is used."),
        ("Lists", "List", "Status and action lists (invulnerable, priority, dispellable and others)."),
        ("Timing", "Basic", "Timers and basic behaviour."),
        ("Interface", "UI", "Every window, label and teaching option."),
        ("Auto", "Auto", "Automatic action use, healing and the finer rules."),
        ("Target", "Target", "Target filters and the hostile selection."),
        ("Extra", "Extra", "Events, macros and backup in the full window."),
        ("Debug", "Debug", "Debug information (needs Debug mode, below)."),
        ("About and macros", "Main", "About, command list, links and compatibility."),
    ];

    public static void Draw()
    {
        PvpApprovedChrome.Card("advanced_editor", () =>
        {
            PvpUi.Heading("Full Rotation Solver settings", true);
            PvpUi.Paragraph("The original settings window has every option, including ones this window leaves out. Its job options only show for the job that is loaded in a PvP zone.");
            if (PvpUi.Button("Open the full Rotation Solver settings", style: PvpApprovedChrome.ButtonStyle.Accent))
            {
                PvPSolverPlugin.OpenAdvancedWindow();
            }

            PvpApprovedChrome.Tighten(4);
            PvpUi.Help("Same as /pvpsolver advanced.", after: 16);
            PvpApprovedChrome.Para("Open it on a tab", 1f, Theme.TextPrimary, 8);
            bool first = true;
            foreach ((string label, string tab, string what) in Links)
            {
                if (PvpApprovedChrome.FlowButton("link" + tab, label, first))
                {
                    PvPSolverPlugin.OpenAdvancedWindow(tab);
                }

                PvpUi.Tip(what);
                first = false;
            }
        });

        PvpApprovedChrome.Card("advanced_tools", () =>
        {
            PvpUi.Heading("Backup and tools", true);
            if (PvpUi.Button("Back up settings"))
            {
                RunGuarded(() => Service.Config.Backup(), "Backup");
            }

            PvpApprovedChrome.Tighten(4);
            PvpUi.Help("Saves a copy of the settings next to the settings file (RotationSolver_Backup.json). An earlier backup is overwritten.", after: 16);
            if (PvpUi.Button("Restore the backup..."))
            {
                _openRestore = true;
            }

            PvpApprovedChrome.Tighten(4);
            PvpUi.Help("Replaces the current settings by the backup. A safety copy of the current settings is kept (RotationSolver_SafetySave.json).", after: 16);
            if (PvpUi.Button("Open the settings folder"))
            {
                RunGuarded(() => { _ = Process.Start("explorer.exe", Svc.PluginInterface.ConfigDirectory.FullName); }, "Open folder");
            }

            ImGui.SameLine(0, 8);
            if (PvpUi.Button("First-start tutorial"))
            {
                PvPSolverPlugin.OpenFirstStartTutorial();
            }

            PvpApprovedChrome.Tighten(4);
            PvpUi.Help("The tutorial walks through this settings window, one tab at a time.", after: 0);
            if (_message.Length > 0)
            {
                PvpUi.WarningText(_message);
            }
        });

        PvpApprovedChrome.Card("advanced_reset", () =>
        {
            PvpUi.Heading("Reset", true);
            if (PvpUi.Button("Reset all settings...", style: PvpApprovedChrome.ButtonStyle.Danger))
            {
                _openReset = true;
            }

            PvpApprovedChrome.Tighten(4);
            PvpUi.Help("Returns every PvP Solver setting to its default, including the ones in the full settings window.", after: 0);
        });

        DrawPopups();

        foreach (string section in PvpSettingsCatalog.SectionsOf(PvpSettingsCatalog.Advanced))
        {
            PvpUi.DrawSection(PvpSettingsCatalog.Advanced, section);
        }
    }

    private static void RunGuarded(Action action, string what)
    {
        try
        {
            _message = string.Empty;
            action();
        }
        catch (Exception ex)
        {
            PvpUi.LogOnce(what, ex);
            _message = $"{what} failed: {ex.Message}";
        }
    }

    private static void DrawPopups()
    {
        if (_openReset)
        {
            ImGui.OpenPopup(ResetPopup);
            _openReset = false;
        }

        if (_openRestore)
        {
            ImGui.OpenPopup(RestorePopup);
            _openRestore = false;
        }

        // A modal sized to its content and centred over the settings window; the shell darkens the window behind it.
        // The 24 px padding, rounding and hidden resize grip are pushed for both popups; neither has a title bar.
        Vector2 center = PvpApprovedChrome.WindowPos + (PvpApprovedChrome.WindowSize / 2f);
        float width = MathF.Min(460, PvpApprovedChrome.WindowSize.X - 48);
        using var vars = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(24, 20))
            .Push(ImGuiStyleVar.WindowRounding, 8f)
            .Push(ImGuiStyleVar.WindowBorderSize, 1f);
        using var colors = ImRaii.PushColor(ImGuiCol.PopupBg, Theme.Surface)
            .Push(ImGuiCol.Border, Theme.SurfaceBorder)
            .Push(ImGuiCol.ResizeGrip, 0u)
            .Push(ImGuiCol.ResizeGripHovered, 0u)
            .Push(ImGuiCol.ResizeGripActive, 0u);

        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(width, 0));
        using (var reset = ImRaii.PopupModal(ResetPopup, ImGuiWindowFlags.NoTitleBar))
        {
            if (reset)
            {
                DrawConfirm(width, "Reset all PvP Solver settings to their defaults?",
                    "This is often recommended after an update that changes defaults. It cannot be undone, but a backup made earlier can be restored.",
                    "Yes, reset", true, () => RunGuarded(() =>
                    {
                        Service.Config = new Configs();
                        Service.Config.Save();
                    }, "Reset"));
            }
        }

        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(width, 0));
        using (var restore = ImRaii.PopupModal(RestorePopup, ImGuiWindowFlags.NoTitleBar))
        {
            if (restore)
            {
                DrawConfirm(width, "Replace the current settings by the backup?",
                    "The current settings are copied to RotationSolver_SafetySave.json first.",
                    "Yes, restore", false, () => RunGuarded(() => Service.Config.Restore(), "Restore"));
            }
        }
    }

    private static void DrawConfirm(float width, string question, string detail, string confirm, bool danger, Action onConfirm)
    {
        PvpApprovedChrome.ModalOpen = true;
        PvpApprovedChrome.PushRight(ImGui.GetCursorScreenPos().X + width - 48);
        try
        {
            PvpApprovedChrome.Para(question, Theme.TitleScale, Theme.TextPrimary, 8);
            PvpApprovedChrome.Para(detail, Theme.SecondaryScale, Theme.TextSecondary, 16);
            float noWidth = PvpApprovedChrome.ButtonWidth("No", 96);
            float yesWidth = PvpApprovedChrome.ButtonWidth(confirm, 96);
            float right = PvpApprovedChrome.Avail() + ImGui.GetCursorScreenPos().X;
            float y = ImGui.GetCursorScreenPos().Y;
            if (PvpApprovedChrome.Button("confirm", confirm, new Vector2(right - yesWidth, y), new Vector2(yesWidth, PvpApprovedChrome.BodyControl),
                    danger ? PvpApprovedChrome.ButtonStyle.Danger : PvpApprovedChrome.ButtonStyle.Accent))
            {
                onConfirm();
                ImGui.CloseCurrentPopup();
            }

            if (PvpApprovedChrome.Button("cancel", "No", new Vector2(right - yesWidth - 8 - noWidth, y), new Vector2(noWidth, PvpApprovedChrome.BodyControl)))
            {
                ImGui.CloseCurrentPopup();
            }
        }
        finally
        {
            PvpApprovedChrome.PopRight();
        }
    }
}
