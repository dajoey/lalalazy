// Shared source (NOT a shared DLL). Core/ is Dalamud-free.
using System;
using System.Collections.Generic;
using System.Threading;

namespace Lalalazy.Telemetry;

/// <summary>
/// One per plugin load. Turns exceptions into rate-limited <c>ER|</c> lines, keeps the always-on ring
/// buffers, owns the guards' trip / recover notices and builds <c>RP|</c> reports. Thread-safe: errors
/// arrive from the framework thread, task continuations and the finalizer thread.
/// </summary>
public sealed class TelemetryHub : IDisposable
{
    public const string ErrorPrefix = "ER|";

    public const int MaxMessageChars = 512;
    public const int MaxDetailChars = 300;
    public const int MaxStackChars = 8000;

    /// <summary>A ring copy of an ER| line is cut to this many characters (the full line is in the error ring).</summary>
    public const int RingErrorChars = 300;

    /// <summary>Shortest gap between two reports (a report is a large one-shot block).</summary>
    public const long ReportCooldownMs = 10_000;

    private readonly object _gate = new();
    private readonly ITelemetrySink _sink;
    private readonly Func<GameContext> _context;
    private readonly Func<long> _monotonicMs;
    private readonly Func<long> _unixMs;
    private readonly List<TelemetryGuard> _guards = new();
    private long _lastReportMs = long.MinValue;
    private bool _disposed;

    public TelemetryHub(
        TelemetryIdentity identity,
        ITelemetrySink sink,
        Func<GameContext>? context = null,
        Func<long>? monotonicMs = null,
        Func<long>? unixMs = null,
        int ringCapacity = 200,
        int errorCapacity = 32,
        RateLimiter? limiter = null)
    {
        Identity = identity;
        _sink = sink;
        _context = context ?? (() => GameContext.Empty);
        _monotonicMs = monotonicMs ?? (() => Environment.TickCount64);
        _unixMs = unixMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Lines = new RingBuffer(ringCapacity);
        Errors = new RingBuffer(errorCapacity);
        Limiter = limiter ?? new RateLimiter();
    }

    public TelemetryIdentity Identity { get; }

    /// <summary>Last ~200 telemetry lines (every XX| line the plugin formatted, plus ER| heads and notices).</summary>
    public RingBuffer Lines { get; }

    /// <summary>Last ~32 full ER| lines.</summary>
    public RingBuffer Errors { get; }

    public RateLimiter Limiter { get; }

    public long MonotonicMs() => _monotonicMs();

    public long UnixMs() => _unixMs();

    /// <summary>Guards created through this hub (for a report's state section).</summary>
    public IReadOnlyList<TelemetryGuard> Guards
    {
        get { lock (_gate) return _guards.ToArray(); }
    }

    /// <summary>Keeps a line that the plugin already formatted. Never logs it; never formats anything.</summary>
    public void Record(string? line)
    {
        if (line is null || _disposed)
            return;
        Lines.Add(_unixMs(), line);
    }

    /// <summary>
    /// A failure caught at a top-level catch point (ERR). Returns true when a full line was written.
    /// <paramref name="detail"/> (optional, <c>d=</c>) is free context such as an item id; it is not part of
    /// the fingerprint, so it never splits a group.
    /// </summary>
    public bool Error(string area, Exception ex, string? detail = null) => Write("error", TelemetryLevel.Error, area, ex, detail);

    /// <summary>A failure deliberately caught and continued past (WRN, rate-limited like everything else).</summary>
    public bool Swallowed(string area, Exception ex, string? detail = null) => Write("swallowed", TelemetryLevel.Warning, area, ex, detail);

    /// <summary>An unobserved task exception that involves this plugin's assembly (ERR).</summary>
    public bool Unobserved(Exception ex) => Write("unobserved", TelemetryLevel.Error, "task.unobserved", ex, null);

    private sealed record Occurrence(string Kind, TelemetryLevel Level, string Area, string Type, string Message, GameContext Context);

