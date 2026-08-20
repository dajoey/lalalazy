namespace GluttonyCombo.CustomComboNS.Functions;

internal abstract partial class CustomComboFunctions
{
    /// <summary>
    ///     Occult Crescent statuses that make one of the player's own cast-time cooldowns
    ///     redundant. Fork-local file so the nightly WrathCombo merge never has to resolve it.
    /// </summary>
    public static class OccultInstantCast
    {
        /// <summary>Occult Quick - Phantom Time Mage, action 41625.</summary>
        public const ushort OccultQuick = 4260;

        /// <summary>
        ///     Occult Crescent's own Dualcast, granted by Phantom Red Mage. This is NOT
        ///     Red Mage's Dualcast (1249), and unlike RDM's job-restricted variant (1393)
        ///     it carries no job restriction: it makes the next spell of any kind instant.
        ///     Mirrors <c>OccultCrescent.Buffs.Dualcast</c>, duplicated here so the shared
        ///     gates below don't reach into the content namespace.
        /// </summary>
        public const ushort Dualcast = 5438;
    }

    /// <summary>
    ///     Occult Quick is up, so every spell is already instant.
    ///     <para/>
    ///     Its tooltip: "reduces cast times for spells by 10 seconds. Duration: 20s". Nothing in
    ///     the game casts longer than that, it is a window rather than a one-shot proc, and
    ///     casting does not consume it. Any cast-time cooldown pressed underneath it is pure
    ///     waste - a 60s role cooldown, a Triplecast charge or an Acceleration charge spent on
    ///     something a 20s window is already giving away for free.
    ///     <para/>
    ///     Outside Occult Crescent this status cannot be present, so every gate that reads it is
    ///     inert there. That is what keeps the blast radius of these checks to the one zone.
    /// </summary>
    public static bool HasFreeInstantCasts =>
        HasStatusEffect(OccultInstantCast.OccultQuick);

    /// <summary>
    ///     Occult Dualcast is up, so the NEXT spell - one spell - is instant.
    ///     <para/>
    ///     Deliberately NOT treated the same as <see cref="HasFreeInstantCasts"/>. This is a
    ///     recurring proc that returns on its own, not a 20s blanket window, so it only gates
    ///     the raise paths, where the raise is demonstrably the next spell and a wasted
    ///     Swiftcast also delays the rez. It is kept out of the damage rotations on purpose:
    ///     a proc that may be up half the time would push a tightly-timed cooldown - BLM's
    ///     post-Despair Swiftcast especially - clean out of its window, losing more than the
    ///     proc is worth.
    /// </summary>
    public static bool HasOccultDualcast =>
        HasStatusEffect(OccultInstantCast.Dualcast);
}
