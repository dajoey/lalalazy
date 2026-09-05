using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lalalazy.Changelog;

namespace AutoPotion;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "AutoPotion";

    [PluginService] internal static IDalamudPluginInterface Pi { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/autopotion";

    public Configuration Config { get; }

    private readonly PotionService _service;
    private readonly WindowSystem _windows = new("AutoPotion");
    private readonly ConfigWindow _configWindow;
    private readonly ChangelogGate _changelog;

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);

        // Read BEFORE anything saves the config: tells the changelog gate "update" from "fresh install".
        var existingInstall = pi.ConfigFile.Exists;
        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        Config.MigrateIfNeeded();
        _service = new PotionService(this);

        _configWindow = new ConfigWindow(this);
        _windows.AddWindow(_configWindow);

        // Shared "What's new" popup (repo standing rule): shows this plugin's CHANGELOG once after an update.
        _changelog = new ChangelogGate(new ChangelogGate.Options
        {
            PluginAssembly = typeof(Plugin).Assembly,
            DisplayName = "AutoPotion",
            ChangelogPath = "src/AutoPotion/CHANGELOG.md",
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
            HelpMessage = "Open the AutoPotion settings window. /autopotion changelog shows what's new, /autopotion telemetry toggles the diagnostic log."
        });
    }

    public void SaveConfig() => Pi.SavePluginConfig(Config);

    private void OpenConfig() => _configWindow.IsOpen = true;

    private void OnFrameworkUpdate(IFramework framework)
    {
        try { _service.Tick(); }
        catch (Exception ex) { Log.Error(ex, "AutoPotion tick failed"); }
    }

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim();
        if (arg.Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            _service.LogDebugState();
            return;
        }
        if (arg.Equals("changelog", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("whatsnew", StringComparison.OrdinalIgnoreCase))
        {
            _changelog.ShowNow();
            return;
        }
        if (arg.StartsWith("telemetry", StringComparison.OrdinalIgnoreCase))
        {
            HandleTelemetryCommand(arg["telemetry".Length..].Trim());
            return;
        }
        _configWindow.IsOpen = !_configWindow.IsOpen;
    }

    /// <summary>
    ///     <c>/autopotion telemetry [on|off|toggle|status]</c> — the off-by-default
    ///     decision tap behind <see cref="Configuration.DecisionTelemetry"/>
    ///     (mirrors GluttonyCombo's <c>/gluttony telemetry</c>).
    /// </summary>
    private void HandleTelemetryCommand(string sub)
    {
        var current = Config.DecisionTelemetry;
        bool? wanted = sub.ToLowerInvariant() switch
        {
            "on" or "enable" or "1" => true,
            "off" or "disable" or "0" => false,
            "toggle" => !current,
            _ => null,
        };

        if (wanted is null)
        {
            if (sub.Length > 0 && !sub.Equals("status", StringComparison.OrdinalIgnoreCase))
                ChatGui.Print("[AutoPotion] Usage: /autopotion telemetry <on|off|toggle|status>");
            ChatGui.Print($"[AutoPotion] Decision telemetry is {(current ? "ON" : "OFF")} " +
                          $"(diagnostic lines starting with {PotionTelemetry.Prefix} in the plugin log).");
            return;
        }

        if (wanted.Value != current)
        {
            Config.DecisionTelemetry = wanted.Value;
            SaveConfig();
            // Toggling on forgets the last near-miss so the current state is reported
            // immediately instead of being deduplicated against a stale pre-toggle one.
            if (wanted.Value) PotionTelemetry.Reset();
        }

        ChatGui.Print($"[AutoPotion] Decision telemetry {(wanted.Value ? "ON" : "OFF")}.");
        Log.Information($"AutoPotion decision telemetry {(wanted.Value ? "ON" : "OFF")}.");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        Pi.UiBuilder.Draw -= _windows.Draw;
        Pi.UiBuilder.OpenConfigUi -= OpenConfig;
        Pi.UiBuilder.OpenMainUi -= OpenConfig;
        _changelog.Dispose();
        _windows.RemoveAllWindows();
        Commands.RemoveHandler(CommandName);
    }
}
