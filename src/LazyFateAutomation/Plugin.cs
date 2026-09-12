using Dalamud.Plugin;
using ECommons;
using ECommons.SimpleGui;
using ECommons.EzIpcManager;
using Lalalazy.Changelog;
using LazyFateAutomation.Helpers.IPC;
using LazyFateAutomation.Helpers.Services;
using LazyFateAutomation.Helpers.Internal;

namespace LazyFateAutomation;

public class Plugin : IDalamudPlugin {
    public static string Name => "Lazy Fate Automation";
    public static Plugin P { get; private set; } = null!;
    public static Configuration Config { get; private set; } = null!;
    public static FateToolKit FateToolKit { get; private set; } = null!;
    public static FateToolKitWindow Window { get; private set; } = null!;
    private ChangelogGate _changelog = null!;
    private readonly FateSnapshotService _fateSnapshot = new();
    private readonly FateSnapshotServer _fateHttp;


    public Plugin(IDalamudPluginInterface pluginInterface) {
        P = this;
        ECommonsMain.Init(pluginInterface, this, ECommons.Module.DalamudReflector, ECommons.Module.ObjectFunctions);

        // Read BEFORE anything saves the config: tells the changelog gate "update" from "fresh install".
        var existingInstall = pluginInterface.ConfigFile.Exists;
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Initialize IPC and helper services
        Svc.Init(pluginInterface);

        // One-time cleanup for configs that grew duplicate SortOrder entries before 0.0.3.1's
        // [JsonProperty(ObjectCreationHandling.Replace)] fix (see Configuration.cs). Must run
        // after Svc.Init() - Config.Save() calls Svc.PluginInterface.SavePluginConfig.
        if (Config.DedupeSortOrder())
            Config.Save();

        Service.BossMod = new BossModIPC();
        Service.Navmesh = Svc.Navmesh; // Use the initialized Navmesh IPC from Svc
        Service.TextAdvance = new TextAdvanceIpc();
        Service.Gluttony = new GluttonyComboIPC();
        Service.Automation = new Automation();

        FateToolKit = new FateToolKit();
        FateToolKit.Enable();
        _fateHttp = new FateSnapshotServer(() => _fateSnapshot.Current);


        Window = new FateToolKitWindow(FateToolKit);
        
        EzConfigGui.Init(Window, nameOverride: Name);

        // Shared "What's new" popup (repo standing rule): shows this plugin's CHANGELOG once after an
        // update. This plugin has no WindowSystem of its own - EzConfigGui.Init (above) creates one and
        // hooks its Draw, so the changelog window rides on that. Must stay AFTER EzConfigGui.Init.
        _changelog = new ChangelogGate(new ChangelogGate.Options
        {
            PluginAssembly = typeof(Plugin).Assembly,
            DisplayName = "Lazy Fate Automation",
            ChangelogPath = "src/LazyFateAutomation/CHANGELOG.md",
            Framework = Svc.Framework,
            ClientState = Svc.ClientState,
            Condition = Svc.Condition,
            Log = Svc.Log,
            Windows = EzConfigGui.WindowSystem,
            ExistingInstall = existingInstall,
            SeenStore = new DelegateSeenStore(
                () => Config.LastSeenChangelogVersion,
                v => { Config.LastSeenChangelogVersion = v; Config.Save(); }),
        });

        // Standalone commands
        Svc.Commands.AddHandler("/lazyfate", new Dalamud.Game.Command.CommandInfo(OnCommand) {
            HelpMessage = "Opens the Lazy Fate Automation UI. /lazyfate changelog shows what's new.",
            ShowInHelp = true
        });
        Svc.Commands.AddHandler("/vfate", new Dalamud.Game.Command.CommandInfo(OnCommand) {
            HelpMessage = "Alias for /lazyfate",
            ShowInHelp = false
        });
        Svc.Framework.Update += OnFrameworkUpdateSnapshot;

    }

    private void OnFrameworkUpdateSnapshot(Dalamud.Plugin.Services.IFramework framework) {
        try {
            _fateSnapshot.Tick();
            _fateHttp.EnsureStarted();
        } catch (Exception ex) {
            Svc.Log.Error(ex, "LazyFateAutomation snapshot tick failed");
        }
    }

    public void Dispose() {
        Svc.Commands.RemoveHandler("/lazyfate");
        Svc.Commands.RemoveHandler("/vfate");
        
        _changelog?.Dispose();
        FateToolKit.Disable();
        Service.Automation.Stop();
        Service.Gluttony?.Release();

        Svc.Framework.Update -= OnFrameworkUpdateSnapshot;
        _fateHttp?.Dispose();

        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string arguments) {
        var a = arguments.Trim();
        if (a.Equals("changelog", StringComparison.OrdinalIgnoreCase) || a.Equals("whatsnew", StringComparison.OrdinalIgnoreCase)) {
            _changelog.ShowNow();
            return;
        }
        if (a.Equals("snapshot", StringComparison.OrdinalIgnoreCase)) {
            var s = _fateSnapshot.Current;
            Svc.Log.Information("LazyFateAutomation snapshot: " + (s == null
                ? "none"
                : $"{s.Char}@{s.World} zone={s.ZoneId} fates={s.Fates.Count} hunt={(s.HuntTarget ?? "none")} bills={s.BillsUnlocked}/{s.BillKills} cleared={s.Cleared} http='{_fateHttp.LastError}'"));
            return;
        }
        FateToolKit.OnCommand(command, arguments);
    }
}
