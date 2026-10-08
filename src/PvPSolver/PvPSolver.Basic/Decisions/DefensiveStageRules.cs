namespace RotationSolver.Decisions;

/// <summary>One other row of the table, as the chaining rule sees it.</summary>
/// <param name="Kind">The other row's kind.</param>
/// <param name="Enabled">Whether the other row is in force (master switch on and the row enabled).</param>
/// <param name="StatusActive">Whether a status the other row provides is active on the character.</param>
public readonly record struct DefensiveSibling(DefensiveKind Kind, bool Enabled, bool StatusActive);

/// <summary>
///     Fork-only decision core (no game types): the two rules that keep the generic defensive stage from walking
///     down the table in one go. The worst case (several enabled rows can still be used one after another during
///     one long low-HP episode) is stated in every row tooltip and the changelog.
/// </summary>
public static class DefensiveChaining
{
    /// <summary>After one generic row is used, no other generic row is used for this long.</summary>
    public const long LockoutMs = 2500;

    /// <summary>
    ///     True when a row of <paramref name="kind"/> must be skipped: the lockout after the last generic use is
    ///     still running (<paramref name="nowMs"/> before <paramref name="lockoutUntilMs"/>), or any other row of the
    ///     same kind is enabled and has its status active on the character.
    /// </summary>
    public static bool Blocked(DefensiveKind kind, long nowMs, long lockoutUntilMs, IReadOnlyList<DefensiveSibling> others)
    {
        if (nowMs < lockoutUntilMs)
        {
            return true;
        }

        for (int i = 0; i < others.Count; i++)
        {
            DefensiveSibling other = others[i];
            if (other.Kind == kind && other.Enabled && other.StatusActive)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Fork-only decision core (no game types): when the whole generic defensive stage stays silent.</summary>
public static class DefensiveSilence
{
    /// <summary>The character must have been alive for more than this many seconds.</summary>
    public const float MinTimeAliveSeconds = 5f;

    /// <summary>
    ///     True when the stage must not use anything: PvP Guard is up, a status that silences it is active
    ///     (<see cref="DefensiveTable.SilenceStatuses"/>), no hostile is within the maximum range (a quiet moment), or
    ///     the character has not been alive for more than five seconds. An unknown time alive (NaN) is silent.
    /// </summary>
    public static bool Silenced(bool guardUp, bool silencingStatusActive, bool hostileInMaxRange, float timeAliveSeconds) =>
        guardUp || silencingStatusActive || !hostileInMaxRange || !(timeAliveSeconds > MinTimeAliveSeconds);
}

/// <summary>Fork-only decision core (no game types): the gate on the existing Recuperate call.</summary>
public static class DefensiveGate
{
    /// <summary>
    ///     A no-op while the Recuperate row is not in force. In force, the existing call also needs the HP test, so
    ///     Recuperate is used only while the health ratio is strictly below the row's percentage.
    /// </summary>
    public static bool RecuperateAllows(bool rowInForce, float healthRatio, float percent) =>
        !rowInForce || DefensiveTrigger.ShouldUse(true, healthRatio, percent);
}
