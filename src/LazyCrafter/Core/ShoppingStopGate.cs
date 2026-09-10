using System;
using System.Collections.Generic;

namespace LazyCrafter.Core;

/// <summary>
/// The wave-start gate (0.1.6.16, card t_68532446): after <c>StartWave</c> has sent the character somewhere
/// (a vendor stop, a market trip, a currency-shop flag) the fetch phases must not queue a bell session until
/// that navigation is actually finished.
///
/// <para>
/// The field evidence (0.1.6.15, 18:03:38-18:03:40) is why this lives in Core as pure data: the pass-2 re-plan
/// announced the vendor stop AND queued the batch fetch in the same second, the batch session answered
/// <c>Busy()==false</c> 1.5 s later (mid-teleport, Artisan idle), the run printed "0 material(s) moved", the
/// pass-3 re-plan then said "no progress this pass" - and the vendor stop re-emitted on top of all of it. Three
/// sends in one second, none of which was the one the plan promised ("vendor stop 1 of 1 ... then press Resume").
/// The trip itself was never tracked: <c>Lifestream.Teleport</c> returns the moment it is ACCEPTED, so
/// <c>IsBusy()</c> reading false says nothing about whether the character has arrived.
/// </para>
///
/// <para>
/// The verdict, in order: a Lifestream trip in progress (or a trip we FIRED and have not yet seen go idle) holds;
/// a window owning the client holds (the fetch phases' existing rule); a settle beat after the last sign of
/// movement holds - Artisan's bell scan answers from a game state that settles a frame or two behind a teleport,
/// and the 0.1.6.12 run queued blind 0.1 s after a throw for exactly that kind of reason; past the settle window
/// the fetch may queue.
/// </para>
///
/// <para>
/// The second half of the card is here too: a batch session that ended with ZERO materials moved is not
/// "progress = false, try the next pass" - it is a blocked run naming the bell. The stall guard (0.1.6.13)
/// only catches a HUNG session (2-min zero-change while Busy); an INSTANT zero-move session (Busy false after
/// ~1.5 s, nothing in the bags) sailed through it and became "no progress this pass". A session that could not
/// move anything is a dead bell, not a slow one.
/// </para>
/// </summary>
public static class ShoppingStopGate
{
    /// <summary>
    /// How long after the last sign of a navigation the fetch phases still hold. One beat - long enough for a
    /// just-fired Teleport to reach Lifestream's queue and for a just-landed zone change to settle - not so long
    /// the fetch dawdles. The 0.1.6.15 field failure queued 0.07 s after the teleport line.
    /// </summary>
    public static readonly TimeSpan SettleWindow = TimeSpan.FromSeconds(5);

    /// <summary>How long the whole wait may run before the run stops cleanly instead (cart held, Resume re-plans).</summary>
    public static readonly TimeSpan WaitCap = TimeSpan.FromMinutes(3);

    /// <summary>What the gate decided for this tick.</summary>
    public enum Verdict { Proceed, Hold }

    /// <summary>
    /// Should the fetch phases hold this tick? <paramref name="tripActive"/> is "a navigation is under way OR one
    /// was fired and has not yet been seen idle" (the caller owns the fired-flag - Core stays stateless);
    /// <paramref name="busyBecause"/> the usual <c>ClientReadiness.BusyBecause()</c> answer; <paramref name="sinceTrip"/>
    /// how long ago the last navigation was fired; <paramref name="held"/> how long this hold has run.
    /// </summary>
    public static Verdict Decide(bool tripActive, string? busyBecause, TimeSpan sinceTrip, TimeSpan held)
    {
        if (held >= WaitCap) return Verdict.Proceed;   // the cap forces the issue: queue and let the fetch's own gates answer
        if (tripActive) return Verdict.Hold;
        // 0.1.7.2 (card t_37f9fa98): the game's own "a loading screen is up" signal - checked BEFORE the
        // fetch phases' ignore-list, not through it. FetchGatePolicy.FetchHoldIgnoredLabels excuses "a zone
        // change" for FetchClientHold's window-ownership gate on the assumption Lifestream.IsBusy() already
        // covers a trip in flight - but LifestreamDispatch's own doc comment says Teleport returns the
        // moment it is ACCEPTED, so IsBusy() can (and in the field, does) read false while the zone is still
        // loading. A cross-zone vendor teleport (Kugane, 2026-09-10 14:36:52-58) proved both halves of that
        // assumption wrong at once: IsBusy() went false and the fixed 5 s SettleWindow lapsed while the
        // character was still mid-load, and Fetch.SessionPreflight()'s own bell-reachability scan answered
        // Proceed too - Artisan's GetReachableRetainerBell() reads the object table, which during a zone
        // transition can still hold stale entries from the OLD zone. Holding on the live condition flag
        // stops the gate from ever reaching BellGateAtQueue while that stale data could still fool it.
        if (busyBecause == "a zone change") return Verdict.Hold;
        // The same window rule the fetch phases already live by - and the same IGNORED set: a character
        // standing at a bell ("the summoning bell"), inside a retainer's inventory, at a quantity prompt or
        // in a dialogue is exactly where a session is about to run or just ran; holding on those would stall
        // the normal at-the-bell case for the whole cap. Only what a PLAYER opened (the market board above
        // all) holds here, via the fetch phases' own filter.
        if (FetchGatePolicy.ShouldHoldFetch(busyBecause)) return Verdict.Hold;
        if (sinceTrip < SettleWindow) return Verdict.Hold;
        return Verdict.Proceed;
    }

    /// <summary>The status line while held for the shopping stop's travel.</summary>
    public static string HoldStatus(TimeSpan held) => $"waiting for the shopping stop's trip to finish ({held:m\\:ss})";

    /// <summary>The heartbeat while held - names what the run is waiting on, never silence.</summary>
    public static string HoldHeartbeat() => "waiting for the shopping stop's trip to finish before the retainer fetch starts";

    /// <summary>
    /// The blocked-run reason when a batch session ends with zero materials moved. The bell is named because in
    /// the field this verdict has only two causes - the character was never really at a bell (mid-teleport), or
    /// the bell session could not run (a window, a jam) - and both are answered by standing at a working bell.
    /// </summary>
    public static string BatchMovedNothing(int retrievalsLeft) =>
        $"the retainer fetch moved nothing into the bags ({retrievalsLeft} material{(retrievalsLeft == 1 ? "" : "s")} still to fetch) - make sure the character is standing at a summoning bell and no window is open, then press Resume (or /lcraft resume) to continue the same cart";
}
