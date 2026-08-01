using System.Linq;
using System.Numerics;
using LazyOccultCrescent.Chains;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;
using LazyOccultCrescent.Modules.Automator;
using LazyOccultCrescent.Modules.StateManager;
using Dalamud.Interface;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;

namespace LazyOccultCrescent.Modules.Teleporter;

public class Teleporter(TeleporterModule module)
{
    public void Button(Aethernet? aethernet, Vector3 destination, string name, string id, EventData ev)
    {
        if (!module.TryGetIPCSubscriber<VNavmesh>(out var vnav) || vnav == null || !vnav.IsReady())
        {
            return;
        }

        if (aethernet == null)
        {
            aethernet = ZoneData.GetClosestAethernetShard(destination);
        }

        OcelotUi.Indent(() =>
        {
            PathfindingButton(destination, name, id, ev);
            TeleportButton((Aethernet)aethernet, destination, name, id, ev);
        });
    }

    private void PathfindingButton(Vector3 destination, string name, string id, EventData ev)
    {
        if (!module.TryGetIPCSubscriber<VNavmesh>(out var vnav) || vnav == null || !vnav.IsReady())
        {
            return;
        }

        if (ImGuiEx.IconButton(FontAwesomeIcon.Running, $"{name}##{id}"))
        {
            Svc.Log.Info($"Pathfinding to {name} at {destination}");

            Plugin.Chain.Submit(() => Chain.Create("Pathfinding")
                .Then(new PathfindingChain(vnav, destination, ev, 20f))
                .ConditionalThen(_ => module.Config.ShouldMount, ChainHelper.MountChain())
                .WaitUntilNear(vnav, destination, 205f)
            );
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Pathfind to {name}");
        }

        if (!module.TryGetIPCSubscriber<Lifestream>(out var lifestream) || lifestream == null || !lifestream.IsReady())
        {
            return;
        }

        ImGui.SameLine();
    }

    private void TeleportButton(Aethernet aethernet, Vector3 destination, string name, string id, EventData ev)
    {
        if (!module.TryGetIPCSubscriber<Lifestream>(out var lifestream) || lifestream == null || !lifestream.IsReady())
        {
            return;
        }

        var isNearShards = ZoneData.GetNearbyAethernetShards().Any();
        var isNearCurrentShard = ZoneData.IsNearAethernetShard(aethernet);

        if (ImGuiEx.IconButton(FontAwesomeIcon.LocationArrow, $"{name}##{id}", enabled: isNearShards && !isNearCurrentShard))
        {
            Chain Factory()
            {
                var chain = Chain.Create("Teleport Sequence")
                    .Then(ChainHelper.TeleportChain(aethernet))
                    .Debug("Waiting for lifestream to not be 'busy'")
                    .Then(new TaskManagerTask(() => !lifestream.IsBusy(), new TaskManagerConfiguration { TimeLimitMS = 30000 }));

                if (module.TryGetIPCSubscriber<VNavmesh>(out var vnav) && vnav != null && vnav.IsReady())
                {
                    chain.RunIf(() => module.Config.PathToDestination)
                        .Then(new PathfindingChain(vnav, destination, ev, 20f))
                        .ConditionalThen(_ => module.Config.ShouldMount, ChainHelper.MountChain())
                        .WaitUntilNear(vnav, destination, 20f);
                }

                return chain;
            }

            Plugin.Chain.Submit(Factory);
        }

        if (!ImGui.IsItemHovered())
        {
            return;
        }

        if (!isNearShards)
        {
            ImGui.SetTooltip($"You must be near an aetheryte to teleport");
        }
        else if (isNearCurrentShard)
        {
            ImGui.SetTooltip($"You're already at this aetheryte");
        }
        else
        {
            ImGui.SetTooltip($"Teleport to {aethernet.ToFriendlyString()}");
        }
    }

    public void OnFateEnd(StateManagerModule states)
    {
        if (module.GetModule<AutomatorModule>().IsEnabled)
        {
            return;
        }

        if (!module.Config.ReturnAfterFate)
        {
            return;
        }

        Return();
    }

    public void OnCriticalEncounterEnd(StateManagerModule states)
    {
        if (module.GetModule<AutomatorModule>().IsEnabled)
        {
            return;
        }

        if (!module.Config.ReturnAfterCriticalEncounter)
        {
            return;
        }

        Return();
    }

    public void Return()
    {
        if (ZoneData.IsInForkedTower())
        {
            return;
        }

        Plugin.Chain.Submit(ChainHelper.ReturnChain());
    }

    public bool IsReady()
    {
        return module.TryGetIPCSubscriber<Lifestream>(out _);
    }
}
