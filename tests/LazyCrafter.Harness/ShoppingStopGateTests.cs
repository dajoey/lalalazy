using LazyCrafter.Core;

namespace LazyCrafter.Harness;

/// <summary>
/// Pins for the wave-start gate (0.1.6.16, card t_68532446). The 0.1.6.15 field failure: pass-2 re-planned,
/// announced the vendor stop AND queued the batch fetch in the same second; the session answered Busy()==false
/// 1.5 s later mid-teleport, the run printed "0 material(s) moved", pass-3 said "no progress this pass", and
/// the vendor stop re-emitted on top. Core pins here; the dispatcher wiring is proved by the shipped-DLL
/// artifact scan (as in 0.1.6.8 / 0.1.6.13).
/// </summary>
public static class ShoppingStopGateTests
{
    private static readonly TimeSpan JustFired = TimeSpan.Zero;
    private static readonly TimeSpan Settled = ShoppingStopGate.SettleWindow + TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CapMinus = ShoppingStopGate.WaitCap - TimeSpan.FromSeconds(1);

    public static IEnumerable<(string Name, Func<bool> Check)> Tests
    {
        get
        {
            // ---- the trip itself ----
            yield return ("a trip in progress holds the fetch",
                () => ShoppingStopGate.Decide(tripActive: true, busyBecause: null, sinceTrip: JustFired, held: JustFired)
                    == ShoppingStopGate.Verdict.Hold);

            yield return ("a just-fired trip holds even while Lifestream still reads idle (the 0.07 s queue is the bug)",
                () => ShoppingStopGate.Decide(tripActive: false, busyBecause: null, sinceTrip: TimeSpan.FromMilliseconds(70), held: JustFired)
                    == ShoppingStopGate.Verdict.Hold);

            yield return ("after the settle beat with nothing else wrong, the fetch proceeds",
                () => ShoppingStopGate.Decide(tripActive: false, busyBecause: null, sinceTrip: Settled, held: Settled)
                    == ShoppingStopGate.Verdict.Proceed);

            yield return ("a window the player opened (market board) holds the fetch",
                () => ShoppingStopGate.Decide(tripActive: false, busyBecause: "the market board", sinceTrip: Settled, held: Settled)
                    == ShoppingStopGate.Verdict.Hold);

            // ---- the at-the-bell cases the fetch phases already ignore - holding on them would stall the
            //      normal path for the whole cap ----
            yield return ("standing at the summoning bell does NOT hold (the session is about to run there)",
                () => ShoppingStopGate.Decide(tripActive: false, busyBecause: "the summoning bell", sinceTrip: Settled, held: Settled)
                    == ShoppingStopGate.Verdict.Proceed);

            yield return ("a retainer's inventory does NOT hold (a session may be working inside it)",
                () => ShoppingStopGate.Decide(tripActive: false, busyBecause: "a retainer's inventory", sinceTrip: Settled, held: Settled)
                    == ShoppingStopGate.Verdict.Proceed);

            yield return ("a dialogue box does NOT hold (the session clicks through them)",
                () => ShoppingStopGate.Decide(tripActive: false, busyBecause: "a dialogue box", sinceTrip: Settled, held: Settled)
                    == ShoppingStopGate.Verdict.Proceed);

            // ---- the cap ----
            yield return ("the 3-minute cap forces Proceed - the fetch's own gates then answer",
                () => ShoppingStopGate.Decide(tripActive: true, busyBecause: "the market board", sinceTrip: JustFired, held: ShoppingStopGate.WaitCap)
                    == ShoppingStopGate.Verdict.Proceed);

            yield return ("just under the cap still holds",
                () => ShoppingStopGate.Decide(tripActive: true, busyBecause: null, sinceTrip: JustFired, held: CapMinus)
                    == ShoppingStopGate.Verdict.Hold);

            // ---- sentences ----
            yield return ("the hold status names the shopping stop's trip and a clock",
                () => ShoppingStopGate.HoldStatus(TimeSpan.FromSeconds(12)).Contains("shopping stop", StringComparison.Ordinal)
                    && ShoppingStopGate.HoldStatus(TimeSpan.FromSeconds(12)).Contains("0:12", StringComparison.Ordinal));

            yield return ("the hold heartbeat names what is being waited on and never goes silent",
                () => ShoppingStopGate.HoldHeartbeat().Contains("trip", StringComparison.Ordinal)
                    && ShoppingStopGate.HoldHeartbeat().Contains("retainer fetch", StringComparison.Ordinal));

            yield return ("the zero-move block names the bell, the count, and the Resume cadence",
                () => ShoppingStopGate.BatchMovedNothing(3).Contains("summoning bell", StringComparison.Ordinal)
                    && ShoppingStopGate.BatchMovedNothing(3).Contains("3 materials", StringComparison.Ordinal)
                    && ShoppingStopGate.BatchMovedNothing(3).Contains("press Resume", StringComparison.Ordinal));

            yield return ("the zero-move block singularises one material",
                () => ShoppingStopGate.BatchMovedNothing(1).Contains("1 material still", StringComparison.Ordinal)
                    && !ShoppingStopGate.BatchMovedNothing(1).Contains("materials", StringComparison.Ordinal));

            // ---- wording regression pins: a blocked zero-move run must never again read as "no progress" ----
            yield return ("the zero-move block never says 'no progress this pass'",
                () => !ShoppingStopGate.BatchMovedNothing(7).Contains("no progress", StringComparison.OrdinalIgnoreCase));

            yield return ("the hold status never claims a bell errand for a shopping stop (the 0.1.6.14 lesson)",
                () => !ShoppingStopGate.HoldStatus(Settled).Contains("bell", StringComparison.OrdinalIgnoreCase));
        }
    }
}
