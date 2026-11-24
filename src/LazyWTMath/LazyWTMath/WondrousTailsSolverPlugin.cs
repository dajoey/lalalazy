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

    // Fork: Share one WindowSystem between the existing changelog and the refreshed probability window.
    // Upstream EzWondrousTails has no WindowSystem, no configuration and no command at all - the whole
    // plugin is a native node bolted onto the WeeklyBingo addon. The shared "What's new" popup needs a
    // WindowSystem to live in, so this fork creates a window system of its own and draws it. There is deliberately no slash command.
    // Seen-version goes to a sidecar json (SidecarSeenStore), never to a config class, so upstream merges stay clean.
    private readonly WindowSystem _windows = new("LazyWTMath");
    private readonly ChangelogGate _changelog;
    // Fork: Host the plugin-local probability window beside the shared changelog window.
    private readonly LazyWTMathWindow _mainWindow;

    public WondrousTailsSolverPlugin(IDalamudPluginInterface pluginInterface) {
        pluginInterface.Inject(this);

        // This plugin never writes pi.ConfigFile, so its existence cannot tell an update from a fresh
        // install. The config DIRECTORY is created by Dalamud for any plugin that has ever been loaded,
        // and the sidecar itself lives in it - treat "directory already there" as "already installed".
        var existingInstall = pluginInterface.ConfigFile.Exists || pluginInterface.ConfigDirectory.Exists;

        // Tip KamiToolKit has no sync Initialize (it was replaced by InitializeAsync). The plugin load
        // thread is a Task.Run thread, not the framework thread, so blocking here is safe: the
        // framework thread keeps pumping and completes the queued work next tick.
        KamiToolKitLibrary.InitializeAsync(pluginInterface).GetAwaiter().GetResult();

        System.PerfectTails = new PerfectTails();
        System.AddonWeeklyBingoController = new AddonWeeklyBingoController();

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

        // Fork: Add the refreshed read-only UI while retaining the changelog's existing update gate.
        _mainWindow = new LazyWTMathWindow(_changelog.ShowNow);
        _windows.AddWindow(_mainWindow);

        pluginInterface.UiBuilder.Draw += _windows.Draw;
        // Fork: Dalamud's Open actions show the probability window; its top bar retains manual changelog access.
        pluginInterface.UiBuilder.OpenMainUi += OpenMainWindow;
        pluginInterface.UiBuilder.OpenConfigUi += OpenMainWindow;

    }

    // Fork: Open the same single main window from either Dalamud entry point.
    private void OpenMainWindow() => _mainWindow.IsOpen = true;

    public void Dispose() {
        Pi.UiBuilder.Draw -= _windows.Draw;
        // Fork: Unsubscribe the refreshed window handlers during teardown.
        Pi.UiBuilder.OpenMainUi -= OpenMainWindow;
        Pi.UiBuilder.OpenConfigUi -= OpenMainWindow;
        System.AddonWeeklyBingoController.Dispose();
        _changelog.Dispose();
        _windows.RemoveAllWindows();
    }
}
