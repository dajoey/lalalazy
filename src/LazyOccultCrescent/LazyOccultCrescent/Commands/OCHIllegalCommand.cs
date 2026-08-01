using System.Collections.Generic;
using LazyOccultCrescent.Enums;
using LazyOccultCrescent.Modules.Automator;
using LazyOccultCrescent.Modules.CriticalEncounters;
using LazyOccultCrescent.Modules.Fates;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Ocelot.Commands;
using Ocelot.Modules;

namespace LazyOccultCrescent.Commands;

[OcelotCommand]
public class OCHIllegalCommand(Plugin plugin) : OcelotCommand
{
    protected override string Command
    {
        get => "/lazyoccultillegal";
    }

    protected override string Description
    {
        get => @"
Manage och automator/illegal mode.
 - /lazyoccultillegal (Toggles the automator lens window)
 - /lazyoccultillegal on (Enables illegal mode (Automation))
 - /lazyoccultillegal off (Disables illegal mode (Automation))
 - /lazyoccultillegal toggle (Toggles illegal mode (Automation))
--------------------------------
".Trim();
    }

    protected override IReadOnlyList<string> Aliases
    {
        get => ["/lazyocillegal"];
    }

    protected override IReadOnlyList<string> ValidArguments
    {
        get => ["on", "off", "toggle"];
    }

    public override void Execute(string command, string arguments)
    {
        if (arguments.Trim() == "")
        {
            plugin.Windows.GetWindow<AutomatorWindow>()?.Toggle();
            return;
        }

        if (!plugin.Modules.TryGetModule<AutomatorModule>(out var automator) || automator == null)
        {
            return;
        }

        switch (arguments)
        {
            case "on":
                automator.EnableIllegalMode();
                break;
            case "off":
                automator.DisableIllegalMode();
                break;
            case "toggle":
                AutomatorModule.ToggleIllegalMode(plugin);
                break;
        }

        plugin.Config.Save();
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
