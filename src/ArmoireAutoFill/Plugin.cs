using ArmoireAutoFill.Data;
using ArmoireAutoFill.Logic;
using ArmoireAutoFill.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using Lalalazy.Changelog;

namespace ArmoireAutoFill;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public static Configuration Configuration { get; private set; } = null!;

    private readonly WindowSystem _windowSystem;
    private readonly MainWindow _mainWindow;
    private readonly ConfigWindow _configWindow;
    private readonly CabinetObserver _cabinetObserver;
    private readonly InventoryScanner _scanner;
    private readonly ArmoireAutoStore _autoStore;
    private readonly ChangelogGate _changelog;

    public Plugin()
    {
        // Read BEFORE anything saves the config (the v3 migration below does): tells the changelog
        // gate "update" from "fresh install".
        var existingInstall = PluginInterface.ConfigFile.Exists;
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Configuration.Version < 3)
        {
            // v3 migration: auto-store on armoire open is now the default behavior.
            Configuration.AutoStoreOnOpen = true;
            Configuration.Version = 3;
            Configuration.Save();
        }
        ECommonsMain.Init(PluginInterface, this);

        ArmoireGearDatabase.Build();

        _cabinetObserver = new CabinetObserver();
        _scanner = new InventoryScanner(_cabinetObserver);
        _autoStore = new ArmoireAutoStore();

        // Re-scan whenever the cabinet snapshot changes so armoire state shows up
        // without the user having to push a button.
        _cabinetObserver.OnSnapshotChanged += _scanner.Scan;

        _mainWindow = new MainWindow(_scanner, _cabinetObserver, _autoStore);
        _configWindow = new ConfigWindow();

        _windowSystem = new WindowSystem("ArmoireAutoFill");
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_configWindow);

        // Shared "What's new" popup (repo standing rule): shows this plugin's CHANGELOG once after an update.
        _changelog = new ChangelogGate(new ChangelogGate.Options
        {
            PluginAssembly = typeof(Plugin).Assembly,
            DisplayName = "Armoire Auto-Fill",
            ChangelogPath = "src/ArmoireAutoFill/CHANGELOG.md",
            Framework = Framework,
            ClientState = ClientState,
            Condition = Condition,
            Log = Log,
            Windows = _windowSystem,
            ExistingInstall = existingInstall,
            SeenStore = new DelegateSeenStore(
                () => Configuration.LastSeenChangelogVersion,
                v => { Configuration.LastSeenChangelogVersion = v; Configuration.Save(); }),
        });

        CommandManager.AddHandler("/armoire", new CommandInfo(OnArmoireCommand)
        {
            HelpMessage = "Open the Armoire Auto-Fill window. /armoire changelog shows what's new."
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
        _changelog.Dispose();
        _windowSystem.RemoveAllWindows();
        _cabinetObserver.OnSnapshotChanged -= _scanner.Scan;
        _cabinetObserver.Dispose();
        _autoStore.Dispose();
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
        var a = args.Trim();
        if (a.Equals("changelog", StringComparison.OrdinalIgnoreCase) || a.Equals("whatsnew", StringComparison.OrdinalIgnoreCase))
        {
            _changelog.ShowNow();
            return;
        }
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
