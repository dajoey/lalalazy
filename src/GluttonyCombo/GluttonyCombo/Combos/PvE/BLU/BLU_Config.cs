using ECommons.ImGuiMethods;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using GluttonyCombo.Core;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Extensions;
using GluttonyCombo.Resources.Localization.JobConfigs;
using GluttonyCombo.Services;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;
using static GluttonyCombo.Window.Functions.UserConfig;

namespace GluttonyCombo.Combos.PvE;

internal partial class BLU
{
    internal static class Config
    {
        public static UserInt
            BLU_DoTHP = new("BLU_DoTHP", 2),
            BLU_DoTTime = new("BLU_DoTTime", 3),
            BLU_Balance_Content = new("BLU_Balance_Content", 1),
            BLU_SelectedOpener = new("BLU_SelectedOpener", 0),
            // Filler overrides. 0 = auto-detect from your active spell slots.
            BLU_ST_DPS_Filler = new("BLU_ST_DPS_Filler", 0),
            BLU_AoE_DPS_Filler = new("BLU_AoE_DPS_Filler", 0),
            BLU_ST_Tank_Filler = new("BLU_ST_Tank_Filler", 0),
            BLU_AoE_Tank_Filler = new("BLU_AoE_Tank_Filler", 0);

        internal static void Draw(Preset preset)
        {
            switch (preset)
            {
                case Preset.BLU_ST_DPS:
                    DrawFillerPicker(FillerSlot.StDps);
                    break;

                case Preset.BLU_AoE_DPS:
                    DrawFillerPicker(FillerSlot.AoeDps);
                    break;

                case Preset.BLU_ST_Tank:
                    DrawFillerPicker(FillerSlot.StTank);
                    break;

                case Preset.BLU_AoE_Tank:
                    DrawFillerPicker(FillerSlot.AoeTank);
                    break;

                case Preset.BLU_ST_DPS_Opener:
                    DrawBossOnlyChoice(BLU_Balance_Content);
                    ImGuiEx.TextUnderlined("Select Opener");
                    ImGui.Spacing();
                    DrawRadioButton(BLU_SelectedOpener,
                        "Winged Opener",
                        "Winged Reprobation opener. Standard 2.50 spell speed.", 0, descriptionAsTooltip: true);
                    DrawRadioButton(BLU_SelectedOpener,
                        "DoT Opener",
                        "Mortal Flame or Breath of Magic instead of Winged Reprobation. Requires 2.20 or faster spell speed.",
                        1, descriptionAsTooltip: true);
                    break;

                case Preset.BLU_ST_DPS_SongOfTorment:
                case Preset.BLU_ST_DPS_Breath:
                case Preset.BLU_ST_DPS_Flame:
                case Preset.BLU_ST_Tank_SongOfTorment:
                    DrawSliderInt(0, 100, BLU_DoTHP, Generics.StopEnemyHpPercent);
                    DrawSliderInt(0, 15, BLU_DoTTime, Generics.StopSeconds);
                    break;
            }
        }

        #region Filler picker (fork divergence — see BLU_Fillers.cs)

        /// <summary>
        ///     Dropdown letting the user pin the filler spell for a rotation slot,
        ///     instead of being stuck with the one upstream hard-codes. Spells the
        ///     user does not currently have in their 24 active slots are listed but
        ///     greyed, because a filler that is not slotted cannot be cast.
        /// </summary>
        private static void DrawFillerPicker(FillerSlot slot)
        {
            var config = FillerConfig(slot);
            var selected = (uint)(int)config;
            var resolved = ResolveFiller(slot);
            var auto = selected == 0;

            ImGui.Spacing();
            ImGuiEx.TextUnderlined("Filler Spell");
            ImGuiEx.HelpMarker(
                "Which spell this rotation spams when nothing better is available.\n\n" +
                "Blue Mage only gets 24 active spell slots, so the default filler may " +
                "not be one you carry. Leave this on Automatic and the rotation uses " +
                "the best filler you actually have slotted.");

            var preview = auto
                ? $"Automatic ({(resolved == 0 ? "none slotted" : resolved.ActionName())})"
                : selected.ActionName();

            ImGui.PushItemWidth(250f.Scale());
            if (ImGui.BeginCombo($"###BLUFiller{slot}", preview))
            {
                if (ImGui.Selectable("Automatic", auto))
                {
                    config.Value = 0;
                    Service.Configuration.Save();
                }

                if (ImGui.IsItemHovered())
                    ImGuiEx.Tooltip("Use the strongest filler you have slotted. Recommended.");

                ImGui.Separator();

                foreach (var filler in Candidates(slot))
                {
                    var active = IsSpellActive(filler.ActionId);
                    using (ImRaii.PushColor(ImGuiCol.Text,
                               active ? ImGuiColors.DalamudWhite : ImGuiColors.DalamudGrey))
                    {
                        var label = filler.ActionId.ActionName();
                        if (!active)
                            label += " (not slotted)";

                        if (ImGui.Selectable($"{label}###BLUFiller{slot}{filler.ActionId}",
                                selected == filler.ActionId))
                        {
                            config.Value = (int)filler.ActionId;
                            Service.Configuration.Save();
                        }
                    }

                    if (!ImGui.IsItemHovered())
                        continue;

                    var tip = $"{filler.Potency} potency, {filler.Range}y.";
                    if (filler.Caveat is not null)
                        tip += $"\n{filler.Caveat}";
                    if (!filler.AutoSafe)
                        tip += "\nNot chosen automatically — pick it here if you want it.";
                    if (!active)
                        tip += "\n\nYou do not have this spell in your active slots.";
                    ImGuiEx.Tooltip(tip);
                }

                ImGui.EndCombo();
            }

            ImGui.PopItemWidth();

            if (resolved == 0)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DPSRed))
                    ImGui.TextWrapped(
                        "None of the usable filler spells are in your active slots. " +
                        "This rotation will not replace its button until you slot one.");
            }
            else if (!auto && !IsSpellActive(selected))
            {
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange))
                    ImGui.TextWrapped(
                        $"{selected.ActionName()} is not in your active slots, so " +
                        $"{resolved.ActionName()} is being used instead.");
            }

            ImGui.Spacing();
        }

        #endregion
    }
}
