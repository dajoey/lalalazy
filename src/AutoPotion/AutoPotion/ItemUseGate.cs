namespace AutoPotion;

/// <summary>
///     The uninterruptible-sequence gate for AutoPotion's item use (v0.2.5.2).
///     A self-targeted item use while the player holds an open multi-step input
///     sequence cancels it: on Ninja, any non-mudra action while mudra seals are
///     held (or mid Ten Chi Jin) drops the jutsu, so an automatic potion landing
///     in that window destroys the sequence the player or rotation was executing.
/// </summary>
/// <remarks>
///     Pure on purpose — no Dalamud types — so <c>tests/AutoPotion.MudraGateHarness</c>
///     compiles this file unchanged and asserts the hold/fire decisions offline,
///     the same split as <see cref="PotionTelemetryFormat"/> and
///     <c>tests/AutoPotion.TelemetryHarness</c>. The status ids are the ones
///     GluttonyCombo's Ninja module reads (NIN_Helper.cs <c>Buffs.Mudra</c> /
///     <c>Buffs.TenChiJin</c>); Kassatsu (497) is deliberately NOT gated — it is a
///     persisting buff that an item use does not consume.
/// </remarks>
internal static class ItemUseGate
{
    /// <summary> Mudra seals held (NIN). Same id as GluttonyCombo NIN_Helper <c>Buffs.Mudra</c>. </summary>
    public const uint MudraStatusId = 496;

    /// <summary> Ten Chi Jin active (NIN). Same id as GluttonyCombo NIN_Helper <c>Buffs.TenChiJin</c>. </summary>
    public const uint TenChiJinStatusId = 1186;

    /// <summary>
    ///     A critically low HP overrides the hold: a cancelled jutsu is cheaper than
    ///     dying. Fixed, not a knob — it is a survival floor, not a preference.
    /// </summary>
    public const float EmergencyHpFloor = 0.25f;

    /// <summary>
    ///     True when every item use should be held this tick. The hold is bounded by
    ///     the sequence itself (a mudra window is ~6 s, Ten Chi Jin ~10 s) and lifts
    ///     on the next tick once the status drops; only HP at or below the emergency
    ///     floor punches through.
    /// </summary>
    public static bool ShouldHold(bool mudraSealsUp, bool tenChiJinUp, float hpRatio)
        => (mudraSealsUp || tenChiJinUp) && hpRatio > EmergencyHpFloor;
}
