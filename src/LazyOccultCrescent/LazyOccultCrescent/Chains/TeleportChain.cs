using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;
using LazyOccultCrescent.Modules.Teleporter;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;

namespace LazyOccultCrescent.Chains;

public class TeleportChain(Aethernet aethernet, Lifestream lifestream, TeleporterModule module) : ChainFactory
{
    // The shard we depart FROM, resolved while the chain runs rather than while
    // it is assembled.
    private AethernetData? departure;

    private bool InRange()
    {
        return departure != null && Player.DistanceTo(departure.Position) < AethernetData.DISTANCE;
    }

    protected override Chain Create(Chain chain)
    {
        var vnav = module.GetIPCSubscriber<VNavmesh>();

        // Upstream resolved the departure shard via GetNearbyAethernetShards(3.8y)
        // and bailed out of the whole chain when it found none - silently, because
        // as far as the chain was concerned there was nothing to do.
        //
        // The mechanism, corrected: ChainFactory.Create() is LAZY (Factory()
        // returns a Func<Chain> invoked when the step is reached - verified
        // against the shipped Ocelot 1.1.5 assembly), so this is not a
        // build-time-versus-run-time bug. It is a coordinate mismatch. The
        // approach walk targets the SURVEYED table position and completes via
        // WaitUntilNear, which also accepts "vnavmesh stopped"; the guard then
        // measured 3.8y against the LIVE object-table EventObj. vnavmesh parks at
        // the aetheryte's collision edge, so the walk legitimately finished while
        // the guard still read empty - and the caller walked the whole way instead.
        //
        // Resolving from the known shard table and waiting for real proximity
        // removes both halves of that mismatch.
        chain.Then(_ => lifestream.Abort());

        chain.Then(_ =>
        {
            // From the known shard table, not the object table: it is zone-scoped
            // and does not require the shard to already be in render range.
            departure = AethernetData.GetClosestToPlayer();
            Svc.Log.Debug($"[Teleport] departing via {departure.Aethernet.ToFriendlyString()}, {Player.DistanceTo(departure.Position):F1}y away");
        });

        // `() =>` so the parent waits for the approach. The InRange gate below
        // masked this, but relying on a later guard to paper over a step that does
        // not block is how the original out-of-range teleport happened.
        chain.ConditionalThen(
            _ => !InRange(),
            () => Chain.Create("Teleport:Approach")
                .Then(new PathfindAndMoveToChain(vnav, departure!.Position, AethernetData.DISTANCE - 0.8f)));

        chain.Then(_ => vnav.Stop());

        // Do not fire the teleport until genuinely in range. Firing early is a
        // silent no-op - Lifestream just does nothing - and the caller then walks
        // to the far side of the zone believing it already teleported.
        chain.Then(new TaskManagerTask(
            InRange,
            new TaskManagerConfiguration { TimeLimitMS = 30000 }));

        chain.Then(_ => lifestream.AethernetTeleportByPlaceNameId((uint)aethernet));
        chain.WaitToCycleCondition(ConditionFlag.BetweenAreas);

        // Mount if we should mount and not pathfind, otherwise let the pathfinder handle it
        chain.ConditionalThen(_ => module.Config is { ShouldMount: true, PathToDestination: false }, ChainHelper.MountChain());

        return chain;
    }
}
