using LazyOccultCrescent.ActionHelpers;
using LazyOccultCrescent.Data;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;

namespace LazyOccultCrescent.Modules.Mount.Chains;

public class MountChain(MountConfig config) : RetryChainFactory
{
    protected override Chain Create(Chain chain)
    {
        chain.BreakIf(Breaker);

        return !config.MountRoulette ? Actions.Mount(config.Mount).CastOnChain(chain) : Actions.MountRoulette.CastOnChain(chain);
    }

    private bool Breaker()
    {
        return Svc.Condition[ConditionFlag.Mounted]
               || Svc.Condition[ConditionFlag.BetweenAreas]
               || Svc.Condition[ConditionFlag.BetweenAreas51]
               || Svc.Condition[ConditionFlag.InCombat]
               || Player.Status.Has(PlayerStatus.HoofingIt)
               || Player.IsCasting
               || Player.IsDead;
    }

    public override int GetThrottle()
    {
        return attempt == 0 ? 500 : 5000;
    }

    public override bool IsComplete()
    {
        return Svc.Condition[ConditionFlag.Mounted];
    }
}
