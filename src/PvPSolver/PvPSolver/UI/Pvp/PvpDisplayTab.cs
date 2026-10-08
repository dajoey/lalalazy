using Dalamud.Interface.Utility.Raii;
using RotationSolver.Decisions;

namespace RotationSolver.UI.Pvp;

/// <summary>Tab 5, Display: the server info bar, the overlay windows, teaching mode, hotbar tint and tooltips.</summary>
internal static class PvpDisplayTab
{
    public static void Draw()
    {
        foreach (string section in PvpSettingsCatalog.SectionsOf(PvpSettingsCatalog.Display))
        {
            PvpUi.DrawSection(PvpSettingsCatalog.Display, section);
            if (section == "Teaching mode")
            {
                DrawTeachingColor();
            }
        }

        PvpUi.Heading("What's new");
        if (ImGui.Button("Show what's new"))
        {
            PvPSolverPlugin.ShowChangelog();
        }

        PvpUi.Help("Opens the list of changes in this version.");
    }

    private static void DrawTeachingColor()
    {
        using var id = ImRaii.PushId("TeachingModeColor");
        bool parentOn = Service.Config.TeachingMode.Value;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (24f * PvpUi.Scale));
        using var off = ImRaii.Disabled(!parentOn);
        Vector4 color = Service.Config.TeachingModeColor;
        if (ImGui.ColorEdit4("Highlight colour##color", ref color, ImGuiColorEditFlags.NoInputs))
        {
            Service.Config.TeachingModeColor = color;
            PvpUi.MarkDirty();
        }

        PvpUi.SaveOnRelease();
        PvpUi.Help("The colour of the teaching mode highlight on the hotbars.", 48f);
    }
}
