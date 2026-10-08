using RotationSolver.Decisions;

namespace RotationSolver.UI.Pvp;

/// <summary>Tab 2, Survival: Guard, Purify, Sprint and the master switch of the automatic defensives.</summary>
internal static class PvpSurvivalTab
{
    public static void Draw()
    {
        foreach (string section in PvpSettingsCatalog.SectionsOf(PvpSettingsCatalog.Survival))
        {
            PvpUi.DrawSection(PvpSettingsCatalog.Survival, section);
            if (section == "Defensive abilities")
            {
                PvpUi.Help("The list of defensives for each job is in the My Job tab, under Defensive abilities.");
                PvpUi.Help("Not covered by this switch: Guard, Purify, Recuperate when 15,000 or more HP is missing, and Standard-issue Elixir when HP or MP is at one third or less.");
            }
        }
    }
}
