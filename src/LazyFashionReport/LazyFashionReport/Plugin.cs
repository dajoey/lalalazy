using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lalalazy.Changelog;

namespace LazyFashionReport;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "LazyFashionReport";

    [PluginService] internal static IDalamudPluginInterface Pi { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    private const string CommandName = "/lfr";

    public Configuration Config { get; }

    internal readonly FashionService Service;
    private readonly WindowSystem _windows = new("LazyFashionReport");
    private readonly ConfigWindow _configWindow;
    private readonly ReportWindow _reportWindow;
    private readonly ChangelogGate _changelog;

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);

        // Read BEFORE anything saves the config: tells the changelog gate "update" from "fresh install".
        var existingInstall = pi.ConfigFile.Exists;
        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();

        Service = new FashionService(this);

        _configWindow = new ConfigWindow(this);
        _reportWindow = new ReportWindow(this);
        _windows.AddWindow(_configWindow);
        _windows.AddWindow(_reportWindow);

        // Shared "What's new" popup: shows this plugin's CHANGELOG once after an update.
        _changelog = new ChangelogGate(new ChangelogGate.Options
        {
            PluginAssembly = typeof(Plugin).Assembly,
            DisplayName = "LazyFashionReport",
            ChangelogPath = "src/LazyFashionReport/CHANGELOG.md",
            Framework = Framework,
            ClientState = ClientState,
            Condition = Condition,
            Log = Log,
            Windows = _windows,
            ExistingInstall = existingInstall,
            SeenStore = new DelegateSeenStore(
                () => Config.LastSeenChangelogVersion,
                v => { Config.LastSeenChangelogVersion = v; SaveConfig(); }),
        });

        Pi.UiBuilder.Draw += _windows.Draw;
        Pi.UiBuilder.OpenConfigUi += OpenConfig;
        Pi.UiBuilder.OpenMainUi += OpenReport;

        Framework.Update += OnFrameworkUpdate;

        Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Fashion Report assistant. /lfr opens the report window; /lfr changelog shows what's new."
        });

        Service.Start();
    }

    public void SaveConfig() => Pi.SavePluginConfig(Config);

    private void OpenConfig() => _configWindow.IsOpen = true;
    internal void OpenReport() => _reportWindow.IsOpen = true;

    private void OnFrameworkUpdate(IFramework framework)
    {
        try { Service.Tick(); }
        catch (Exception ex) { Log.Error(ex, "LazyFashionReport tick failed"); }
    }

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim();
        if (arg.Equals("changelog", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("whatsnew", StringComparison.OrdinalIgnoreCase))
        {
            _changelog.ShowNow();
            return;
        }
        if (arg.Equals("refresh", StringComparison.OrdinalIgnoreCase))
        {
            Service.RequestRefresh();
            return;
        }
        OpenReport();
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        Pi.UiBuilder.Draw -= _windows.Draw;
        Pi.UiBuilder.OpenConfigUi -= OpenConfig;
        Pi.UiBuilder.OpenMainUi -= OpenReport;
        _changelog.Dispose();
        _windows.RemoveAllWindows();
        Service.Dispose();
        Commands.RemoveHandler(CommandName);
    }
}
