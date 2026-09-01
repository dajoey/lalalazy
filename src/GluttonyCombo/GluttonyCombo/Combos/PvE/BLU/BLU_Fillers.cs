using System.Collections.Generic;
using System.Linq;
using GluttonyCombo.CustomComboNS.Functions;
using static GluttonyCombo.Combos.PvE.BLU.Config;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;

namespace GluttonyCombo.Combos.PvE;

/// <summary>
///     FORK DIVERGENCE (GluttonyCombo, not upstream WrathCombo).
///     <para>
///         Upstream hard-codes four filler spells into the BLU one-button rotations:
///         Sonic Boom (ST DPS), Electrogenesis (AoE DPS), Goblin Punch (ST tank) and
///         Right Round (AoE tank). A Blue Mage only gets 24 active spell slots, so a
///         user who slotted any of the ~30 other perfectly good fillers instead got a
///         rotation that fell through to the un-replaced action and did nothing.
///     </para>
///     <para>
///         This file turns each of those four into a selection. Default behaviour is
///         auto-detect: pick the highest-value filler the user actually has slotted,
///         which for almost everyone resolves to exactly one candidate. A per-slot
///         config override forces a specific spell.
///     </para>
///     <para>
///         Action data (potency, range, shape) verified 2026-08-31 against XIVAPI v2
///         (exdschema rev 83e965d0) and cross-checked on Garland Tools; see
///         src/GluttonyCombo/CHANGELOG.md v1.0.4.165.
///     </para>
/// </summary>
internal partial class BLU
{
    #region Filler model

    /// <summary> The shape of a filler, which decides what it can be used for. </summary>
    internal enum FillerShape
    {
        /// <summary> Hits one enemy only. ST slots only. </summary>
        Single,

        /// <summary> Needs a hostile target and deals full potency to it, with falloff to anything else caught in the circle/line/cone. Usable in ST and AoE slots. </summary>
        Targeted,

        /// <summary> Centred on the player, no target needed. AoE slots only. </summary>
        PointBlank,

        /// <summary> Placed on the ground. AoE slots only. </summary>
        Ground,
    }

    internal sealed record FillerInfo(
        uint ActionId,
        FillerShape Shape,
        float Range,
        int Potency,
        bool AutoSafe,
        string? Caveat = null)
    {
        /// <summary> Melee-range fillers cannot be used as a ranged filler when the target is out of reach. </summary>
        public bool IsMelee => Range <= 3f;

        /// <summary> Whether this filler can fill a single-target slot. </summary>
        public bool CanSingleTarget => Shape is FillerShape.Single or FillerShape.Targeted;

        /// <summary> Whether this filler can fill an AoE slot. </summary>
        public bool CanAoE => Shape is not FillerShape.Single;
    }

