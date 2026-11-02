// Fork (1.0.4.325): pure decision core for the treasure-dungeon mandragora AoE hold.
// Deliberately free of Dalamud and ECommons types so the offline harness
// (tests/GluttonyCombo.MandragoraHoldHarness) asserts the exact semantics that ship.
// The Dalamud side (CustomCombo/Functions/MandragoraHold.cs) reads the object table
// and the Action sheet and hands the plain values in here.

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     The treasure dungeons' mandragora pack (onion, egg, garlic, tomato, queen) has to die
///     one at a time, in order, for the full reward. While any of them is alive, no action that
///     can hit more than one enemy may fire: the auto-rotation runs its single-target presets
///     only, a multi-target damage action that a single-target rotation resolves is replaced by
///     the job's own single-target button (a weave is skipped), and readiness checks report
///     such actions as not ready so rotations move on to their next priority.
/// </summary>
internal static class MandragoraHold
{
    /// <summary>
    ///     BNpcBase ids of the five-piece mandragora pack in every treasure dungeon (icon 1 to 5,
    ///     "needs to be killed in order from 1 to 5 for maximum rewards").
    /// </summary>
    internal static readonly FrozenSet<uint> PackBaseIds = new uint[]
    {
        0x1FCD, 0x1FCE, 0x1FCF, 0x1FD0, 0x1FD1, // The Hidden Canals of Uznair: canal onion, egg, garlic, tomato, queen
        0x2546, 0x2547, 0x2548, 0x2549, 0x254A, // The Shifting Altars of Uznair: altar
        0x2A06, 0x2A07, 0x2A08, 0x2A09, 0x2A0A, // The Dungeons of Lyhe Ghiah: dungeon
        0x301D, 0x301E, 0x301F, 0x3020, 0x3021, // The Shifting Oubliettes of Lyhe Ghiah: secret
        0x3808, 0x3809, 0x380A, 0x380B, 0x380C, // The Excitatron 6000: exciting
        0x3D4F, 0x3D50, 0x3D51, 0x3D52, 0x3D53, // The Shifting Gymnasion Agonon: gymnastic
        0x4300, 0x4301, 0x4302, 0x4303, 0x4304, // Cenote Ja Ja Gural: Turali onion..tomato, Tuligora queen
        0x48B9, 0x48BA, 0x48BB, 0x48BC, 0x48BD, // Vault Oneiron: vault
    }.ToFrozenSet();

    /// <summary>
    ///     BNpcName ids of the same pack, so a dungeon that reuses a family's names under other
    ///     base ids (the Lost Canals of Uznair shares the canal names) is held too.
    /// </summary>
    internal static readonly FrozenSet<uint> PackNameIds = new uint[]
    {
        6847, 6848, 6849, 6850, 6851,      // canal onion, egg, garlic, tomato, queen
        7604, 7605, 7606, 7607, 7608,      // altar
        8684, 8685, 8686, 8687, 8688,      // dungeon
        9801, 9802, 9803, 9804, 9805,      // secret
        10835, 10836, 10837, 10838, 10839, // exciting
        12036, 12037, 12038, 12039, 12040, // gymnastic
        13242, 13243, 13244, 13245, 13246, // Turali onion..tomato, Tuligora queen
        14016, 14017, 14018, 14019, 14020, // vault
    }.ToFrozenSet();

    /// <summary>Action sheet CastType 1: the action hits its one target only.</summary>
    internal const byte SingleTargetCastType = 1;

    /// <summary>Whether a battle NPC with this base id or name id is one of the ordered mandragora pack.</summary>
    internal static bool IsPackMember(uint baseId, uint nameId) =>
        PackBaseIds.Contains(baseId) || PackNameIds.Contains(nameId);

    /// <summary>
    ///     Whether the hold is on: at least one pack member in <paramref name="npcs"/> is alive.
    ///     The hold lasts until the whole pack is dead.
    /// </summary>
    internal static bool IsHolding(IEnumerable<(uint BaseId, uint NameId, bool IsDead)> npcs) =>
        npcs.Any(n => !n.IsDead && IsPackMember(n.BaseId, n.NameId));

