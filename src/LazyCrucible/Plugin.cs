using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using System.Text;
using Lalalazy.Changelog;
using Lalalazy.Telemetry;

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

    internal static Configuration Config { get; private set; } = null!;

    /// <summary> Shared error reporting (ER| lines, report button); null only before load / after unload, or if it failed to start. </summary>
    internal static DalamudTelemetry? Telemetry { get; private set; }

    private readonly WindowSystem _windowSystem = new("LazyCrucible");
    private readonly MainWindow _window;
    private readonly ChangelogGate _changelog;
    private readonly DalamudTelemetry? _telemetry;
    // Circuit breakers (src/Shared/LalaTelemetry): a handler that keeps throwing is skipped after repeated
    // failures (one ER|trip line + one chat notice) and retries on its own, 30 s doubling to 5 min.
    private readonly TelemetryGuard _tickGuard;
    private readonly TelemetryGuard _selectGuard;
    private readonly TelemetryGuard _recorderGuard;
    private bool _wasBst;
    private bool _gluttonyConflict;
    private DateTime _nextConflictCheck = DateTime.MinValue;

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);
        ECommonsMain.Init(pi, this);

        // Shared error reporting FIRST (after ECommons, which provides the services), so a failure anywhere
        // below is an ER| line carrying version, channel, commit and zone. Install never throws.
        _telemetry = DalamudTelemetry.Install(new DalamudTelemetry.Options
        {
            PluginInterface = pi,
            PluginAssembly = typeof(Plugin).Assembly,
            DisplayName = "LazyCrucible",
            Command = CommandName,
            Log = PluginLog,
            Framework = Framework,
            ClientState = ClientState,
            Condition = Condition,
            Chat = Svc.Chat,
            PlayerState = Svc.PlayerState,
            Objects = Svc.Objects,
            Targets = Svc.Targets,
            ConfigSummary = () => ConfigSummary.Describe(Config),
            StateSummary = TelemetryStateSummary,
            // The full AtkValues of every visible XBM screen, split into report addon records (bounded).
            ExtraAddons = () => CrucibleTelemetry.SplitCaptures(ScreenRecorder.VisibleXbmDumps()),
            RingCapacity = CrucibleTelemetry.RingCapacity,
        });
        Telemetry = _telemetry;
        _tickGuard = LalaTelemetry.CreateGuard("tick", "the per-frame update");
        _selectGuard = LalaTelemetry.CreateGuard("tick.select", "familiar selection");
        _recorderGuard = LalaTelemetry.CreateGuard("tick.recorder", "the screen recorder");
        CrucibleLog.CreateGuards();

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
            HelpMessage = "Open LazyCrucible. /lazycrucible changelog shows what's new. /lazycrucible report <what happened> writes a problem report to the plugin log.",
        });
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!_tickGuard.TryEnter())
            return;

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
            PetSelect.YieldReason = _gluttonyConflict ? "conflict_gluttony"
                : Config.YieldToAutoDuty && ExternalDrivers.AutoDutyRunning ? "autoduty_running"
                : null;
            AgentProbe.Ensure();

            // Own breakers, so a failing recorder can never stop familiar selection and the other way round.
            if (_recorderGuard.TryEnter())
            {
                try
                {
                    ScreenRecorder.Tick(Config.RecordScreens);
                }
                catch (Exception ex)
                {
                    _recorderGuard.Failed(ex);
                }
            }

            if (_selectGuard.TryEnter())
            {
                try
                {
                    PetSelect.Tick();
                }
                catch (Exception ex)
                {
                    _selectGuard.Failed(ex);
                }
            }
        }
        catch (Exception ex)
        {
            _tickGuard.Failed(ex);
        }
    }

    /// <summary>
    ///     What the plugin thinks it is doing, for a report's state section: on Beastmaster, why it is standing
    ///     down (if it is), the last selection summary, and how much of its telemetry reached the report ring.
    /// </summary>
    private string TelemetryStateSummary()
    {
        var last = PetSelect.LastSummary.Replace(';', ',');
        if (last.Length > 300)
            last = last[..300];
        return new StringBuilder(512)
            .Append("bst=").Append(_wasBst ? 1 : 0)
            .Append(";yield=").Append(PetSelect.YieldReason ?? "none")
            .Append(";gluttonyConflict=").Append(_gluttonyConflict ? 1 : 0)
            .Append(";ringKept=").Append(CrucibleLog.Ring.Kept)
            .Append(";ringSkipped=").Append(CrucibleLog.Ring.Skipped)
            .Append(";lastAt=").Append(PetSelect.LastSummaryAt == default ? "" : PetSelect.LastSummaryAt.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture))
            .Append(";last=").Append(last)
            .ToString();
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
            if (p.InternalName == "GluttonyCombo" && p.IsLoaded && FormationLogic.GluttonyStillWritesFamiliars(p.Version))
            {
                blocked = true;
                version = p.Version.ToString();
                break;
            }
        }

        if (blocked == _gluttonyConflict)
            return;
        _gluttonyConflict = blocked;
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
        // `/lazycrucible report <what happened>`: the text is kept exactly as typed.
        if (a.Equals("report", StringComparison.OrdinalIgnoreCase) || a.StartsWith("report ", StringComparison.OrdinalIgnoreCase))
        {
            if (_telemetry is null)
                Svc.Chat.PrintError("[LazyCrucible] Problem reports are unavailable this session: error reporting did not start (the reason is in the plugin log).");
            else
                _telemetry.FileReport(a.Length > 6 ? a[6..].Trim() : string.Empty);
            return;
        }
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
        // Last (before ECommons, which owns the services it writes through): anything that failed while
        // tearing down above was still reported.
        _telemetry?.Dispose();
        Telemetry = null;
        ECommonsMain.Dispose();
    }
}
