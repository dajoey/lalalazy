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

        // Upstream resolved the departure shard HERE, at assembly time, via
        // GetNearbyAethernetShards(3.8y) - shards within interaction range of the
        // player right now - and bailed out of the whole chain when it found
        // none. In WalkTeleportWalk and ReturnTeleportWalk the chain is assembled
        // while the player is still on the far side of the zone, so that lookup
        // was always empty, the entire teleport block was skipped, and the caller
        // fell through to walking the whole way. Nothing logged, because as far
        // as the chain was concerned there was simply nothing to do.
        //
        // Everything below therefore resolves and re-checks at execution time.
        chain.Then(_ => lifestream.Abort());

        chain.Then(_ =>
        {
            // From the known shard table, not the object table: it is zone-scoped
            // and does not require the shard to already be in render range.
            departure = AethernetData.GetClosestToPlayer();
            Svc.Log.Debug($"[Teleport] departing via {departure.Aethernet.ToFriendlyString()}, {Player.DistanceTo(departure.Position):F1}y away");
        });

        chain.ConditionalThen(
            _ => !InRange(),
            _ => Chain.Create("Teleport:Approach")
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
