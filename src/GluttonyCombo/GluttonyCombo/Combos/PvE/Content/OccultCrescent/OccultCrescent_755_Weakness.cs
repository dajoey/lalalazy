#region Dependencies

using System.Collections.Generic;
using GluttonyCombo.Extensions;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;

#endregion

namespace GluttonyCombo.Combos.PvE;

// ============================================================================================
//  GLUTTONY 7.55 PHANTOM JOB STOPGAP -- elemental weakness support.
//  DELETE THIS WHOLE FILE when upstream Wrath ships its own 7.55 phantom job support.
//
//  Several 7.55 phantom actions only pay off when the target carries the matching elemental
//  weakness debuff. Occult Libra (Phantom Red Mage) applies it; otherwise the weakness is an
//  intrinsic property of the mob. We therefore check the LIVE status first and fall back to a
//  static per-mob table.
//
//  The nameId -> weakness table is adapted from FFXIV-CombatReborn/RotationSolverReborn
//  (StatusHelper.OccultWeaknessByNameId, commits 2ac940563..443f4e0be, 2026-07-29..08-01).
//  That is their datamining work, not ours.
// ============================================================================================
internal partial class OccultCrescent
{
    internal static class Weak755
    {
        // Elemental weakness status IDs (datamined from live 7.55 sqpack, 2026-08-01).
        public const ushort
            FireWeakness = 5322,
            IceWeakness = 5323,
            LightningWeakness = 5324,
            WindWeakness = 5325;

        // Bit flags, kept terse so the table below stays readable.
        private const byte F = 1, I = 2, L = 4, W = 8;

        /// <summary>
        ///     Maps a mob's nameId to the elemental weakness(es) it is intrinsically vulnerable to.
        /// </summary>
        internal static readonly Dictionary<uint, byte> ByNameId = new()
        {
            { 13876, L }, { 13939, I }, { 13940, W }, { 13941, F }, { 13942, L },
            { 14490, W }, { 14491, F }, { 14503, I }, { 14505, L }, { 14508, L },
            { 14509, L }, { 14520, F }, { 14523, I }, { 14714, F }, { 14717, (byte)(F | W) },
            { 14720, I }, { 14726, F }, { 14728, L }, { 14735, F }, { 14736, I },
            { 14738, F }, { 14762, F }, { 14764, (byte)(L | W) }, { 14765, F }, { 14767, W },
            { 14771, F }, { 14772, I }, { 14775, L }, { 14776, F }, { 14785, F },
            { 14790, F }, { 14791, I }, { 14801, W }, { 14802, L }, { 14804, L },
            { 14805, I }, { 14806, L }, { 14809, L }, { 14817, L }, { 14820, L },
            { 14840, I }, { 14841, I }, { 14857, L }, { 14858, L }, { 14859, L },
            { 14860, F }, { 14861, F }, { 14862, F }, { 14863, I }, { 14864, W },
            { 14865, F }, { 14866, F }, { 14867, I }, { 14868, L }, { 14869, (byte)(F | I) },
            { 14870, F }, { 14871, F }, { 14872, W }, { 14873, W }, { 14874, L },
            { 14875, (byte)(L | I) }, { 14876, L }, { 14877, L }, { 14878, (byte)(F | W) }, { 14879, I },
            { 14880, F }, { 14881, I }, { 14882, F }, { 14883, (byte)(I | W) }, { 14884, W },
            { 14885, F }, { 14886, F }, { 14887, F }, { 14888, F }, { 14889, I },
            { 14890, L }, { 14891, L }, { 14892, F }, { 14893, F }, { 14894, F },
            { 14895, I }, { 14896, W }, { 14897, I }, { 14898, I }, { 14899, W },
            { 14900, L }, { 14901, L }, { 14902, I }, { 14903, W }, { 14904, W },
            { 14905, L }, { 14906, I }, { 14907, W }, { 14908, (byte)(I | W) }, { 14909, F },
            { 14910, W }, { 14911, W }, { 14912, W }, { 14913, F }, { 14914, I },
            { 14915, F }, { 14917, W }, { 14918, W }, { 14919, F }, { 14920, I },
            { 14921, W }, { 14922, L }, { 14923, W }, { 14929, F }, { 14930, W },
            { 14931, F }, { 14932, I },
        };

        internal static byte FlagFor(ushort weaknessStatus) => weaknessStatus switch
        {
            FireWeakness => F,
            IceWeakness => I,
            LightningWeakness => L,
            WindWeakness => W,
            _ => 0,
        };
    }

    /// <summary>
    ///     Whether the current target is weak to the given element. Prefers the live debuff
    ///     (e.g. revealed by Occult Libra); falls back to the static per-mob table.
    /// </summary>
    private static bool TargetWeakTo(ushort weaknessStatus)
    {
        if (CurrentTarget is not { } target)
            return false;

        // A live weakness debuff on the target always wins.
        if (HasStatusEffect(weaknessStatus, target, anyOwner: true))
            return true;

        byte flag = Weak755.FlagFor(weaknessStatus);
        return flag != 0 &&
               Weak755.ByNameId.TryGetValue(target.GetNameId(), out byte known) &&
               (known & flag) != 0;
    }

    /// <summary>
    ///     Whether the target is carrying a LIVE elemental weakness debuff, ignoring the static
    ///     nameId table entirely. This is deliberately NOT <see cref="TargetWeakTo"/>: that answers
    ///     "do we believe this mob is weak to X", which is the right question for choosing a spell.
    ///     This answers "has the weakness actually been revealed on it", which is the right
    ///     question for deciding whether Occult Libra still has work to do.
    /// </summary>
    private static bool TargetHasAnyWeaknessDebuff()
    {
        if (CurrentTarget is not { } target)
            return false;

        return HasStatusEffect(Weak755.FireWeakness, target, anyOwner: true) ||
               HasStatusEffect(Weak755.IceWeakness, target, anyOwner: true) ||
               HasStatusEffect(Weak755.LightningWeakness, target, anyOwner: true) ||
               HasStatusEffect(Weak755.WindWeakness, target, anyOwner: true);
    }

    /// <summary>
    ///     Weakness gate honouring the user's "only when weak" toggle. When the toggle is off we
    ///     let the action fire regardless, since an unresisted cast is still damage.
    /// </summary>
    private static bool WeaknessGate(ushort weaknessStatus) =>
        !IsEnabled(Preset.Phantom755_RequireWeakness) || TargetWeakTo(weaknessStatus);
}
