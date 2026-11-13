using ECommons.ExcelServices;
using LalaUiKit;
namespace GluttonyCombo.Window.MessagesNS;

internal static class Messages
{
    internal static bool PrintBLUMessage(Job job)
    {
        if (job is Job.BLU) //Blue Mage ID
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.StateRed);
            ImGui.TextWrapped("*** WARNING: BLU AUTO-ROTATION IS CURRENTLY BROKEN ***\nThe BLU Auto-Rotation (DPS/Heal) presets below are known non-functional in this release. They remain visible for configuration only - do not rely on them until a fix ships.");
            ImGui.PopStyleColor();
            ImGui.Separator();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
            ImGui.TextWrapped($"Please note that even if you do not have all the required spells active, you may still use these features.\nAny spells you do not have active will be skipped over so if a feature is not working as intended then\nplease try and enable more required spells.");
            ImGui.PopStyleColor();
        }

        return true;
    }
}