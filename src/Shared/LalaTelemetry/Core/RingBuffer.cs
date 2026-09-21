// Shared source (NOT a shared DLL). Core/ is Dalamud-free.
using System;
using System.Threading;

namespace Lalalazy.Telemetry;

/// <summary>
/// Fixed-capacity, thread-safe buffer of the most recent telemetry lines. Adding costs one lock and one
/// array store (the line string is the caller's, already formatted) - it never formats anything itself.
/// </summary>
public sealed class RingBuffer
{
    public readonly record struct Entry(long UnixMs, string Line);

    private readonly Entry[] _items;
    private readonly object _gate = new();
    private int _next;
    private int _count;
    private long _total;

    public RingBuffer(int capacity)
    {
        _items = new Entry[Math.Max(1, capacity)];
    }

    public int Capacity => _items.Length;

    public int Count
    {
        get { lock (_gate) return _count; }
    }

    /// <summary>Lines ever added (so a report can say how many fell off the front).</summary>
    public long TotalAdded => Interlocked.Read(ref _total);

    public void Add(long unixMs, string? line)
    {
        if (line is null)
            return;
        lock (_gate)
        {
            _items[_next] = new Entry(unixMs, line);
            _next = (_next + 1) % _items.Length;
            if (_count < _items.Length)
                _count++;
            _total++;
        }
    }

    /// <summary>Oldest first.</summary>
    public Entry[] Snapshot()
    {
        lock (_gate)
        {
            var result = new Entry[_count];
            var start = (_next - _count + _items.Length) % _items.Length;
            for (var i = 0; i < _count; i++)
                result[i] = _items[(start + i) % _items.Length];
            return result;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_items);
            _next = 0;
            _count = 0;
        }
    }
}

/// <summary>Token bucket: at most <c>capacity</c> in a burst, refilled at <c>perSecond</c>. Single-threaded.</summary>
public struct TokenBucket
{
    private readonly double _capacity;
    private readonly double _perMs;
    private double _tokens;
    private long _lastMs;
    private bool _started;

    public TokenBucket(int capacity, double perSecond)
    {
        _capacity = Math.Max(1, capacity);
        _perMs = Math.Max(0.0, perSecond) / 1000.0;
        _tokens = _capacity;
        _lastMs = 0;
        _started = false;
    }

    public bool TryTake(long nowMs)
    {
        if (!_started)
        {
            _started = true;
            _lastMs = nowMs;
        }
        else if (nowMs > _lastMs)
        {
            _tokens = Math.Min(_capacity, _tokens + (nowMs - _lastMs) * _perMs);
            _lastMs = nowMs;
        }

        if (_tokens < 1.0)
            return false;
        _tokens -= 1.0;
        return true;
    }
}
