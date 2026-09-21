// Shared source (NOT a shared DLL) - the thin Dalamud adapter over Core/. Compiled into each plugin that
// opts in (csproj: <Compile Include="..\Shared\LalaTelemetry\**\*.cs" .../> + the Stamp.targets import).
// Needs AllowUnsafeBlocks (the visible-addon dump reads AtkUnitBase).
//
// Wiring, FIRST thing in the plugin constructor (so nothing that throws during load is missed); Install never
// throws and returns null if it could not start:
//   _telemetry = DalamudTelemetry.Install(new DalamudTelemetry.Options {
//       PluginInterface = pi, PluginAssembly = typeof(Plugin).Assembly, DisplayName = "My Plugin", Command = "/myplugin",
//       Log = Log, Framework = Framework, ClientState = ClientState, Condition = Condition, Chat = ChatGui,
//       PlayerState = PlayerState, Objects = Objects, Targets = Targets,
//       ConfigSummary = () => ConfigSummary.Describe(Configuration), StateSummary = () => "...",
//   });
//   `/myplugin report <text>` -> _telemetry?.FileReport(text);   a window: _telemetry?.DrawReportButton();
//   Dispose -> _telemetry.Dispose() LAST, after everything else has been torn down.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Lalalazy.Telemetry;

/// <summary>
/// Installs a <see cref="TelemetryHub"/> for one plugin load: identity (version, testing/production channel
/// from Dalamud, build commit), log + chat sinks, a game-state reader, unobserved-task capture and the
/// problem-report command / button.
/// </summary>
public sealed class DalamudTelemetry : IDisposable
{
    public sealed class Options
    {
        public required IDalamudPluginInterface PluginInterface { get; init; }
        public required Assembly PluginAssembly { get; init; }

        /// <summary>Player-facing name used in notices, e.g. "Gluttony Combo".</summary>
        public required string DisplayName { get; init; }

        /// <summary>The plugin's main slash command, e.g. "/gluttony" (the notices point at "&lt;cmd&gt; report").</summary>
        public required string Command { get; init; }

        public required IPluginLog Log { get; init; }
        public required IFramework Framework { get; init; }
        public required IClientState ClientState { get; init; }
        public required ICondition Condition { get; init; }
        public required IChatGui Chat { get; init; }
        public IPlayerState? PlayerState { get; init; }
        public IObjectTable? Objects { get; init; }
        public ITargetManager? Targets { get; init; }

        /// <summary>Compact config summary for a report (e.g. <c>() =&gt; ConfigSummary.Describe(config)</c>).</summary>
        public Func<string>? ConfigSummary { get; init; }

        /// <summary>Plugin-specific live state for a report (what the automation thinks it is doing).</summary>
        public Func<string>? StateSummary { get; init; }

        public bool CaptureUnobservedTasks { get; init; } = true;
        public int RingCapacity { get; init; } = 200;
    }

    /// <summary>How often the cached game context (used off the framework thread) is refreshed.</summary>
    private const long ContextRefreshMs = 1_000;

    private readonly Options _o;
    private readonly GameStateReader _state;
    private readonly UnobservedTaskWatcher? _unobserved;
    private readonly ReportPopup _popup;
    private readonly AssemblyLoadContext? _alc;
    private long _nextRefreshMs;
    private bool _disposed;

    private DalamudTelemetry(Options o)
    {
        _o = o;
        _state = new GameStateReader(o.Framework, o.ClientState, o.Condition, o.PlayerState, o.Objects, o.Targets);

        var identity = new TelemetryIdentity
        {
            Plugin = SafeInternalName(o),
            DisplayName = o.DisplayName,
            Version = BuildStamp.VersionOf(o.PluginAssembly),
            Channel = ChannelOf(o.PluginInterface),
            Commit = BuildStamp.CommitOf(o.PluginAssembly),
            Command = o.Command,
        };

        Hub = new TelemetryHub(identity, new Sink(o.Log, o.Chat), () => _state.Context(), ringCapacity: o.RingCapacity);
        LalaTelemetry.Hub = Hub;

        if (o.CaptureUnobservedTasks)
            _unobserved = new UnobservedTaskWatcher(o.PluginAssembly, ex => Hub.Unobserved(ex));

        _popup = new ReportPopup(this);
        o.Framework.Update += OnFrameworkUpdate;

        // Safety net for a constructor that throws AFTER this install: Dalamud never calls the plugin's
        // Dispose then, and the process-wide TaskScheduler subscription above would pin the failed load's
        // AssemblyLoadContext forever. Unloading fires when Dalamud unloads that context.
        _alc = AssemblyLoadContext.GetLoadContext(o.PluginAssembly);
        if (_alc is not null && _alc != AssemblyLoadContext.Default)
            _alc.Unloading += OnUnloading;

        o.Log.Information("{Line:l}", new TelemetryLineBuilder("ER|", Hub.UnixMs(), "start")
            .Field("p", identity.Plugin, 64)
            .Field("v", identity.Version, 32)
            .Field("ch", identity.Channel, 16)
            .Field("c", identity.Commit, 40)
            .ToString());
    }