    /// <summary>
    ///     Every 2.5s-recast damaging GCD a Blue Mage can reasonably spam, ordered by
    ///     descending value so auto-detect naturally prefers the strongest slotted one.
    ///     <para>
    ///         Deliberately excluded, because they are traps as a filler rather than
    ///         fillers with a downside: 1000 Needles (6s cast, damage SPLIT between
    ///         targets), Final Sting / Self-destruct (incapacitate the caster), Wild
    ///         Rage (consumes half your max HP), Missile / Tail Screw / Launcher /
    ///         Doom / Dimensional Shift (chance-based and useless at level), Ruby
    ///         Dynamics and The Rose of Destruction (30s shared recast, so they are
    ///         cooldowns and the Primals option already handles them), Song of Torment
    ///         / Aetherial Spark / Breath of Magic / Mortal Flame (DoTs, already
    ///         driven by their own presets), and Revenge Blast (50 potency unless you
    ///         are under 20% HP).
    ///     </para>
    ///     <para>
    ///         <c>AutoSafe: false</c> means "never pick this on your own" — knockbacks,
    ///         draw-ins, conditional potency and status application that a party would
    ///         object to. Those stay manually selectable; the user gets to decide.
    ///     </para>
    /// </summary>
    internal static readonly FillerInfo[] Fillers =
    [
        // --- Pure single target ---
        new(SonicBoom, FillerShape.Single, 25f, 210, true),
        new(AbyssalTransfixion, FillerShape.Single, 25f, 220, true),
        new(Reflux, FillerShape.Single, 25f, 220, false, "Applies Heavy, ignoring resistance."),
        new(PerpetualRay, FillerShape.Single, 25f, 220, false, "3s cast, and stuns — the Perpetual Ray preset also wants this button."),
        new(SharpenedKnife, FillerShape.Single, 3f, 220, true),
        new(GoblinPunch, FillerShape.Single, 3f, 220, true, "120 potency from behind; 220 from the front or under Mighty Guard."),
        new(WaterCannon, FillerShape.Single, 25f, 200, true),
        new(BloodDrain, FillerShape.Single, 25f, 50, false, "50 potency — worth it only for the MP restore."),

        // --- Targeted AoE (full potency on the primary target, so these fill an ST slot too) ---
        new(Electrogenesis, FillerShape.Targeted, 25f, 220, true),
        new(Blaze, FillerShape.Targeted, 25f, 220, true),
        new(MustardBomb, FillerShape.Targeted, 25f, 220, true),
        new(Glower, FillerShape.Targeted, 15f, 220, true),
        new(AlpineDraft, FillerShape.Targeted, 20f, 220, true),
        new(FeculentFlood, FillerShape.Targeted, 20f, 220, true),
        new(TheLook, FillerShape.Targeted, 6f, 220, true),
        new(Kaltstrahl, FillerShape.Targeted, 6f, 220, true),
        new(Northerlies, FillerShape.Targeted, 6f, 220, true),
        new(FlameThrower, FillerShape.Targeted, 8f, 220, true),
        new(ConvictionMarcato, FillerShape.Targeted, 25f, 220, true),
        new(PeripheralSynthesis, FillerShape.Targeted, 20f, 220, false, "The Lightheaded preset also wants this button."),
        new(Tatamigaeshi, FillerShape.Targeted, 20f, 220, false, "Stuns."),
        new(LaserEye, FillerShape.Targeted, 25f, 220, false, "Knocks back 5y."),
        new(ProteanWave, FillerShape.Targeted, 15f, 220, false, "Knocks back 15y."),
        new(DrillCannons, FillerShape.Targeted, 20f, 200, true),
        new(InkJet, FillerShape.Targeted, 6f, 200, false, "Applies Blind."),
        new(ChocoMeteor, FillerShape.Targeted, 25f, 200, true),
        new(FireAngon, FillerShape.Targeted, 25f, 200, true),
        new(WhiteKnightsTour, FillerShape.Targeted, 20f, 200, false, "The Knight's Tour preset also wants this button."),
        new(BlackKnightsTour, FillerShape.Targeted, 20f, 200, false, "The Knight's Tour preset also wants this button."),
        new(FlyingFrenzy, FillerShape.Targeted, 20f, 150, true),
        new(AquaBreath, FillerShape.Targeted, 8f, 140, true),
        new(SaintlyBeam, FillerShape.Targeted, 25f, 100, false, "100 potency, or 500 against undead."),
        new(PeatPelt, FillerShape.Targeted, 25f, 100, false, "The Peat Pelt preset also wants this button."),
        new(Tingle, FillerShape.Targeted, 20f, 100, false, "100 potency — this is a buff, and the opener wants it."),
        new(DivinationRune, FillerShape.Targeted, 15f, 100, false, "100 potency — worth it only for the MP restore."),

        // --- Point blank (AoE slots only) ---
        new(Plaincracker, FillerShape.PointBlank, 6f, 220, true),
        new(RamsVoice, FillerShape.PointBlank, 6f, 220, false, "Applies Deep Freeze — the Ultravibration combo wants this."),
        new(DragonsVoice, FillerShape.PointBlank, 20f, 200, false, "Donut: misses anything within 8y of you."),
        new(MindBlast, FillerShape.PointBlank, 6f, 200, false, "Applies Paralysis."),
        new(HighVoltage, FillerShape.PointBlank, 12f, 180, false, "Applies Paralysis."),
        new(HydroPull, FillerShape.PointBlank, 15f, 220, false, "Draws enemies in."),
        new(Stotram, FillerShape.PointBlank, 15f, 140, false, "Becomes a heal under Aetheric Mimicry: Healer."),
        new(RightRound, FillerShape.PointBlank, 8f, 110, false, "Knocks back 10y — including your own party."),

        // --- Ground targeted (AoE slots only) ---
        new(BombToss, FillerShape.Ground, 25f, 200, false, "Ground targeted, and stuns."),
        new(FourTonzeWeight, FillerShape.Ground, 25f, 200, false, "Ground targeted, and applies Heavy."),
    ];

