using Dalamud.Interface.Colors;
using ECommons.ImGuiMethods;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Extensions;
using GluttonyCombo.Resources.Localization.JobConfigs;
using GluttonyCombo.Window.Functions;
using static GluttonyCombo.Window.Functions.UserConfig;
using static GluttonyCombo.Window.Text;
namespace GluttonyCombo.Combos.PvE;

internal partial class OccultCrescent
{
    internal static class Config
    {
        internal static void Draw(Preset preset)
        {
            switch (preset)
            {
                case Preset.Phantom_Freelancer_OccultResuscitation:
                    DrawSliderInt(1, 100, Phantom_Freelancer_Resuscitation_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;

                case Preset.Phantom_Geomancer_Sunbath:
                    DrawSliderInt(1, 100, Phantom_Geomancer_Sunbath_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;

                case Preset.Phantom_Knight_PhantomGuard:
                    DrawSliderInt(1, 100, Phantom_Knight_PhantomGuard_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;
                case Preset.Phantom_Knight_Pray:
                    DrawSliderInt(1, 100, Phantom_Knight_Pray_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;
                case Preset.Phantom_Knight_OccultHeal:
                    DrawSliderInt(1, 100, Phantom_Knight_OccultHeal_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;
                case Preset.Phantom_Knight_Pledge:
                    DrawSliderInt(1, 100, Phantom_Knight_Pledge_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;
                case Preset.Phantom_Bard_MightyMarch:
                    DrawSliderInt(1, 100, Phantom_Bard_MightyMarch_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;

                case Preset.Phantom_Monk_OccultChakra:
                    DrawSliderInt(1, 100, Phantom_Monk_OccultChakra_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;

                case Preset.Phantom_Oracle_Blessing:
                    DrawSliderInt(1, 100, Phantom_Oracle_Blessing_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;

                case Preset.Phantom_Oracle_Starfall:
                    DrawSliderInt(91, 100, Phantom_Oracle_Starfall_Health,
                        Generics.PlayerHPGreaterOrEqual, 200);
                    break;

                case Preset.Phantom_Ranger_OccultUnicorn:
                    DrawSliderInt(1, 100, Phantom_Ranger_OccultUnicorn_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;

                case Preset.Phantom_Ranger_PhantomAim:
                    DrawSliderInt(1, 100, Phantom_Ranger_PhantomAim_Stop,
                        Generics.PlayerHPGreaterOrEqual, 200);
                    break;

                case Preset.Phantom_Thief_Steal:
                    DrawSliderInt(1, 50, Phantom_Thief_Steal_Health,
                        Generics.PlayerHPGreaterOrEqual, 200);
                    break;

                case Preset.Phantom_Samurai_Zeninage:
                    ImGui.Indent();
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudRed, Resources
                        .Localization.Content.OccultCrescent.Costly);
                    ImGui.Unindent();
                    break;

                case Preset.Phantom_Chemist_OccultPotion:
                    ImGui.Indent();
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudRed, Resources
                        .Localization.Content.OccultCrescent.Costly);
                    ImGui.Unindent();
                    DrawSliderInt(1, 100, Phantom_Chemist_OccultPotion_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;

                case Preset.Phantom_Chemist_OccultEther:
                    ImGui.Indent();
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudRed, Resources
                        .Localization.Content.OccultCrescent.Costly);
                    ImGui.Unindent();
                    DrawSliderInt(1, 10000, Phantom_Chemist_OccultEther_MP,
                        Generics.MPLessOrEqual, sliderIncrement: SliderIncrements.Hundreds);
                    break;

                case Preset.Phantom_Chemist_OccultElixir:
                    ImGui.Indent();
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudRed, Resources.Localization.Content.OccultCrescent.VeryCostly);
                    ImGui.Unindent();
                    DrawSliderInt(1, 100, Phantom_Chemist_OccultElixir_HP,
                        Resources.Localization.Content.OccultCrescent.AveragePartyHPLessOrEqual, 200);
                    DrawAdditionalBoolChoice(Phantom_Chemist_OccultElixir_RequireParty,
                        Resources.Localization.Content.OccultCrescent.AtLeast1PartyMember, "");
                    ImGui.Indent();
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudYellow, Resources.Localization.Content.OccultCrescent.NotAdvised);
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudYellow, Resources.Localization.Content.OccultCrescent.SliderShouldBeLow);
                    ImGui.Unindent();
                    break;

                case Preset.Phantom_TimeMage_OccultComet:
                    DrawAdditionalBoolChoice(Phantom_TimeMage_Comet_RequireSpeed,
                        FormatAndCache(Resources.Localization.Content.OccultCrescent.Requires0Or1ToUse2, Caster.Role.Swiftcast.ActionName(), Buffs.OccultQuick.StatusName(), OccultComet.ActionName()), "");
                    if (Phantom_TimeMage_Comet_RequireSpeed)
                    {
                        ImGui.Indent();
                        DrawAdditionalBoolChoice(
                            Phantom_TimeMage_Comet_UseSpeed,
                            FormatAndCache(Resources.Localization.Content.OccultCrescent.Add0Or1PriorUsing2, Caster.Role.Swiftcast.ActionName(), Buffs.OccultQuick.StatusName(), OccultComet.ActionName()), "");
                        ImGui.Unindent();
                    }
                    break;

                case Preset.Phantom_Ninja_Image:
                    DrawSliderInt(1, 100, Phantom_Ninja_Image_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;
                case Preset.Phantom_WhiteMage_OccultBlink:
                    DrawSliderInt(1, 100, Phantom_WhiteMage_OccultBlink_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;
                case Preset.Phantom_WhiteMage_OccultCureII:
                    DrawSliderInt(1, 100, Phantom_WhiteMage_OccultCureII_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;
                case Preset.Phantom_WhiteMage_OccultCureIII:
                    DrawSliderInt(1, 100, Phantom_WhiteMage_OccultCureIII_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;
                case Preset.Phantom_Summoner_EarthenWall:
                    DrawSliderInt(1, 100, Phantom_Summoner_EarthenWall_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;
                case Preset.Phantom_BlueMage_OccultMightyGuard:
                    DrawSliderInt(1, 100, Phantom_BlueMage_OccultMightyGuard_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;
                case Preset.Phantom_BlueMage_OccultWhiteWind:
                    DrawSliderInt(1, 100, Phantom_BlueMage_OccultWhiteWind_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    DrawSliderInt(1, 100, Phantom_BlueMage_OccultWhiteWind_SelfHealth,
                        "Only cast at or above this much of your OWN HP (it heals for your current HP)", 300);
                    break;

                case Preset.Phantom_Necromancer_DrainTouch:
                    ImGui.Indent();
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudYellow,
                        "Deep Freeze, Hell Wind, Chaos Drive and Doomsday each consume 10% of your maximum HP and inflict Doom on you for 10s, whether or not Drain Touch is active. Doom is only removed by healing to FULL within 10s, and Drain Touch's \"cannot drop below 1 HP\" effect does NOT stop it. They are therefore only cast inside the Drain Touch window, where they also gain potency and their rider effects. Drain Touch itself is held until a line spell is actually ready, so the two 40s timers stay in phase.");
                    ImGui.Unindent();
                    DrawSliderInt(1, 100, Phantom_Necromancer_HpFloorPct,
                        "Minimum own HP% before spending 10% HP + Doom on a line spell (this is a SURVIVAL floor: casting at 50% leaves you at 40% needing a heal to FULL within 10s)", 300);
                    break;
                case Preset.Phantom_RedMage_OccultCureII:
                    DrawSliderInt(1, 100, Phantom_RedMage_OccultCureII_Health,
                        Generics.StopFriendlyHpPercent100, 200);
                    break;

                case Preset.Phantom_RestrictToBuff:
                    ImGui.Indent();
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudYellow,
                        Resources.Localization.Content.OccultCrescent.BuffOnlyNotRecommended);
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudRed,
                        Resources.Localization.Content.OccultCrescent.BuffOnlyDont);
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudGrey,
                        Resources.Localization.Content.OccultCrescent.BuffOnlyList);
                    ImGui.Unindent();
                    break;
            }
        }
        #region Variables

        public static UserInt
            Phantom_Freelancer_Resuscitation_Health = new("Phantom_Freelancer_Resuscitation_Health", 50),
            Phantom_Geomancer_Sunbath_Health = new("Phantom_Geomancer_Sunbath_Health", 50),
            Phantom_Knight_PhantomGuard_Health = new("Phantom_Knight_PhantomGuard_Health", 50),
            Phantom_Knight_Pray_Health = new("Phantom_Knight_Pray_Health", 50),
            Phantom_Knight_OccultHeal_Health = new("Phantom_Knight_OccultHeal_Health", 50),
            Phantom_Knight_Pledge_Health = new("Phantom_Knight_Pledge_Health", 50),
            Phantom_Bard_MightyMarch_Health = new("Phantom_Bard_MightyMarch_Health", 50),
            Phantom_Monk_OccultChakra_Health = new("Phantom_Monk_OccultChakra_Health", 29),
            Phantom_Chemist_OccultPotion_Health = new("Phantom_Chemist_OccultPotion_Health", 50),
            Phantom_Chemist_OccultEther_MP = new("Phantom_Chemist_OccultEther_MP", 50),
            Phantom_Chemist_OccultElixir_HP = new("Phantom_Chemist_OccultElixir_HP", 25),
            Phantom_Oracle_Blessing_Health = new("Phantom_Oracle_Blessing_Health", 50),
            Phantom_Oracle_Starfall_Health = new("Phantom_Oracle_Starfall_Health", 100),
            Phantom_Ranger_OccultUnicorn_Health = new("Phantom_Ranger_OccultUnicorn_Health", 50),
            Phantom_Ranger_PhantomAim_Stop = new("Phantom_Ranger_PhantomAim_Stop", 30),
            Phantom_Thief_Steal_Health = new("Phantom_Thief_Steal_Health", 10),
            Phantom_Ninja_Image_Health = new("Phantom_Ninja_Image_Health", 50),
            Phantom_WhiteMage_OccultBlink_Health = new("Phantom_WhiteMage_OccultBlink_Health", 60),
            Phantom_WhiteMage_OccultCureII_Health = new("Phantom_WhiteMage_OccultCureII_Health", 70),
            Phantom_WhiteMage_OccultCureIII_Health = new("Phantom_WhiteMage_OccultCureIII_Health", 65),
            Phantom_Summoner_EarthenWall_Health = new("Phantom_Summoner_EarthenWall_Health", 60),
            Phantom_BlueMage_OccultMightyGuard_Health = new("Phantom_BlueMage_OccultMightyGuard_Health", 60),
            Phantom_BlueMage_OccultWhiteWind_Health = new("Phantom_BlueMage_OccultWhiteWind_Health", 60),
            Phantom_BlueMage_OccultWhiteWind_SelfHealth = new("Phantom_BlueMage_OccultWhiteWind_SelfHealth", 85),
            Phantom_Necromancer_HpFloorPct = new("Phantom_Necromancer_HpFloorPct", 90),
            Phantom_RedMage_OccultCureII_Health = new("Phantom_RedMage_OccultCureII_Health", 60);

        public static UserBool
            Phantom_Chemist_OccultElixir_RequireParty = new("Phantom_Chemist_OccultElixir_RequireParty", true),
            Phantom_TimeMage_Comet_RequireSpeed = new("Phantom_TimeMage_Comet_RequireSpeed", true),
            Phantom_TimeMage_Comet_UseSpeed = new("Phantom_TimeMage_Comet_UseSpeed", true);

        #endregion
    }
}
