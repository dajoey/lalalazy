// Shared source (NOT a shared DLL). Core/ is Dalamud-free.
using System;

namespace Lalalazy.Telemetry;

/// <summary>
/// Circuit breaker + error reporting around one per-frame or event handler.
///
/// Allocation-free pattern for a hot handler (early <c>return</c>s inside the try are fine - a trial that
/// reaches the next frame without calling <see cref="Failed"/> counts as a success):
/// <code>
/// if (!_guard.TryEnter()) return;
/// try { ...body... }
/// catch (Exception ex) { _guard.Failed(ex); }
/// </code>
/// Every failure becomes a rate-limited <c>ER|...|kind=error</c> line. After
/// <see cref="BreakerOptions.Threshold"/> failures inside the window the handler is skipped, one
/// <c>ER|trip</c> line is written and ONE chat notice is shown for the whole session; it then retries on
/// its own (30 s, doubling to 5 min) and writes <c>ER|recover</c> when a retry runs clean.
/// </summary>
public sealed class TelemetryGuard
{
    private readonly TelemetryHub? _hub;
    private bool _tripNoticeShown;
    private bool _recoverNoticeShown;
    private Exception? _lastFailure;

    /// <param name="hub">Null = resolve <see cref="LalaTelemetry.Hub"/> at the moment of each failure.</param>
    /// <param name="area">Stable machine name, e.g. "tick" or "automation.draw" (the ER| <c>a=</c> field).</param>
    /// <param name="label">Player-facing name used in the notice, e.g. "the per-frame update".</param>
    public TelemetryGuard(TelemetryHub? hub, string area, string label, BreakerOptions? options = null)
    {
        _hub = hub;
        Area = area;
        Label = label;
        Breaker = new CircuitBreaker(options);
    }

    public string Area { get; }
    public string Label { get; }
    public CircuitBreaker Breaker { get; }

    /// <summary>True while the handler is being skipped.</summary>
    public bool IsOpen => Breaker.State == BreakerState.Open;

    private TelemetryHub? Hub => _hub ?? LalaTelemetry.Hub;

    private long Now() => Hub?.MonotonicMs() ?? Environment.TickCount64;

    /// <summary>Whether the handler may run this time. One field read while closed.</summary>
    public bool TryEnter()
    {
        if (Breaker.State == BreakerState.Closed)
            return true;

        var ok = Breaker.TryEnter(Now(), out var transition);
        if (transition == BreakerTransition.Recovered)
            Transition(transition, null);
        return ok;
    }

    /// <summary>Optional explicit success; only matters while half-open.</summary>
    public void Succeeded()
    {
        if (Breaker.State != BreakerState.HalfOpen)
            return;
        var t = Breaker.RecordSuccess();
        if (t == BreakerTransition.Recovered)
            Transition(t, null);
    }

    /// <summary>
    /// Records a failure: an ER| line (rate-limited) and a breaker step. Returns true when this failure's
    /// ER| line was written in full (callers can use it to avoid repeating their own chat message).
    /// </summary>
    public bool Failed(Exception ex)
    {
        _lastFailure = ex;
        var hub = Hub;
        var written = false;
        try
        {
            written = hub?.Error(Area, ex) ?? false;
        }
        catch
        {
            // Hub.Error never throws; belt and braces for a guard that runs every frame.
        }

        var t = Breaker.RecordFailure(Now());
        if (t is BreakerTransition.Tripped or BreakerTransition.Reopened)
            Transition(t, ex);
        return written;
    }

    /// <summary>Runs <paramref name="body"/> under the guard. Returns false when skipped or when it threw.</summary>
    public bool Run(Action body)
    {
        if (!TryEnter())
            return false;
        try
        {
            body();
            Succeeded();
            return true;
        }
        catch (Exception ex)
        {
            Failed(ex);
            return false;
        }
    }

    private void Transition(BreakerTransition t, Exception? ex)
    {
        var hub = Hub;
        if (hub is null)
            return;
        hub.Adopt(this);
        hub.OnGuardTransition(this, t, ex ?? (t == BreakerTransition.Recovered ? null : _lastFailure));
    }

    /// <summary>The notice for this transition, or null when this guard already showed that kind once.</summary>
    internal string? TakeNotice(BreakerTransition t, TelemetryIdentity id)
    {
        switch (t)
        {
            case BreakerTransition.Tripped:
            case BreakerTransition.Reopened:
                if (_tripNoticeShown)
                    return null;
                _tripNoticeShown = true;
                var cmd = string.IsNullOrEmpty(id.Command) ? string.Empty
                    : $" \"{id.Command} report <what happened>\" writes a problem report.";
                return $"{id.DisplayName}: {Label} stopped after repeated errors and will retry on its own. Details are in the plugin log.{cmd}";

            case BreakerTransition.Recovered:
                if (!_tripNoticeShown || _recoverNoticeShown)
                    return null;
                _recoverNoticeShown = true;
                return $"{id.DisplayName}: {Label} is running again.";

            default:
                return null;
        }
    }
}
