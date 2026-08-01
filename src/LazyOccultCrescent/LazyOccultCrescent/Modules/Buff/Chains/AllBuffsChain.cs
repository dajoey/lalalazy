using LazyOccultCrescent.Data;
using ECommons.Automation.NeoTaskManager;
using Ocelot.Chain;

namespace LazyOccultCrescent.Modules.Buff.Chains;

public class AllBuffsChain(BuffModule module, Job startingJob) : ChainFactory
{
    protected override Chain Create(Chain chain)
    {
        chain
            .Then(new FreelancerBuffChain(module))
            .Then(new KnightBuffChain(module))
            .Then(new MonkBuffChain(module))
            .Then(new BardBuffChain(module))
            .Then(new DancerBuffChain(module))
            .Then(startingJob.ChangeToChain);

        return chain;
    }

    public override TaskManagerConfiguration Config()
    {
        // Five child chains at 15s each is 75s before a single job change is
        // counted, so the old 60s ceiling could not fit its own contents: with
        // enough buffs due, the parent expired BEFORE the restore link at the end
        // and left the player on whichever phantom job was buffing last. That is
        // the difference between "buffing works" and "buffing gives up" - it was
        // just how many buffs happened to need refreshing.
        //
        // BuffManager also carries an independent restore watchdog, because a
        // budget large enough today is not a guarantee.
        return new TaskManagerConfiguration { TimeLimitMS = 150000 };
    }
}
