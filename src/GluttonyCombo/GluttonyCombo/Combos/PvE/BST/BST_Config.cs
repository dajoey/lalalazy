using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Extensions;
using GluttonyCombo.Resources.Localization.JobConfigs;
using static GluttonyCombo.Window.Functions.UserConfig;
using static GluttonyCombo.Window.Text;
using System.Collections.Generic;
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
            BST_MinFamiliarStay = new("BST_MinFamiliarStay", 10),
            BST_CruciblePetSwapHp = new("BST_CruciblePetSwapHp", 55),
            BST_CrucibleFinalStingHp = new("BST_CrucibleFinalStingHp", 30),
            BST_CrucibleAggro = new("BST_CrucibleAggro", (int)CrucibleAggroMode.Shadow),
            BST_CrucibleSnarlPartingLead = new("BST_CrucibleSnarlPartingLead", 15);

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
            BST_UseRally = new("BST_UseRally", true),
            BST_Crucible = new("BST_Crucible", true),
            BST_CrucibleAllowDisplacing = new("BST_CrucibleAllowDisplacing", true),
            BST_CrucibleHornWarning = new("BST_CrucibleHornWarning", true),
            BST_CrucibleTargeting = new("BST_CrucibleTargeting", true),
            BST_CrucibleScoreMode = new("BST_CrucibleScoreMode", false),
            BST_CrucibleSnarlParting = new("BST_CrucibleSnarlParting", false),
            BST_CrucibleCycleForDamage = new("BST_CrucibleCycleForDamage", false),
            BST_CruciblePrepullHorns = new("BST_CruciblePrepullHorns", false);

        internal static void Draw(Preset preset)
        {
            switch (preset)
            {
                case Preset.BST_ST_SimpleMode:
                case Preset.BST_AoE_SimpleMode:
                    DrawCrucible();
                    break;

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

                    DrawCrucible();
                    break;

                default:
                    break;
            }
        }

        private static void DrawCrucible()
        {
            ImGui.Spacing();
            ImGuiEx.TextUnderlined(BST_Config.SectionCrucible);
            ImGui.TextWrapped(CrucibleStatusText());
            DrawCrucibleAdvisor();
            ImGui.Spacing();

            DrawAdditionalBoolChoice(BST_Crucible, BST_Config.Crucible, BST_Config.CrucibleDesc);
            if (!BST_Crucible)
                return;

            DrawSliderInt(20, 90, BST_CruciblePetSwapHp,
                FormatAndCache(BST_Config.CruciblePetSwapHp0, PartingBlow.ActionName()));

            DrawSliderInt(5, 100, BST_CrucibleFinalStingHp,
                FormatAndCache(BST_Config.CrucibleFinalStingHp0, TemperedRelease.ActionName()));

            ImGui.TextUnformatted(FormatAndCache(BST_Config.CrucibleAggro0And1, Snarl.ActionName(), Challenge.ActionName()));
            DrawHorizontalRadioButton(BST_CrucibleAggro, BST_Config.CrucibleAggroOff, BST_Config.CrucibleAggroDesc, (int)CrucibleAggroMode.Off);
            DrawHorizontalRadioButton(BST_CrucibleAggro, BST_Config.CrucibleAggroShadow, BST_Config.CrucibleAggroDesc, (int)CrucibleAggroMode.Shadow);
            DrawHorizontalRadioButton(BST_CrucibleAggro, BST_Config.CrucibleAggroOn, BST_Config.CrucibleAggroDesc, (int)CrucibleAggroMode.On);

            DrawAdditionalBoolChoice(BST_CrucibleScoreMode, BST_Config.CrucibleScoreMode, BST_Config.CrucibleScoreModeDesc);

            DrawAdditionalBoolChoice(BST_CrucibleSnarlParting,
                FormatAndCache(BST_Config.CrucibleSnarlParting0And1, Snarl.ActionName(), PartingBlow.ActionName()),
                BST_Config.CrucibleSnarlPartingDesc);
            if (BST_CrucibleSnarlParting)
                DrawSliderInt(5, 30, BST_CrucibleSnarlPartingLead, BST_Config.CrucibleSnarlPartingLead);

            DrawAdditionalBoolChoice(BST_CrucibleAllowDisplacing,
                FormatAndCache(BST_Config.CrucibleAllowDisplacing0, TemperedRelease.ActionName()),
                BST_Config.CrucibleAllowDisplacingDesc);

            DrawAdditionalBoolChoice(BST_CrucibleHornWarning, BST_Config.CrucibleHornWarning, BST_Config.CrucibleHornWarningDesc);

            DrawAdditionalBoolChoice(BST_CrucibleTargeting, BST_Config.CrucibleTargeting, BST_Config.CrucibleTargetingDesc);

            DrawAdditionalBoolChoice(BST_CrucibleCycleForDamage,
                FormatAndCache(BST_Config.CrucibleCycleForDamage0, PartingBlow.ActionName()),
                BST_Config.CrucibleCycleForDamageDesc);

            DrawAdditionalBoolChoice(BST_CruciblePrepullHorns, BST_Config.CruciblePrepullHorns, BST_Config.CruciblePrepullHornsDesc);
        }

        private static int _advisorBoard;

        /// <summary> Beast picks per battle for a chosen board, from the captured roster (usable before reaching the board). </summary>
        internal static void DrawCrucibleAdvisor()
        {
            ImGui.Spacing();
            if (!ImGui.CollapsingHeader(BST_Config.CrucibleAdvisorHeader))
                return;

            var here = BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType);
            if (_advisorBoard == 0)
                _advisorBoard = here != 0 ? here : 1;

            ImGui.TextUnformatted(BST_Config.CrucibleAdvisorBoard);
            for (var b = 1; b <= 5; b++)
            {
                ImGui.SameLine();
                ImGui.RadioButton($"{b}##bstCrucibleBoard", ref _advisorBoard, b);
            }

            var board = BST_CrucibleData.Boards[_advisorBoard - 1];
            ImGui.TextWrapped($"{board.Name}: L{board.Level}{(board.ItemLevel > 0 ? $" / iL{board.ItemLevel}" : "")}, beast rank {board.BeastRank}");
            if (!CrucibleRosterLoaded)
                ImGui.TextWrapped(BST_Config.CrucibleAdvisorRosterUnknown);

            var roster = BST_CrucibleAdvisor.BoardRoster(board.Board, CrucibleBeastCaptured);
            ImGui.TextWrapped(string.Format(BST_Config.CrucibleAdvisorRoster0,
                string.Join(", ", roster.ConvertAll(r => $"{BeastName(r.Row)} ({r.Battles})"))));

            var current = here == board.Board ? CurrentCrucibleBattle() : -1;
            foreach (var battle in BST_CrucibleData.Battles)
            {
                if (battle.Board != board.Board)
                    continue;

                var label = BST_CrucibleData.BattleLabel(battle.Board, battle.Battle);
                var tag = battle.Role switch
                {
                    CrucibleRole.Boss => BST_Config.CrucibleAdvisorBoss,
                    CrucibleRole.EliteEnemy => BST_Config.CrucibleAdvisorElite,
                    _ => BST_Config.CrucibleAdvisorEnemy,
                };
                var header = $"{(battle.Battle == current ? "> " : "")}{label} ({tag}{(battle.RandomOnly ? ", " + BST_Config.CrucibleAdvisorRandom : "")})##bstCrucible{battle.Board}_{battle.Battle}";
                if (!ImGui.TreeNode(header))
                    continue;

                var weaknesses = new List<string>();
                foreach (var e in BST_CrucibleData.Enemies)
                    if (e.Board == battle.Board && e.Battle == battle.Battle)
                        weaknesses.Add($"{e.Name}: {e.Weakness}");
                ImGui.TextWrapped(string.Join("; ", weaknesses));
                ImGui.TextWrapped(string.Format(BST_Config.CrucibleAdvisorNeeds0, BST_CrucibleData.BattleNeeds(battle.Board, battle.Battle)));

                var picks = BST_CrucibleAdvisor.Pick(battle.Board, battle.Battle, CrucibleBeastCaptured);
                foreach (var pick in picks)
                    ImGui.BulletText($"{BeastName(pick.Row)}: {pick.Why}");

                var capture = BST_CrucibleAdvisor.WorthCapturing(battle.Board, battle.Battle, CrucibleBeastCaptured, picks);
                if (CrucibleRosterLoaded && capture.Count > 0)
                    ImGui.TextWrapped(BST_Config.CrucibleAdvisorCapture + " " + string.Join(", ",
                        capture.ConvertAll(c => $"{BeastName(c.Row)} (L{BST_Beasts.All[c.Row].CaptureLevel}: {c.Why})")));

                ImGui.TreePop();
            }
        }

        private static string BeastName(int row)
        {
            var name = BST_Beasts.All[row].Name;
            return name.Length == 0 ? "?" : char.ToUpperInvariant(name[0]) + name[1..];
        }
    }
}