    private static readonly Dictionary<uint, FillerInfo> FillerById =
        Fillers.ToDictionary(f => f.ActionId, f => f);

    internal static FillerInfo? GetFiller(uint actionId) =>
        FillerById.GetValueOrDefault(actionId);

    #endregion

    #region Slots

    /// <summary> Which hard-coded filler a selection is standing in for. </summary>
    internal enum FillerSlot
    {
        StDps,
        AoeDps,
        StTank,
        AoeTank,
    }

    /// <summary> The spell upstream hard-codes for each slot. Always the auto-detect first choice, and the fallback when nothing is slotted. </summary>
    internal static uint DefaultFiller(FillerSlot slot) => slot switch
    {
        FillerSlot.AoeDps => Electrogenesis,
        FillerSlot.StTank => GoblinPunch,
        FillerSlot.AoeTank => RightRound,
        _ => SonicBoom,
    };

    internal static UserInt FillerConfig(FillerSlot slot) => slot switch
    {
        FillerSlot.AoeDps => BLU_AoE_DPS_Filler,
        FillerSlot.StTank => BLU_ST_Tank_Filler,
        FillerSlot.AoeTank => BLU_AoE_Tank_Filler,
        _ => BLU_ST_DPS_Filler,
    };

    /// <summary> Every filler that is a legal choice for a slot, best first. </summary>
    internal static IEnumerable<FillerInfo> Candidates(FillerSlot slot)
    {
        bool aoe = slot is FillerSlot.AoeDps or FillerSlot.AoeTank;
        var pool = Fillers.Where(f => aoe ? f.CanAoE : f.CanSingleTarget);

        // The upstream default sorts first so it stays the top of the list and the
        // first thing auto-detect reaches for.
        var def = DefaultFiller(slot);
        return pool.OrderByDescending(f => f.ActionId == def)
            .ThenByDescending(f => f.Potency);
    }

    #endregion

    #region Resolution

    /// <summary>
    ///     The filler to actually cast for a slot, or 0 when the user has none of them
    ///     slotted (in which case the caller must fall through to the original action,
    ///     exactly as upstream does).
    /// </summary>
    /// <param name="slot"> Which filler is being resolved. </param>
    /// <param name="rangedOnly">
    ///     Skip fillers the target is currently out of range of. Used by the tank
    ///     rotation, which wants a ranged filler while it is out of melee range of
    ///     its target.
    /// </param>
    internal static uint ResolveFiller(FillerSlot slot, bool rangedOnly = false)
    {
        var configured = (uint)(int)FillerConfig(slot);

        // Manual override. Honoured whenever the spell is actually slotted, so a stale
        // config (spell swapped out since) degrades to auto-detect instead of jamming.
        if (configured != 0 &&
            IsSpellActive(configured) &&
            (!rangedOnly || InActionRange(configured)))
            return configured;

        var def = DefaultFiller(slot);
        bool singleTargetSlot = slot is FillerSlot.StDps or FillerSlot.StTank;

        foreach (var filler in Candidates(slot))
        {
            // The slot's stock filler is always eligible even when it carries a
            // caveat (Right Round knocks back, but it IS what upstream picks) —
            // otherwise enabling this feature would silently change the rotation
            // for someone who never touched the setting.
            var isDefault = filler.ActionId == def;

            if (!filler.AutoSafe && !isDefault)
                continue;

            // Never auto-pick an AoE-shaped spell for a single-target slot. It would
            // do more damage on paper, but the splash pulls extra mobs and breaks
            // crowd control — that is the user's call to make, not ours. Still fully
            // selectable by hand.
            if (singleTargetSlot && !isDefault && filler.Shape is not FillerShape.Single)
                continue;

            if (rangedOnly && !InActionRange(filler.ActionId))
                continue;
            if (IsSpellActive(filler.ActionId))
                return filler.ActionId;
        }

        return 0;
    }

