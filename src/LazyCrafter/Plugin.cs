using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using LazyCrafter.UI;

namespace LazyCrafter;

/// <summary>
/// Entry point. Wiring only: service injection, window system, command handler.
/// All logic lives in <c>Core/</c> (pure) and <c>Adapters/</c> (Dalamud-facing).
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public string Name => "LazyCrafter";

    [PluginService] internal static IDalamudPluginInterface Pi { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private const string CommandName = "/lcraft";

    public Configuration Config { get; }

    private readonly WindowSystem _windows = new("LazyCrafter");
    private readonly MainWindow _mainWindow;

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);
        // DalamudReflector is needed later for the GBR/ARC reflection dispatch (Plan §Phase 5).
        ECommonsMain.Init(pi, this, Module.DalamudReflector);

        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        Config.MigrateIfNeeded();

        _mainWindow = new MainWindow(this);
        _windows.AddWindow(_mainWindow);

        Pi.UiBuilder.Draw += _windows.Draw;
        Pi.UiBuilder.OpenConfigUi += OpenMain;
        Pi.UiBuilder.OpenMainUi += OpenMain;

        Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the LazyCrafter window. '/lcraft debug' dumps state to the log.",
        });

        Log.Information("LazyCrafter {Version} loaded (core {Core})",
            typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "?", Core.CoreInfo.Version);
    }

    public void SaveConfig() => Pi.SavePluginConfig(Config);

    private void OpenMain() => _mainWindow.IsOpen = true;

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            LogDebugState();
            return;
        }
        _mainWindow.Toggle();
    }

    private void LogDebugState()
    {
        Log.Information("[LazyCrafter debug] core={Core} configVersion={Cfg} loggedIn={In}",
            Core.CoreInfo.Version, Config.Version, ClientState.IsLoggedIn);
    }

    public void Dispose()
    {
        Commands.RemoveHandler(CommandName);
        Pi.UiBuilder.Draw -= _windows.Draw;
        Pi.UiBuilder.OpenConfigUi -= OpenMain;
        Pi.UiBuilder.OpenMainUi -= OpenMain;
        _windows.RemoveAllWindows();
        ECommonsMain.Dispose();
    }
}
