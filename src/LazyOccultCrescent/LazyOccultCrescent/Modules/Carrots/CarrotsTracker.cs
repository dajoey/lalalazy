using System.Collections.Generic;
using System.Linq;
using LazyOccultCrescent.Enums;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;

namespace LazyOccultCrescent.Modules.Carrots;

public class CarrotsTracker
{
    public List<Carrot> carrots = [];

    public void Tick(IFramework _)
    {
        // Full object-table scan + sort + N allocations. Carrots do not move.
        if (!EzThrottler.Throttle("Carrots.Scan", 200))
        {
            return;
        }

        carrots = Svc.Objects
            .Where(o => o.ObjectKind == ObjectKind.EventObj)
            .Where(o => o.BaseId == (uint)OccultObjectType.Carrot)
            .OrderBy(Player.DistanceTo)
            .Select(o => new Carrot(o))
            .Where(c => c.IsValid())
            .ToList();
    }
}
