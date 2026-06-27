using Dalamud.Plugin;
using ECommons;
using ECommons.SimpleGui;
using ECommons.EzIpcManager;
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

    public Plugin(IDalamudPluginInterface pluginInterface) {
        P = this;
        ECommonsMain.Init(pluginInterface, this, ECommons.Module.DalamudReflector, ECommons.Module.ObjectFunctions);

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

        // Standalone commands
        Svc.Commands.AddHandler("/lazyfate", new Dalamud.Game.Command.CommandInfo(OnCommand) {
            HelpMessage = "Opens the Lazy Fate Automation UI",
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
        
        FateToolKit.Disable();
        Service.Automation.Stop();
        Service.Gluttony?.Release();

        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string arguments) {
        FateToolKit.OnCommand(command, arguments);
    }
}
