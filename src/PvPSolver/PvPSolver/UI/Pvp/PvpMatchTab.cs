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

        PvpApprovedChrome.Card("match_commands", () =>
        {
            PvpUi.Heading("Commands");
            for (int i = 0; i < Commands.Length; i++)
            {
                (string command, string what) = Commands[i];
                int index = i;
                PvpApprovedChrome.Row("cmd" + i, command, what, PvpApprovedChrome.ButtonWidth("Copy"), PvpApprovedChrome.BodyControl, pos =>
                {
                    if (PvpApprovedChrome.Button("copy", "Copy", pos, new Vector2(PvpApprovedChrome.ButtonWidth("Copy"), PvpApprovedChrome.BodyControl)))
                    {
                        ImGui.SetClipboardText(Commands[index].Command);
                    }
                });
            }
        });
    }
}
