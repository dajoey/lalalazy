using ECommons.ImGuiMethods;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Extensions;
using GluttonyCombo.Resources.Localization.JobConfigs;
using static GluttonyCombo.Window.Functions.UserConfig;
using static GluttonyCombo.Window.Text;
namespace GluttonyCombo.Combos.PvE;

// Beastmaster options (BST rebuild 2026-09-16). New keys only; the pre-rebuild keys
// (BST_IncludeFamiliarMitigation, BST_HoldPartingBlowForVantage, BST_BattlehornSlotOrder) are no longer
// read: holding for Lingering Vantage stalled below L44 and a fixed horn slot stalled during its lockout.
// Simple Mode uses BST_RotationLogic.BstSettings.Defaults, which these defaults mirror. Labels route
// through Resources.Localization.JobConfigs.BST_Config (fork convention).
internal partial class BST
{
    internal static class Config
    {
        public static UserInt
            BST_MinFamiliarStay = new("BST_MinFamiliarStay", 10);

        public static UserBool
            BST_AllowPetlessCycling = new("BST_AllowPetlessCycling", false),
            BST_FinalStingAsExit = new("BST_FinalStingAsExit", true),
            BST_AllowDisplacingRelease = new("BST_AllowDisplacingRelease", false),
            BST_AllowSleepRelease = new("BST_AllowSleepRelease", false),
            BST_BorrowWhileReleaseRecasts = new("BST_BorrowWhileReleaseRecasts", true),
            BST_SummonBeforeCombat = new("BST_SummonBeforeCombat", true),
            BST_RefreshBetweenPulls = new("BST_RefreshBetweenPulls", true),
            BST_UseBeastskin = new("BST_UseBeastskin", true),
            BST_UseVileskin = new("BST_UseVileskin", true),
            BST_UseSeedsower = new("BST_UseSeedsower", true),
            BST_UseScaleskin = new("BST_UseScaleskin", false),
            BST_UseSoulCrush = new("BST_UseSoulCrush", true),
            BST_UseQuellingWaveRanged = new("BST_UseQuellingWaveRanged", true),
            BST_UseShieldCharge = new("BST_UseShieldCharge", true),
            BST_UseRally = new("BST_UseRally", true);

        internal static void Draw(Preset preset)
        {
            switch (preset)
            {
                case Preset.BST_ST_AdvancedMode:
                case Preset.BST_AoE_AdvancedMode:
                    ImGuiEx.TextUnderlined(BST_Config.SectionFamiliar);
                    ImGui.Spacing();

                    DrawSliderInt(0, 60, BST_MinFamiliarStay,
                        FormatAndCache(BST_Config.MinFamiliarStay0, PartingBlow.ActionName()));

                    DrawAdditionalBoolChoice(BST_AllowPetlessCycling,
                        BST_Config.AllowPetlessCycling,
                        BST_Config.AllowPetlessCyclingDesc);

                    DrawAdditionalBoolChoice(BST_FinalStingAsExit,
                        BST_Config.FinalStingAsExit,
                        FormatAndCache(BST_Config.FinalStingAsExitDesc, TemperedRelease.ActionName(), PartingBlow.ActionName()));

                    DrawAdditionalBoolChoice(BST_AllowDisplacingRelease,
                        FormatAndCache(BST_Config.AllowDisplacingRelease0, TemperedRelease.ActionName()),
                        BST_Config.AllowDisplacingReleaseDesc);

                    DrawAdditionalBoolChoice(BST_AllowSleepRelease,
                        FormatAndCache(BST_Config.AllowSleepRelease0, TemperedRelease.ActionName()),
                        BST_Config.AllowSleepReleaseDesc);

                    DrawAdditionalBoolChoice(BST_BorrowWhileReleaseRecasts,
                        FormatAndCache(BST_Config.BorrowWhileReleaseRecasts0, Borrow.ActionName(), TemperedRelease.ActionName()),
                        BST_Config.BorrowWhileReleaseRecastsDesc);

                    DrawAdditionalBoolChoice(BST_SummonBeforeCombat,
                        BST_Config.SummonBeforeCombat,
                        BST_Config.SummonBeforeCombatDesc);

                    DrawAdditionalBoolChoice(BST_RefreshBetweenPulls,
                        BST_Config.RefreshBetweenPulls,
                        BST_Config.RefreshBetweenPullsDesc);

                    ImGui.Spacing();
                    ImGuiEx.TextUnderlined(FormatAndCache(BST_Config.SectionBeastMode0, BeastMode.ActionName()));
                    ImGui.Spacing();

                    DrawAdditionalBoolChoice(BST_UseBeastskin, Beastskin.ActionName(), BST_Config.UseBeastskinDesc);
                    DrawAdditionalBoolChoice(BST_UseVileskin, Vileskin.ActionName(), BST_Config.UseVileskinDesc);
                    DrawAdditionalBoolChoice(BST_UseSeedsower, Seedsower.ActionName(), BST_Config.UseSeedsowerDesc);
                    DrawAdditionalBoolChoice(BST_UseScaleskin, Scaleskin.ActionName(), BST_Config.UseScaleskinDesc);
                    DrawAdditionalBoolChoice(BST_UseSoulCrush, SoulCrush.ActionName(), BST_Config.UseSoulCrushDesc);
                    DrawAdditionalBoolChoice(BST_UseQuellingWaveRanged, QuellingWave.ActionName(), BST_Config.UseQuellingWaveRangedDesc);

                    ImGui.Spacing();
                    ImGuiEx.TextUnderlined(BST_Config.SectionOther);
                    ImGui.Spacing();

                    DrawAdditionalBoolChoice(BST_UseShieldCharge, ShieldCharge.ActionName(), BST_Config.UseShieldChargeDesc);
                    DrawAdditionalBoolChoice(BST_UseRally,
                        FormatAndCache(BST_Config.UseRally0And1, Rally.ActionName(), RallyingCheer.ActionName()),
                        BST_Config.UseRallyDesc);
                    break;

                default:
                    break;
            }
        }
    }
}