    /// <summary>
    ///     Held although the Action sheet does not mark them as multi-target damage: the damage
    ///     comes from a placed area or a channel (Doton, Salted Earth, Flamethrower), or the
    ///     action starts something whose follow-up is multi-target damage: a dance whose finish
    ///     is an AoE (Standard Step, Technical Step), a primal or demi-summon whose pet attack
    ///     is an AoE (Summon Ifrit II, Summon Titan II, Summon Garuda II, Enkindle Bahamut,
    ///     Enkindle Phoenix, Enkindle Solar Bahamut), or Living Shadow (its simulacrum casts
    ///     Abyssal Drain, Shadowbringer and Disesteem).
    /// </summary>
    internal static readonly FrozenSet<uint> AlwaysHeld = new uint[]
    {
        2270, 3639, 7418,          // Doton, Salted Earth, Flamethrower
        15997, 15998,              // Standard Step, Technical Step
        25838, 25839, 25840,       // Summon Ifrit II, Summon Titan II, Summon Garuda II
        7429, 16516, 36998,        // Enkindle Bahamut, Enkindle Phoenix, Enkindle Solar Bahamut
        16472,                     // Living Shadow
    }.ToFrozenSet();

    /// <summary>
    ///     Shaped and flagged like an AoE attack but deal no damage, so never held: Reprisal
    ///     (tank mitigation, a damage-down on enemies around the tank).
    /// </summary>
    internal static readonly FrozenSet<uint> NeverHeld = new uint[] { 7535 }.ToFrozenSet();

    /// <summary>
    ///     Whether an action can hit more than one enemy: it damages hostiles
    ///     (<paramref name="damagesHostiles"/>, the Action sheet's hostile-effect flag, which is
    ///     false for heals, mitigation and party buffs) and its shape is not single-target
    ///     (circle, cone, line, ground area) with a non-zero effect range, or it is one of
    ///     <see cref="AlwaysHeld"/>; never one of <see cref="NeverHeld"/>.
    /// </summary>
    internal static bool HitsSeveral(uint actionId, byte castType, byte effectRange, bool damagesHostiles)
    {
        if (NeverHeld.Contains(actionId))
            return false;

        if (AlwaysHeld.Contains(actionId))
            return true;

        return damagesHostiles && castType != SingleTargetCastType && effectRange > 0;
    }

    /// <summary>
    ///     The single-target action to use instead of a held one: the button's current form
    ///     (<paramref name="hookedButton"/>, the game's own upgrade of the pressed button) when
    ///     that is single-target, otherwise 0 (none). The button's base action is never used:
    ///     queued presses bypass the action-replacer hook and the game turns the base action
    ///     straight back into its current form (Jolt III into Grand Impact).
    /// </summary>
    internal static uint PickFallback(uint resolved, uint hookedButton, bool hookedHitsSeveral) =>
        hookedButton != 0 && hookedButton != resolved && !hookedHitsSeveral ? hookedButton : 0;

    /// <summary>What to do with an action a rotation resolved while the hold may be on.</summary>
    internal enum Outcome
    {
        /// <summary>Use the resolved action as is.</summary>
        Keep,

        /// <summary>Use the job's own single-target button instead.</summary>
        Fallback,

        /// <summary>Use nothing this tick.</summary>
        Skip,
    }

    /// <summary>
    ///     Decision for one resolved action. A multi-target action while the hold is on is never
    ///     kept: a weave (<paramref name="resolvedIsAbility"/>) is skipped, because the GCD comes
    ///     next anyway; a GCD is replaced by the single-target fallback when there is one
    ///     (<paramref name="hasFallback"/>, from <see cref="PickFallback"/>), otherwise the tick
    ///     is skipped.
    /// </summary>
    internal static Outcome Decide(bool holding, bool resolvedHitsSeveral, bool resolvedIsAbility, bool hasFallback)
    {
        if (!holding || !resolvedHitsSeveral)
            return Outcome.Keep;

        if (resolvedIsAbility || !hasFallback)
            return Outcome.Skip;

        return Outcome.Fallback;
    }

    /// <summary>
    ///     Whether a cached hold reading taken at <paramref name="readAtMs"/> is still fresh at
    ///     <paramref name="nowMs"/>. A hold that has never been read (<paramref name="hasRead"/>
    ///     false) is never fresh; a clock that went backwards is not fresh either.
    /// </summary>
    internal static bool CacheFresh(long nowMs, long readAtMs, bool hasRead, long windowMs)
    {
        if (!hasRead)
            return false;

        var age = nowMs - readAtMs;
        return age >= 0 && age < windowMs;
    }

    /// <summary>
    ///     Whether the auto-rotation may run a preset: while the hold is on, damage presets
    ///     flagged AoE are left out (heal presets are not damage and stay).
    /// </summary>
    internal static bool MayRunPreset(bool holding, bool presetIsAoE, bool presetIsHeal) =>
        !holding || !presetIsAoE || presetIsHeal;
}
