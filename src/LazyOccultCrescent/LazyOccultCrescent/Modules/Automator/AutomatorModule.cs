using System.Collections.Generic;
using System;
using LazyOccultCrescent.Data;
using Ocelot.Chain;
using ECommons.DalamudServices;
using Ocelot;
using Ocelot.IPC;
using Ocelot.Modules;
using Ocelot.Windows;

namespace LazyOccultCrescent.Modules.Automator;

[OcelotModule(int.MaxValue - 1)]
public class AutomatorModule : Module
{
    public override AutomatorConfig Config
    {
        get => PluginConfig.AutomatorConfig;
    }

    public override bool IsEnabled
    {
        get => Config.IsPropertyEnabled(nameof(Config.Enabled));
    }

    public readonly Automator automator = new();

    public readonly Panel panel = new();

    // Territory list lives on ZoneData so a new horn is a one-line change there.
    private readonly List<uint> occultCrescentTerritoryIds = [.. LazyOccultCrescent.Data.ZoneData.OccultTerritories];

    public AutomatorModule(Plugin plugin, Config config)
        : base(plugin, config)
    {
        config.AutomatorConfig.Enabled = false;
        config.Save();
    }


    public override void PostUpdate(UpdateContext context)
    {
        automator.PostUpdate(this, context.Framework);
    }


    public override bool RenderMainUi(RenderContext context)
    {
        panel.Draw(this);
        return true;
    }

    public override void OnTerritoryChanged(uint id)
    {
        if (occultCrescentTerritoryIds.Contains(id))
        {
            return;
        }

        automator.Refresh();
        Config.Enabled = false;
        PluginConfig.Save();
    }

    public static void ToggleIllegalMode(OcelotPlugin plugin)
    {
        var module = plugin.Modules.GetModule<AutomatorModule>();
        if (!module.Config.Enabled)
        {
            module.EnableIllegalMode();
        }
        else
        {
            module.DisableIllegalMode();
        }
    }

    public void EnableIllegalMode()
    {
        var wasDisabled = !Config.Enabled;
        Config.Enabled = true;

        // Nothing to un-cancel: MovementGate is a generation counter, not a sticky
        // flag, so anything started from here latches the current value and runs.

        if (wasDisabled)
        {
            Svc.Chat.Print(T("messages.on"));
        }
    }

    public void DisableIllegalMode()
    {
        var wasEnabled = Config.Enabled;
        Config.Enabled = false;
        automator.Refresh();

        // Cancel in-flight movement FIRST. Stopping vnavmesh on its own is not
        // enough: the movement chain re-issues whenever vnavmesh is idle, so the
        // stop is undone a tick later and the character continues to wherever it
        // was headed even though the action was cancelled.
        MovementGate.CancelAll("emergency stop");
        Plugin.IPC.GetSubscriber<VNavmesh>().Stop();

        // All of them. Aborting only Plugin.Chain left the buff sequence, the mob
        // farmer and the pathfinder's step processor running.
        foreach (var queue in new[]
        {
            "LOC##main",
            "LOC##BuffManager",
            "MobFarmer+Farmer",
            typeof(LazyOccultCrescent.Modules.Treasure.TreasureHunt).FullName!,
            typeof(LazyOccultCrescent.Modules.Carrots.CarrotHunt).FullName!,
        })
        {
            try
            {
                ChainManager.Get(queue).Abort();
            }
            catch (Exception ex)
            {
                Svc.Log.Debug($"[EmergencyStop] could not abort queue {queue}: {ex.Message}");
            }
        }

        if (wasEnabled)
        {
            Svc.Chat.Print(T("messages.off"));
        }
    }
}
