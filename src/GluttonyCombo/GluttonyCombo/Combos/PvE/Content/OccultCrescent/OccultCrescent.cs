using System;
using System.Collections.Generic;
using GluttonyCombo.Data;
using GluttonyCombo.Extensions;
using ECommons.DalamudServices;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;
using static GluttonyCombo.Combos.PvE.OccultCrescent.Config;
using ContentHelper = ECommons.GameHelpers;
using IntendedUse = ECommons.ExcelServices.TerritoryIntendedUseEnum;
namespace GluttonyCombo.Combos.PvE;

internal partial class OccultCrescent
{
    /// In Occult Crescent (in the field or a field raid, including North Horn).
    public static unsafe bool IsInOccult =>
        FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.PublicContentOccultCrescent.GetInstance() != null ||
        (ContentHelper.Content.TerritoryIntendedUse == IntendedUse.Occult_Crescent &&
        (ContentCheck.IsInFieldOperations || ContentCheck.IsInFieldRaids));

    internal static bool TryGetPhantomAction(ref uint actionID)
    {
        if (!IsInOccult)
            return false;

        if (PlayerHP <= 90) LogPhantomHealDiag();

        // Emergency healing outranks every damage button, on every phantom job. See
        // TryGetPhantomHealAction() for why this cannot be left to dispatch order.
        if (TryGetPhantomHealAction(ref actionID)) return true;

        if (TryGetFreelancerAction(ref actionID)) return true;
        if (TryGetKnightAction(ref actionID)) return true;
        if (TryGetMonkAction(ref actionID)) return true;
        if (TryGetThiefAction(ref actionID)) return true;
        if (TryGetSamuraiAction(ref actionID)) return true;
        if (TryGetBerserkerAction(ref actionID)) return true;
        if (TryGetRangerAction(ref actionID)) return true;
        if (TryGetTimeMageAction(ref actionID)) return true;
        if (TryGetChemistAction(ref actionID)) return true;
        if (TryGetBardAction(ref actionID)) return true;
        if (TryGetOracleAction(ref actionID)) return true;
        if (TryGetCannoneerAction(ref actionID)) return true;
        if (TryGetGeomancerAction(ref actionID)) return true;
        if (TryGetDancerAction(ref actionID)) return true;
        if (TryGetMysticKnightAction(ref actionID)) return true;
        if (TryGetGladiatorAction(ref actionID)) return true;

        // 7.55 phantom jobs (stopgap -- see OccultCrescent_755.cs)
        if (TryGet755Action(ref actionID)) return true;

        return false;
    }

    /// <summary>
    ///     Healing pass, run BEFORE any phantom damage dispatch.
    ///     <para/>
    ///     <see cref="TryGetPhantomAction" /> dispatches strictly by job, in a fixed order:
    ///     sixteen pre-7.55 handlers and then the eight 7.55 ones. The first handler holding an
    ///     enabled, slotted, ready action wins outright and nothing below it is ever evaluated.
    ///     That made priority a function of job order rather than urgency - Phantom Ninja's Fuma
    ///     Shuriken outranked Phantom Red Mage's Occult Cure II at any HP, and all four 7.55
    ///     cures sat behind every one of the sixteen pre-7.55 handlers. The user's HP slider was
    ///     always being honoured; the heal simply never got asked while a damage action upstream
    ///     kept answering first.
    ///     <para/>
    ///     Every condition below is a verbatim copy of the one in its owning job handler - same
    ///     parent preset, same child preset, same slider, same weave gating - so this pass can
    ///     never fire something that would not have fired anyway. It only lets a heal win a race
    ///     it was previously losing. The originals stay in place: once a heal wins here the copy
    ///     downstream is unreachable, and when no heal is due both copies decline identically.
    /// </summary>
    private static DateTime _phantomDiagLast = DateTime.MinValue;