    /// <summary>
    ///     Resolves the filler and returns it, or <paramref name="actionID" /> untouched
    ///     when the user has nothing suitable slotted.
    /// </summary>
    internal static uint FillerOr(uint actionID, FillerSlot slot, bool rangedOnly = false)
    {
        var filler = ResolveFiller(slot, rangedOnly);
        return filler == 0 ? actionID : filler;
    }

    /// <summary>
    ///     The set of buttons a one-button preset hooks for a slot: the resolved filler
    ///     plus the upstream default, so the combo keeps working from the stock button
    ///     for anyone who has not touched the setting and has Sonic Boom slotted.
    /// </summary>
    /// <remarks>
    ///     Passed to <see cref="Native.CustomActionHelper.OneButtonRotationChecker" />
    ///     at runtime, which is what lets the hook follow the selection even for spells
    ///     that are not listed in the preset's <c>[ReplaceSkill]</c> attribute (that
    ///     attribute is frozen at startup and only drives the Features-pane icon row).
    ///     <para>
    ///         When the resolved filler IS the stock one we return only that, matching
    ///         upstream exactly. Notably the ST tank rotation casts a ranged filler when
    ///         out of melee, but upstream never hooked that button — and hooking it here
    ///         would make Sonic Boom start triggering the tank combo for users who never
    ///         touched this setting. Parity wins over tidiness.
    ///     </para>
    /// </remarks>
    internal static uint[] HookedActions(FillerSlot slot)
    {
        var resolved = ResolveFiller(slot);
        var def = DefaultFiller(slot);

        if (resolved == 0 || resolved == def)
            return [def];

        // Melee ST fillers pair with a ranged fallback, so hook that button too.
        var ranged = slot is FillerSlot.StDps or FillerSlot.StTank
            ? ResolveFiller(slot, rangedOnly: true)
            : 0;

        return ranged == 0 || ranged == resolved
            ? [resolved, def]
            : [resolved, ranged, def];
    }

    /// <summary> The slot a stock filler belongs to, or null if it isn't one. </summary>
    internal static FillerSlot? FillerSlotForDefault(uint actionId) => actionId switch
    {
        SonicBoom => FillerSlot.StDps,
        Electrogenesis => FillerSlot.AoeDps,
        GoblinPunch => FillerSlot.StTank,
        RightRound => FillerSlot.AoeTank,
        _ => null,
    };

    /// <summary>
    ///     Maps a BLU one-button preset to the filler the user actually has selected,
    ///     for consumers that would otherwise read the frozen <c>[ReplaceSkill]</c>
    ///     attribute (auto-rotation's learned/usable gate). Returns null for any preset
    ///     that is not one of the four filler-driven rotations, and for a user who has
    ///     no filler slotted at all (in which case the stock gate is the right answer).
    /// </summary>
    internal static uint? AutoActionOverride(Preset preset)
    {
        FillerSlot? slot = preset switch
        {
            Preset.BLU_ST_DPS => FillerSlot.StDps,
            Preset.BLU_AoE_DPS => FillerSlot.AoeDps,
            Preset.BLU_ST_Tank => FillerSlot.StTank,
            Preset.BLU_AoE_Tank => FillerSlot.AoeTank,
            _ => null,
        };

        if (slot is null)
            return null;

        var resolved = ResolveFiller(slot.Value);
        return resolved == 0 ? null : resolved;
    }

    #endregion
}
