using System.Linq;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Ocelot.IPC;
using Ocelot.States;

namespace LazyOccultCrescent.Modules.MobFarmer.States;

[State<FarmerPhase>(FarmerPhase.Stacking)]
public class StackingHandler(MobFarmerModule module) : FarmerPhaseHandler(module)
{
    private bool HasRunStack = false;

    public override void Enter()
    {
        base.Enter();
        HasRunStack = false;
    }

    public override FarmerPhase? Handle()
    {
        var vnav = Module.GetIPCSubscriber<VNavmesh>();

        if (HasRunStack && !vnav.IsRunning())
        {
            HasRunStack = false;
            Module.Farmer.RotationPlugin.PhantomJobOn();
            return FarmerPhase.Fighting;
        }

        var furthest = Module.Scanner.InCombat.Where(o => o.Address != Svc.Targets.Target?.Address).OrderBy(Player.DistanceTo).LastOrDefault();
        if (furthest == null)
        {
            return FarmerPhase.Fighting;
        }

        vnav.PathfindAndMoveTo(furthest.Position, false);
        HasRunStack = true;

        return null;
    }
}