    /// <summary>
    ///     Throttled diagnostic for "the phantom heal never fires". Four separate fixes were
    ///     shipped against this on 2026-08-02 on the strength of static reading alone and none
    ///     of them worked, so this stops the guessing: it prints the five duty-action slots the
    ///     plugin can actually see, then every phantom heal candidate with each of the four
    ///     conditions that gate it. Whichever column reads False is the answer.
    ///     <para/>
    ///     Information level so it lands in /xllog without the user enabling Verbose. Throttled
    ///     to 10s and only while damaged, so it cannot flood the log.
    /// </summary>
    private static void LogPhantomHealDiag()
    {
        if ((DateTime.Now - _phantomDiagLast).TotalSeconds < 10) return;
        _phantomDiagLast = DateTime.Now;

        Svc.Log.Information(
            $"[PhantomDiag] duty slots seen: {Action1} / {Action2} / {Action3} / {Action4} / {Action5} " +
            $"| HP={PlayerHP} | weaveWindow={CanWeaveNow} | inOccult={IsInOccult}");

        void Row(string label, Preset parent, Preset child, uint act, double threshold)
        {
            Svc.Log.Information(
                $"[PhantomDiag] {label,-22} id={act,-6} parent={IsEnabled(parent),-5} child={IsEnabled(child),-5} " +
                $"equipped={HasActionEquipped(act),-5} ready={ActionReady(act),-5} hpGate={PlayerHP <= threshold,-5} ({PlayerHP} <= {threshold})");
        }

        Row("Knight_OccultHeal", Preset.Phantom_Knight, Preset.Phantom_Knight_OccultHeal, OccultHeal, Phantom_Knight_OccultHeal_Health);
        Row("Monk_OccultChakra", Preset.Phantom_Monk, Preset.Phantom_Monk_OccultChakra, OccultChakra, Phantom_Monk_OccultChakra_Health);
        Row("Ranger_OccultUnicorn", Preset.Phantom_Ranger, Preset.Phantom_Ranger_OccultUnicorn, OccultUnicorn, Phantom_Ranger_OccultUnicorn_Health);
        Row("Freelancer_Resusc", Preset.Phantom_Freelancer, Preset.Phantom_Freelancer_OccultResuscitation, OccultResuscitation, Phantom_Freelancer_Resuscitation_Health);
        Row("Chemist_OccultPotion", Preset.Phantom_Chemist, Preset.Phantom_Chemist_OccultPotion, OccultPotion, Phantom_Chemist_OccultPotion_Health);
        Row("Geomancer_Sunbath", Preset.Phantom_Geomancer, Preset.Phantom_Geomancer_Sunbath, Sunbath, Phantom_Geomancer_Sunbath_Health);
        Row("WHM_OccultCureII", Preset.Phantom_WhiteMage, Preset.Phantom_WhiteMage_OccultCureII, P755.WHM_OccultCureII, Phantom_WhiteMage_OccultCureII_Health);
        Row("WHM_OccultCureIII", Preset.Phantom_WhiteMage, Preset.Phantom_WhiteMage_OccultCureIII, P755.WHM_OccultCureIII, Phantom_WhiteMage_OccultCureIII_Health);
        Row("RDM_OccultCureII", Preset.Phantom_RedMage, Preset.Phantom_RedMage_OccultCureII, P755.RDM_OccultCureII, Phantom_RedMage_OccultCureII_Health);
        Row("BLU_OccultWhiteWind", Preset.Phantom_BlueMage, Preset.Phantom_BlueMage_OccultWhiteWind, P755.BLU_OccultWhiteWind, Phantom_BlueMage_OccultWhiteWind_Health);
    }

    /// <summary>
    ///     Entry point for the autorotation's emergency scheduler. The damage pipeline reaches
    ///     the same logic through TryGetPhantomAction(), but that path only runs once a DPS
    ///     preset has already won a GCD slot, which an emergency heal repeatedly failed to do.
    /// </summary>
    /// <summary>Who a phantom cure is able to help.</summary>
    internal enum PhantomHealScope
    {
        /// <summary>Only ever affects the caster (potions, chakra, unicorn, sunbath...).</summary>
        SelfOnly,

        /// <summary>A targeted cure - castable on the caster or on any party member.</summary>
        AnyAlly,

        /// <summary>An AoE centred on the caster; the target passed is always the caster.</summary>
        PartyWide,
    }

    /// <summary>One usable phantom cure, with the slider that governs it.</summary>
    internal readonly struct PhantomHealOption
    {
        internal readonly uint Action;
        internal readonly double Threshold;
        internal readonly PhantomHealScope Scope;
        internal readonly bool IsWeave;

        internal PhantomHealOption(uint action, double threshold, PhantomHealScope scope, bool isWeave)
        {
            Action = action;
            Threshold = threshold;
            Scope = scope;
            IsWeave = isWeave;
        }
    }

