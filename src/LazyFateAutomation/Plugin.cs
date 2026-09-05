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

    public Plugin(IDalamudPluginInterface pluginInterface) {
        P = this;
        ECommonsMain.Init(pluginInterface, this, ECommons.Module.DalamudReflector, ECommons.Module.ObjectFunctions);

        // Read BEFORE anything saves the config: tells the changelog gate "update" from "fresh install".
        var existingInstall = pluginInterface.ConfigFile.Exists;
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Initialize IPC and helper services
        Svc.Init(pluginInterface);
        Service.BossMod = new BossModIPC();
        Service.Navmesh = Svc.Navmesh; // Use the initialized Navmesh IPC from Svc
        Service.TextAdvance = new TextAdvanceIpc();
        Service.Gluttony = new GluttonyComboIPC();
        Service.Automation = new Automation();

        FateToolKit = new FateToolKit();
        FateToolKit.Enable();

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
    }

    public void Dispose() {
        Svc.Commands.RemoveHandler("/lazyfate");
        Svc.Commands.RemoveHandler("/vfate");
        
        _changelog?.Dispose();
        FateToolKit.Disable();
        Service.Automation.Stop();
        Service.Gluttony?.Release();

        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string arguments) {
        var a = arguments.Trim();
        if (a.Equals("changelog", StringComparison.OrdinalIgnoreCase) || a.Equals("whatsnew", StringComparison.OrdinalIgnoreCase)) {
            _changelog.ShowNow();
            return;
        }
        FateToolKit.OnCommand(command, arguments);
    }
}
