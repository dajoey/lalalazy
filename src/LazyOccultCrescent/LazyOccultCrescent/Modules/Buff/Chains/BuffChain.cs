using LazyOccultCrescent.ActionHelpers;
using LazyOccultCrescent.Data;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;

namespace LazyOccultCrescent.Modules.Buff.Chains;

public abstract class BuffChain(Job job, PlayerStatus buff, Action action) : ChainFactory
{
    // Remaining time on the buff before we cast, so success can be judged as
    // "it went up" rather than against a hardcoded number.
    private float before;

    protected override Chain Create(Chain chain)
    {
        // Skip rather than abort. Changing phantom job is rejected in combat, so
        // running this then would leave ChangeToChain spinning on a status that
        // never arrives until the time limit kills it - which also kills the
        // parent, and with it the job restore at the end of the sequence.
        chain.RunIf(() => ShouldRun() && !Svc.Condition[ConditionFlag.InCombat]);

        chain.Then(_ => before = Player.Status.Get(buff)?.RemainingTime ?? 0f);
        chain.Then(job.ChangeToChain);

        return action
            .CastOnChain(chain)
            .Then(_ => Player.Status.Has(buff))
            // Upstream waited for RemainingTime >= 1780 - roughly 29.7 minutes -
            // as a proxy for "freshly applied". Any buff whose real duration is
            // shorter than that, or which the server reports a hair under, can
            // never satisfy it, so the chain span until its 15s limit and took the
            // whole sequence down. Comparing against the pre-cast value asks the
            // actual question: did this cast refresh the buff?
            .Then(_ => (Player.Status.Get(buff)?.RemainingTime ?? 0f) > before + 1f);
    }

    public override TaskManagerConfiguration? Config()
    {
        return new TaskManagerConfiguration { TimeLimitMS = 15000 };
    }

    protected abstract bool ShouldRun();
}
