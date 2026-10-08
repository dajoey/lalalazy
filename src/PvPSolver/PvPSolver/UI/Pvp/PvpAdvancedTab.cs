using Dalamud.Interface.Utility.Raii;
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
        PvpUi.Heading("Full Rotation Solver settings");
        PvpUi.Paragraph("The original settings window has every option, including ones this window leaves out. Its job options only show for the job that is loaded in a PvP zone.");
        if (ImGui.Button("Open the full Rotation Solver settings"))
        {
            PvPSolverPlugin.OpenAdvancedWindow();
        }

        PvpUi.Help("Same as /pvpsolver advanced.", 0f);
        ImGui.Spacing();
        ImGui.TextUnformatted("Open it on a tab:");
        bool first = true;
        foreach ((string label, string tab, string what) in Links)
        {
            // keep the buttons on one line while they fit, then start a new line
            float width = ImGui.CalcTextSize(label).X + (ImGui.GetStyle().FramePadding.X * 2f) + ImGui.GetStyle().ItemSpacing.X;
            if (!first && ImGui.GetContentRegionAvail().X >= width)
            {
                ImGui.SameLine();
            }

            first = false;
            if (ImGui.SmallButton(label + "##link" + tab))
            {
                PvPSolverPlugin.OpenAdvancedWindow(tab);
            }

            PvpUi.Tip(what);
        }

        PvpUi.Heading("Backup and tools");
        if (ImGui.Button("Back up settings"))
        {
            RunGuarded(() => Service.Config.Backup(), "Backup");
        }

        PvpUi.Help("Saves a copy of the settings next to the settings file (RotationSolver_Backup.json). An earlier backup is overwritten.");
        if (ImGui.Button("Restore the backup..."))
        {
            _openRestore = true;
        }

        PvpUi.Help("Replaces the current settings by the backup. A safety copy of the current settings is kept (RotationSolver_SafetySave.json).");
        if (ImGui.Button("Open the settings folder"))
        {
            RunGuarded(() => { _ = Process.Start("explorer.exe", Svc.PluginInterface.ConfigDirectory.FullName); }, "Open folder");
        }

        ImGui.SameLine();
        if (ImGui.Button("First-start tutorial"))
        {
            PvPSolverPlugin.OpenFirstStartTutorial();
        }

        PvpUi.Help("The tutorial walks through the full Rotation Solver settings.");
        if (_message.Length > 0)
        {
            PvpUi.WarningText(_message);
        }

        PvpUi.Heading("Reset");
        if (ImGui.Button("Reset all settings..."))
        {
            _openReset = true;
        }

        PvpUi.Help("Returns every PvP Solver setting to its default, including the ones in the full settings window.");

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

        using (var reset = ImRaii.PopupModal(ResetPopup))
        {
            if (reset)
            {
                ImGui.TextUnformatted("Reset all PvP Solver settings to their defaults?");
                PvpUi.Help("This is often recommended after an update that changes defaults. It cannot be undone, but a backup made earlier can be restored.", 0f);
                if (ImGui.Button("Yes, reset", new Vector2(140f * PvpUi.Scale, 0f)))
                {
                    RunGuarded(() =>
                    {
                        Service.Config = new Configs();
                        Service.Config.Save();
                    }, "Reset");
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("No", new Vector2(140f * PvpUi.Scale, 0f)))
                {
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        using (var restore = ImRaii.PopupModal(RestorePopup))
        {
            if (restore)
            {
                ImGui.TextUnformatted("Replace the current settings by the backup?");
                PvpUi.Help("The current settings are copied to RotationSolver_SafetySave.json first.", 0f);
                if (ImGui.Button("Yes, restore", new Vector2(140f * PvpUi.Scale, 0f)))
                {
                    RunGuarded(() => Service.Config.Restore(), "Restore");
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("No", new Vector2(140f * PvpUi.Scale, 0f)))
                {
                    ImGui.CloseCurrentPopup();
                }
            }
        }
    }
}
