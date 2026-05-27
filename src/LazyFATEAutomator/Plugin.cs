using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;

namespace LazyFATEAutomator;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Lazy FATE Automator";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager   CommandManager  { get; private set; } = null!;
    [PluginService] internal static IClientState      ClientState     { get; private set; } = null!;
    [PluginService] internal static IFramework        Framework       { get; private set; } = null!;
    [PluginService] internal static ICondition        Condition       { get; private set; } = null!;
    [PluginService] internal static IDataManager      DataManager     { get; private set; } = null!;
    [PluginService] internal static IPluginLog        PluginLog       { get; private set; } = null!;
    [PluginService] internal static IFateTable        FateTable       { get; private set; } = null!;
    [PluginService] internal static ITargetManager    TargetManager   { get; private set; } = null!;
    [PluginService] internal static IObjectTable      ObjectTable     { get; private set; } = null!;

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
        ECommonsMain.Init(pi, this);

        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Migrate(); // applies schema migration if Version is behind CurrentSchemaVersion

        Navigation = new NavigationHelper();
        FatesSolver = new FATESolver(this);
        StuckTracker = new StuckTracker(this);
        StateController = new StateController(this);

        _mainWindow = new FATEAutomatorWindow(this);
        _windowSystem.AddWindow(_mainWindow);

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi   += ToggleWindow;

        Framework.Update += OnFrameworkUpdate;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Lazy FATE Automator panel. Subcommands: start | stop | toggle | status."
        });
    }

    public void SaveConfig() => Config.Save();

    private void ToggleWindow() => _mainWindow.IsOpen = !_mainWindow.IsOpen;

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!ClientState.IsLoggedIn) return;
        // Tick is internally try/caught with back-off, so no need to wrap here.
        StateController.Tick();
    }

    private void OnCommand(string command, string args)
    {
        var arg = (args ?? string.Empty).Trim().ToLowerInvariant();
        switch (arg)
        {
            case "":
                ToggleWindow();
                break;
            case "start":
                if (!StateController.IsEnabled) StateController.Start();
                break;
            case "stop":
                if (StateController.IsEnabled) StateController.Stop();
                break;
            case "toggle":
                StateController.Toggle();
                break;
            case "status":
                PluginLog.Information($"[LazyFATE] enabled={StateController.IsEnabled} state={StateController.State} status={StateController.Status} completed={StateController.CompletedFatesCount}");
                break;
            default:
                PluginLog.Information($"[LazyFATE] unknown subcommand: {arg}. Try: start | stop | toggle | status, or run /lazyfate alone to open the panel.");
                break;
        }
    }

    public void Dispose()
    {
        try { StateController.Dispose(); }
        catch (Exception ex) { PluginLog.Warning(ex, "StateController.Dispose threw"); }

        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi   -= ToggleWindow;
        _windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
        ECommonsMain.Dispose();
    }
}