    /// <summary>
    ///     Every phantom cure that is enabled, slotted and off cooldown right now, with the scope
    ///     and threshold the scheduler needs to pick a target.
    ///     <para/>
    ///     This deliberately does NOT apply any HP gate and does NOT stop at the first match. The
    ///     previous design returned a single cure chosen by a fixed job order and gated on the
    ///     caster's own HP, which meant: a cure on cooldown aborted the whole attempt instead of
    ///     falling through to the next; a healthy caster with a dying party selected nothing; and
    ///     the AoE cures, being checked last and gated on party AVERAGE HP, effectively never
    ///     fired. Handing the caller the full set lets it choose by urgency and fall back
    ///     properly. <see cref="IsEnabledAndUsable" /> already covers preset, equipped and
    ///     ready, so anything on cooldown simply is not yielded.
    /// </summary>
    internal static IEnumerable<PhantomHealOption> EnumerateHealOptions()
    {
        if (!IsInOccult)
            yield break;

        // ---- oGCD self heals: cost a weave slot, never a GCD ----
        if (IsEnabled(Preset.Phantom_Knight) &&
            IsEnabledAndUsable(Preset.Phantom_Knight_OccultHeal, OccultHeal) && PlayerMP >= 5000)
            yield return new PhantomHealOption(OccultHeal, Phantom_Knight_OccultHeal_Health, PhantomHealScope.SelfOnly, true);

        if (IsEnabled(Preset.Phantom_Monk) &&
            IsEnabledAndUsable(Preset.Phantom_Monk_OccultChakra, OccultChakra))
            yield return new PhantomHealOption(OccultChakra, Phantom_Monk_OccultChakra_Health, PhantomHealScope.SelfOnly, true);

        if (IsEnabled(Preset.Phantom_Ranger) &&
            IsEnabledAndUsable(Preset.Phantom_Ranger_OccultUnicorn, OccultUnicorn) &&
            !HasStatusEffect(Buffs.OccultUnicorn, anyOwner: true))
            yield return new PhantomHealOption(OccultUnicorn, Phantom_Ranger_OccultUnicorn_Health, PhantomHealScope.SelfOnly, true);

        if (IsEnabled(Preset.Phantom_Oracle) &&
            IsEnabledAndUsable(Preset.Phantom_Oracle_Blessing, Blessing) &&
            HasStatusEffect(Buffs.PredictionOfBlessing))
            yield return new PhantomHealOption(Blessing, Phantom_Oracle_Blessing_Health, PhantomHealScope.SelfOnly, true);

        // ---- GCD self heals ----
        if (IsEnabled(Preset.Phantom_Freelancer) &&
            IsEnabledAndUsable(Preset.Phantom_Freelancer_OccultResuscitation, OccultResuscitation))
            yield return new PhantomHealOption(OccultResuscitation, Phantom_Freelancer_Resuscitation_Health, PhantomHealScope.SelfOnly, false);

        if (IsEnabled(Preset.Phantom_Chemist) &&
            IsEnabledAndUsable(Preset.Phantom_Chemist_OccultPotion, OccultPotion))
            yield return new PhantomHealOption(OccultPotion, Phantom_Chemist_OccultPotion_Health, PhantomHealScope.SelfOnly, false);

        if (IsEnabled(Preset.Phantom_Geomancer) && IsEnabled(Preset.Phantom_Geomancer_Weather) &&
            IsEnabledAndUsable(Preset.Phantom_Geomancer_Sunbath, Sunbath))
            yield return new PhantomHealOption(Sunbath, Phantom_Geomancer_Sunbath_Health, PhantomHealScope.SelfOnly, false);

        // ---- targeted cures: caster OR any party member ----
        if (IsEnabled(Preset.Phantom_WhiteMage) &&
            IsEnabledAndUsable(Preset.Phantom_WhiteMage_OccultCureII, P755.WHM_OccultCureII))
            yield return new PhantomHealOption(P755.WHM_OccultCureII, Phantom_WhiteMage_OccultCureII_Health, PhantomHealScope.AnyAlly, false);

        if (IsEnabled(Preset.Phantom_RedMage) &&
            IsEnabledAndUsable(Preset.Phantom_RedMage_OccultCureII, P755.RDM_OccultCureII))
            yield return new PhantomHealOption(P755.RDM_OccultCureII, Phantom_RedMage_OccultCureII_Health, PhantomHealScope.AnyAlly, false);

        // ---- party-wide AoE, centred on the caster ----
        if (IsEnabled(Preset.Phantom_WhiteMage) &&
            IsEnabledAndUsable(Preset.Phantom_WhiteMage_OccultCureIII, P755.WHM_OccultCureIII) && IsInParty())
            yield return new PhantomHealOption(P755.WHM_OccultCureIII, Phantom_WhiteMage_OccultCureIII_Health, PhantomHealScope.PartyWide, false);

        // White Wind heals for the caster's CURRENT HP, so it is worthless when we are low.
        if (IsEnabled(Preset.Phantom_BlueMage) &&
            IsEnabledAndUsable(Preset.Phantom_BlueMage_OccultWhiteWind, P755.BLU_OccultWhiteWind) &&
            PlayerHP >= Phantom_BlueMage_OccultWhiteWind_SelfHealth)
            yield return new PhantomHealOption(P755.BLU_OccultWhiteWind, Phantom_BlueMage_OccultWhiteWind_Health, PhantomHealScope.PartyWide, false);

        if (IsEnabled(Preset.Phantom_Chemist) &&
            IsEnabledAndUsable(Preset.Phantom_Chemist_OccultElixir, OccultElixir) && InCombatNow &&
            (!Phantom_Chemist_OccultElixir_RequireParty || IsInParty()))
            yield return new PhantomHealOption(OccultElixir, Phantom_Chemist_OccultElixir_HP, PhantomHealScope.PartyWide, false);
    }

