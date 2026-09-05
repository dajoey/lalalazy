using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lalalazy.Changelog;

namespace LazyRetainerLive;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "LazyRetainerLive";

    [PluginService] internal static IDalamudPluginInterface Pi { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/lazyretainerlive";

    public Configuration Config { get; }

    private readonly RetainerLiveService _service;
    private readonly HttpServer _http;
    private readonly WindowSystem _windows = new("LazyRetainerLive");
    private readonly ConfigWindow _configWindow;
    private readonly ChangelogGate _changelog;

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);

        // Read BEFORE anything saves the config: tells the changelog gate "update" from "fresh install".
        var existingInstall = pi.ConfigFile.Exists;
        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();

        _service = new RetainerLiveService(this);
        _http = new HttpServer(_service, () => Config.Port, () => Config.Enabled);

        _configWindow = new ConfigWindow(this);
        _windows.AddWindow(_configWindow);

        // Shared "What's new" popup: shows this plugin's CHANGELOG once after an update.
        _changelog = new ChangelogGate(new ChangelogGate.Options
        {
            PluginAssembly = typeof(Plugin).Assembly,
            DisplayName = "LazyRetainerLive",
            ChangelogPath = "src/LazyRetainerLive/CHANGELOG.md",
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
        Pi.UiBuilder.OpenMainUi += OpenConfig;

        Framework.Update += OnFrameworkUpdate;

        Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the LazyRetainerLive settings window. /lazyretainerlive changelog shows what's new."
        });

        Log.Information($"LazyRetainerLive loaded (port {Config.Port}, enabled {Config.Enabled})");
    }

    public void SaveConfig() => Pi.SavePluginConfig(Config);

    /// <summary>Latest live snapshot for the config window status line (null = none yet).</summary>
    internal RetainerLiveService? State => _service;

    /// <summary>Last listener bind error, for the config window status line.</summary>
    internal string HttpError => _http.LastError;

    private void OpenConfig() => _configWindow.IsOpen = true;

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            _service.Tick();
            _http.EnsureStarted();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "LazyRetainerLive tick failed");
        }
    }

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim();
        if (arg.Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            var snap = _service.Current;
            Log.Information(
                $"LazyRetainerLive state: enabled={Config.Enabled} port={Config.Port} " +
                $"listenerError='{_http.LastError}' snapshot={(snap == null ? "none" : $"{snap.Char}@{snap.World} retainers={snap.Retainers.Count}")} " +
                $"reason='{_service.LastReason}'");
            return;
        }
        if (arg.Equals("changelog", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("whatsnew", StringComparison.OrdinalIgnoreCase))
        {
            _changelog.ShowNow();
            return;
        }
        _configWindow.IsOpen = !_configWindow.IsOpen;
    }

    public void Dispose()
    {
        _http.Dispose();
        Framework.Update -= OnFrameworkUpdate;
        Pi.UiBuilder.Draw -= _windows.Draw;
        Pi.UiBuilder.OpenConfigUi -= OpenConfig;
        Pi.UiBuilder.OpenMainUi -= OpenConfig;
        _changelog.Dispose();
        _windows.RemoveAllWindows();
        Commands.RemoveHandler(CommandName);
    }
}