    private bool Write(string kind, TelemetryLevel level, string area, Exception? ex, string? detail)
    {
        if (ex is null || _disposed)
            return false;

        try
        {
            var now = _monotonicMs();
            var unix = _unixMs();
            var fp = ExceptionFingerprint.Compute(Identity.Plugin, area, ex);
            var ctx = SafeContext();
            var occ = new Occurrence(kind, level, area, ex.GetType().FullName ?? ex.GetType().Name, ex.Message, ctx);

            RateLimiter.Verdict verdict;
            long total;
            RateLimiter.Pending summary;
            lock (_gate)
                verdict = Limiter.Check(fp, now, occ, out total, out summary);

            switch (verdict)
            {
                case RateLimiter.Verdict.Full:
                    Emit(level, BuildErrorLine(unix, kind, area, fp, total, ctx, ex, detail));
                    return true;
                case RateLimiter.Verdict.SummaryThenFull:
                    EmitSummary(unix, summary);
                    Emit(level, BuildErrorLine(unix, kind, area, fp, total, ctx, ex, detail));
                    return true;
                case RateLimiter.Verdict.Summary:
                    EmitSummary(unix, summary);
                    return false;
                default:
                    return false;
            }
        }
        catch
        {
            // Telemetry must never be the thing that breaks the plugin. There is no safe place left to
            // report a failure of the reporter itself.
            return false;
        }
    }

    /// <summary>
    /// <c>ER|unixms|kind|p|v|ch|c|a|fp|n|tt|j|lv|cb|du|x|m|[d]|st</c> - the full grammar is docs/telemetry-lines.md.
    /// </summary>
    public string BuildErrorLine(long unixMs, string kind, string area, string fp, long count, GameContext ctx, Exception ex, string? detail = null)
    {
        var b = Head(unixMs, kind, area, fp, count, ctx)
            .Field("x", ex.GetType().FullName ?? ex.GetType().Name, 200)
            .Field("m", ex.Message, MaxMessageChars);
        if (!string.IsNullOrEmpty(detail))
            b.Field("d", detail, MaxDetailChars);
        b.Field("st", ex.ToString(), MaxStackChars);
        return b.ToString();
    }

    private TelemetryLineBuilder Head(long unixMs, string kind, string area, string fp, long count, GameContext ctx)
    {
        return new TelemetryLineBuilder(ErrorPrefix, unixMs, kind, 1024)
            .Field("p", Identity.Plugin, 64)
            .Field("v", Identity.Version, 32)
            .Field("ch", Identity.Channel, 16)
            .Field("c", Identity.Commit, 40)
            .Field("a", area, 64)
            .Field("fp", fp, 16)
            .Field("n", count)
            .Field("tt", ctx.TerritoryId)
            .Field("j", ctx.ClassJobId)
            .Field("lv", ctx.Level)
            .Field("cb", ctx.InCombat)
            .Field("du", ctx.BoundByDuty);
    }

    private void EmitSummary(long unixMs, RateLimiter.Pending p)
    {
        if (p.Meta is not Occurrence occ)
            return;
        var line = Head(unixMs, "summary", occ.Area, p.Fingerprint, p.Total, occ.Context)
            .Field("of", occ.Kind, 16)
            .Field("sup", p.Suppressed)
            .Field("span", p.SpanMs)
            .Field("x", occ.Type, 200)
            .Field("m", occ.Message, MaxMessageChars)
            .ToString();
        Emit(occ.Level, line);
    }

    private void Emit(TelemetryLevel level, string line)
    {
        var unix = _unixMs();
        Errors.Add(unix, line);
        Lines.Add(unix, Cut(line, RingErrorChars));
        _sink.Write(level, line);
    }

    /// <summary>Writes summaries that are due because a failing fingerprint went quiet. Cheap when nothing is pending.</summary>
    public void Pump(bool force = false)
    {
        if (_disposed)
            return;
        try
        {
            List<RateLimiter.Pending> due;
            lock (_gate)
            {
                if (!Limiter.HasPending)
                    return;
                due = Limiter.Flush(_monotonicMs(), force);
            }
            var unix = _unixMs();
            foreach (var p in due)
                EmitSummary(unix, p);
        }
        catch
        {
            // See Write: the reporter has nowhere to report its own failure.
        }
    }

    // ------------------------------------------------------------------ guards

    public TelemetryGuard CreateGuard(string area, string label, BreakerOptions? options = null)
    {
        var g = new TelemetryGuard(this, area, label, options);
        lock (_gate)
            _guards.Add(g);
        return g;
    }

    internal void Adopt(TelemetryGuard guard)
    {
        lock (_gate)
            if (!_guards.Contains(guard))
                _guards.Add(guard);
    }

