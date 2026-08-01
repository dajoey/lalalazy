using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using Ocelot.IPC;
using Ocelot.States;

namespace LazyOccultCrescent.Modules.MobFarmer.States;

[State<FarmerPhase>(FarmerPhase.Fighting)]
public class FightingHandler(MobFarmerModule module) : FarmerPhaseHandler(module)
{
    public override FarmerPhase? Handle()
    {
        var anyInCombat = Module.Scanner.InCombat.Any();
        if (anyInCombat && EzThrottler.Throttle("Targetter"))
        {
            Svc.Targets.Target = Module.Scanner.InCombat.Centroid();
        }


        var startingPoint = Module.Farmer.StartingPoint;
        var shouldReturnHome = Module.Config.ReturnToStartInWaitingPhase && Player.DistanceTo(startingPoint) >= Module.Config.MinEuclideanDistanceToReturnHome;
        if (shouldReturnHome && !anyInCombat)
        {
            var vnav = Module.GetIPCSubscriber<VNavmesh>();
            if (!vnav.IsRunning())
            {
                vnav.PathfindAndMoveTo(startingPoint, false);
            }

            return Player.DistanceTo(startingPoint) <= 2f ? FarmerPhase.Waiting : null;
        }

        if (!anyInCombat && !Svc.Condition[ConditionFlag.InCombat])
        {
            Module.Farmer.RotationPlugin.PhantomJobOff();
            return FarmerPhase.Waiting;
        }

        return null;
    }
}
