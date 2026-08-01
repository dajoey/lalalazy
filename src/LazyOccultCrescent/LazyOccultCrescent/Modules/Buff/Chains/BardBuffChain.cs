using LazyOccultCrescent.ActionHelpers;
using LazyOccultCrescent.Data;

namespace LazyOccultCrescent.Modules.Buff.Chains;

public class BardBuffChain(BuffModule module) : BuffChain(Job.Bard, PlayerStatus.RomeosBallad, Actions.Bard.RomeosBallad)
{
    protected override bool ShouldRun()
    {
        return module.Config.ApplyRomeosBallad;
    }
}
