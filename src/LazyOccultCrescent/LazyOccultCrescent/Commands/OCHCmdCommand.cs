using System.Collections.Generic;
using LazyOccultCrescent.Enums;
using LazyOccultCrescent.Modules.CriticalEncounters;
using LazyOccultCrescent.Modules.Fates;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Ocelot.Commands;
using Ocelot.Modules;

namespace LazyOccultCrescent.Commands;

[OcelotCommand]
public class OCHCmdCommand(Plugin plugin) : OcelotCommand
{
    protected override string Command
    {
        get => "/lazyoccultcmd";
    }

    protected override string Description
    {
        get => @"
Utility command.
 - Flag commands clear active flag before trying to place a new one
   - /lazyoccultcmd flag-active-ce (Place a flag marker on the current Critical Engagement)
   - /lazyoccultcmd flag-active-fate (Place a flag marker on a current Fate)
   - /lazyoccultcmd flag-active-non-pot-fate (Place a flag marker on a current fate that isn't a pot fate)
--------------------------------
".Trim();
    }

    protected override IReadOnlyList<string> Aliases
    {
        get => ["/lazyoccmd"];
    }

    protected override IReadOnlyList<string> ValidArguments
    {
        get => ["flag-active-ce", "flag-active-fate", "flag-active-non-pot-fate"];
    }

    public override unsafe void Execute(string command, string arguments)
    {
        var map = AgentMap.Instance();
        map->FlagMarkerCount = 0;

        switch (arguments)
        {
            case "flag-active-ce": FlagActiveCe(map); break;
            case "flag-active-fate": FlagActiveFate(map, false); break;
            case "flag-active-non-pot-fate": FlagActiveFate(map, true); break;
        }
    }

    private unsafe void FlagActiveCe(AgentMap* map)
    {
        if (!plugin.Modules.TryGetModule<CriticalEncountersModule>(out var source) || source == null)
        {
            return;
        }

        foreach (var encounter in source.CriticalEncounters.Values)
        {
            if (encounter.EventType >= 4 || encounter.State != DynamicEventState.Register)
            {
                continue;
            }

            map->SetFlagMapMarker(Svc.ClientState.TerritoryType, Svc.ClientState.MapId, encounter.MapMarker.Position);
            return;
        }
    }

    private unsafe void FlagActiveFate(AgentMap* map, bool ignorePots)
    {
        if (!plugin.Modules.TryGetModule<FatesModule>(out var source) || source == null)
        {
            return;
        }

        foreach (var fate in source.fates.Values)
        {
            if (ignorePots && fate.Data.Note == MonsterNote.PersistentPots)
            {
                continue;
            }

            map->SetFlagMapMarker(Svc.ClientState.TerritoryType, Svc.ClientState.MapId, fate.StartPosition);
            return;
        }
    }
}
