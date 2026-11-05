namespace RotationSolver.Decisions;

/// <summary>
///     Fork-only decision core (no Dalamud or game types, compiles standalone): the radius inside which an enemy
///     must stand for <c>AttackAbility</c> to be considered. Used only at the two <c>AttackAbility</c> gates in
///     <c>CustomRotation.Ability</c>; the shared <c>DataCenter.JobRange</c> is not changed.
/// </summary>
public static class AttackRangeGate
{
    /// <summary>PvP melee weaponskills reach 5 yalms.</summary>
    public const float MeleeWeaponskillRange = 5f;

    /// <summary>
    ///     5 yalms for a melee job in PvP, otherwise <paramref name="jobRange"/> unchanged (tanks, ranged jobs and
    ///     PvE keep their value).
    /// </summary>
    public static float Radius(bool isPvP, bool isMelee, float jobRange) =>
        isPvP && isMelee ? MeleeWeaponskillRange : jobRange;
}
