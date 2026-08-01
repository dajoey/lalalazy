using LazyOccultCrescent.Data;
using ECommons.Automation.NeoTaskManager;
using Ocelot.Chain;

namespace LazyOccultCrescent.Modules.Buff.Chains;

public class AllBuffsChain(BuffModule module) : ChainFactory
{
    private readonly Job StartingJob = Job.Current;

    protected override Chain Create(Chain chain)
    {
        chain
            .Then(new FreelancerBuffChain(module))
            .Then(new KnightBuffChain(module))
            .Then(new MonkBuffChain(module))
            .Then(new BardBuffChain(module))
            .Then(new DancerBuffChain(module))
            .Then(StartingJob.ChangeToChain);

        return chain;
    }

    public override TaskManagerConfiguration Config()
    {
        return new TaskManagerConfiguration { TimeLimitMS = 60000 };
    }
}
