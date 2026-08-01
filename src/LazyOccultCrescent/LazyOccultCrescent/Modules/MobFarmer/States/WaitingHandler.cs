using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using Ocelot.States;

namespace LazyOccultCrescent.Modules.MobFarmer.States;

[State<FarmerPhase>(FarmerPhase.Waiting)]
public class WaitingHandler(MobFarmerModule module) : FarmerPhaseHandler(module)
{
    public override FarmerPhase? Handle()
    {
        if (Svc.Condition[ConditionFlag.InCombat])
        {
            return FarmerPhase.Fighting;
        }

        var mobs = Module.Scanner.Mobs;

        return mobs.Count() >= Module.Config.MinimumMobsToStartLoop ? FarmerPhase.Buffing : null;
    }
}
