using ArmoireAutoFill.Data;
using ArmoireAutoFill.Logic;
using ArmoireAutoFill.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;

namespace ArmoireAutoFill;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    public static Configuration Configuration { get; private set; } = null!;

    private readonly WindowSystem _windowSystem;
    private readonly MainWindow _mainWindow;
    private readonly ConfigWindow _configWindow;
    private readonly CabinetObserver _cabinetObserver;
    private readonly InventoryScanner _scanner;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ECommonsMain.Init(PluginInterface, this);

        ArmoireGearDatabase.Build();

        _cabinetObserver = new CabinetObserver();
        _scanner = new InventoryScanner(_cabinetObserver);

        _mainWindow = new MainWindow(_scanner, _cabinetObserver);
        _configWindow = new ConfigWindow();

        _windowSystem = new WindowSystem("ArmoireAutoFill");
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_configWindow);

        CommandManager.AddHandler("/armoire", new CommandInfo(OnArmoireCommand)
        {
            HelpMessage = "Open the Armoire Auto-Fill window"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;

        ClientState.Login += OnLogin;
        Framework.Update += OnFrameworkUpdate;

        if (ClientState.IsLoggedIn)
        {
            OnLogin();
        }
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ClientState.Login -= OnLogin;
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUI;
        CommandManager.RemoveHandler("/armoire");
        _windowSystem.RemoveAllWindows();
        _cabinetObserver.Dispose();
        ECommonsMain.Dispose();
    }

    private bool _initialScanDone;

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!_initialScanDone && ClientState.IsLoggedIn)
        {
            if (Configuration.ScanOnLoad)
            {
                _scanner.Scan();
            }
            _initialScanDone = true;
        }
    }

    private void OnLogin()
    {
        _initialScanDone = false;
    }

    private void OnArmoireCommand(string command, string args)
    {
        ToggleMainUI();
    }

    private void DrawUI()
    {
        _windowSystem.Draw();
    }

    private void ToggleMainUI()
    {
        _mainWindow.Toggle();
    }

    private void ToggleConfigUI()
    {
        _configWindow.Toggle();
    }
}
