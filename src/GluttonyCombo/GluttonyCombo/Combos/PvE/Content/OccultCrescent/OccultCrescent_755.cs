#region Dependencies

using GluttonyCombo.Data;
using GluttonyCombo.Extensions;
using static GluttonyCombo.Combos.PvE.OccultCrescent.Config;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;

#endregion

namespace GluttonyCombo.Combos.PvE;

// ============================================================================================
//  GLUTTONY 7.55 PHANTOM JOB STOPGAP
//
//  Upstream Wrath had not implemented the eight phantom jobs added in patch 7.55 as of
//  2026-08-01. This file fills that gap so Occult Crescent: North Horn is usable. It is
//  DELIBERATELY SELF-CONTAINED and meant to be deleted wholesale once upstream ships theirs.
//
//  2026-08-05 UPSTREAM COLLISION NOTE:
//    Upstream shipped its own phantom-job implementation in the 36-commit sync merged
//    this day, defining identically-named TryGet<Job>Action methods on this same
//    partial class. The fork methods are suffixed 755 to coexist, and TryGet755Action
//    still runs FIRST in TryGetPhantomAction, so fork behaviour is unchanged.
//    Retiring this file is NOT a straight delete: upstream has no HP floor, no
//    already-Doomed check and no instant-cast-proc protection, and the P755 constants
//    feed the fork-only phantom-heal integration in OccultCrescent.cs. See CHANGELOG.
//
//  RIP-OUT PROCEDURE:
//    1. Delete this file and OccultCrescent_755_Weakness.cs.
//    2. Delete the Phantom_* preset range 110090-110139 in CustomComboPreset.cs.
//    3. Delete the single TryGet755Action() call in OccultCrescent.TryGetPhantomAction().
//    4. Delete the OccultCrescent_755 slider cases + UserInts in OccultCrescent_Config.cs.
//    5. Revert the JobIDs enum entries 16-23 to whatever upstream ships.
//
//  Action and status IDs were datamined from the live 7.55 sqpack on 2026-08-01
//  (dumper: C:\temp\phantomdump). They are NOT guesses. Note that action NAMES are not
//  unique -- "Occult Cure II" is 49067 on White Mage and 49093 on Red Mage -- so everything
//  here is keyed by explicit ID.
// ============================================================================================
internal partial class OccultCrescent
{
    #region Action IDs -- contiguous block 49062-49101

    internal static class P755
    {
        public const uint
            // --- Phantom Ninja (job 16, max level 6)
            NIN_FumaShuriken = 49062,   // lv1  instant, 60s,  single, 30y
            NIN_Smoke = 49063,          // lv2  instant, 5s,   self
            NIN_LightningScroll = 49064,// lv3  instant, 60s,  5y aoe   [Lightning weakness]
            NIN_FlameScroll = 49065,    // lv4  instant, 60s,  5y aoe   [Fire weakness]
            NIN_Image = 49066,          // lv6  instant, 120s, self

            // --- Phantom White Mage (job 17, max level 5)
            WHM_OccultCureII = 49067,   // lv1  1.5s, 2.5s, single heal
            WHM_OccultCureIII = 49068,  // lv2  2.3s, 2.5s, 15y aoe heal
            WHM_OccultBlink = 49069,    // lv3  instant, 90s
            WHM_OccultRaise = 49070,    // lv4  instant, 5s, raise
            WHM_OccultHoly = 49071,     // lv5  2.3s, 60s, 8y aoe damage

            // --- Phantom Black Mage (job 18, max level 5)
            BLM_OccultFireIII = 49072,  // lv1  1.5s, 40s, 5y aoe  [Fire weakness]
            BLM_OccultBlizzardIII = 49073, // lv2 1.5s, 40s, 5y aoe [Ice weakness]
            BLM_OccultThunderIII = 49074,  // lv3 1.5s, 40s, 5y aoe [Lightning weakness]
            BLM_OccultToad = 49075,     // lv4  1.5s, 2.5s, single -- CC, applies Occult Toad
            BLM_OccultFlare = 49076,    // lv5  2.3s, 60s, 8y aoe

            // --- Phantom Dragoon (job 19, max level 4)
            DRG_OccultJump = 49077,     // lv1  instant, 60s, grants Vulnerability Down
            DRG_StepForth = 49078,      // lv2  instant, 10s, 10y movement
            DRG_Lance = 49079,          // lv3  instant, 30s, grants Lance

