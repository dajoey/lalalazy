using LazyOccultCrescent.Chains;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;
using LazyOccultCrescent.Modules.CriticalEncounters;
using LazyOccultCrescent.Modules.Fates;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Commands;
using Ocelot.IPC;
using Ocelot.Modules;

namespace LazyOccultCrescent.Commands;

[OcelotCommand]
public class TeleportCommand(Plugin plugin) : OcelotCommand
{
    protected override string Command
    {
        get => "/lazyocculttp";
    }

    protected override string Description
    {
        get => "";
    }


    public override void Execute(string command, string arguments)
    {
        if (ZoneData.GetNearbyAethernetShards().Count <= 0)
        {
            Svc.Chat.Print("You are not near a aethernet shards.");
            return;
        }

        var lifestream = plugin.IPC.GetSubscriber<Lifestream>();
        if (!lifestream.IsReady() || lifestream.IsBusy())
        {
            Svc.Chat.Print("Lifestream is busy");
            return;
        }

        Aethernet? shard = null;
        if (arguments.Length <= 0)
        {
            shard ??= GetCriticalEncounterAethernet();
            shard ??= GetFateAethernet();
            shard ??= GetPotFateAethernet();
        }
        else
        {
            switch (arguments)
            {
                case "fate":
                    shard = GetFateAethernet();
                    break;
                case "ce":
                    shard = GetCriticalEncounterAethernet();
                    break;
                case "pot":
                    shard = GetPotFateAethernet();
                    break;
            }
        }

        if (shard == null)
        {
            Svc.Chat.Print("No aethernet shard found");
            return;
        }

        if (ZoneData.IsNearAethernetShard((Aethernet)shard))
        {
            Svc.Chat.Print("You are already at the closest shard");
            return;
        }

        Plugin.Chain.Submit(ChainHelper.TeleportChain((Aethernet)shard));
    }

    private Aethernet? GetFateAethernet()
    {
        var source = plugin.Modules.GetModule<FatesModule>();
        foreach (var fate in source.fates.Values)
        {
            if (fate.IsPotFate())
            {
                continue;
            }

            return fate.GetAethernet();
        }

        return null;
    }

    private Aethernet? GetPotFateAethernet()
    {
        var source = plugin.Modules.GetModule<FatesModule>();
        foreach (var fate in source.fates.Values)
        {
            if (!fate.IsPotFate())
            {
                continue;
            }

            return fate.GetAethernet();
        }

        return null;
    }

    private Aethernet? GetCriticalEncounterAethernet()
    {
        var source = plugin.Modules.GetModule<CriticalEncountersModule>();
        foreach (var encounter in source.CriticalEncounters.Values)
        {
            if (encounter.EventType >= 4 || encounter.State != DynamicEventState.Register)
            {
                continue;
            }

            if (!EventData.CriticalEncounters.TryGetValue(encounter.DynamicEventId, out var data))
            {
                continue;
            }

            return data.Aethernet ?? ZoneData.GetClosestAethernetShard(data.StartPosition ?? encounter.MapMarker.Position);
        }

        return null;
    }
}
