using LazyOccultCrescent.ActionHelpers;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Modules.Treasure;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;

namespace LazyOccultCrescent.Chains;

public class TreasureSightChain(TreasureModule module) : ChainFactory
{
    // Resolved when the chain runs, not when it is constructed. BuffManager drives
    // its buff sequence on a separate queue and hops the player through five
    // phantom jobs; a field initializer could sample Job.Current mid-hop and then
    // "restore" the player onto a job they never picked.
    private Job startingJob = Job.Freelancer;

    protected override Chain Create(Chain chain)
    {
        chain.RunIf(() => module.Config.CastTreasureSightUponReturn);

        chain.Then(_ => startingJob = Job.Current);
        chain.Then(Job.Freelancer.ChangeToChain);
        chain.Then(Actions.Freelancer.Treasuresight.GetCastChain()).Wait(1000);
        // `() =>` not `_ =>`: the Action overload would fire the job change and
        // continue immediately, so the next step ran before the job had changed.
        chain.Then(() => startingJob.ChangeToChain());

        return chain;
    }
}
