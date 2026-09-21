using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using Lalalazy.Changelog;

namespace LazyCrucible;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    private const string CommandName = "/lazycrucible";

    /// <summary>
    ///     The last GluttonyCombo that carried its own familiar writer (BST_CruciblePetSelect). With it loaded,
    ///     two plugins would write the same screens, so LazyCrucible stands down until GluttonyCombo is updated.
    /// </summary>
    private static readonly Version LastGluttonyWithPetSelect = new(1, 0, 4, 229);

    internal static Configuration Config { get; private set; } = null!;

    private readonly WindowSystem _windowSystem = new("LazyCrucible");
    private readonly MainWindow _window;
    private readonly ChangelogGate _changelog;
    private bool _wasBst;
    private DateTime _nextConflictCheck = DateTime.MinValue;

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);
        ECommonsMain.Init(pi, this);

        // Read BEFORE anything saves the config: tells the changelog gate "update" from "fresh install".
        var existingInstall = pi.ConfigFile.Exists;
        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();

        _window = new MainWindow(this);
        _windowSystem.AddWindow(_window);

        // Shared "What's new" popup (repo standing rule): shows this plugin's CHANGELOG once after an update.
        _changelog = new ChangelogGate(new ChangelogGate.Options
        {
            PluginAssembly = typeof(Plugin).Assembly,
            DisplayName = "LazyCrucible",
            ChangelogPath = "src/LazyCrucible/CHANGELOG.md",
            Framework = Framework,
            ClientState = ClientState,
            Condition = Condition,
            Log = PluginLog,
            Windows = _windowSystem,
            ExistingInstall = existingInstall,
            SeenStore = new DelegateSeenStore(
                () => Config.LastSeenChangelogVersion,
                v => { Config.LastSeenChangelogVersion = v; Config.Save(); }),
        });

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;
        Framework.Update += OnFrameworkUpdate;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open LazyCrucible. /lazycrucible changelog shows what's new.",
        });
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            var bst = Player.Available && Player.Job == Job.BST;
            if (!bst)
            {
                if (_wasBst)
                {
                    AgentProbe.Teardown();
                    ScreenRecorder.Stop();
                    PetSelect.ResetRun();
                }
                _wasBst = false;
                return;
            }
            _wasBst = true;

            CheckGluttonyConflict();
            AgentProbe.Ensure();
            ScreenRecorder.Tick(Config.RecordScreens);
            PetSelect.Tick();
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "framework update");
        }
    }

    private void CheckGluttonyConflict()
    {
        var now = DateTime.UtcNow;
        if (now < _nextConflictCheck)
            return;
        _nextConflictCheck = now.AddSeconds(10);

        var blocked = false;
        string? version = null;
        foreach (var p in PluginInterface.InstalledPlugins)
        {
            if (p.InternalName == "GluttonyCombo" && p.IsLoaded && p.Version <= LastGluttonyWithPetSelect)
            {
                blocked = true;
                version = p.Version.ToString();
                break;
            }
        }

        if (blocked == PetSelect.WritesBlocked)
            return;
        PetSelect.WritesBlocked = blocked;
        if (blocked)
        {
            CrucibleLog.Warning($"GluttonyCombo {version} also fills Crucible familiars; LazyCrucible is not writing until GluttonyCombo is updated.");
            Svc.Chat.PrintError($"[LazyCrucible] GluttonyCombo {version} also fills Crucible familiars. LazyCrucible will not change familiars until GluttonyCombo is updated.");
        }
    }

    private void ToggleWindow() => _window.IsOpen = !_window.IsOpen;

    private void OnCommand(string command, string args)
    {
        var a = args.Trim();
        if (a.Equals("changelog", StringComparison.OrdinalIgnoreCase) || a.Equals("whatsnew", StringComparison.OrdinalIgnoreCase))
        {
            _changelog.ShowNow();
            return;
        }
        ToggleWindow();
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        AgentProbe.Teardown();
        ScreenRecorder.Stop();
        PetSelect.Dispose();
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;
        _changelog.Dispose();
        _windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
        ECommonsMain.Dispose();
    }
}