            // --- Phantom Summoner (job 20, max level 5)
            SMN_Hellfire = 49080,       // lv1  4s, 60s, 12y aoe  [Fire weakness]
            SMN_JudgmentBolt = 49081,   // lv2  4s, 60s, 12y aoe  [Lightning weakness]
            SMN_EarthenWall = 49082,    // lv3  2.5s, 120s, 20y self mitigation
            SMN_Thunderstorm = 49083,   // lv4  4s, 60s, 30y aoe  [WIND weakness -- not lightning]
            SMN_Megaflare = 49084,      // lv5  6s, 90s, 15y aoe

            // --- Phantom Blue Mage (job 21, max level 3)
            BLU_OccultAero = 49085,     // lv1  1.5s, 30s, single (upgrades to 49089/49091)
            BLU_OccultMissile = 49086,  // lv1  1.5s, 30s, single
            BLU_OccultAquaBreath = 49087, // lv1 1.5s, 60s, 5y aoe
            BLU_OccultMightyGuard = 49088, // lv2 instant, 120s, 20y self mitigation
            BLU_OccultWhiteWind = 49090,   // lv3 1.5s, 150s, 15y aoe heal

            // --- Phantom Red Mage (job 22, max level 6)
            RDM_OccultFireII = 49092,   // lv1  1.5s, 30s, 5y aoe  [Fire weakness]
            RDM_OccultCureII = 49093,   // lv2  1.5s, 2.5s, single heal
            RDM_OccultLibra = 49094,    // lv3  instant, 5s -- REVEALS elemental weakness
            RDM_OccultBlizzardII = 49095,  // lv4 1.5s, 30s, 5y aoe [Ice weakness]
            RDM_OccultThunderII = 49096,   // lv5 1.5s, 30s, 5y aoe [Lightning weakness]

            // --- Phantom Necromancer (job 23, max level 5)
            NEC_DrainTouch = 49097,     // lv1  instant, 40s, single
            NEC_DeepFreeze = 49098,     // lv2  1.5s, 40s, 30y aoe  [Ice weakness]
            NEC_HellWind = 49099,       // lv3  1.5s, 40s, 30y aoe  [Wind weakness]
            NEC_ChaosDrive = 49100,     // lv4  1.5s, 40s, 30y aoe  [Lightning weakness]
            NEC_Doomsday = 49101;       // lv5  1.5s, 120s, 30y aoe
    }

    #endregion

    #region Status IDs

    internal static class Buffs755
    {
        public const ushort
            Smoke = 5327,
            Image = 4873,
            OccultBlink = 5316,
            VulnerabilityDown = 5318,   // granted by DRG Occult Jump
            Lance = 5319,
            EarthenWall = 5320,
            OccultMightyGuard = 5321,
            DrainTouch = 5326;
    }

    internal static class Debuffs755
    {
        public const ushort OccultToad = 5317;

        // Self-Doom, applied by EVERY HP-cost Necromancer spell. "Certain death when counter
        // reaches zero. Effect dissipates once fully healed." 5473 is the 7.55 row (verified on
        // the live Status sheet, sitting next to Cryptic Communications 5472); 1769 is the older
        // row carrying identical wording and is checked defensively in case the server reuses it.
        public const ushort NecromancerDoom = 5473,
                            NecromancerDoomLegacy = 1769;
    }

    /// <summary>Phantom Job identity statuses, contiguous 5328-5335.</summary>
    internal static class JobStatus755
    {
        public const ushort
            Ninja = 5328,
            WhiteMage = 5329,
            BlackMage = 5330,
            Dragoon = 5331,
            Summoner = 5332,
            BlueMage = 5333,
            RedMage = 5334,
            Necromancer = 5335;
    }

    #endregion

    /// <summary>
    ///     Single entry point for every 7.55 phantom job. One call from TryGetPhantomAction()
    ///     keeps the rip-out surface to a single line.
    /// </summary>
    internal static bool TryGet755Action(ref uint actionID)
    {
        if (TryGetNinjaAction755(ref actionID)) return true;
        if (TryGetWhiteMageAction755(ref actionID)) return true;
        if (TryGetBlackMageAction755(ref actionID)) return true;
        if (TryGetDragoonAction755(ref actionID)) return true;
        if (TryGetSummonerAction755(ref actionID)) return true;
        if (TryGetBlueMageAction755(ref actionID)) return true;
        if (TryGetRedMageAction755(ref actionID)) return true;
        if (TryGetNecromancerAction755(ref actionID)) return true;

        return false;
    }

