using System.Linq;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using Ocelot.Windows;

namespace LazyOccultCrescent.Modules.Treasure;

public class Radar
{
    public void Draw(RenderContext context)
    {
        if (!ZoneData.IsInOccultCrescent() || Svc.Condition[ConditionFlag.InCombat])
        {
            return;
        }

        if (!context.IsForModule<TreasureModule>(out var module))
        {
            return;
        }

        var config = module.Config;
        if (config is { ShouldDrawLineToBronzeChests: false, ShouldDrawLineToSilverChests: false })
        {
            return;
        }


        foreach (var treasure in module.Treasures.Where(treasure => treasure.IsValid()))
        {
            if (config.ShouldDrawLineToBronzeChests && treasure.GetTreasureType() == TreasureType.Bronze)
            {
                context.DrawLine(treasure.GetPosition(), treasure.GetColor());
            }

            if (config.ShouldDrawLineToSilverChests && treasure.GetTreasureType() == TreasureType.Silver)
            {
                context.DrawLine(treasure.GetPosition(), treasure.GetColor());
            }
        }
    }
}
