using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using KamiToolKit;
using Lalalazy.Changelog;

namespace WondrousTailsSolver;

public sealed class WondrousTailsSolverPlugin : IDalamudPlugin {
    // NOTE: this fork declares its own `static class System` (System.cs), which shadows the BCL
    // `System` namespace inside this file. Anything from the real System namespace must therefore be
    // written as `global::System....` here - that is why the usings above are types, not namespaces.
    [PluginService] internal static IDalamudPluginInterface Pi { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;

    private const string CommandName = "/lazywtmath";

    // Upstream EzWondrousTails has no WindowSystem, no configuration and no command at all - the whole
    // plugin is a native node bolted onto the WeeklyBingo addon. The shared "What's new" popup needs a
    // WindowSystem to live in, so this fork creates a one-window system of its own and draws it, plus a
    // command to reopen the popup. Seen-version goes to a sidecar json (SidecarSeenStore), never to a
    // config class, so upstream merges stay clean.
    private readonly WindowSystem _windows = new("LazyWTMath");
    private readonly ChangelogGate _changelog;

    public WondrousTailsSolverPlugin(IDalamudPluginInterface pluginInterface) {
        pluginInterface.Inject(this);

        // This plugin never writes pi.ConfigFile, so its existence cannot tell an update from a fresh
        // install. The config DIRECTORY is created by Dalamud for any plugin that has ever been loaded,
        // and the sidecar itself lives in it - treat "directory already there" as "already installed".
        var existingInstall = pluginInterface.ConfigFile.Exists || pluginInterface.ConfigDirectory.Exists;

        System.PerfectTails = new PerfectTails();
        System.AddonWeeklyBingoController = new AddonWeeklyBingoController(pluginInterface);

        // Shared "What's new" popup (repo standing rule): shows this plugin's CHANGELOG once after an update.
        _changelog = new ChangelogGate(new ChangelogGate.Options {
            PluginAssembly = typeof(WondrousTailsSolverPlugin).Assembly,
            DisplayName = "Lazy WT Math",
            ChangelogPath = "src/LazyWTMath/CHANGELOG.md",
            Framework = Framework,
            ClientState = ClientState,
            Condition = Condition,
            Log = Log,
            Windows = _windows,
            ExistingInstall = existingInstall,
            SeenStore = new SidecarSeenStore(pluginInterface, (ex, msg) => Log.Warning(ex, msg)),
        });

        pluginInterface.UiBuilder.Draw += _windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi += _changelog.ShowNow;
        pluginInterface.UiBuilder.OpenConfigUi += _changelog.ShowNow;

        Commands.AddHandler(CommandName, new CommandInfo(OnCommand) {
            HelpMessage = "Show what's new in Lazy WT Math (the probabilities themselves appear in the Wondrous Tails window).",
        });
    }

    private void OnCommand(string command, string args) {
        _changelog.ShowNow();
    }

    public void Dispose() {
        Commands.RemoveHandler(CommandName);
        Pi.UiBuilder.Draw -= _windows.Draw;
        Pi.UiBuilder.OpenMainUi -= _changelog.ShowNow;
        Pi.UiBuilder.OpenConfigUi -= _changelog.ShowNow;
        System.AddonWeeklyBingoController.Dispose();
        _changelog.Dispose();
        _windows.RemoveAllWindows();
    }
}