using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using System;

namespace LazyFATEAutomator;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Lazy FATE Automator";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] internal static IFateTable FateTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;

    private const string CommandName = "/lazyfate";

    public Configuration Config { get; }
    public StateController StateController { get; }
    public FATESolver FatesSolver { get; }
    public StuckTracker StuckTracker { get; }
    public NavigationHelper Navigation { get; }

    private readonly WindowSystem _windowSystem = new("LazyFATEAutomator");
    private readonly FATEAutomatorWindow _mainWindow;

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);

        // Initialize ECommons framework helpers
        ECommonsMain.Init(pi, this);

        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();

        // Create our support services
        Navigation = new NavigationHelper();
        FatesSolver = new FATESolver(this);
        StuckTracker = new StuckTracker(this);
        StateController = new StateController(this);

        // Create user interface
        _mainWindow = new FATEAutomatorWindow(this);
        _windowSystem.AddWindow(_mainWindow);

        // Register window callbacks
        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;

        // Register core update loop
        Framework.Update += OnFrameworkUpdate;

        // Command handler registration
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Lazy FATE Automator control panel."
        });
    }

    public void SaveConfig() => Config.Save();

    private void ToggleWindow() => _mainWindow.IsOpen = !_mainWindow.IsOpen;

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!ClientState.IsLoggedIn) return;

        try
        {
            StateController.Tick();
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Lazy FATE Automator tick iteration failed");
        }
    }

    private void OnCommand(string command, string args)
    {
        ToggleWindow();
    }

    public void Dispose()
    {
        StateController.Dispose();
        
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;

        _windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);

        ECommonsMain.Dispose();
    }
}
