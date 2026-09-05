using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace LazyFishSitter;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Lazy Fish Sitter";

    [PluginService] internal static IDalamudPluginInterface Pi { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/lazyfishsitter";

    public Configuration Config { get; }

    internal readonly FishSitService _service;
    private readonly WindowSystem _windows = new("LazyFishSitter");
    private readonly ConfigWindow _configWindow;

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);

        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();

        _service = new FishSitService(this);
        _configWindow = new ConfigWindow(this);
        _windows.AddWindow(_configWindow);

        Pi.UiBuilder.Draw += _windows.Draw;
        Pi.UiBuilder.OpenConfigUi += OpenConfig;
        Pi.UiBuilder.OpenMainUi += OpenConfig;

        Framework.Update += OnFrameworkUpdate;

        Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Lazy Fish Sitter settings window."
        });
    }

    public void SaveConfig() => Pi.SavePluginConfig(Config);

    private void OpenConfig() => _configWindow.IsOpen = true;

    private void OnFrameworkUpdate(IFramework framework)
    {
        try { _service.Tick(); }
        catch (Exception ex) { Log.Error(ex, "LazyFishSitter tick failed"); }
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            _service.LogDebugState();
            return;
        }
        _configWindow.IsOpen = !_configWindow.IsOpen;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        Pi.UiBuilder.Draw -= _windows.Draw;
        Pi.UiBuilder.OpenConfigUi -= OpenConfig;
        Pi.UiBuilder.OpenMainUi -= OpenConfig;
        _windows.RemoveAllWindows();
        Commands.RemoveHandler(CommandName);
    }
}