    /// <summary>Shared "only act under a damage buff" gate, matching the pre-7.55 jobs.</summary>
    private static bool BuffGateBlocks =>
        IsEnabled(Preset.Phantom_RestrictToBuff) && !Bursting.PlayerIsDamageBuffed;

    /// <summary>
    ///     Whether the player is holding an instant-cast proc that a phantom spell must not be
    ///     allowed to eat.
    ///     <para/>
    ///     Phantom spells are ordinary spells as far as these procs are concerned - Phantom
    ///     Summoner is the sole exception, and its actions say so explicitly ("Cast and recast
    ///     timer cannot be affected by status effects or gear attributes"). A carve-out that
    ///     specific only exists because the default is the opposite. The pre-7.55 Time Mage
    ///     handler relies on exactly this: it spends Swiftcast, Occult Quick, Triplecast,
    ///     Requiescat or Dualcast to make Occult Comet instant.
    ///     <para/>
    ///     The 7.55 set inverts that trade, because of the cast times involved. Comet is an 8.0s
    ///     cast, which is worth a proc. Megaflare is 6.0s but is Summoner, so no proc can touch
    ///     it. Everything else here tops out at 2.3s (Occult Holy, Occult Cure III, Occult
    ///     Flare), and most of it is 1.5s. Nothing in 7.55 is worth burning a Swiftcast the
    ///     player was holding for a raise, or a Dualcast earmarked for Verraise - so instead of
    ///     spending procs like the Time Mage path, we stand down while one is up and let the
    ///     player's own job use it.
    ///     <para/>
    ///     This gate guards CAST-TIME actions only. The instants are untouched, which is what
    ///     keeps Occult Raise - the most valuable button in the set - firing regardless.
    /// </summary>
    private static bool HoldingInstantCastProc =>
        HasStatusEffect(RoleActions.Magic.Buffs.Swiftcast) ||
        HasStatusEffect(RDM.Buffs.Dualcast) ||
        HasStatusEffect(BLM.Buffs.Triplecast) ||
        HasStatusEffect(PLD.Buffs.Requiescat);

    #region Phantom Ninja

