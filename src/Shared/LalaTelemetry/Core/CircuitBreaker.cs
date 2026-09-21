// Shared source (NOT a shared DLL). Core/ is Dalamud-free.
using System;

namespace Lalalazy.Telemetry;

public enum BreakerState
{
    /// <summary>Running normally.</summary>
    Closed,
    /// <summary>Disabled after repeated failures; waiting out the cooldown.</summary>
    Open,
    /// <summary>Cooldown elapsed: calls run again on trial. The first failure re-opens, the first clean call closes.</summary>
    HalfOpen,
}

public enum BreakerTransition
{
    None,
    /// <summary>Closed -&gt; Open: the failure threshold was reached inside the window.</summary>
    Tripped,
    /// <summary>HalfOpen -&gt; Open: the trial failed; the cooldown doubles (capped).</summary>
    Reopened,
    /// <summary>HalfOpen -&gt; Closed: a trial call completed without failing.</summary>
    Recovered,
}

public sealed class BreakerOptions
{
    /// <summary>Failures inside <see cref="WindowMs"/> that open the breaker.</summary>
    public int Threshold { get; init; } = 10;

    public long WindowMs { get; init; } = 5_000;

    /// <summary>First cooldown before a trial; doubles on every failed trial up to <see cref="MaxCooldownMs"/>.</summary>
    public long CooldownMs { get; init; } = 30_000;

    public long MaxCooldownMs { get; init; } = 300_000;

    public static BreakerOptions Default { get; } = new();
}

/// <summary>
/// Failure-count circuit breaker for per-frame / event handlers. A handler that throws on every frame
/// (a failed static initializer re-throws for the whole session) trips it within a fraction of a second;
/// a transient burst shorter than <see cref="BreakerOptions.Threshold"/> failures does not. Once open, the
/// handler is skipped until the cooldown passes, then runs on trial ("half-open"): a trial that fails
/// re-opens with double the cooldown, a trial that completes closes the breaker again, so a condition
/// that clears (zone change, addon closed) does not leave the feature off for the rest of the session.
/// Single-threaded by design: one breaker belongs to one handler on one thread.
/// </summary>
public sealed class CircuitBreaker
{
    private readonly BreakerOptions _o;
    private readonly long[] _failTimes;
    private int _failCount;
    private int _failNext;
    private bool _trialAdmitted;

    public CircuitBreaker(BreakerOptions? options = null)
    {
        _o = options ?? BreakerOptions.Default;
        _failTimes = new long[Math.Max(1, _o.Threshold)];
        CurrentCooldownMs = _o.CooldownMs;
    }

    public BreakerOptions Options => _o;
    public BreakerState State { get; private set; } = BreakerState.Closed;

    /// <summary>Times the breaker opened (first trip and every failed trial).</summary>
    public int Trips { get; private set; }

    public long OpenedAtMs { get; private set; }
    public long CurrentCooldownMs { get; private set; }

    /// <summary>Failures recorded since the breaker was created.</summary>
    public long TotalFailures { get; private set; }

    /// <summary>
    /// Whether the handler may run now. Closed: always. Open: only once the cooldown has passed, which
    /// moves to HalfOpen and admits a trial. HalfOpen: if the previously admitted trial did not fail, the
    /// breaker closes (<see cref="BreakerTransition.Recovered"/>) and the call runs.
    /// </summary>
    public bool TryEnter(long nowMs, out BreakerTransition transition)
    {
        transition = BreakerTransition.None;
        switch (State)
        {
            case BreakerState.Closed:
                return true;

            case BreakerState.Open:
                if (nowMs - OpenedAtMs < CurrentCooldownMs)
                    return false;
                State = BreakerState.HalfOpen;
                _trialAdmitted = true;
                return true;

            default: // HalfOpen
                if (_trialAdmitted)
                {
                    Close();
                    transition = BreakerTransition.Recovered;
                    return true;
                }
                _trialAdmitted = true;
                return true;
        }
    }

    /// <summary>Explicit success (optional: a later <see cref="TryEnter"/> infers it). Closes a half-open breaker.</summary>
    public BreakerTransition RecordSuccess()
    {
        if (State != BreakerState.HalfOpen)
            return BreakerTransition.None;
        Close();
        return BreakerTransition.Recovered;
    }

    public BreakerTransition RecordFailure(long nowMs)
    {
        TotalFailures++;

        if (State == BreakerState.HalfOpen)
        {
            CurrentCooldownMs = Math.Min(CurrentCooldownMs * 2, Math.Max(_o.CooldownMs, _o.MaxCooldownMs));
            Open(nowMs);
            return BreakerTransition.Reopened;
        }

        if (State == BreakerState.Open)
            return BreakerTransition.None;

        _failTimes[_failNext] = nowMs;
        _failNext = (_failNext + 1) % _failTimes.Length;
        if (_failCount < _failTimes.Length)
            _failCount++;

        if (_failCount < _failTimes.Length)
            return BreakerTransition.None;

        // Buffer full: _failNext now points at the OLDEST of the last Threshold failures.
        var oldest = _failTimes[_failNext];
        if (nowMs - oldest > _o.WindowMs)
            return BreakerTransition.None;

        CurrentCooldownMs = _o.CooldownMs;
        Open(nowMs);
        return BreakerTransition.Tripped;
    }

    private void Open(long nowMs)
    {
        State = BreakerState.Open;
        OpenedAtMs = nowMs;
        Trips++;
        _trialAdmitted = false;
    }

    private void Close()
    {
        State = BreakerState.Closed;
        _trialAdmitted = false;
        _failCount = 0;
        _failNext = 0;
        CurrentCooldownMs = _o.CooldownMs;
    }
}
