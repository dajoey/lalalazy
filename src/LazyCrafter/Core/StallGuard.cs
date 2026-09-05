namespace LazyCrafter.Core;

/// <summary>
/// "Has anything changed in the last N minutes?" - for the waits on GBR and Artisan that previously had no bound
/// (card t_efde145c). Feed it a progress signal (bag counts of the items being gathered / made, joined into one
/// string) every poll; it reports how long the signal has been unchanged. A stall is a signal that has not moved
/// for <see cref="Limit"/>. <see cref="Observe"/> with <c>paused</c> holds the clock (GBR waiting for a timed node
/// window is not a stall - the node is not up yet).
/// </summary>
public sealed class StallGuard
{
    private string? _last;
    private DateTime _since;

    public StallGuard(TimeSpan limit) { Limit = limit; }

    public TimeSpan Limit { get; }

    /// <summary>How long the signal has been unchanged, as of the last <see cref="Observe"/>.</summary>
    public TimeSpan Unchanged { get; private set; }

    public void Reset() { _last = null; _since = default; Unchanged = TimeSpan.Zero; }

    /// <summary>Record <paramref name="signal"/> at <paramref name="now"/>; <c>true</c> when it has not changed for <see cref="Limit"/> or longer.</summary>
    public bool Observe(string signal, DateTime now, bool paused = false)
    {
        if (_last is null || signal != _last || paused)
        {
            _last = signal;
            _since = now;
            Unchanged = TimeSpan.Zero;
            return false;
        }
        Unchanged = now - _since;
        return Unchanged >= Limit;
    }
}
