using System.Collections.Generic;
using LazyOccultCrescent.Modules.StateManager;
using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.WindowManager;

[OcelotModule(5)]
public class WindowManagerModule(Plugin _plugin, Config _config) : Module(_plugin, _config)
{
    public override WindowManagerConfig Config
    {
        get => PluginConfig.WindowManagerConfig;
    }

    public override bool ShouldInitialize
    {
        get => true;
    }


    // Territory list lives on ZoneData so a new horn is a one-line change there.
    private readonly List<uint> occultCrescentTerritoryIds = [.. LazyOccultCrescent.Data.ZoneData.OccultTerritories];

    private bool mainClosed = false;

    private bool configClosed = false;


    public override void PostInitialize()
    {
        if (Config.OpenMainOnStartUp)
        {
            Plugin.Windows.OpenMainUI();
        }


        if (Config.OpenConfigOnStartUp)
        {
            Plugin.Windows.OpenConfigUI();
        }

        GetModule<StateManagerModule>().OnEnterInCombat += EnterCombat;
        GetModule<StateManagerModule>().OnEnterInCriticalEncounter += EnterCombat;
        GetModule<StateManagerModule>().OnEnterInFate += EnterCombat;
        GetModule<StateManagerModule>().OnEnterIdle += ExitCombat;
    }

    public override void OnTerritoryChanged(uint id)
    {
        if (occultCrescentTerritoryIds.Contains(id))
        {
            if (Config.OpenMainOnEnter)
            {
                Plugin.Windows.OpenMainUI();
            }


            if (Config.OpenConfigOnEnter)
            {
                Plugin.Windows.OpenConfigUI();
            }
        }
        else
        {
            if (Config.CloseMainOnExit)
            {
                Plugin.Windows.CloseMainUI();
            }


            if (Config.CloseConfigOnExit)
            {
                Plugin.Windows.CloseConfigUI();
            }
        }
    }

    private void EnterCombat(StateManagerModule module)
    {
        if (Config.HideMainInCombat && Plugin.Windows.IsMainUIOpen())
        {
            Plugin.Windows.CloseMainUI();
            mainClosed = true;
        }

        if (Config.HideConfigInCombat && Plugin.Windows.IsConfigUIOpen())
        {
            Plugin.Windows.CloseConfigUI();
            configClosed = true;
        }
    }

    private void ExitCombat(StateManagerModule module)
    {
        if (Config.HideMainInCombat && mainClosed)
        {
            Plugin.Windows.OpenMainUI();
            mainClosed = false;
        }

        if (Config.HideConfigInCombat && configClosed)
        {
            Plugin.Windows.OpenConfigUI();
            configClosed = false;
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        GetModule<StateManagerModule>().OnEnterInCombat -= EnterCombat;
        GetModule<StateManagerModule>().OnEnterInCriticalEncounter -= EnterCombat;
        GetModule<StateManagerModule>().OnEnterInFate -= EnterCombat;
        GetModule<StateManagerModule>().OnEnterIdle -= ExitCombat;
    }
}
