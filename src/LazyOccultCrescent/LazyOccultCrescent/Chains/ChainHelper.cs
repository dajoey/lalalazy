// OVERLOAD HAZARD - read before touching any .Then/.ConditionalThen in this file.
//
// Ocelot's Chain exposes both:
//     Then(Action<ChainContext>)              // 1-param lambda
//     Then(Func<Chain>, TaskManagerConfiguration)   // 0-arg lambda
// and the same pair for ConditionalThen. A lambda written `_ => Chain.Create()...`
// binds to the ACTION overload: the sub-chain is constructed, self-registers with
// the framework via its own TaskManager, and runs - but the parent DOES NOT WAIT
// for it. Written `() => Chain.Create()...` it binds to the Func<Chain> overload
// and the parent blocks until the sub-chain reports complete.
//
// The failure is silent and looks like a race: steps fire in the right order but
// overlap. Verified against the shipped Ocelot 1.1.5 assembly.

using System;
using System.Numerics;
using LazyOccultCrescent.Enums;
using LazyOccultCrescent.Modules.Mount;
using LazyOccultCrescent.Modules.Mount.Chains;
using LazyOccultCrescent.Modules.Teleporter;
using LazyOccultCrescent.Modules.Treasure;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using Ocelot.Modules;

namespace LazyOccultCrescent.Chains;

public class ChainHelper
{
    private static ChainHelper? _instance = null;

    private static ChainHelper Instance
    {
        get
        {
            if (_instance == null)
            {
                throw new InvalidOperationException("ChainHelper has not been initialized. Call Initialize(plugin) first.");
            }

            return _instance;
        }
    }

    private readonly Plugin Plugin;

    private static ModuleManager Modules
    {
        get => Instance.Plugin.Modules;
    }

    private static IPCManager IPC
    {
        get => Instance.Plugin.IPC;
    }

    private ChainHelper(Plugin plugin)
    {
        Plugin = plugin;
    }

    public static void Initialize(Plugin plugin)
    {
        _instance ??= new ChainHelper(plugin);
    }

    public static ReturnChain ReturnChain()
    {
        var config = new ReturnChainConfig
        {
            ApproachAetheryte = Instance.Plugin.Config.TeleporterConfig.ApproachAetheryte,
        };

        return ReturnChain(config);
    }

    public static ReturnChain ReturnChain(ReturnChainConfig config)
    {
        return new ReturnChain(Modules.GetModule<TeleporterModule>(), config);
    }

    public static TeleportChain TeleportChain(Aethernet aethernet)
    {
        return new TeleportChain(
            aethernet,
            IPC.GetSubscriber<Lifestream>(),
            Modules.GetModule<TeleporterModule>()
        );
    }

    public static MountChain MountChain()
    {
        return new MountChain(Modules.GetModule<MountModule>().Config);
    }

    public static Func<Chain> PathfindToAndWait(Vector3 destination, float distance)
    {
        var vnav = IPC.GetSubscriber<VNavmesh>();
        return () => Chain.Create()
            // `() =>` not `_ =>`: see the overload hazard note at the top of this file.
            // As written upstream this helper - named PathfindToAndWait - did not
            // actually wait for the walk, so callers proceeded while the character
            // was still moving. Activity's WalkTeleportWalk branch walks to the
            // departure shard with this and then teleports, which is why the
            // teleport used to fire out of range.
            .ConditionalThen(_ => Player.DistanceTo(destination) > distance, () =>
                Chain.Create()
                    .Then(new PathfindAndMoveToChain(vnav, destination, distance))
                    .WaitUntilNear(vnav, destination, distance)
                    .Then(_ => vnav.Stop())
            );
    }

    public static Func<Chain> MoveToAndWait(Vector3 destination, float distance)
    {
        var vnav = IPC.GetSubscriber<VNavmesh>();
        return () => Chain.Create()
            .ConditionalThen(_ => Player.DistanceTo(destination) > distance, () =>
                Chain.Create()
                    .Then(_ => vnav.FollowPath([destination], false))
                    .WaitUntilNear(vnav, destination, distance)
                    .Then(_ => vnav.Stop())
            );
    }

    public static TreasureSightChain TreasureSightChain()
    {
        return new TreasureSightChain(Modules.GetModule<TreasureModule>());
    }
}
