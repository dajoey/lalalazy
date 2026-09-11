using ECommons.ImGuiMethods;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Extensions;
using GluttonyCombo.Resources.Localization.JobConfigs;
using GluttonyCombo.Window.Functions;
using static GluttonyCombo.Window.Functions.UserConfig;
using static GluttonyCombo.Window.Text;
namespace GluttonyCombo.Combos.PvE;

// Beastmaster (BST) rotation config (t_02fe2681, resx migration t_05b797ea). Three new keys
// only - no migration needed (RULES OF THE JOB: "Config: ... new keys only"), drawn under the
// Advanced Mode preset the same way GNB/DNC draw their mitigation/priority radios under their
// advanced ST preset.
//
// Labels/descriptions route through Resources.Localization.JobConfigs.BST_Config +
// FormatAndCache, matching the idiom MNK_Config.cs/VPR_Config.cs use throughout - the prose
// itself is unchanged (Joey: "labeling is a mess... clean up the wording" referred to the
// hand-written-literal plumbing, not the wording, per beastmaster-rotation-spec.md §4/
// gluttony-rotation-framework.md §7). ActionName()/StatusName() interpolate the live action
// names so a future ability rename doesn't leave a stale label.
internal partial class BST
{
    internal static class Config
    {
        public static UserBool
            BST_IncludeFamiliarMitigation = new("BST_IncludeFamiliarMitigation"),
            BST_HoldPartingBlowForVantage = new("BST_HoldPartingBlowForVantage", true);

        public static UserInt
            BST_BattlehornSlotOrder = new("BST_BattlehornSlotOrder", 0);

        internal static void Draw(Preset preset)
        {
            switch (preset)
            {
                case Preset.BST_ST_AdvancedMode:
                    DrawAdditionalBoolChoice(BST_IncludeFamiliarMitigation,
                        FormatAndCache(BST_Config.IncludeFamiliarMitigation0, BeastMode.ActionName()),
                        FormatAndCache(BST_Config.IncludeFamiliarMitigation0Desc, BeastMode.ActionName()));

                    DrawAdditionalBoolChoice(BST_HoldPartingBlowForVantage,
                        FormatAndCache(BST_Config.Hold0For1, PartingBlow.ActionName(), Buffs.LingeringVantage.StatusName()),
                        FormatAndCache(BST_Config.Hold0For1Desc, Buffs.LingeringVantage.StatusName(), Borrow.ActionName(), PartingBlow.ActionName()));

                    ImGui.Spacing();
                    ImGuiEx.TextUnderlined(BST_Config.AutoBattlehornSlotOrder);
                    ImGui.Spacing();
                    DrawHorizontalRadioButton(BST_BattlehornSlotOrder,
                        BST_Config.Rotate123,
                        FormatAndCache(BST_Config.Rotate123Desc, Borrow.ActionName(), TemperedRelease.ActionName()), 0);
                    DrawHorizontalRadioButton(BST_BattlehornSlotOrder,
                        BST_Config.AlwaysSlot1,
                        BST_Config.AlwaysSlot1Desc, 1);
                    DrawHorizontalRadioButton(BST_BattlehornSlotOrder,
                        BST_Config.AlwaysSlot2,
                        BST_Config.AlwaysSlot2Desc, 2);
                    DrawHorizontalRadioButton(BST_BattlehornSlotOrder,
                        BST_Config.AlwaysSlot3,
                        BST_Config.AlwaysSlot3Desc, 3);
                    break;

                default:
                    break;
            }
        }
    }
}