    public TelemetryHub Hub { get; }

    /// <summary>
    /// Installs telemetry for this load. Never throws: error reporting must not be the reason a plugin fails
    /// to load - on failure it logs why and returns null, and every <see cref="LalaTelemetry"/> call stays a
    /// no-op (guards still break circuits, they just write nothing).
    /// </summary>
    public static DalamudTelemetry? Install(Options options)
    {
        try
        {
            return new DalamudTelemetry(options);
        }
        catch (Exception ex)
        {
            try
            {
                LalaTelemetry.Hub = null;
                options.Log.Error(ex, "[LalaTelemetry] install failed; error reporting is off for this load");
            }
            catch
            {
                // Nothing left to report through.
            }
            return null;
        }
    }

    /// <summary>"dev" / "testing" / "production" as Dalamud reports this load (IDalamudPluginInterface.IsDev / IsTesting).</summary>
    public static string ChannelOf(IDalamudPluginInterface pi)
    {
        try
        {
            if (pi.IsDev)
                return "dev";
            return pi.IsTesting ? "testing" : "production";
        }
        catch
        {
            // Older/odd hosts: the channel is informational, never worth failing a load over.
            return "unknown";
        }
    }

    private static string SafeInternalName(Options o)
    {
        try
        {
            return o.PluginInterface.InternalName;
        }
        catch
        {
            return o.PluginAssembly.GetName().Name ?? "plugin";
        }
    }

    private void OnUnloading(AssemblyLoadContext context) => Dispose();

    private void OnFrameworkUpdate(IFramework framework)
    {
        // One comparison per frame; once a second: refresh the cached context and flush due summaries.
        var now = Environment.TickCount64;
        if (_disposed || now < _nextRefreshMs)
            return;
        _nextRefreshMs = now + ContextRefreshMs;
        _state.Refresh();
        Hub.Pump();
    }

    /// <summary>
    /// Writes an RP| problem report and prints its id in chat. Call from the framework / UI thread (it reads
    /// the game state and the visible addons). Returns the id, or null when rate-limited.
    /// </summary>
    public string? FileReport(string text)
    {
        if (_disposed)
            return null;

        if (!Hub.CanReport(out var waitMs))
        {
            _o.Chat.PrintError($"[{_o.DisplayName}] A problem report was just written; the next one is possible in {Math.Max(1, (waitMs + 999) / 1000)} s.");
            return null;
        }

        string? id = null;
        try
        {
            id = Hub.WriteReport(reportId => Collect(reportId, text));
        }
        catch (Exception ex)
        {
            Hub.Error("report", ex);
        }

        if (id is null)
        {
            _o.Chat.PrintError($"[{_o.DisplayName}] The problem report could not be written; the reason is in the plugin log.");
            return null;
        }

        _o.Chat.Print($"[{_o.DisplayName}] Problem report {id} written to the plugin log.");
        return id;
    }

    private ReportInput Collect(string id, string text)
    {
        var notes = new List<string>();

        var game = Try(notes, "game", () => _state.Snapshot(), GameContext.Empty);
        var state = Try(notes, "state", () => _o.StateSummary?.Invoke() ?? string.Empty, string.Empty);
        var config = Try(notes, "config", () => _o.ConfigSummary?.Invoke() ?? string.Empty, string.Empty);
        var addons = Try(notes, "addons", () => AddonDump.Capture(), AddonDump.Result.Empty);

        return Hub.BaseInput(id, text?.Trim() ?? string.Empty, game, state, config, addons.Visible, addons.Details, notes);
    }

