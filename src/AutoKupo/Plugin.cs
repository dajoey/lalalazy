using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.IoC;

namespace AutoKupo;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/autokupo";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    public Configuration Configuration { get; init; }
    public readonly WindowSystem WindowSystem = new("AutoKupo");

    private readonly ConfigWindow ConfigWindow;
    private readonly StateMachine StateMachine;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(Configuration);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle Kupo of Fortune auto-turnin. /autokupo [on|off|stop]"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        StateMachine = new StateMachine(Configuration);

        Log.Information("AutoKupo loaded. Stand near Lizbeth in the Firmament and type /autokupo.");
    }

    public void Dispose()
    {
        StateMachine.Dispose();
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim().ToLowerInvariant();

        switch (arg)
        {
            case "on":
            case "start":
            case "":
                Configuration.Enabled = true;
                Configuration.Save();
                StateMachine.Start();
                ChatGui.Print("[AutoKupo] Started. Scanning for Lizbeth...");
                break;

            case "off":
            case "stop":
                Configuration.Enabled = false;
                Configuration.Save();
                StateMachine.Stop();
                ChatGui.Print("[AutoKupo] Stopped.");
                break;

            default:
                ChatGui.PrintError($"[AutoKupo] Unknown argument: {arg}. Use on/off/stop.");
                break;
        }
    }

    private void ToggleConfigUi() => ConfigWindow.Toggle();
}
