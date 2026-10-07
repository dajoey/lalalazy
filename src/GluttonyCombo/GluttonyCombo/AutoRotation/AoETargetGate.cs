// Fork (1.0.4.292): pure decision core for the AutoRotation AoE enemy-count gate
// (task tasks-20261007-ffxiv-blm1-aoe2-build-01, ranked row BLM-1). Deliberately
// free of Dalamud and ECommons types so the offline harness
// (tests/GluttonyCombo.RotationHarness.BLM) asserts the exact semantics that
// ship; AutoRotationController consults it from ExecuteAoE where the previous
// inline check sent every job back to the single-target rotation whenever the
// best AoE target's enemy count sat below the configured DPSAoETargets count.

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     Decision core for the AoE enemy-count gate in ExecuteAoE. The AoE
///     rotation may run when the user has an AoE enemy-count configured and the
///     best AoE target has at least that many enemies in range. BLM-1: BLM
///     additionally runs its AoE rotation at exactly 2 enemies, where the BLM
///     AoE combo's own exactly-2 branch picks Blizzard4 over Freeze; every
///     other job and every other enemy count follow the configured threshold
///     alone. A null configured count (AoE disabled) means never.
/// </summary>
internal static class AoETargetGate
{
    /// <summary>
    ///     Whether ExecuteAoE may hand over to the job's AoE combo.
    ///     <paramref name="aoeTargets"/> null means the user disabled AoE
    ///     rotations entirely: no job runs AoE at any enemy count, BLM's
    ///     exactly-2 exception included.
    /// </summary>
    internal static bool MayRunAoe(int? aoeTargets, int maxHit, bool isBlackMage)
    {
        if (aoeTargets == null)
            return false;

        if (maxHit >= aoeTargets.Value)
            return true;

        // BLM-1: BLM's AoE combo has a dedicated exactly-2 branch
        // (BLM_Helper.cs:631-635 - Blizzard4 at exactly 2, Freeze otherwise) that
        // the default threshold of 3 made unreachable through AutoRotation. For
        // BLM and only BLM, exactly 2 enemies in range runs the AoE rotation;
        // 1 enemy stays single-target and every other count follows the
        // configured threshold.
        return isBlackMage && maxHit == 2;
    }
}
