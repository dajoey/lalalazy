namespace GluttonyCombo.Core;

/// <summary>
///     The Dalamud-free decision table for "which detected tank buster VFXs are in scope".
/// </summary>
/// <remarks>
///     <para>
///         This exists so that the auto-rotation shield and the TTS/toast alert cannot drift apart
///         again. Before v1.0.4.175 they each carried their own copy of the scope test:
///         <c>TryGetTankBusterTarget</c> honoured "Also shield tankbusters outside your party" and
///         <c>PlayTankbusterAlert</c> was hardcoded to party members, so on an out-of-party tank
///         buster the plugin would shield the victim while staying completely silent about it -
///         acting on an event it did not announce.
///     </para>
///     <para>
///         Kept free of every Dalamud and ECommons type on purpose: the caller in
///         <c>CustomCombo/Functions/VFX.cs</c> does the game-state gathering, this answers the
///         question, and <c>tests/GluttonyCombo.TankbusterScopeHarness</c> compiles THIS file and
///         asserts the whole truth table offline. Fork-owned, so upstream WrathCombo merges do not
///         touch it.
///     </para>
/// </remarks>
internal static class TankbusterScope
{
    /// <summary>
    ///     How much we actually know about the VFX target's combat role.
    /// </summary>
    internal enum TargetRole
    {
        /// <summary>The target resolved as a Tank.</summary>
        Tank,

        /// <summary>The target resolved as a real, non-Tank combat role (Healer or DPS).</summary>
        OtherCombatRole,

        /// <summary>
        ///     No usable role came back. This is <c>CombatRole.NonCombat</c> from ECommons'
        ///     <c>GetRole(ICharacter)</c>, which reads <c>ICharacter.ClassJob</c> - and that is
        ///     empty for trusted / Occult Crescent NPCs. This fork already works around the same
        ///     hole for NPCs that ARE in your party, by reading the job out of
        ///     <c>InfoProxyPartyMember</c> instead (<c>Party.cs</c> <c>NPCClassJob</c> and the
        ///     separate <c>GetRole(WrathPartyMember)</c> in <c>BattleCharaExtensions.cs</c>). An
        ///     out-of-party NPC is not in that proxy at all, so nothing can resolve it.
        /// </summary>
        Unresolved,
    }

    /// <summary>
    ///     Decides whether a tank buster VFX that has already matched
    ///     <c>IsTankBusterEffectPath</c> is one this plugin should act on or announce.
    /// </summary>
    /// <param name="inParty">The VFX target is a member of the player's party.</param>
    /// <param name="isFriendly">
    ///     The VFX target is friendly - in practice "we can land a heal on it", since the caller
    ///     derives this from <c>TargetIsFriendly</c>, which probes <c>CanUseOn(Esuna)</c> with a
    ///     <c>Cure</c> fallback for event NPCs. This is what keeps the out-of-party arm honest.
    /// </param>
    /// <param name="role">What we managed to learn about the target's role.</param>
    /// <param name="includeOutOfParty">
    ///     The user's "Also shield tankbusters outside your party" setting
    ///     (<c>HealerSettings.TankbustersBeyondParty</c>).
    /// </param>
    /// <returns><see langword="true" /> if the buster is in scope.</returns>
    internal static bool Allows(
        bool inParty, bool isFriendly, TargetRole role, bool includeOutOfParty)
    {
        // Party members: unchanged from every version that has ever shipped this feature - the
        // target must resolve as a Tank. That test is doing real work, because one tracked path
        // ("vfx/lockon/eff/target_ae_s5f", YorHa 3) also matches some spread markers, and the
        // role is what tells those apart. Party members always resolve a role, so nothing is
        // lost by demanding one here.
        if (inParty)
            return role is TargetRole.Tank;

        // Everything below is reachable only with the setting on, so with it off this function
        // is exactly the old party-only behaviour.
        if (!includeOutOfParty || !isFriendly)
            return false;

        // Out of party. An alliance player resolves a real role, so an alliance HEALER or DPS is
        // still correctly excluded by OtherCombatRole. An unresolved role, however, is the normal
        // state of a trusted / Occult NPC rather than evidence that it is not the buster target -
        // and refusing it there is what made v1.0.4.171's promise of "alliance members and
        // trusted NPCs in the Occult Crescent" only half true: the alliance half shipped, the NPC
        // half was filtered out before it ever reached the shield.
        //
        // Accepting it is safe because two much stronger signals have already been checked: the
        // VFX carries a tank buster path, and isFriendly has proven we can cast a heal on it.
        return role is TargetRole.Tank or TargetRole.Unresolved;
    }
}
