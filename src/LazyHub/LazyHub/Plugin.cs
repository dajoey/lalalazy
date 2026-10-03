using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using KamiToolKit;
using Lalalazy.Changelog;
using LazyHub.Core;

namespace LazyHub;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Lazy Hub";

    [PluginService] internal static IDalamudPluginInterface Pi { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;

    private const string CommandName = "/lazy";
    private const string AltCommandName = "/lazyhub";
    private const string DtrTitle = "Lazy Hub";

    private readonly Configuration _config;
    private readonly WindowSystem _windows = new("LazyHub");
    private readonly ChangelogGate _changelog;
    private readonly PluginMonitor _monitor;
    private readonly HubClient _client;
    private readonly IconLoader _icons;
    private readonly GluttonyProbe _probe;
    private NativeAddon _addon;
    private readonly IDtrBarEntry _dtr;
    private long _nextDtrUpdateTicks;

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);

        // Read BEFORE anything saves the config: tells the changelog gate "update" from "fresh install".
        var existingInstall = pi.ConfigFile.Exists;
        _config = pi.GetPluginConfig() as Configuration ?? new Configuration();

        KamiToolKitLibrary.Initialize(pi);

        _monitor = new PluginMonitor(pi);
        _client = new HubClient(pi, Log);
        _icons = new IconLoader(Textures, Framework, Log);
        _probe = new GluttonyProbe(pi, Log);
        _addon = BuildAddon();

        // Shared "What's new" popup: shows this plugin's CHANGELOG once after an update.
        _changelog = new ChangelogGate(new ChangelogGate.Options
        {
            PluginAssembly = typeof(Plugin).Assembly,
            DisplayName = "Lazy Hub",
            ChangelogPath = "src/LazyHub/CHANGELOG.md",
            Framework = Framework,
            ClientState = ClientState,
            Condition = Condition,
            Log = Log,
            Windows = _windows,
            ExistingInstall = existingInstall,
            SeenStore = new DelegateSeenStore(
                () => _config.LastSeenChangelogVersion,
                v => { _config.LastSeenChangelogVersion = v; Pi.SavePluginConfig(_config); }),
        });

        _dtr = DtrBar.Get(DtrTitle);
        _dtr.Tooltip = new SeString(new TextPayload("Lazy Hub: click to open the lalalazy window"));
        _dtr.OnClick = _ => _addon.Toggle();
        _dtr.Shown = true;
        UpdateDtr();

        Pi.UiBuilder.Draw += _windows.Draw;
        Pi.UiBuilder.OpenMainUi += OpenWindow;
        Pi.UiBuilder.OpenConfigUi += OpenWindow;
        Framework.Update += OnFrameworkUpdate;

        var info = new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the lalalazy window. /lazy safe switches to the plain window and back. /lazy changelog shows what's new.",
        };
        Commands.AddHandler(CommandName, info);
        Commands.AddHandler(AltCommandName, new CommandInfo(OnCommand) { HelpMessage = "Same as /lazy.", ShowInHelp = false });
    }

    private void OpenWindow() => _addon.Open();

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim();
        if (arg.Equals("changelog", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("whatsnew", StringComparison.OrdinalIgnoreCase))
        {
            _changelog.ShowNow();
            return;
        }
        if (arg.Equals("safe", StringComparison.OrdinalIgnoreCase))
        {
            SetSafeMode(!_config.SafeMode);
            return;
        }
        _addon.Toggle();
    }

    /// <summary>The full window, or the plain 0.1.0.1 window when safe mode is on.</summary>
    private NativeAddon BuildAddon() => _config.SafeMode
        ? new SafeHubAddon(_monitor, _probe, _icons)
        {
            InternalName = "LazyHub",
            Title = "lalalazy (safe mode)",
            Size = new Vector2(620f, 640f),
        }
        : new HubAddon(_monitor, _client, _icons, Textures, Log)
        {
            InternalName = "LazyHub",
            Title = "lalalazy",
            Size = new Vector2(780f, 690f),
        };

    private void SetSafeMode(bool on)
    {
        // The old window closes asynchronously, so it is not reopened here: the next /lazy opens the new one.
        _addon.Dispose();
        _config.SafeMode = on;
        Pi.SavePluginConfig(_config);
        _addon = BuildAddon();
        ChatGui.Print(on
            ? "[Lazy Hub] Safe mode ON: /lazy opens the plain window. /lazy safe switches back."
            : "[Lazy Hub] Safe mode OFF: /lazy opens the full window.");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // The bar text only needs to move when a plugin loads or unloads: once a second is plenty.
        var now = Environment.TickCount64;
        if (now < _nextDtrUpdateTicks) return;
        _nextDtrUpdateTicks = now + 1000;

        try { UpdateDtr(); }
        catch (Exception ex) { Log.Error(ex, "Lazy Hub bar update failed"); }
    }

    private void UpdateDtr()
    {
        var summary = PluginStatus.Summary(_monitor.Snapshot().Select(r => r.State));
        _dtr.Text = new SeString(new TextPayload($"lala {summary}"));
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        Pi.UiBuilder.Draw -= _windows.Draw;
        Pi.UiBuilder.OpenMainUi -= OpenWindow;
        Pi.UiBuilder.OpenConfigUi -= OpenWindow;

        Commands.RemoveHandler(CommandName);
        Commands.RemoveHandler(AltCommandName);

        _dtr.Remove();
        _addon.Dispose();
        KamiToolKitLibrary.Dispose();

        _changelog.Dispose();
        _windows.RemoveAllWindows();
    }
}
