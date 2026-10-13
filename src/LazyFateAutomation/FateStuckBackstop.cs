namespace LazyFateAutomation;

/// <summary>
///     Pure decision core for FateGrind's combat stuck fallback (vnavmesh straight to the target). No Dalamud
///     references: the offline harness compiles this file.
///
///     With the grind's BossMod preset active, BossMod walks to the target (MiscAI.StayCloseToTarget) and dodges
///     (NormalMovement), so the fallback is only a backstop: it waits longer before calling the character stuck,
///     and it counts the character as "at the target" anywhere inside the BossMod role range (plus slack), so it
///     never drags a ranged job into melee or fights a dodge. Without BossMod it keeps the old 1.5 s / melee rule.
/// </summary>
internal static class FateStuckBackstop {
    /// <summary>Standing still this long while out of reach counts as stuck when nothing else moves the character.</summary>
    public const long StuckMsAlone = 1500;

    /// <summary>With BossMod driving, give it time to path around obstacles and finish a dodge first.</summary>
    public const long StuckMsWithBossMod = 4000;

    /// <summary>Extra yalms beyond BossMod's goal radius before the fallback calls the character out of reach.</summary>
    public const float ReachSlack = 1.0f;

    public static bool IsStuck(long stillForMs, bool bossModDriving)
        => stillForMs > (bossModDriving ? StuckMsWithBossMod : StuckMsAlone);

    /// <summary>
    ///     Distance from the target's centre that counts as "at the target". <paramref name="bossModRange"/> is the
    ///     StayCloseToTarget range in yalms while BossMod drives (its goal is range + hitbox radius from the centre),
    ///     or null when it does not.
    /// </summary>
    public static float ReachRange(float targetRadius, float? bossModRange)
        => bossModRange is { } r ? targetRadius + r + ReachSlack : Math.Max(3.0f, targetRadius + 2.0f);
}