    private static bool TryGetNinjaAction755(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Ninja))
            return false;

        // Everything Ninja has is instant, so it all wants a weave window.
        if (!CanWeaveNow) return false;

        if (IsEnabledAndUsable(Preset.Phantom_Ninja_Image, P755.NIN_Image) &&
            !HasStatusEffect(Buffs755.Image) && PlayerHP <= Phantom_Ninja_Image_Health)
        {
            actionID = P755.NIN_Image; // decoy / survival
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Ninja_Smoke, P755.NIN_Smoke) &&
            !HasStatusEffect(Buffs755.Smoke) && InCombatNow)
        {
            actionID = P755.NIN_Smoke;
            return true;
        }

        if (BuffGateBlocks) return false;
        if (!HasTargetNow) return false;

        // Fuma Shuriken is 230 flat; the scrolls are 150, or 195 against a matching weakness.
        // So Fuma out-damages both scrolls on a single target EVEN when the weakness lands, and
        // only loses once the scrolls' 5y splash catches a second mob. The old order put the
        // scrolls first unconditionally.
        bool scrollsOutDamageFuma = NumberOfEnemiesInRange(P755.NIN_FlameScroll, CurrentTarget) >= 2;

        if (!scrollsOutDamageFuma &&
            IsEnabledAndUsable(Preset.Phantom_Ninja_FumaShuriken, P755.NIN_FumaShuriken) &&
            InActionRange(P755.NIN_FumaShuriken))
        {
            actionID = P755.NIN_FumaShuriken;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Ninja_FlameScroll, P755.NIN_FlameScroll) &&
            InActionRange(P755.NIN_FlameScroll) && WeaknessGate(Weak755.FireWeakness))
        {
            actionID = P755.NIN_FlameScroll;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Ninja_LightningScroll, P755.NIN_LightningScroll) &&
            InActionRange(P755.NIN_LightningScroll) && WeaknessGate(Weak755.LightningWeakness))
        {
            actionID = P755.NIN_LightningScroll;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Ninja_FumaShuriken, P755.NIN_FumaShuriken) &&
            InActionRange(P755.NIN_FumaShuriken))
        {
            actionID = P755.NIN_FumaShuriken;
            return true;
        }

        return false;
    }

    #endregion

    #region Phantom White Mage

    private static bool TryGetWhiteMageAction755(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_WhiteMage))
            return false;

        // Occult Blink is the only instant worth weaving.
        if (CanWeaveNow)
        {
            if (IsEnabledAndUsable(Preset.Phantom_WhiteMage_OccultBlink, P755.WHM_OccultBlink) &&
                !HasStatusEffect(Buffs755.OccultBlink) &&
                PlayerHP <= Phantom_WhiteMage_OccultBlink_Health)
            {
                actionID = P755.WHM_OccultBlink;
                return true;
            }

            return false;
        }

        if (IsEnabledAndUsable(Preset.Phantom_WhiteMage_OccultRaise, P755.WHM_OccultRaise) &&
            CurrentTarget.IfCanUseOn(P755.WHM_OccultRaise).IfDead() is not null)
        {
            actionID = P755.WHM_OccultRaise;
            return true;
        }

        // Occult Raise above, and the cures immediately below, deliberately sit AHEAD of the
        // instant-cast-proc gate. That gate exists to stop a 1.5s filler nova eating a Swiftcast
        // the player was saving for a raise. It must never stop the raise itself, and it must
        // never stop a cure that only fired because HP crossed the user's own emergency
        // threshold - by definition the player has already said that HP number is worth a GCD,
        // and a proc is cheaper than a death.
        //
        // Regression fixed in v1.0.4.105: v1.0.4.103 introduced the gate and left the cures
        // below it. On any job holding a listed proc the cures were suppressed - worst on BLM,
        // where the plugin manages Triplecast itself and therefore keeps the gate shut almost
        // continuously, so phantom healing never fired at all. RDM only looked healthy because
        // Dualcast drops every other GCD and left windows.
        if (IsEnabledAndUsable(Preset.Phantom_WhiteMage_OccultCureIII, P755.WHM_OccultCureIII) &&
            IsInParty() && GetPartyAvgHPPercent() <= Phantom_WhiteMage_OccultCureIII_Health)
        {
            actionID = P755.WHM_OccultCureIII;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_WhiteMage_OccultCureII, P755.WHM_OccultCureII) &&
            PlayerHP <= Phantom_WhiteMage_OccultCureII_Health)
        {
            actionID = P755.WHM_OccultCureII;
            return true;
        }

        // Everything below here is filler damage, which the gate still applies to.
        if (HoldingInstantCastProc) return false;

        if (BuffGateBlocks) return false;

        if (IsEnabledAndUsable(Preset.Phantom_WhiteMage_OccultHoly, P755.WHM_OccultHoly) &&
            HasTargetNow && InActionRange(P755.WHM_OccultHoly))
        {
            actionID = P755.WHM_OccultHoly;
            return true;
        }

        return false;
    }

    #endregion

    #region Phantom Black Mage

    private static bool TryGetBlackMageAction755(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_BlackMage))
            return false;

        // Every Black Mage action is a hard cast.
        if (CanWeaveNow) return false;
        if (!HasTargetNow) return false;
        if (HoldingInstantCastProc) return false;

        // Toad is crowd control, not damage, so it sits ahead of the buff gate.
        if (IsEnabledAndUsable(Preset.Phantom_BlackMage_OccultToad, P755.BLM_OccultToad) &&
            !HasStatusEffect(Debuffs755.OccultToad, CurrentTarget, anyOwner: true) &&
            InActionRange(P755.BLM_OccultToad))
        {
            actionID = P755.BLM_OccultToad;
            return true;
        }

        if (BuffGateBlocks) return false;

        // The elemental trio goes first: a matched weakness is 520, which beats Flare's 500.
        // They sit on separate recasts (trio 40s shared, Flare 60s) so nothing is lost by
        // ordering this way - Flare still fires, just on a later GCD. When no weakness is known
        // the trio self-skips through WeaknessGate and Flare leads as before.
        if (IsEnabledAndUsable(Preset.Phantom_BlackMage_OccultFireIII, P755.BLM_OccultFireIII) &&
            InActionRange(P755.BLM_OccultFireIII) && WeaknessGate(Weak755.FireWeakness))
        {
            actionID = P755.BLM_OccultFireIII;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_BlackMage_OccultBlizzardIII, P755.BLM_OccultBlizzardIII) &&
            InActionRange(P755.BLM_OccultBlizzardIII) && WeaknessGate(Weak755.IceWeakness))
        {
            actionID = P755.BLM_OccultBlizzardIII;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_BlackMage_OccultThunderIII, P755.BLM_OccultThunderIII) &&
            InActionRange(P755.BLM_OccultThunderIII) && WeaknessGate(Weak755.LightningWeakness))
        {
            actionID = P755.BLM_OccultThunderIII;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_BlackMage_OccultFlare, P755.BLM_OccultFlare) &&
            InActionRange(P755.BLM_OccultFlare))
        {
            actionID = P755.BLM_OccultFlare; // 500 unaspected, never needs a weakness
            return true;
        }

        return false;
    }

    #endregion

    #region Phantom Dragoon

    private static bool TryGetDragoonAction755(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Dragoon))
            return false;

        if (!CanWeaveNow) return false;

        // Step Forth is pure movement -- off by default, it will yank you around otherwise.
        if (IsEnabledAndUsable(Preset.Phantom_Dragoon_StepForth, P755.DRG_StepForth) &&
            InCombatNow && HasTargetNow)
        {
            actionID = P755.DRG_StepForth;
            return true;
        }

        if (BuffGateBlocks) return false;
        if (!HasTargetNow) return false;

        if (IsEnabledAndUsable(Preset.Phantom_Dragoon_Lance, P755.DRG_Lance) &&
            !HasStatusEffect(Buffs755.Lance) && InActionRange(P755.DRG_Lance))
        {
            actionID = P755.DRG_Lance;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Dragoon_OccultJump, P755.DRG_OccultJump) &&
            InActionRange(P755.DRG_OccultJump))
        {
            actionID = P755.DRG_OccultJump; // damage + Vulnerability Down on self
            return true;
        }

        return false;
    }

    #endregion

    #region Phantom Summoner

    private static bool TryGetSummonerAction755(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Summoner))
            return false;

        if (CanWeaveNow) return false;
        if (HoldingInstantCastProc) return false;

        if (IsEnabledAndUsable(Preset.Phantom_Summoner_EarthenWall, P755.SMN_EarthenWall) &&
            !HasStatusEffect(Buffs755.EarthenWall) &&
            PlayerHP <= Phantom_Summoner_EarthenWall_Health)
        {
            actionID = P755.SMN_EarthenWall;
            return true;
        }

        if (BuffGateBlocks) return false;
        if (!HasTargetNow) return false;

        if (IsEnabledAndUsable(Preset.Phantom_Summoner_Megaflare, P755.SMN_Megaflare) &&
            InActionRange(P755.SMN_Megaflare))
        {
            actionID = P755.SMN_Megaflare; // 6s cast, no weakness requirement
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Summoner_Hellfire, P755.SMN_Hellfire) &&
            InActionRange(P755.SMN_Hellfire) && WeaknessGate(Weak755.FireWeakness))
        {
            actionID = P755.SMN_Hellfire;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_Summoner_JudgmentBolt, P755.SMN_JudgmentBolt) &&
            InActionRange(P755.SMN_JudgmentBolt) && WeaknessGate(Weak755.LightningWeakness))
        {
            actionID = P755.SMN_JudgmentBolt;
            return true;
        }

        // Thunderstorm is a WIND spell despite the name -- upstream RSR shipped this gated on
        // Lightning and had to hotfix it (731446871, 2026-08-01). Do not "correct" this.
        if (IsEnabledAndUsable(Preset.Phantom_Summoner_Thunderstorm, P755.SMN_Thunderstorm) &&
            InActionRange(P755.SMN_Thunderstorm) && WeaknessGate(Weak755.WindWeakness))
        {
            actionID = P755.SMN_Thunderstorm;
            return true;
        }

        return false;
    }

    #endregion

    #region Phantom Blue Mage

    private static bool TryGetBlueMageAction755(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_BlueMage))
            return false;

        if (CanWeaveNow)
        {
            if (IsEnabledAndUsable(Preset.Phantom_BlueMage_OccultMightyGuard, P755.BLU_OccultMightyGuard) &&
                !HasStatusEffect(Buffs755.OccultMightyGuard) &&
                PlayerHP <= Phantom_BlueMage_OccultMightyGuard_Health)
            {
                actionID = P755.BLU_OccultMightyGuard;
                return true;
            }

            return false;
        }

        // White Wind restores an amount equal to the caster's CURRENT HP, so it is strongest at
        // full and nearly worthless when low - the exact inverse of an emergency button. Gating
        // on party HP alone fired it precisely when it healed least, because whatever hurt the
        // party usually hurt us too. Require the party to need it AND us to still be worth
        // spending.
        //
        // Ahead of the instant-cast-proc gate - see the rationale in the White Mage block.
        if (IsEnabledAndUsable(Preset.Phantom_BlueMage_OccultWhiteWind, P755.BLU_OccultWhiteWind) &&
            GetPartyAvgHPPercent() <= Phantom_BlueMage_OccultWhiteWind_Health &&
            PlayerHP >= Phantom_BlueMage_OccultWhiteWind_SelfHealth)
        {
            actionID = P755.BLU_OccultWhiteWind;
            return true;
        }

        if (HoldingInstantCastProc) return false;

        if (BuffGateBlocks) return false;
        if (!HasTargetNow) return false;

        // Blue Mage damage is not weakness-gated.
        if (IsEnabledAndUsable(Preset.Phantom_BlueMage_OccultAquaBreath, P755.BLU_OccultAquaBreath) &&
            InActionRange(P755.BLU_OccultAquaBreath))
        {
            actionID = P755.BLU_OccultAquaBreath;
            return true;
        }

        // Occult Aero auto-upgrades to Aero II (49089) / Aero III (49091) by trait.
        if (IsEnabledAndUsable(Preset.Phantom_BlueMage_OccultAero, P755.BLU_OccultAero) &&
            InActionRange(P755.BLU_OccultAero))
        {
            actionID = OriginalHook(P755.BLU_OccultAero);
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_BlueMage_OccultMissile, P755.BLU_OccultMissile) &&
            InActionRange(P755.BLU_OccultMissile))
        {
            actionID = P755.BLU_OccultMissile;
            return true;
        }

        return false;
    }

    #endregion

    #region Phantom Red Mage

    private static bool TryGetRedMageAction755(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_RedMage))
            return false;

        // Libra is instant and reveals the target's elemental weakness, which every other
        // elemental caster in the zone then benefits from. Weave it early.
        if (CanWeaveNow)
        {
            // Keyed to the LIVE debuff, not to what the static nameId table happens to know.
            // Libra's tooltip says it discerns affinity "increasing the potency of elemental
            // attacks that exploit their weaknesses" - if that +30% is gated on the debuff being
            // applied, then skipping Libra because the table already knew the answer silently
            // forfeited 30% for the whole party on every elemental cast. Libra is instant, 5s
            // recast and weaveable: the cast is nearly free, the forfeit is not.
            if (IsEnabledAndUsable(Preset.Phantom_RedMage_OccultLibra, P755.RDM_OccultLibra) &&
                HasTargetNow && InActionRange(P755.RDM_OccultLibra) &&
                !TargetHasAnyWeaknessDebuff())
            {
                actionID = P755.RDM_OccultLibra;
                return true;
            }

            return false;
        }

        // Cure ahead of the instant-cast-proc gate - see the rationale in the White Mage block.
        if (IsEnabledAndUsable(Preset.Phantom_RedMage_OccultCureII, P755.RDM_OccultCureII) &&
            PlayerHP <= Phantom_RedMage_OccultCureII_Health)
        {
            actionID = P755.RDM_OccultCureII;
            return true;
        }

        if (HoldingInstantCastProc) return false;

        if (BuffGateBlocks) return false;
        if (!HasTargetNow) return false;

        if (IsEnabledAndUsable(Preset.Phantom_RedMage_OccultFireII, P755.RDM_OccultFireII) &&
            InActionRange(P755.RDM_OccultFireII) && WeaknessGate(Weak755.FireWeakness))
        {
            actionID = P755.RDM_OccultFireII;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_RedMage_OccultBlizzardII, P755.RDM_OccultBlizzardII) &&
            InActionRange(P755.RDM_OccultBlizzardII) && WeaknessGate(Weak755.IceWeakness))
        {
            actionID = P755.RDM_OccultBlizzardII;
            return true;
        }

        if (IsEnabledAndUsable(Preset.Phantom_RedMage_OccultThunderII, P755.RDM_OccultThunderII) &&
            InActionRange(P755.RDM_OccultThunderII) && WeaknessGate(Weak755.LightningWeakness))
        {
            actionID = P755.RDM_OccultThunderII;
            return true;
        }

        return false;
    }

    #endregion

    #region Phantom Necromancer

    /// <summary>
    ///     How much Drain Touch must still be on the clock before a line spell is allowed to
    ///     start casting.
    ///     <para/>
    ///     The line spells are 1.5s casts and the buff is only 6s, so a bare
    ///     <c>HasStatusEffect</c> is not a safe test - it is still true at 0.1s remaining, which
    ///     puts the RESOLVE outside the window. That case pays the full price (10% of maximum HP
    ///     plus a 10s Doom) for an unbuffed 300 potency with no rider and no HP protection: the
    ///     exact "all cost, no payload" outcome this whole gate exists to prevent.
    /// </summary>
    private const float DrainTouchCastHeadroom = 2.0f; // 1.5s cast + latency

    /// <summary>Own HP is above the user's floor.</summary>
    private static bool NecromancerHpOk => PlayerHP >= Phantom_Necromancer_HpFloorPct;

    /// <summary>
    ///     Not already carrying the self-Doom. Re-casting refreshes the 10s timer but takes
    ///     another 10% of maximum HP, which moves full HP - the only thing that clears Doom -
    ///     further away rather than closer.
    /// </summary>
    private static bool NecromancerNotDoomed =>
        !HasStatusEffect(Debuffs755.NecromancerDoom) &&
        !HasStatusEffect(Debuffs755.NecromancerDoomLegacy);

    /// <summary>
    ///     Whether the four HP-cost line spells (Deep Freeze, Hell Wind, Chaos Drive, Doomsday)
    ///     are worth what they cost right now.
    ///     <para/>
    ///     Read the tooltips carefully: "Consumes 10% of maximum HP when executed" and "Afflicts
    ///     yourself with Doom / Duration: 10s / Effect is dispelled when HP is restored to full"
    ///     carry NO "when under the effect of Drain Touch" qualifier. The cost is unconditional.
    ///     Only the payoff is conditional - Drain Touch raises 300 potency to 400 (350 to 500 on
    ///     Doomsday), unlocks the riders that make the job worth playing (the 4s time freeze, the
    ///     petrify chance, the paralysis, and Doomsday's enemy-buff dispel), and - the part that
    ///     is easy to miss - grants "Most attacks cannot reduce own HP to less than 1" for its 6s
    ///     duration. Drain Touch is the survival window, not merely a damage buff.
    ///     <para/>
    ///     That protection does NOT cover the Doom, because Doom is not an attack. Only a heal
    ///     to FULL inside 10s clears it. Three gates:
    ///     <list type="number">
    ///         <item>Drain Touch must still have <see cref="DrainTouchCastHeadroom"/> left, so
    ///         the cast resolves inside the window rather than just starting inside it.</item>
    ///         <item>HP must be above the user's floor, so there is headroom to heal to full and
    ///         shed the Doom before the counter lands.</item>
    ///         <item>Not already Doomed.</item>
    ///     </list>
    /// </summary>
    private static bool NecromancerCostIsAffordable =>
        GetStatusEffectRemainingTime(Buffs755.DrainTouch) >= DrainTouchCastHeadroom &&
        NecromancerHpOk &&
        NecromancerNotDoomed;

    /// <summary>
    ///     The single best line spell to spend the current Drain Touch window on, or 0 if there
    ///     is none.
    ///     <para/>
    ///     Only one line spell can be spent per window, because the first cast applies the Doom
    ///     that <see cref="NecromancerNotDoomed"/> then blocks on - so this is a pick, not a
    ///     priority queue, and picking wrong forfeits the better option outright.
    ///     <para/>
    ///     Deep Freeze, Hell Wind and Chaos Drive share one 40s recast (cooldown group 84), so
    ///     at most one of the three is ever ready anyway; Doomsday is on its own 120s recast
    ///     (group 87).
    /// </summary>
    private static uint BestNecromancerLineSpell()
    {
        // Weakness-matched trio first. Under Drain Touch against a matching weakness these hit
        // 520, which beats Doomsday's 500 - and taking Doomsday here would also spend the only
        // enemy-buff dispel in the phantom set on whatever happens to be in front of us.
        if (TargetWeakTo(Weak755.IceWeakness) &&
            IsEnabledAndUsable(Preset.Phantom_Necromancer_DeepFreeze, P755.NEC_DeepFreeze) &&
            InActionRange(P755.NEC_DeepFreeze))
            return P755.NEC_DeepFreeze;

        if (TargetWeakTo(Weak755.WindWeakness) &&
            IsEnabledAndUsable(Preset.Phantom_Necromancer_HellWind, P755.NEC_HellWind) &&
            InActionRange(P755.NEC_HellWind))
            return P755.NEC_HellWind;

        if (TargetWeakTo(Weak755.LightningWeakness) &&
            IsEnabledAndUsable(Preset.Phantom_Necromancer_ChaosDrive, P755.NEC_ChaosDrive) &&
            InActionRange(P755.NEC_ChaosDrive))
            return P755.NEC_ChaosDrive;

        // Doomsday next: 500 under Drain Touch, unaspected, so no weakness requirement and the
        // "only when weak" toggle has nothing to say about it.
        if (IsEnabledAndUsable(Preset.Phantom_Necromancer_Doomsday, P755.NEC_Doomsday) &&
            InActionRange(P755.NEC_Doomsday))
            return P755.NEC_Doomsday;

        // Unweakened trio last, at 400. WeaknessGate honours the user's "only when weak"
        // toggle - with it enabled this tier drops out entirely and the window goes unspent,
        // which is the point of the toggle.
        if (IsEnabledAndUsable(Preset.Phantom_Necromancer_DeepFreeze, P755.NEC_DeepFreeze) &&
            InActionRange(P755.NEC_DeepFreeze) && WeaknessGate(Weak755.IceWeakness))
            return P755.NEC_DeepFreeze;

        if (IsEnabledAndUsable(Preset.Phantom_Necromancer_HellWind, P755.NEC_HellWind) &&
            InActionRange(P755.NEC_HellWind) && WeaknessGate(Weak755.WindWeakness))
            return P755.NEC_HellWind;

        if (IsEnabledAndUsable(Preset.Phantom_Necromancer_ChaosDrive, P755.NEC_ChaosDrive) &&
            InActionRange(P755.NEC_ChaosDrive) && WeaknessGate(Weak755.LightningWeakness))
            return P755.NEC_ChaosDrive;

        return 0;
    }

    private static bool TryGetNecromancerAction755(ref uint actionID)
    {
        if (!IsEnabled(Preset.Phantom_Necromancer))
            return false;

        if (BuffGateBlocks) return false;
        if (!HasTargetNow) return false;

        if (CanWeaveNow)
        {
            // Drain Touch is an oGCD - ActionCategory 4, cooldown group 83, 40s recast, instant,
            // 30y - so it belongs in the weave window. What it must NOT do is fire on cooldown.
            //
            // Its buff lasts 6s and its recast is 40s, which is exactly the shared recast of
            // Deep Freeze / Hell Wind / Chaos Drive. Opening the window with no payoff spell
            // ready spends the whole 40s on a 150 potency poke; the window then expires empty,
            // and when a line spell finally does come up Drain Touch has ~34s left, so
            // NecromancerCostIsAffordable is false and nothing casts. Once those two 40s timers
            // drift apart nothing pulls them back, and the job quietly does nothing but weave.
            // So the weave is gated on the payoff, not on the cooldown.
            if (HasStatusEffect(Buffs755.DrainTouch)) return false;

            if (!IsEnabledAndUsable(Preset.Phantom_Necromancer_DrainTouch, P755.NEC_DrainTouch) ||
                !InActionRange(P755.NEC_DrainTouch))
                return false;

            // The same affordability gates the line spell will face, checked up front: there is
            // no point opening a window we are not allowed to spend.
            if (!NecromancerHpOk || !NecromancerNotDoomed) return false;

            // And the proc gate too. HoldingInstantCastProc stands the GCD branch down for the
            // full 6s, so opening a window we have already decided not to use is the same waste
            // by a different route.
            if (HoldingInstantCastProc) return false;

            if (BestNecromancerLineSpell() == 0) return false;

            actionID = P755.NEC_DrainTouch;
            return true;
        }

        if (HoldingInstantCastProc) return false;
        if (!NecromancerCostIsAffordable) return false;

        uint lineSpell = BestNecromancerLineSpell();
        if (lineSpell == 0) return false;

        actionID = lineSpell;
        return true;
    }

    #endregion
}
