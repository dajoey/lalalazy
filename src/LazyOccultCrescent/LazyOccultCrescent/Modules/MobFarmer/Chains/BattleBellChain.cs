using LazyOccultCrescent.ActionHelpers;
using LazyOccultCrescent.Data;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;

namespace LazyOccultCrescent.Modules.MobFarmer.Chains;

public class BattleBellChain(MobFarmerModule module) : ChainFactory
{
    // See TreasureSightChain: resolved at execution, not construction.
    private Job startingJob = Job.Freelancer;

    protected override Chain Create(Chain chain)
    {
        chain.BreakIf(() => Actions.Geomancer.BattleBell.GetRecastTime() >= module.Config.MaximumBattleBellWaitTime);

        chain.Then(_ => startingJob = Job.Current);
        chain.Then(Job.Geomancer.ChangeToChain);
        chain.Then(Actions.Geomancer.BattleBell.GetCastChain()).Wait(1000);
        // `() =>` not `_ =>`: the Action overload would fire the job change and
        // continue immediately, so the next step ran before the job had changed.
        chain.Then(() => startingJob.ChangeToChain());

        return chain;
    }
}
