using System.Linq;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;
using LazyOccultCrescent.Modules.Teleporter;
using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;

namespace LazyOccultCrescent.Chains;

public class TeleportChain(Aethernet aethernet, Lifestream lifestream, TeleporterModule module) : ChainFactory
{
    protected override Chain Create(Chain chain)
    {
        var vnav = module.GetIPCSubscriber<VNavmesh>();
        var nearby = ZoneData.GetNearbyAethernetShards(AethernetData.DISTANCE);
        if (nearby.Count <= 0)
        {
            return chain;
        }

        chain.Then(_ => lifestream.Abort());
        chain.BreakIf(() => nearby.Count <= 0);

        var nearest = nearby.First();
        if (Player.DistanceTo(nearest.Position) >= AethernetData.DISTANCE)
        {
            // Must finish inside AethernetData.DISTANCE or the teleport that
            // follows silently has nothing to interact with.
            chain.Then(new PathfindAndMoveToChain(vnav, nearest.Position, AethernetData.DISTANCE - 0.8f));
            chain.Then(_ => lifestream.GetActiveCustomAetheryte() != 0 && Player.DistanceTo(nearest.Position) < AethernetData.DISTANCE);
        }

        chain.Then(_ => vnav.Stop());
        chain.Then(_ => lifestream.AethernetTeleportByPlaceNameId((uint)aethernet));
        chain.WaitToCycleCondition(ConditionFlag.BetweenAreas);
        // Mount if we should mount and not pathfind, otherwise let the pathfinder handle it
        chain.ConditionalThen(_ => module.Config is { ShouldMount: true, PathToDestination: false }, ChainHelper.MountChain());

        return chain;
    }
}
