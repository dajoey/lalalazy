using System;
using System.Linq;
using System.Numerics;
using LazyOccultCrescent.ActionHelpers;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;
using LazyOccultCrescent.Modules.Buff;
using LazyOccultCrescent.Modules.Buff.Chains;
using LazyOccultCrescent.Modules.Teleporter;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;

namespace LazyOccultCrescent.Chains;

public class ReturnChain(TeleporterModule module, ReturnChainConfig config) : RetryChainFactory
{
    private bool complete = false;

    protected override Chain Create(Chain chain)
    {
        chain.BreakIf(() => Player.IsDead);

        var shouldReturn = GetCostToReturn() < GetCostToWalk();

        if (shouldReturn)
        {
            chain = Actions.Return.CastOnChain(chain);
            chain.WaitToCast().WaitToCycleCondition(ConditionFlag.BetweenAreas);
        }

        chain.Then(ChainHelper.TreasureSightChain());
        chain.Then(ApplyBuffs);

        if (config.ApproachAetheryte)
        {
            var vnav = module.GetIPCSubscriber<VNavmesh>();
            var lifestream = module.GetIPCSubscriber<Lifestream>();
            var position = GetAetherytePosition();

            chain.Then(new PathfindAndMoveToChain(vnav, GetAetherytePosition(), AethernetData.DISTANCE - 0.8f));
            chain.Then(_ => lifestream.GetActiveCustomAetheryte() != 0 && Player.DistanceTo(position) <= AethernetData.DISTANCE);
            chain.Then(_ => vnav.Stop());
        }


        return chain.Then(_ => complete = true);
    }

    private IGameObject? knowledgeCrystal;

    private DateTime crystalDeadline = DateTime.MinValue;

    private Chain ApplyBuffs()
    {
        var vnav = module.GetIPCSubscriber<VNavmesh>();
        var buffs = module.GetModule<BuffModule>();

        var chain = Chain.Create("Return:ApplyBuffs");
        chain.BreakIf(() => !buffs.ShouldRefreshBuffs() || !vnav.IsReady());

        chain.Then(_ =>
        {
            knowledgeCrystal = null;
            crystalDeadline = DateTime.UtcNow.AddSeconds(5);
        });

        // Poll rather than take one reading. This step runs within a frame or two
        // of the Return teleport completing, while the object table is still
        // repopulating, so a single lookup here frequently found nothing and the
        // whole buff sequence was skipped silently.
        chain.Then(new TaskManagerTask(() =>
        {
            knowledgeCrystal ??= ZoneData.GetNearbyKnowledgeCrystal(60f).FirstOrDefault();
            return knowledgeCrystal != null || DateTime.UtcNow >= crystalDeadline;
        }, new TaskManagerConfiguration { TimeLimitMS = 6000 }));

        chain.Then(_ =>
        {
            if (knowledgeCrystal == null)
            {
                Svc.Log.Info("[Return] no knowledge crystal within 60y - skipping buff refresh");
            }
        });

        chain.BreakIf(() => knowledgeCrystal == null);

        chain.Then(_ => Actions.TryUnmount());

        // Position read at execution time, from whichever crystal the poll found.
        // `() =>` binds the Func<Chain> overload so the parent waits for the walk;
        // with `_ =>` the buff sequence started while still 60y from the crystal.
        chain.ConditionalThen(
            _ => knowledgeCrystal != null,
            () => Chain.Create("Return:ToCrystal")
                .Then(new PathfindAndMoveToChain(vnav, knowledgeCrystal!.Position, AethernetData.DISTANCE))
                .Then(_ => vnav.Stop()));

        chain.Then(buffs.BuffManager.CreateSequence(buffs));

        return chain;
    }

    public override bool IsComplete()
    {
        return complete;
    }

    public override int GetMaxAttempts()
    {
        return 5;
    }

    public override TaskManagerConfiguration? Config()
    {
        return new TaskManagerConfiguration { TimeLimitMS = 60000 };
    }

    private Vector3 GetAetherytePosition()
    {
        if (ZoneData.Aetherytes.TryGetValue(Svc.ClientState.TerritoryType, out var position))
        {
            return position;
        }

        throw new Exception("Unable to determine Aetheryte position");
    }

    private float GetCostToReturn()
    {
        if (ZoneData.StartingLocations.TryGetValue(Svc.ClientState.TerritoryType, out var start))
        {
            return Vector3.Distance(start, GetAetherytePosition()) + 75f;
        }


        throw new Exception("Unable to determine Starting position");
    }

    private float GetCostToWalk()
    {
        return Player.DistanceTo(GetAetherytePosition());
    }
}
