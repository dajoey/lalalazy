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
            .ConditionalThen(_ => Player.DistanceTo(destination) > distance, _ =>
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
            .ConditionalThen(_ => Player.DistanceTo(destination) > distance, _ =>
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
