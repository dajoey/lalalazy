using System;
using System.Linq;
using System.Numerics;
using LazyOccultCrescent.ActionHelpers;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Modules.Fates;
using LazyOccultCrescent.Modules.StateManager;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Ocelot.IPC;

namespace LazyOccultCrescent.Modules.Automator;

public class FateActivity(EventData data, Lifestream lifestream, VNavmesh vnav, AutomatorModule module, Fate fate)
    : Activity(data, lifestream, vnav, module)
{
    protected override TaskManagerTask GetPathfindingWatcher(StateManagerModule states)
    {
        var lastTargetPos = Vector3.Zero;

        return new TaskManagerTask(() =>
        {
            if (EzThrottler.Throttle("FatePathfindingWatcher.EnemyScan", 100))
            {
                if (Svc.Targets.Target == null)
                {
                    var enemy = GetEnemies().Centroid();
                    if (enemy != null)
                    {
                        Svc.Targets.Target = enemy;
                    }
                }
            }

            var target = Svc.Targets.Target;
            if (target != null)
            {
                if (Vector3.Distance(target.Position, lastTargetPos) > 5f)
                {
                    vnav.PathfindAndMoveTo(target.Position, false);
                    lastTargetPos = target.Position;
                }

                if (states.GetState() == State.InFate)
                {
                    var distance = Vector3.Distance(Player.Position, target.Position) - target.HitboxRadius;
                    if (distance <= module.Config.EngagementRange)
                    {
                        Actions.TryUnmount();

                        vnav.Stop();

                        return true;
                    }
                }
            }

            if (!vnav.IsRunning())
            {
                throw new VnavmeshStoppedException();
            }

            return false;
        }, new TaskManagerConfiguration { TimeLimitMS = 180000, ShowError = false });
    }

    protected override float GetRadius()
    {
        return module.GetModule<FatesModule>().fates[data.Id].Radius;
    }

    public override bool IsValid()
    {
        return Svc.Fates.Any(f => f.FateId == fate.Id);
    }

    protected override Vector3 GetPosition()
    {
        return fate.StartPosition;
    }

    public override string GetName()
    {
        return fate.Name;
    }

    protected override unsafe bool IsActivityTarget(IBattleNpc obj)
    {
        try
        {
            var battleChara = (BattleChara*)obj.Address;

            return battleChara->FateId == data.Id;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex.Message);
            return false;
        }
    }

    protected override ActivityState GetPostPathfindingState()
    {
        return ActivityState.Participating;
    }
}
