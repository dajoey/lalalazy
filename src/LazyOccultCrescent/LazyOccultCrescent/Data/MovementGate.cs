using ECommons.DalamudServices;

namespace LazyOccultCrescent.Data;

// One switch that cancels every movement currently in flight.
//
// The emergency stop used to call vnav.Stop() and abort a single chain queue.
// That was not enough for two reasons:
//
//   1. There are four queues (LOC##main, LOC##BuffManager, MobFarmer+Farmer, and
//      the pathfinder's step processor). Aborting one leaves the others driving.
//   2. PathfindAndMoveToChain re-issues movement whenever vnavmesh is not running,
//      because that is how it recovers from a failed solve. An external Stop()
//      therefore read as a stall and was immediately undone - the character kept
//      walking to wherever it had been going even though the action was cancelled.
//
// A generation counter fixes this without a sticky "movement disabled" flag that
// would block later, legitimate movement. A movement chain latches the current
// generation on its first tick; if the value moves under it, it has been
// cancelled and abandons. Anything started afterwards latches the new value and
// runs normally, so the stop cancels what is in flight without disabling the
// plugin.
public static class MovementGate
{
    public static int Generation { get; private set; }

    public static void CancelAll(string reason)
    {
        Generation++;
        Svc.Log.Information($"[MovementGate] cancelling all movement ({reason}), generation {Generation}");
    }
}
