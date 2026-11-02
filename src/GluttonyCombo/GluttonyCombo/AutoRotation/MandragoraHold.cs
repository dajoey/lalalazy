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
    ///     Multi-target damage the Action sheet's hostile-effect flag does not mark: the damage
    ///     comes from a placed area or a channel, not from the action itself (Doton, Salted
    ///     Earth, Flamethrower).
    /// </summary>
    internal static readonly FrozenSet<uint> UnflaggedAreaDamage = new uint[] { 2270, 3639, 7418 }.ToFrozenSet();

    /// <summary>
    ///     Whether an action can hit more than one enemy: it damages hostiles
    ///     (<paramref name="damagesHostiles"/>, the Action sheet's hostile-effect flag, which is
    ///     false for heals, mitigation and party buffs) and its shape is not single-target
    ///     (circle, cone, line, ground area) with a non-zero effect range, or it is one of
    ///     <see cref="UnflaggedAreaDamage"/>.
    /// </summary>
    internal static bool HitsSeveral(uint actionId, byte castType, byte effectRange, bool damagesHostiles) =>
        UnflaggedAreaDamage.Contains(actionId)
        || (damagesHostiles && castType != SingleTargetCastType && effectRange > 0);

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
    ///     next anyway; a GCD is replaced by the button's own single-target action
    ///     (<paramref name="fallbackHitsSeveral"/> false and a different action), otherwise the
    ///     tick is skipped.
    /// </summary>
    internal static Outcome Decide(
        bool holding,
        bool resolvedHitsSeveral,
        bool resolvedIsAbility,
        bool fallbackHitsSeveral,
        bool fallbackIsResolved)
    {
        if (!holding || !resolvedHitsSeveral)
            return Outcome.Keep;

        if (resolvedIsAbility)
            return Outcome.Skip;

        if (fallbackIsResolved || fallbackHitsSeveral)
            return Outcome.Skip;

        return Outcome.Fallback;
    }

    /// <summary>
    ///     Whether the auto-rotation may run a preset: while the hold is on, damage presets
    ///     flagged AoE are left out (heal presets are not damage and stay).
    /// </summary>
    internal static bool MayRunPreset(bool holding, bool presetIsAoE, bool presetIsHeal) =>
        !holding || !presetIsAoE || presetIsHeal;
}
