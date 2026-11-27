namespace AutoPotion;

/// <summary>BuddyAction row IDs, also stored in CompanionInfo.ActiveCommand.</summary>
internal enum ChocoboStance : byte
{
    None = 0,
    Free = 4,
    Defender = 5,
    Attacker = 6,
    Healer = 7,
}

/// <summary>
/// Pure chocobo stance transitions. The game adapter supplies snapshots and executes
/// returned commands; keeping this separate makes every combat/HP boundary testable.
/// </summary>
internal sealed class ChocoboControl
{
    public const int HealerAttackerThreshold = 3;

    private ChocoboStance? _previousStance;

    public bool IsEngaged => _previousStance.HasValue;
    public ChocoboStance PreviousStance => _previousStance ?? ChocoboStance.None;

    public void Reset() => _previousStance = null;

    public static bool IsFullyHealed(uint currentHp, uint maxHp) => maxHp > 0 && currentHp >= maxHp;

    public static bool CountsAsAttacker(bool isTargetable, bool isEnemy, bool targetsPlayer) =>
        isTargetable && isEnemy && targetsPlayer;

    /// <summary>
    /// Return the stance to command, or None when no command is needed. Once engaged,
    /// Healer is retained until BOTH gates clear: fewer than three attackers and full HP.
    /// </summary>
    public ChocoboStance Update(
        bool enabled,
        bool companionPresent,
        bool livingPlayer,
        int attackerCount,
        bool fullyHealed,
        ChocoboStance currentStance)
    {
        // Disabling automation suspends commands but deliberately retains the saved
        // stance. Re-enabling can therefore finish the same encounter safely.
        if (!enabled)
            return ChocoboStance.None;

        // A dead player or missing companion ends the encounter. Do not carry a stale
        // snapshot into a later companion summon or a different life.
        if (!companionPresent || !livingPlayer)
        {
            Reset();
            return ChocoboStance.None;
        }

        if (!_previousStance.HasValue)
        {
            if (attackerCount < HealerAttackerThreshold)
                return ChocoboStance.None;

            // Never invent a stance while client state is unavailable or unrecognized.
            // Wait for a later tick; only then capture exactly what was active before.
            if (currentStance is not (ChocoboStance.Free or ChocoboStance.Defender
                or ChocoboStance.Attacker or ChocoboStance.Healer))
                return ChocoboStance.None;
            _previousStance = currentStance;
        }

        if (attackerCount < HealerAttackerThreshold && fullyHealed)
        {
            var restore = _previousStance.Value;
            if (currentStance == restore)
            {
                Reset();
                return ChocoboStance.None;
            }
            return restore;
        }

        // This also reasserts Healer if another command changes stance while either
        // safety gate is still closed. The adapter rate-limits actual command sends.
        return currentStance == ChocoboStance.Healer ? ChocoboStance.None : ChocoboStance.Healer;
    }
}
