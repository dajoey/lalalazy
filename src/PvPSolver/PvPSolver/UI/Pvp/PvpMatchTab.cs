using RotationSolver.Decisions;

namespace RotationSolver.UI.Pvp;

/// <summary>Tab 1, Match: when PvP Solver switches itself on and off, and the commands that do it by hand.</summary>
internal static class PvpMatchTab
{
    private static readonly (string Command, string What)[] Commands =
    [
        ("/pvpsolver Auto", "Turns PvP Solver on. It also picks the targets. A target type can follow, for example /pvpsolver Auto Big."),
        ("/pvpsolver Manual", "Turns PvP Solver on and leaves choosing the target to the player."),
        ("/pvpsolver Off", "Turns PvP Solver off."),
        ("/pvpsolver advanced", "Opens the full Rotation Solver settings. A tab name can follow, for example /pvpsolver advanced Actions."),
        ("/pvpsolver changelog", "Shows what is new in this version."),
    ];

    public static void Draw()
    {
        PvpUi.DrawTab(PvpSettingsCatalog.Match);

        PvpUi.Heading("Commands");
        for (int i = 0; i < Commands.Length; i++)
        {
            (string command, string what) = Commands[i];
            if (ImGui.SmallButton("Copy##cmd" + i))
            {
                ImGui.SetClipboardText(command);
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(command);
            PvpUi.Help(what);
        }
    }
}
