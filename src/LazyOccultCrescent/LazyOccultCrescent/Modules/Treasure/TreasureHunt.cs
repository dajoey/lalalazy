using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Pathfinding;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace LazyOccultCrescent.Modules.Treasure;

public class TreasureHunt(TreasureModule module) : Hunter(module)
{
    private List<TreasureData.TreasureDatum> Treasure = [];

    protected override IEnumerable<IGameObject> GetValidObjects()
    {
        return Svc.Objects
            .Where(o => o is
            {
                ObjectKind: ObjectKind.Treasure,
                IsDead: false,
                IsTargetable: true,
            } && o.IsValid());
    }

    protected override Vector3 GetDestinationForCurrentStep()
    {
        return Treasure.First(t => t.Id == CurrentStep.NodeId).Position;
    }

    protected override unsafe IPathfinder CreatePathfinder()
    {
        Treasure.Clear();
        var layout = LayoutWorld.Instance()->ActiveLayout;
        if (layout == null)
        {
            Svc.Log.Warning("No active layout");
        }

        if (!layout->InstancesByType.TryGetValue(InstanceType.Treasure, out var mapPtr, false))
        {
            Svc.Log.Warning("No active treasure map");
        }

        foreach (ILayoutInstance* instance in mapPtr.Value->Values)
        {
            var transform = instance->GetTransformImpl();
            var position = transform->Translation;
            if (position.Y <= -10f)
            {
                continue;
            }

            var treasureRowId = Unsafe.Read<uint>((byte*)instance + 0x30);
            var sgbId = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Treasure>().GetRow(treasureRowId).SGB.RowId;
            if (sgbId != 1596 && sgbId != 1597)
            {
                continue;
            }

            Treasure.Add(new TreasureData.TreasureDatum(treasureRowId, position, sgbId));
        }

        Treasure = Treasure.OrderBy(t => t.Id).ToList();

        return new Pathfinder(Treasure, module.PluginConfig.PathfinderConfig.ReturnCost, module.PluginConfig.PathfinderConfig.TeleportCost);
    }

    protected override Func<Chain> GetInteractionChain(IGameObject obj)
    {
        return () => Chain.Create()
            .BreakIf(() => !GetValidObjects().Any(o => Vector3.Distance(o.Position, obj.Position) <= DISTANCE_TO_NODE_TO_USE))
            .Then(new TaskManagerTask(() =>
            {
                if (!EzThrottler.Throttle("ChestInteract", 250))
                {
                    return false;
                }

                if (Player.DistanceTo(obj) > DISTANCE_TO_NODE_TO_USE)
                {
                    return true;
                }

                unsafe
                {
                    Svc.Targets.Target = obj;
                    var gameObject = (GameObject*)(void*)obj.Address;
                    var instance = (FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure*)gameObject;
                    TargetSystem.Instance()->InteractWithObject(gameObject);
                    return instance->Flags.HasFlag(FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure.TreasureFlags.Opened);
                }
            }));
    }

    protected override List<uint> GetValidNodes(int max)
    {
        return TreasureData.Levels.Where(node => node.Value <= max).Select(node => node.Key).ToList();
    }
}