    internal void OnGuardTransition(TelemetryGuard g, BreakerTransition t, Exception? last)
    {
        if (_disposed)
            return;
        try
        {
            var unix = _unixMs();
            var ctx = SafeContext();
            var b = new TelemetryLineBuilder(ErrorPrefix, unix, t == BreakerTransition.Recovered ? "recover" : "trip", 512)
                .Field("p", Identity.Plugin, 64)
                .Field("v", Identity.Version, 32)
                .Field("ch", Identity.Channel, 16)
                .Field("c", Identity.Commit, 40)
                .Field("a", g.Area, 64)
                .Field("ev", t.ToString().ToLowerInvariant(), 16)
                .Field("trips", g.Breaker.Trips)
                .Field("fails", g.Breaker.TotalFailures)
                .Field("cool", t == BreakerTransition.Recovered ? 0 : g.Breaker.CurrentCooldownMs)
                .Field("tt", ctx.TerritoryId)
                .Field("j", ctx.ClassJobId)
                .Field("lv", ctx.Level)
                .Field("cb", ctx.InCombat)
                .Field("du", ctx.BoundByDuty);
            if (last is not null)
            {
                b.Field("fp", ExceptionFingerprint.Compute(Identity.Plugin, g.Area, last), 16)
                 .Field("x", last.GetType().FullName ?? last.GetType().Name, 200)
                 .Field("m", last.Message, MaxMessageChars);
            }

            Emit(t == BreakerTransition.Recovered ? TelemetryLevel.Info : TelemetryLevel.Error, b.ToString());

            var notice = g.TakeNotice(t, Identity);
            if (notice is not null)
            {
                Lines.Add(unix, new TelemetryLineBuilder("NT|", unix, "notice").Field("a", g.Area, 64).Field("m", notice, 300).ToString());
                _sink.Notice(notice);
            }
        }
        catch
        {
            // See Write.
        }
    }

    // ------------------------------------------------------------------ reports

    /// <summary>True when a report may be filed now (reports are rate-limited to one per <see cref="ReportCooldownMs"/>).</summary>
    public bool CanReport(out long waitMs)
    {
        var now = _monotonicMs();
        lock (_gate)
        {
            waitMs = _lastReportMs == long.MinValue ? 0 : Math.Max(0, ReportCooldownMs - (now - _lastReportMs));
            return waitMs == 0;
        }
    }

    /// <summary>
    /// Writes one RP| block (WRN, one log call). Pending ER| summaries are flushed first so the block's error
    /// section and the log agree. Returns the report id, or null when rate-limited.
    /// </summary>
    public string? WriteReport(Func<string, ReportInput> collect)
    {
        if (_disposed || !CanReport(out _))
            return null;
        lock (_gate)
            _lastReportMs = _monotonicMs();

        Pump(force: true);
        var unix = _unixMs();
        var id = ReportBuilder.NewId(unix);
        var input = collect(id);
        var block = string.Join("\n", ReportBuilder.BuildLines(input));
        Lines.Add(unix, new TelemetryLineBuilder(ReportBuilder.Prefix, unix, "filed").Field("id", id, 16).ToString());
        _sink.Write(TelemetryLevel.Warning, block);
        return id;
    }

    /// <summary>Base report input with the hub-owned parts filled (identity, rings, guard states).</summary>
    public ReportInput BaseInput(string id, string text, GameContext game, string state, string config,
        IReadOnlyList<string>? visibleAddons = null, IReadOnlyList<AddonCapture>? addons = null, IReadOnlyList<string>? collectionErrors = null)
    {
        return new ReportInput
        {
            Id = id,
            UnixMs = _unixMs(),
            Identity = Identity,
            Text = text,
            Game = game,
            State = GuardStates() + (string.IsNullOrEmpty(state) ? string.Empty : ";" + state),
            Config = config,
            Ring = Lines.Snapshot(),
            RingTotal = Lines.TotalAdded,
            Errors = Errors.Snapshot(),
            ErrorTotal = Errors.TotalAdded,
            VisibleAddons = visibleAddons ?? Array.Empty<string>(),
            Addons = addons ?? Array.Empty<AddonCapture>(),
            CollectionErrors = collectionErrors ?? Array.Empty<string>(),
        };
    }

    /// <summary><c>guard.&lt;area&gt;=&lt;state&gt;/&lt;trips&gt;/&lt;failures&gt;</c> for every guard, ';'-joined.</summary>
    public string GuardStates()
    {
        var parts = new List<string>();
        foreach (var g in Guards)
            parts.Add($"guard.{g.Area}={g.Breaker.State.ToString().ToLowerInvariant()}/{g.Breaker.Trips}/{g.Breaker.TotalFailures}");
        return string.Join(";", parts);
    }

    /// <summary>First <paramref name="max"/> characters, never ending on half a surrogate pair.</summary>
    private static string Cut(string line, int max)
    {
        if (line.Length <= max)
            return line;
        var n = char.IsHighSurrogate(line[max - 1]) ? max - 1 : max;
        return line[..n];
    }

    private GameContext SafeContext()
    {
        try
        {
            return _context() ?? GameContext.Empty;
        }
        catch
        {
            return GameContext.Empty;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Pump(force: true);
        _disposed = true;
    }
}