    private static T Try<T>(List<string> notes, string section, Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            // A section that cannot be read must not cost the whole report; the note line says which and why.
            notes.Add($"{section}: {ex.GetType().Name}: {ex.Message}");
            return fallback;
        }
    }

    /// <summary>A small "Report a problem" button; opens a popup for the free text. Call inside a window's Draw.</summary>
    public void DrawReportButton(string label = "Report a problem") => _popup.DrawButton(label);

    /// <summary>For a tab bar: a trailing tab-style button that opens the same popup.</summary>
    public void DrawReportTabButton(string label = "Report a problem") => _popup.DrawTabButton(label);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_alc is not null && _alc != AssemblyLoadContext.Default)
            _alc.Unloading -= OnUnloading;
        _o.Framework.Update -= OnFrameworkUpdate;
        _unobserved?.Dispose();
        Hub.Dispose();
        if (ReferenceEquals(LalaTelemetry.Hub, Hub))
            LalaTelemetry.Hub = null;
    }

    /// <summary>IPluginLog for lines, chat for notices. "{Line:l}" keeps braces in messages from being read as a template.</summary>
    private sealed class Sink : ITelemetrySink
    {
        private readonly IPluginLog _log;
        private readonly IChatGui _chat;

        public Sink(IPluginLog log, IChatGui chat)
        {
            _log = log;
            _chat = chat;
        }

        public void Write(TelemetryLevel level, string text)
        {
            switch (level)
            {
                case TelemetryLevel.Error: _log.Error("{Line:l}", text); break;
                case TelemetryLevel.Warning: _log.Warning("{Line:l}", text); break;
                default: _log.Information("{Line:l}", text); break;
            }
        }

        public void Notice(string text) => _chat.PrintError(text);
    }
}

/// <summary>
/// Reads the game state for ER| lines and reports. Off the framework thread (task continuations, the
/// finalizer thread) it never touches game memory: it returns the context cached by the last framework-thread
/// read, marked <c>src=cached</c>.
/// </summary>
internal sealed class GameStateReader
{
    private readonly IFramework _framework;
    private readonly IClientState _client;
    private readonly ICondition _condition;
    private readonly IPlayerState? _player;
    private readonly IObjectTable? _objects;
    private readonly ITargetManager? _targets;
    private volatile GameContext _cached = GameContext.Empty;

    public GameStateReader(IFramework framework, IClientState client, ICondition condition, IPlayerState? player, IObjectTable? objects, ITargetManager? targets)
    {
        _framework = framework;
        _client = client;
        _condition = condition;
        _player = player;
        _objects = objects;
        _targets = targets;
    }

    /// <summary>The ER| context: live on the framework thread, cached elsewhere.</summary>
    public GameContext Context()
    {
        if (!OnFrameworkThread())
            return _cached with { Source = _cached.Source == "none" ? "none" : "cached" };
        return Refresh();
    }

    /// <summary>Reads the light context and caches it. Framework thread only.</summary>
    public GameContext Refresh()
    {
        if (!OnFrameworkThread())
            return _cached;
        try
        {
            var ctx = ReadLight();
            _cached = ctx;
            return ctx;
        }
        catch
        {
            // Mid zone-change reads can fail; the previous context is the best available answer.
            return _cached;
        }
    }

    /// <summary>The full report snapshot (position, target, every set condition). Framework thread only.</summary>
    public GameContext Snapshot()
    {
        var ctx = OnFrameworkThread() ? ReadLight() : _cached with { Source = "cached" };
        if (!OnFrameworkThread())
            return ctx;

        var abbr = string.Empty;
        if (_player is not null && _player.IsLoaded)
            abbr = _player.ClassJob.ValueNullable?.Abbreviation.ExtractText() ?? string.Empty;

        float? x = null, y = null, z = null;
        if (_objects?.LocalPlayer is { } me)
        {
            x = me.Position.X;
            y = me.Position.Y;
            z = me.Position.Z;
        }

        var target = string.Empty;
        if (_targets?.Target is { } t)
        {
            // Never a player's name in a report: other players are "pc".
            var name = t.ObjectKind == ObjectKind.Pc ? "pc" : t.Name.TextValue;
            target = $"{t.ObjectKind}:{t.BaseId}:{t.EntityId}:{name}";
        }

        var conditions = _condition.AsReadOnlySet().Select(f => f.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        return ctx with { JobAbbr = abbr, X = x, Y = y, Z = z, Target = target, Conditions = conditions };
    }

    /// <summary>Numeric ids and flags only (runs for every ER| occurrence, including suppressed ones).</summary>
    private GameContext ReadLight()
    {
        uint job = 0;
        var level = 0;
        if (_player is not null && _player.IsLoaded)
        {
            job = _player.ClassJob.RowId;
            level = _player.Level;
        }

        return new GameContext
        {
            TerritoryId = _client.TerritoryType,
            ClassJobId = job,
            Level = level,
            InCombat = _condition[ConditionFlag.InCombat],
            BoundByDuty = _condition[ConditionFlag.BoundByDuty] || _condition[ConditionFlag.BoundByDuty56] || _condition[ConditionFlag.BoundByDuty95],
            LoggedIn = _client.IsLoggedIn,
            Source = "live",
            CapturedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    private bool OnFrameworkThread()
    {
        try
        {
            return _framework.IsInFrameworkUpdateThread;
        }
        catch
        {
            return false;
        }
    }
}
