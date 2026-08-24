using System;

namespace GluttonyCombo.CustomComboNS.Functions;

internal abstract partial class CustomComboFunctions
{
    /// <summary>
    ///     Occult Crescent statuses that make one of the player's own cast-time cooldowns
    ///     redundant. Fork-local file so the nightly WrathCombo merge never has to resolve it.
    /// </summary>
    public static class OccultInstantCast
    {
        /// <summary>Occult Quick, the status.</summary>
        public const ushort OccultQuick = 4260;

        /// <summary>
        ///     Occult Quick, the action (Phantom Time Mage). Needed as well as the status
        ///     because the buff does not exist until the server applies it, and the plugin
        ///     presses Swiftcast from a different code path in the meantime.
        /// </summary>
        public const uint OccultQuickAction = 41625;

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
    ///     <para/>
    ///     The <c>JustUsed</c> half is not belt-and-braces, it is the whole bug. Gluttony
    ///     presses Occult Quick itself, and the status lands a beat later; for those few ticks
    ///     the status check alone reads false and the very next thing the rotation does is
    ///     spend Swiftcast. That is exactly the "Swiftcast immediately after Occult Quick" the
    ///     v1.0.4.144 gates failed to stop. The pre-7.55 Occult Comet handler has always
    ///     carried the same pairing (<c>!HasStatusEffect(Buffs.OccultQuick) &amp;&amp;
    ///     !JustUsed(OccultQuick)</c>) for this reason - v1.0.4.144 copied the status half and
    ///     not the timing half.
    /// </summary>
    public static bool HasFreeInstantCasts =>
        HasStatusEffect(OccultInstantCast.OccultQuick) ||
        OccultQuickJustOffered ||
        JustUsed(OccultInstantCast.OccultQuickAction);

    /// <summary>
    ///     Our own record of Occult Quick going out, because ActionWatching's is written too
    ///     late to be any use here.
    ///     <para/>
    ///     <c>ActionWatching</c> does not stamp <c>ActionTimestamps</c> when an action is used -
    ///     it schedules the stamp with
    ///     <c>Svc.Framework.RunOnTick(..., castTime - 480ms)</c>. Occult Quick is a 1500ms cast,
    ///     so the record does not exist for the first <b>1020ms</b>, and the only immediate
    ///     stamp path in that file is gated on ground-targeted actions and items. During the
    ///     window where the next action is chosen and queued, both <c>JustUsed</c> and the
    ///     status read false - which is why the v1.0.4.145 and .146 gates were no-ops.
    ///     <para/>
    ///     Stamped at the moment the plugin OFFERS Occult Quick, which is marginally earlier
    ///     than it landing: a cast that then fails on range or MP still suppresses Swiftcast for
    ///     the window. That is the deliberate trade - a short over-suppression beats the
    ///     cooldown being burned.
    /// </summary>
    private static long _occultQuickOfferedTick;

    private const long OccultQuickSpacingMs = 3000;

    public static bool OccultQuickJustOffered =>
        _occultQuickOfferedTick != 0 &&
        Environment.TickCount64 - _occultQuickOfferedTick <= OccultQuickSpacingMs;

    /// <summary>Record that Occult Quick is on its way out. See <see cref="OccultQuickJustOffered"/>.</summary>
    public static void MarkOccultQuickOffered() =>
        _occultQuickOfferedTick = Environment.TickCount64;

    /// <summary>
    ///     Occult Dualcast is up, so the NEXT spell - one spell - is instant.
    ///     <para/>
    ///     Still not the same object as <see cref="HasFreeInstantCasts"/>: Occult Quick is a 20s
    ///     blanket window that casting does not consume, this is a single held charge. But
    ///     v1.0.4.144's decision to keep it out of the damage rotations was wrong, and the
    ///     status sheet says why. Status 5438 is flagged <c>IsPermanent</c>. RDM's Dualcast
    ///     (1249), Swiftcast (167), Triplecast (1211) and Occult Quick (4260) are every one of
    ///     them flagged timed; this is not. It has no clock, so it cannot be lost to time - it
    ///     can only be SPENT, and FFXIV spends a Dualcast on the execution of any action that
    ///     is not an ability, already-instant spells included.
    ///     <para/>
    ///     Two wastes fall out of that one fact, and Joey reported both:
    ///     <list type="bullet">
    ///     <item>Buying an instant while holding one. A Triplecast charge or a 60s Swiftcast
    ///     pressed under Dualcast pays for a cast that was already free.</item>
    ///     <item>Spending it on a spell that was ALREADY instant. This is the "loses the buff
    ///     before it can be used" half - nothing expired, because nothing can; a movement-filler
    ///     Xenoglossy or a 1.5s phantom nova ate it for no gain.</item>
    ///     </list>
    ///     <para/>
    ///     Having no clock is also what makes standing down safe. A gate on a timed proc can
    ///     strand the cooldown it suppressed; this one cannot, because whatever the rotation
    ///     casts instead is what spends the Dualcast, so the gate is open again on the very
    ///     next GCD. The .144 note feared this would push BLM's post-Despair Swiftcast "clean
    ///     out of its window" - the delay is exactly one GCD, and that GCD was free.
    /// </summary>
    public static bool HasOccultDualcast =>
        HasStatusEffect(OccultInstantCast.Dualcast);

    /// <summary>
    ///     Occult Crescent is already making the next spell instant, by either route - the
    ///     Occult Quick window or a held Occult Dualcast. The gate for "do not buy an instant
    ///     cast" and for "do not spend a movement filler that was instant anyway".
    /// </summary>
    public static bool HasOccultInstantCast =>
        HasFreeInstantCasts || HasOccultDualcast;
}
