using System;
using Dalamud.Plugin.Services;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

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

        /// <summary>
        ///     Phantom Red Mage's index in the MKDSupportJob sheet, which is what
        ///     <c>PublicContentOccultCrescent.State.CurrentSupportJob</c> holds. Mirrors
        ///     <c>OccultCrescent.JobIDs.RedMage</c>, duplicated here for the same reason the
        ///     status id is: this file does not reach into the content namespace.
        /// </summary>
        public const byte RedMageSupportJob = 22;
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
    ///     Occult Dualcast is in hand, so the NEXT spell - one spell - is instant.
    ///     <para/>
    ///     Not the same object as <see cref="HasFreeInstantCasts"/>: Occult Quick is a 20s
    ///     blanket window that casting does not consume, this is a single charge. Cast anything
    ///     with a cast time and the buff appears; it behaves like Swiftcast, with a comparable
    ///     duration, and it EXPIRES if it is not used. It comes straight back off the next hard
    ///     cast, which is what makes it cheap to lose and easy to keep losing.
    ///     <para/>
    ///     <b>v1.0.4.150 correction.</b> v1.0.4.148 claimed this proc has no clock, reasoning
    ///     from status 5438 carrying <c>IsPermanent</c> while RDM's Dualcast (1249), Swiftcast
    ///     (167), Triplecast (1211) and Occult Quick (4260) do not. That inference was wrong and
    ///     is retracted. What is permanent is the Phantom Red Mage TRAIT, not the proc it grants.
    ///     The flag is not a duration signal either: statuses 1378, 1798 and 5438 all carry
    ///     <c>IsPermanent</c> with the identical description "The next spell will be cast
    ///     immediately", and they are not three untimed procs. Duration lives on whatever applies
    ///     the status, not on the row. Do not re-derive timing from this field.
    ///     <para/>
    ///     The behaviour built on the wrong reason still holds, for a better one. Two wastes,
    ///     both of which Joey reported:
    ///     <list type="bullet">
    ///     <item>Buying an instant while holding one. A Triplecast charge or a 60s Swiftcast
    ///     pressed under Dualcast pays for a cast that was already free.</item>
    ///     <item>Spending it on a spell that was ALREADY instant - a movement-filler Xenoglossy
    ///     destroys the proc for no gain, and a proc left unspent simply times out. Since it
    ///     does expire, spending it promptly is more urgent than .148 assumed, not less.</item>
    ///     </list>
    ///     <para/>
    ///     What .148 got right for the wrong reason: standing down does not strand the cooldown
    ///     it suppressed, because whatever the rotation casts instead is what spends the
    ///     Dualcast, so the gate reopens on the very next GCD. The delay is one GCD, and that
    ///     GCD was free.
    /// </summary>
    public static bool HasOccultDualcast =>
        HasStatusEffect(OccultInstantCast.Dualcast);

    /// <summary>
    ///     Occult Crescent is making the next spell instant RIGHT NOW - the Occult Quick window
    ///     or a Dualcast already in hand. Strict: for sites that affirmatively pick a long cast
    ///     because it will come out instant. To suppress a press, use
    ///     <see cref="HasOrExpectsOccultInstantCast"/> instead.
    /// </summary>
    public static bool HasOccultInstantCast =>
        HasFreeInstantCasts || HasOccultDualcast;

    /// <summary>
    ///     The gate for every "do not buy an instant cast" decision - Swiftcast, Triplecast, a
    ///     movement filler. Covers a Dualcast that has not landed yet as well as one in hand.
    ///     <para/>
    ///     Deliberately wider than <see cref="HasOccultInstantCast"/>: suppressing a press that
    ///     turns out to be unnecessary costs a cooldown briefly held, while pressing one that
    ///     turns out to be redundant costs the cooldown outright.
    /// </summary>
    public static bool HasOrExpectsOccultInstantCast =>
        HasOccultInstantCast || OccultDualcastIncoming;

    /// <summary>
    ///     A Dualcast is in hand or inbound. Dualcast-only on purpose: the movement blocks that
    ///     read this stand down because their fillers would CONSUME the proc, and an Occult Quick
    ///     window is not consumed by casting, so it does not belong in the same test.
    /// </summary>
    public static bool HasOrExpectsOccultDualcast =>
        HasOccultDualcast || OccultDualcastIncoming;

    /// <summary>Phantom Red Mage is the equipped support job, so the Dualcast trait is live.</summary>
    public static unsafe bool PhantomRedMageEquipped
    {
        get
        {
            var instance = PublicContentOccultCrescent.GetInstance();
            return (nint)instance != nint.Zero &&
                   instance->State.CurrentSupportJob == OccultInstantCast.RedMageSupportJob;
        }
    }

    private static long _occultDualcastExpectedUntilTick;
    private static long _occultDualcastCastDueTick;
    private static bool _seenOccultDualcast;

    /// <summary>Slack between a cast finishing and the server applying the Dualcast.</summary>
    private const long OccultDualcastLandingGraceMs = 1500;

    /// <summary>
    ///     How early a cast has to stop before it counts as interrupted rather than finished.
    ///     Matches the tolerance <c>CheckInterruptedCasts</c> already uses for the same judgement.
    /// </summary>
    private const long OccultDualcastInterruptToleranceMs = 500;

    /// <summary>
    ///     A Dualcast is on its way but has not landed, so treat it as held.
    ///     <para/>
    ///     Joey named the case: slide-casting. Move at the tail of a cast and the proc still
    ///     comes, but the input for the NEXT GCD is chosen and queued while that cast is still
    ///     running - before the status exists. A gate reading only the buff is open at exactly
    ///     the moment it matters, and out goes a Swiftcast or a Triplecast for an instant already
    ///     paid for. Same shape as the v1.0.4.145/.146 Occult Quick failures, reached from the
    ///     other end: there the plugin raced its own press, here it races the player's movement.
    ///     <para/>
    ///     The tell is the cast bar itself. Anything an instant-cast effect already covered never
    ///     shows one, and a spell cast under such an effect does not grant a Dualcast either - so
    ///     while the trait is live, "a cast is running" and "a Dualcast is coming" are the same
    ///     statement. No need to know which spell it is, and it covers casts the player started
    ///     by hand as well as ones the plugin chose.
    ///     <para/>
    ///     Gated on having actually seen status 5438 at least once under this support job, rather
    ///     than on a trait level this file would have to guess at. Costs the first proc of a
    ///     session its prediction and nothing after that.
    ///     <para/>
    ///     An interrupted cast drops the expectation rather than riding out the grace window.
    ///     Not symmetry for its own sake - the sites that read this are the movement blocks, and
    ///     a cast is usually interrupted BY movement, so holding a dead prediction would suppress
    ///     the movement Triplecast at the exact moment it is wanted. Normal completion keeps the
    ///     grace, because there the proc really is landing. Only the last 500ms of a cast is
    ///     ambiguous, and there the grace is kept.
    /// </summary>
    public static bool OccultDualcastIncoming =>
        _occultDualcastExpectedUntilTick != 0 &&
        Environment.TickCount64 <= _occultDualcastExpectedUntilTick &&
        !HasStatusEffect(OccultInstantCast.Dualcast);

    /// <summary>
    ///     Framework tick for <see cref="OccultDualcastIncoming"/>. Registered in
    ///     <c>TimerSetup</c> so it observes every cast, not only the ones a combo evaluation
    ///     happens to run alongside.
    /// </summary>
    internal static void TrackOccultDualcast(IFramework framework)
    {
        if (!Player.Available || !PhantomRedMageEquipped)
        {
            _occultDualcastExpectedUntilTick = 0;
            _occultDualcastCastDueTick = 0;
            _seenOccultDualcast = false;
            return;
        }

        if (HasStatusEffect(OccultInstantCast.Dualcast))
            _seenOccultDualcast = true;

        if (!_seenOccultDualcast)
            return;

        var now = Environment.TickCount64;

        if (Player.Object.TotalCastTime <= 0 || Player.Object.CurrentCastTime <= 0)
        {
            // No cast running. If one was due later than this, it was cut short and no proc is
            // coming - drop the expectation rather than suppress a movement Triplecast for the
            // rest of the grace window, since movement is what usually did the interrupting.
            if (_occultDualcastCastDueTick != 0 &&
                now < _occultDualcastCastDueTick - OccultDualcastInterruptToleranceMs)
                _occultDualcastExpectedUntilTick = 0;

            _occultDualcastCastDueTick = 0;
            return;
        }

        var remainingMs =
            (long)((Player.Object.TotalCastTime - Player.Object.CurrentCastTime) * 1000f);

        _occultDualcastCastDueTick = now + remainingMs;
        _occultDualcastExpectedUntilTick =
            _occultDualcastCastDueTick + OccultDualcastLandingGraceMs;
    }
}
