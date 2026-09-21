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

    /// <summary> The per-fight information repository (embedded CrucibleGuide.json). </summary>
    internal static CrucibleGuide Guide { get; private set; } = CrucibleGuide.Parse("{}");

    private readonly WindowSystem _windowSystem = new("LazyCrucible");
    private readonly MainWindow _window;
    private readonly GuideWindow _guideWindow;
    private readonly ChangelogGate _changelog;
    private readonly DalamudTelemetry? _telemetry;
    // Circuit breakers (src/Shared/LalaTelemetry): a handler that keeps throwing is skipped after repeated
    // failures (one ER|trip line + one chat notice) and retries on its own, 30 s doubling to 5 min.
    private readonly TelemetryGuard _tickGuard;
    private readonly TelemetryGuard _selectGuard;
    private readonly TelemetryGuard _recorderGuard;
    private readonly TelemetryGuard _screensGuard;
    private readonly TelemetryGuard _overlayGuard;
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
        _screensGuard = LalaTelemetry.CreateGuard("tick.screens", "the selection screens (feed, shop, spoils, campsite, treasure)");
        _overlayGuard = LalaTelemetry.CreateGuard("overlay.draw", "the suggestion frames over Crucible screens");
        CrucibleLog.CreateGuards();

        // Read BEFORE anything saves the config: tells the changelog gate "update" from "fresh install".
        var existingInstall = pi.ConfigFile.Exists;
        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();

        Guide = LoadGuide();
        _window = new MainWindow(this);
        _windowSystem.AddWindow(_window);
        _guideWindow = new GuideWindow();
        _windowSystem.AddWindow(_guideWindow);

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
        PluginInterface.UiBuilder.Draw += DrawOverlay;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;
        Framework.Update += OnFrameworkUpdate;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open LazyCrucible. /lazycrucible guide opens the fight guide; /lazycrucible changelog shows what's new; /lazycrucible report <what happened> writes a problem report to the plugin log.",
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
                    SelectionScreens.Reset();
                    RunTracker.Reset();
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

            // Own breakers, so a failing recorder, familiar selection or selection screen can never stop the
            // others.
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

            // The selection screens (Beast Feed picker, shop, spoils, campsite, treasure) together with the prompt
            // texts and the run tracker they decide from: one breaker, so a failing tracker also stops the inputs
            // that would read it. A failure clears any input in flight (SelectionScreens.Tick) before it lands here.
            if (_screensGuard.TryEnter())
            {
                try
                {
                    PromptTexts.Load();
                    RunTracker.Tick();
                    SelectionScreens.Tick(PetSelect.YieldReason);
                }
                catch (Exception ex)
                {
                    _screensGuard.Failed(ex);
                }
            }

            _guideWindow.FollowFocus();
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

    /// <summary> The suggestion frames over Crucible screens, under their own breaker (draw-only). </summary>
    private void DrawOverlay()
    {
        if (!_overlayGuard.TryEnter())
            return;
        try
        {
            SuggestionOverlay.Draw();
        }
        catch (Exception ex)
        {
            _overlayGuard.Failed(ex);
        }
    }

    internal void ToggleGuide() => _guideWindow.IsOpen = !_guideWindow.IsOpen;

    /// <summary> The embedded fight guide; an unreadable file leaves an empty guide and one error line. </summary>
    private static CrucibleGuide LoadGuide()
    {
        try
        {
            using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("CrucibleGuide.json");
            if (stream is null)
                return CrucibleGuide.Parse("{}");
            using var reader = new StreamReader(stream);
            var guide = CrucibleGuide.Parse(reader.ReadToEnd());
            var problems = guide.Validate();
            if (problems.Count > 0)
                CrucibleLog.Warning($"Fight guide: {problems.Count} problem(s), first: {problems[0]}");
            return guide;
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "fight guide");
            return CrucibleGuide.Parse("{}");
        }
    }

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
        if (a.Equals("guide", StringComparison.OrdinalIgnoreCase))
        {
            _guideWindow.IsOpen = !_guideWindow.IsOpen;
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
        PluginInterface.UiBuilder.Draw -= DrawOverlay;
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