    /// <summary>
    ///     Combo-path healing, for when the player presses a rotation button rather than running
    ///     autorotation. The autorotation path does not come through here - it uses
    ///     <see cref="EnumerateHealOptions" /> directly via the emergency scheduler, which can
    ///     hold the GCD and pick an ally. This is the button-press equivalent: same candidate
    ///     set, self-centred, first usable match wins.
    /// </summary>
    private static bool TryGetPhantomHealAction(ref uint actionID)
    {
        foreach (var option in EnumerateHealOptions())
        {
            // oGCDs only in a weave window; casts only outside one.
            if (option.IsWeave != CanWeaveNow)
                continue;

            if (option.Scope == PhantomHealScope.PartyWide)
            {
                if (!IsInParty() || GetPartyAvgHPPercent() > option.Threshold)
                    continue;
            }
            else if (PlayerHP > option.Threshold)
            {
                continue;
            }

            actionID = option.Action;
            return true;
        }

        return false;
    }

    private static bool TryGetFreelancerAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Freelancer))
            return false;

        if (IsEnabledAndUsable(Preset.Phantom_Freelancer_OccultResuscitation, OccultResuscitation) &&
            PlayerHP <= Phantom_Freelancer_Resuscitation_Health && !CanWeaveNow)
        {
            actionID = OccultResuscitation; // self-heal
            return true;
        }

        return false;
    }

    private static bool TryGetKnightAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Knight))
            return false;

        if (IsEnabledAndUsable(Preset.Phantom_Knight_Pray, Pray) &&
            PlayerHP <= Phantom_Knight_Pray_Health && !HasStatusEffect(Buffs.Pray) && !CanWeaveNow)
        {
            actionID = Pray; // regen
            return true;
        }

        // Skip things we want to weave, if not in a weave window
        if (!CanWeaveNow) return false;

        if (IsEnabledAndUsable(Preset.Phantom_Knight_PhantomGuard, PhantomGuard) &&
            PlayerHP <= Phantom_Knight_PhantomGuard_Health)
        {
            actionID = PhantomGuard; // mit
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Knight_OccultHeal, OccultHeal) &&
            PlayerHP <= Phantom_Knight_OccultHeal_Health && PlayerMP >= 5000)
        {
            actionID = OccultHeal; // heal
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Knight_Pledge, Pledge) &&
            PlayerHP <= Phantom_Knight_Pledge_Health)
        {
            actionID = Pledge; // inv
            return true;
        }

        return false;
    }

    private static bool TryGetMonkAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Monk))
            return false;

        if (IsEnabledAndUsable(Preset.Phantom_Monk_Counterstance, Counterstance) &&
            IsPlayerTargeted() && !HasStatusEffect(Buffs.Counterstance) && !CanWeaveNow)
        {
            actionID = Counterstance; // counterstance
            return true;
        }

        // Skip things we want to weave, if not in a weave window
        if (!CanWeaveNow) return false;

        if (IsEnabledAndUsable(Preset.Phantom_Monk_OccultChakra, OccultChakra) &&
            PlayerHP <= Phantom_Monk_OccultChakra_Health)
        {
            actionID = OccultChakra; // heal
            return true;
        }

        // Skip if no damage buff, and user wants things under buffs
        if (IsEnabled(Preset.Phantom_RestrictToBuff) &&
            !Bursting.PlayerIsDamageBuffed)
            return false;

        if (IsEnabledAndUsable(Preset.Phantom_Monk_PhantomKick, PhantomKick) &&
            !IsMovingNow && InActionRange(PhantomKick))
        {
            actionID = PhantomKick; // damage buff + dash
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Monk_OccultCounter, OccultCounter) &&
            InActionRange(OccultCounter))
        {
            actionID = OccultCounter; // counter-attack
            return true;
        }

        return false;
    }

    private static bool TryGetThiefAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Thief))
            return false;

        if (IsEnabledAndUsable(Preset.Phantom_Thief_Vigilance, Vigilance) &&
            !HasStatusEffect(Buffs.Vigilance) && !InCombatNow)
        {
            actionID = Vigilance; // damage buff out of combat
            return true;
        }

        // Skip things we want to weave, if not in a weave window
        if (!CanWeaveNow) return false;

        if (IsEnabledAndUsable(Preset.Phantom_Thief_OccultSprint, OccultSprint) &&
            IsMovingNow)
        {
            actionID = OccultSprint; // movement speed
            return true;
        }

        if (HasTargetNow && InActionRange(Steal))
        {
            if (IsEnabledAndUsable(Preset.Phantom_Thief_Steal, Steal) &&
                TargetHP <= Phantom_Thief_Steal_Health)
            {
                actionID = Steal; // drops items if used before death
                return true;
            }

            // Skip if no damage buff, and user wants things under buffs
            if (IsEnabled(Preset.Phantom_RestrictToBuff) &&
                !Bursting.PlayerIsDamageBuffed)
                return false;

            if (IsEnabledAndUsable(Preset.Phantom_Thief_PilferWeapon, PilferWeapon) &&
                !HasStatusEffect(Debuffs.WeaponPlifered, CurrentTarget))
            {
                actionID = PilferWeapon; // weaken target
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSamuraiAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Samurai))
            return false;

        if (IsEnabledAndUsable(Preset.Phantom_Samurai_Shirahadori, Shirahadori) &&
            CanWeaveNow && BeingTargetedHostile)
        {
            actionID = Shirahadori; // inv against physical
            return true;
        }

        // GCDs
        if (!CanWeaveNow && HasTargetNow)
        {
            if (IsEnabledAndUsable(Preset.Phantom_Samurai_Mineuchi, Mineuchi) &&
                CanInterruptEnemy() && InActionRange(Mineuchi))
            {
                actionID = Mineuchi; // stun
                return true;
            }

            // Skip if no damage buff, and user wants things under buffs
            if (IsEnabled(Preset.Phantom_RestrictToBuff) &&
                !Bursting.PlayerIsDamageBuffed)
                return false;

            if (IsEnabledAndUsable(Preset.Phantom_Samurai_Zeninage, Zeninage) &&
                ActionWatching.NumberOfGcdsUsed > 4)
            {
                actionID = Zeninage; // burst
                return true;
            }

            if (IsEnabledAndUsable(Preset.Phantom_Samurai_Iainuki, Iainuki) &&
                !IsMovingNow && InActionRange(Iainuki))
            {
                actionID = Iainuki; // cone
                return true;
            }
        }

        return false;
    }

    private static bool TryGetBerserkerAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Berserker))
            return false;

        if (!HasTargetNow) return false;

        // Skip if no damage buff, and user wants things under buffs
        if (IsEnabled(Preset.Phantom_RestrictToBuff) &&
            !Bursting.PlayerIsDamageBuffed)
            return false;

        if (IsEnabledAndUsable(Preset.Phantom_Berserker_Rage, Rage) &&
            InActionRange(Rage) && CanWeaveNow)
        {
            actionID = Rage; // buff
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Berserker_DeadlyBlow, DeadlyBlow) &&
            GetStatusEffectRemainingTime(Buffs.PentupRage) <= 3f && (HasStatusEffect(Buffs.PentupRage) || CurrentJobLevel < 3) && InActionRange(DeadlyBlow) && !CanWeaveNow)
        {
            actionID = DeadlyBlow; // better when buff timer is low
            return true;
        }

        return false;
    }

    private static bool TryGetRangerAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Ranger))
            return false;

        // Skip things we want to weave, if not in a weave window
        if (!CanWeaveNow) return false;

        if (IsEnabledAndUsable(Preset.Phantom_Ranger_OccultUnicorn, OccultUnicorn) &&
            !HasStatusEffect(Buffs.OccultUnicorn, anyOwner: true) && PlayerHP <= Phantom_Ranger_OccultUnicorn_Health)
        {
            actionID = OccultUnicorn; // heal
            return true;
        }

        // Skip if no damage buff, and user wants things under buffs
        if (IsEnabled(Preset.Phantom_RestrictToBuff) &&
            !Bursting.PlayerIsDamageBuffed)
            return false;

        if (IsEnabledAndUsable(Preset.Phantom_Ranger_PhantomAim, PhantomAim) &&
            TargetHP >= Phantom_Ranger_PhantomAim_Stop)
        {
            actionID = PhantomAim; // damage buff
            return true;
        }

        // Ground-target action OccultFalcon intentionally left commented as in original

        return false;
    }

    private static bool TryGetTimeMageAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_TimeMage))
            return false;

        if (IsEnabledAndUsable(Preset.Phantom_TimeMage_OccultMageMasher, OccultMageMasher) &&
            HasTargetNow && !HasStatusEffect(Debuffs.OccultMageMasher, CurrentTarget) && CanWeaveNow)
        {
            actionID = OccultMageMasher; // weaken target's magic attack
            return true;
        }

        if (CanWeaveNow) return false;

        if (IsEnabledAndUsable(Preset.Phantom_TimeMage_OccultQuick, OccultQuick) &&
            !HasStatusEffect(Buffs.OccultQuick) && ActionWatching.NumberOfGcdsUsed > 3)
        {
            actionID = OccultQuick; // damage buff
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_TimeMage_OccultDispel, OccultDispel) &&
            HasTargetNow && HasPhantomDispelStatus(CurrentTarget))
        {
            actionID = OccultDispel; // cleanse
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_TimeMage_OccultComet, OccultComet))
        {

            // Skip if no damage buff, and user wants things under buffs
            if (IsEnabled(Preset.Phantom_RestrictToBuff) &&
                !Bursting.PlayerIsDamageBuffed)
                return false;

            // Make the comet fast
            if (Phantom_TimeMage_Comet_RequireSpeed &&
                Phantom_TimeMage_Comet_UseSpeed &&
                !HasStatusEffect(Buffs.OccultQuick) && !JustUsed(OccultQuick) &&
                !HasStatusEffect(RoleActions.Magic.Buffs.Swiftcast) && !JustUsed(RoleActions.Magic.Swiftcast) &&
                !HasStatusEffect(BLM.Buffs.Triplecast) && !JustUsed(BLM.Triplecast) &&
                !HasStatusEffect(PLD.Buffs.Requiescat) && !JustUsed(PLD.Imperator) &&
                !HasStatusEffect(RDM.Buffs.Dualcast))
            {
                if (HasActionEquipped(OccultQuick) && ActionReady(OccultQuick))
                {
                    actionID = OccultQuick;
                    return true;
                }

                if (ActionReady(RoleActions.Magic.Swiftcast))
                {
                    actionID = RoleActions.Magic.Swiftcast;
                    return true;
                }
            }

            if (!Phantom_TimeMage_Comet_RequireSpeed ||
                HasStatusEffect(Buffs.OccultQuick) ||
                HasStatusEffect(RoleActions.Magic.Buffs.Swiftcast) ||
                HasStatusEffect(BLM.Buffs.Triplecast) ||
                HasStatusEffect(PLD.Buffs.Requiescat) ||
                HasStatusEffect(RDM.Buffs.Dualcast))
            {
                actionID = OccultComet; // damage
                return true;
            }
        }

        if (IsEnabledAndUsable(Preset.Phantom_TimeMage_OccultSlowga, OccultSlowga) &&
            HasTargetNow && !HasStatusEffect(Debuffs.Slow, CurrentTarget) &&
            (IsNotEnabled(Preset.Phantom_TimeMage_OccultSlowga_Wait) ||
             (ICDTracker.TimeUntilExpired(Debuffs.Slow, CurrentTarget.GameObjectId) < TimeSpan.FromSeconds(1.5) ||
              ICDTracker.NumberOfTimesApplied(Debuffs.Slow, CurrentTarget.GameObjectId) < 3)))
        {
            actionID = OccultSlowga; // aoe slow
            return true;
        }

        return false;
    }

    private static bool TryGetChemistAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Chemist))
            return false;

        if (CanWeaveNow) return false;

        if (IsEnabledAndUsable(Preset.Phantom_Chemist_Revive, Revive) &&
            CurrentTarget.IfCanUseOn(Revive).IfDead() is not null)
        {
            actionID = Revive;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Chemist_OccultPotion, OccultPotion) &&
            PlayerHP <= Phantom_Chemist_OccultPotion_Health)
        {
            actionID = OccultPotion;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Chemist_OccultEther, OccultEther) &&
            PlayerMP <= Phantom_Chemist_OccultEther_MP)
        {
            actionID = OccultEther;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Chemist_OccultElixir, OccultElixir) &&
            GetPartyAvgHPPercent() <= Phantom_Chemist_OccultElixir_HP && InCombatNow &&
            (!Phantom_Chemist_OccultElixir_RequireParty || IsInParty()))
        {
            actionID = OccultElixir;
            return true;
        }

        return false;
    }

    private static bool TryGetBardAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Bard))
            return false;

        if (!CanWeaveNow) return false;

        // Skip if no damage buff, and user wants things under buffs
        if (IsEnabled(Preset.Phantom_RestrictToBuff) &&
            !Bursting.PlayerIsDamageBuffed)
            return false;

        if (!IsEnabled(Preset.Phantom_RestrictToBuff) || Bursting.PlayerIsDamageBuffed)
        {
            if (IsEnabledAndUsable(Preset.Phantom_Bard_HerosRime, HerosRime))
            {
                actionID = HerosRime; // burst song
                return true;
            }
        }

        if (IsEnabledAndUsable(Preset.Phantom_Bard_OffensiveAria, OffensiveAria) &&
            !HasStatusEffect(Buffs.OffensiveAria) && !HasStatusEffect(Buffs.HerosRime, anyOwner: true))
        {
            actionID = OffensiveAria; // off-song
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Bard_RomeosBallad, RomeosBallad) &&
            CanInterruptEnemy())
        {
            actionID = RomeosBallad; // interrupt
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Bard_MightyMarch, MightyMarch) &&
            !HasStatusEffect(Buffs.MightyMarch) && PlayerHP <= Phantom_Bard_MightyMarch_Health)
        {
            actionID = MightyMarch; // aoe heal
            return true;
        }

        return false;
    }

    private static bool TryGetOracleAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Oracle))
            return false;

        if (!IsEnabled(Preset.Phantom_RestrictToBuff) || Bursting.PlayerIsDamageBuffed)
        {
            if (IsEnabledAndUsable(Preset.Phantom_Oracle_Predict, Predict) && InCombatNow && !CanWeaveNow &&
                !HasStatusEffect(Buffs.PredictionOfJudgment) && !HasStatusEffect(Buffs.PredictionOfCleansing) &&
                !HasStatusEffect(Buffs.PredictionOfBlessing) && !HasStatusEffect(Buffs.PredictionOfStarfall))
            {
                actionID = Predict; // start of the chain
                return true;
            }
        }

        // Skip things we want to weave, if not in a weave window
        if (!CanWeaveNow) return false;

        if (IsEnabledAndUsable(Preset.Phantom_Oracle_Blessing, Blessing) &&
            HasStatusEffect(Buffs.PredictionOfBlessing) && PlayerHP <= Phantom_Oracle_Blessing_Health)
        {
            actionID = Blessing; // heal
            return true;
        }

        // Skip if no damage buff, and user wants things under buffs
        if (IsEnabled(Preset.Phantom_RestrictToBuff) &&
            !Bursting.PlayerIsDamageBuffed)
            return false;

        if (IsEnabledAndUsable(Preset.Phantom_Oracle_PhantomJudgment, PhantomJudgment) &&
            HasStatusEffect(Buffs.PredictionOfJudgment))
        {
            actionID = PhantomJudgment; // damage + heal
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Oracle_Cleansing, Cleansing) &&
            HasStatusEffect(Buffs.PredictionOfCleansing)) // removed interrupt. it hits 20% harder than Judgment. 120k aoe.
        {
            actionID = Cleansing; // damage plus interrupt
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Oracle_Starfall, Starfall) &&
            HasStatusEffect(Buffs.PredictionOfStarfall) && PlayerHP >= Phantom_Oracle_Starfall_Health)
        {
            actionID = Starfall; // damage + 90% total HP damage to self
            return true;
        }

        return false;
    }

    private static bool TryGetCannoneerAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Cannoneer))
            return false;

        // GCDs
        if (CanWeaveNow || !HasTargetNow) return false;

        // Skip if no damage buff, and user wants things under buffs
        if (IsEnabled(Preset.Phantom_RestrictToBuff) &&
            !Bursting.PlayerIsDamageBuffed)
            return false;

        if (IsEnabledAndUsable(Preset.Phantom_Cannoneer_SilverCannon, SilverCannon) &&
            ((!HasStatusEffect(Debuffs.SilverSickness, CurrentTarget, true) ||
              GetStatusEffectRemainingTime(Debuffs.SilverSickness, CurrentTarget, true) < 30f) ||
             IsNotEnabled(Preset.Phantom_Cannoneer_HolyCannon)))
        {
            actionID = SilverCannon; // debuff
            return true;
        }

        foreach((Preset preset, uint action) in new[]
        {
            (Preset.Phantom_Cannoneer_PhantomFire, PhantomFire),
            (Preset.Phantom_Cannoneer_HolyCannon, HolyCannon),
            (Preset.Phantom_Cannoneer_DarkCannon, DarkCannon),
            (Preset.Phantom_Cannoneer_ShockCannon, ShockCannon)
        })
        {
            if (IsEnabledAndUsable(preset, action))
            {
                actionID = action;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetGeomancerAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Geomancer))
            return false;

        if (IsEnabled(Preset.Phantom_Geomancer_Weather) && !CanWeaveNow)
        {
            if (IsEnabledAndUsable(Preset.Phantom_Geomancer_Sunbath, Sunbath) &&
                PlayerHP <= Phantom_Geomancer_Sunbath_Health)
            {
                actionID = Sunbath; // heal
                return true;
            }

            if (IsEnabledAndUsable(Preset.Phantom_Geomancer_AetherialGain, AetherialGain) &&
                !HasStatusEffect(Buffs.AetherialGain) &&
                (!IsEnabled(Preset.Phantom_RestrictToBuff) || Bursting.PlayerIsDamageBuffed))
            {
                actionID = AetherialGain; // damage buff
                return true;
            }

            if (IsEnabledAndUsable(Preset.Phantom_Geomancer_CloudyCaress, CloudyCaress) &&
                !HasStatusEffect(Buffs.CloudyCaress))
            {
                actionID = CloudyCaress; // Increases HP recovery
                return true;
            }

            if (IsEnabledAndUsable(Preset.Phantom_Geomancer_BlessedRain, BlessedRain) &&
                !HasStatusEffect(Buffs.BlessedRain))
            {
                actionID = BlessedRain; // shield
                return true;
            }

            if (IsEnabledAndUsable(Preset.Phantom_Geomancer_MistyMirage, MistyMirage) &&
                !HasStatusEffect(Buffs.MistyMirage))
            {
                actionID = MistyMirage; // evasion
                return true;
            }

            if (IsEnabledAndUsable(Preset.Phantom_Geomancer_HastyMirage, HastyMirage) &&
                !HasStatusEffect(Buffs.HastyMirage))
            {
                actionID = HastyMirage; // movement speed
                return true;
            }
        }

        // Skip things we want to weave, if not in a weave window
        if (!CanWeaveNow) return false;

        if (IsEnabledAndUsable(Preset.Phantom_Geomancer_BattleBell, BattleBell) &&
            !HasStatusEffect(Buffs.BattleBell))
        {
            actionID = BattleBell; // buff
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Geomancer_RingingRespite, RingingRespite) &&
            !HasStatusEffect(Buffs.RingingRespite))
        {
            actionID = RingingRespite; // heal after damage
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Geomancer_Suspend, Suspend) &&
            !HasStatusEffect(Buffs.Suspend))
        {
            actionID = Suspend; // float
            return true;
        }

        // GCDs

        return false;
    }

    private static bool TryGetMysticKnightAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_MysticKnight))
            return false;

        if (IsEnabledAndUsable(Preset.Phantom_MysticKnight_MagicShell, MagicShell) && CanWeave() && InCombat())
        {
            actionID = MagicShell;
            return true;
        }

        if (CanWeaveNow) return false;

        // Skip if no damage buff, and user wants things under buffs
        if (IsEnabled(Preset.Phantom_RestrictToBuff) &&
            !Bursting.PlayerIsDamageBuffed)
            return false;

        if (IsEnabledAndUsable(Preset.Phantom_MysticKnight_BlazingSpellblade, BlazingSpellblade) && !CanWeave() &&
            HasBattleTarget() && InActionRange(BlazingSpellblade) &&
            (!HasStatusEffect(Buffs.BlazingSpellblade) || GetStatusEffectRemainingTime(Buffs.BlazingSpellblade) <= 15))
        {
            actionID = BlazingSpellblade;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_MysticKnight_HolySpellblade, HolySpellblade) && !CanWeave() &&
            HasBattleTarget() && InActionRange(BlazingSpellblade))
        {
            actionID = HolySpellblade;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_MysticKnight_SunderingSpellblade, SunderingSpellblade) && !CanWeave() &&
            HasBattleTarget() && InActionRange(SunderingSpellblade))
        {
            actionID = SunderingSpellblade;
            return true;
        }

        return false;
    }

    private static bool TryGetDancerAction(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Dancer))
            return false;

        if (!IsEnabled(Preset.Phantom_RestrictToBuff) || Bursting.PlayerIsDamageBuffed)
        {
            if (IsEnabledAndUsable(Preset.Phantom_Dancer_Dance, Dance) && CanWeave())
            {
                actionID = Dance;
                return true;
            }
        }

        if (IsEnabledAndUsable(Preset.Phantom_Dancer_Mesmerize, Mesmerize) && InCombat() && CanWeave())
        {
            actionID = Mesmerize; //Damage Debuff
            return true;
        }

        if (CanWeaveNow) return false;

        // Skip if no damage buff, and user wants things under buffs
        if (!IsEnabled(Preset.Phantom_RestrictToBuff) ||
            Bursting.PlayerIsDamageBuffed)
        {
            #region Dances

            if (IsEnabled(Preset.Phantom_Dancer_Dance) && HasStatusEffect(Buffs.PoisedToSwordDance))
            {
                actionID = PoisedToSwordDance;
                return true;
            }
            if (IsEnabled(Preset.Phantom_Dancer_Dance) && HasStatusEffect(Buffs.TemptedToTango))
            {
                actionID = TemptedToTango;
                return true;
            }
            if (IsEnabled(Preset.Phantom_Dancer_Dance) && HasStatusEffect(Buffs.Jitterbugged))
            {
                actionID = Jitterbug;
                return true;
            }
            if (IsEnabled(Preset.Phantom_Dancer_Dance) && HasStatusEffect(Buffs.WillingToWaltz))
            {
                actionID = WillingToWaltz;
                return true;
            }

            #endregion
        }

        if (IsEnabledAndUsable(Preset.Phantom_Dancer_QuickStep, Quickstep) && !HasStatusEffect(Buffs.Quickstep))
        {
            actionID = Quickstep; //Evasion self buff
            return true;
        }

        return false;
    }

    private static bool TryGetGladiatorAction(ref uint actionID)
    {
        if (CanWeaveNow) return false;
        if (!IsEnabled(Preset.Phantom_RestrictToBuff) || Bursting.PlayerIsDamageBuffed)
        {
            if (IsEnabledAndUsable(Preset.Phantom_Gladiator_Finisher, Finisher) && HasBattleTarget() && InMeleeRange())
            {
                actionID = Finisher;
                return true;
            }
        }

        if (IsEnabledAndUsable(Preset.Phantom_Gladiator_Defend, Defend) && InCombat())
        {
            actionID = Defend;
            return true;
        }

        // Skip if no damage buff, and user wants things under buffs
        if (IsEnabled(Preset.Phantom_RestrictToBuff) &&
            !Bursting.PlayerIsDamageBuffed)
            return false;
        
        if (IsEnabledAndUsable(Preset.Phantom_Gladiator_LongReach, LongReach) && HasBattleTarget())
        {
            actionID = LongReach;
            return true;
        }
        if (IsEnabledAndUsable(Preset.Phantom_Gladiator_BladeBlitz, BladeBlitz) && InCombat() && InActionRange(BladeBlitz))
        {
            actionID = BladeBlitz;
            return true;
        }

        return false;
    }

    #region Shorter variables

    private static bool IsMovingNow => IsMoving();
    private static bool InCombatNow => InCombat();
    private static bool CanWeaveNow => CanWeave();
    private static bool HasTargetNow => HasBattleTarget();
    private static float TargetHP => GetTargetHPPercent();
    private static float PlayerHP => PlayerHealthPercentageHp();
    private static uint PlayerMP => LocalPlayer.CurrentMp;

    #endregion
}
