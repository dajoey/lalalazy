using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     R4 target2 lane 2026-10-15 (Second Master's Board kill order and target choice): the Cross Charge sprite
///     ranked so the invulnerable Behemoth is dropped while a sprite lives, the Kinged Swordsmog first (her death
///     ends the fight), Hapalit before the Dirty Eye, the Summon's two Lamias killed evenly, and a petrified
///     Cyclops killed on purpose. Every title starts "r4-target2:".
/// </summary>
internal static partial class Program
{
    private static void R4Target2Lane20261015()
    {
        const uint spriteId = 14686, behemothId = 14684, giantId = 14685;
        const uint swordsmogId = 14680, mogmuggerId = 14683, melomogId = 14681, kingedCasterId = 14677;
        const uint hapalitId = 14691, dirtyEyeId = 14692;
        const uint lamiaId = 14661;

        BST_CrucibleLogic.TargetCandidate C(uint id, float hp = 100f) => new(id, hp, false);
        List<int> Allowed(params BST_CrucibleLogic.TargetCandidate[] c) => BST_CrucibleLogic.AllowedTargets(c);

        // ---- R4-fix-sprite-kill-first (5.12): the Cross Charge sprite is a priority add, so the Behemoth holding
        //      Invincible 775 while sprites live is dropped and the sprites are killed first.
        Check("r4-target2: R4-fix-sprite-kill-first: the Cross Charge sprite (14686) is a priority add",
            BST_CrucibleData.PriorityAdds.Contains(spriteId));
        Check("r4-target2: R4-fix-sprite-kill-first: sprite + Behemoth + Giant up: only the sprite is targetable (the Behemoth holds Invincible 775 while sprites live)",
            Allowed(C(spriteId), C(behemothId), C(giantId)).SequenceEqual(new[] { 0 }));

        // ---- R4-fix-kinged-swordsmog-order (5.11): she is the first tier — killing her ends the fight even with
        //      others alive, so she is killed as soon as reachable instead of last.
        Check("r4-target2: R4-fix-kinged-swordsmog-order: Kinged Swordsmog, Mogmugger, Melomog and kinged caster up: she is the pick (killing her ends the fight)",
            Allowed(C(swordsmogId), C(mogmuggerId), C(melomogId), C(kingedCasterId)).SequenceEqual(new[] { 0 }));

        // ---- R4-fix-hapalit-before-eye (5.13): the tier is split so the Burns source dies before the Dirty Eye.
        Check("r4-target2: R4-fix-hapalit-before-eye: Hapalit and the Dirty Eye up: only Hapalit is targetable (Hapalit first for its Burns)",
            Allowed(C(hapalitId), C(dirtyEyeId)).SequenceEqual(new[] { 0 }));

        // ---- R4 lamia-even-kill (5.7): the two Summon Lamias share one id and Raise 49287 revives a dead Lamia,
        //      so they must die evenly: the pair rule constrains two actors of the same id.
        Check("r4-target2: R4-lamia-even-kill: the Summon's two Lamias (one id, 40 points apart) are killed evenly: the pair is registered and only the healthier is targetable",
            BST_CrucibleData.Pairs.Contains((lamiaId, lamiaId))
            && Allowed(C(lamiaId, 80f), C(lamiaId, 40f)).SequenceEqual(new[] { 0 }));

        // ---- R4 petrify-cyclops (5.7): a petrified Cyclops (Stone Curse 437 from the baited Petrifying Regard)
        //      dies to any hit and must be hit on purpose; the state was not read before round 4.
        Check("r4-target2: R4-petrify-cyclops: the petrify state is read (a PetrifiedStatuses table exists and the targeting candidate carries it)",
            typeof(BST_CrucibleData).GetField("PetrifiedStatuses", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) is not null
            && typeof(BST_CrucibleLogic.TargetCandidate).GetProperty("Petrified") is not null);
    }
}
