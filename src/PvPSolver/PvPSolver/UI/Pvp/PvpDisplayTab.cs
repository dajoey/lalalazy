using RotationSolver.Decisions;

namespace RotationSolver.UI.Pvp;

/// <summary>Tab 5, Display: the server info bar, the overlay windows, teaching mode, hotbar tint and tooltips.</summary>
internal static class PvpDisplayTab
{
    public static void Draw()
    {
        foreach (string section in PvpSettingsCatalog.SectionsOf(PvpSettingsCatalog.Display))
        {
            PvpUi.DrawSection(PvpSettingsCatalog.Display, section, after: section == "Teaching mode" ? DrawTeachingColor : null);
        }

        PvpApprovedChrome.Card("display_whats_new", () =>
        {
            PvpUi.Heading("What's new");
            PvpApprovedChrome.Row("whats_new", "Show what's new", "Opens the list of changes in this version.",
                PvpApprovedChrome.ButtonWidth("Show"), PvpApprovedChrome.BodyControl, pos =>
                {
                    if (PvpApprovedChrome.Button("show", "Show", pos, new Vector2(PvpApprovedChrome.ButtonWidth("Show"), PvpApprovedChrome.BodyControl)))
                    {
                        PvPSolverPlugin.ShowChangelog();
                    }
                });
        });
    }

    // The highlight colour belongs to teaching mode, so it is a row of that card, indented like the other children.
    private static void DrawTeachingColor()
    {
        bool parentOn = Service.Config.TeachingMode.Value;
        PvpApprovedChrome.Row("TeachingModeColor", "Highlight colour", "The colour of the teaching mode highlight on the hotbars.",
            PvpApprovedChrome.BodyControl, PvpApprovedChrome.BodyControl, pos =>
            {
                Vector4 color = Service.Config.TeachingModeColor;
                ImGui.SetCursorScreenPos(pos);
                if (PvpApprovedChrome.Field(() => ImGui.ColorEdit4("##color", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel)))
                {
                    Service.Config.TeachingModeColor = color;
                    PvpUi.MarkDirty();
                }

                PvpUi.SaveOnRelease();
            }, indent: 1, disabled: !parentOn, joined: true);
    }
}
