using LazyOccultCrescent.ActionHelpers;
using LazyOccultCrescent.Modules.MobFarmer.Chains;
using Ocelot.States;

namespace LazyOccultCrescent.Modules.MobFarmer.States;

[State<FarmerPhase>(FarmerPhase.Buffing)]
public class BuffingHandler(MobFarmerModule module) : FarmerPhaseHandler(module)
{
    private bool HasRunBuff = false;

    public override void Enter()
    {
        base.Enter();
        HasRunBuff = false;
    }

    public override FarmerPhase? Handle()
    {
        if (!Module.Config.ApplyBattleBell)
        {
            return FarmerPhase.Gathering;
        }

        if (Plugin.Chain.IsRunning)
        {
            return null;
        }

        if (HasRunBuff)
        {
            HasRunBuff = false;
            Plugin.Chain.Submit(Actions.Sprint.GetCastChain());
            return FarmerPhase.Gathering;
        }

        Plugin.Chain.Submit(new BattleBellChain(Module));
        HasRunBuff = true;

        return null;
    }
}
