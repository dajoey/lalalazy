using LazyOccultCrescent.ActionHelpers;
using LazyOccultCrescent.Data;

namespace LazyOccultCrescent.Modules.Buff.Chains;

public class MonkBuffChain(BuffModule module) : BuffChain(Job.Monk, PlayerStatus.Fleetfooted, Actions.Monk.Counterstance)
{
    protected override bool ShouldRun()
    {
        return module.Config.ApplyFleetfooted;
    }
}
