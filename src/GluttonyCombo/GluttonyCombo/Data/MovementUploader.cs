using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ECommons.Logging;
using Lalalazy.Telemetry;

namespace GluttonyCombo.Data;

/// <summary>
///     The movement tap's one transport: batches <c>M|</c>/<c>H|</c>/<c>MP|</c> lines and POSTs
///     them to the movement ingest endpoint (<c>/move/ingest</c>) on jobunthree, which alone writes
///     ffxivdb (the game PC never sees a credential). Nothing
///     here touches the game thread: the batch is filled on the framework tick (one list add per
///     line) and the POST runs on a pool thread with a single request in flight; any failure just
///     drops the batch and counts it. Nothing here moves the character or presses anything.
///     The decision-question transport that used to share this client was removed on
///     2026-11-09 with the AI decision layer; the ingest path is unchanged.
/// </summary>
internal static class MovementUploader
{
    /// <summary>
    ///     LAN relay base, overridable in config. Owned by
    ///     <see cref="MovementFormat"/> since 2026-11-06 so the harness pins it; a blank
    ///     config falls back to this (it used to switch the uploads off silently).
    /// </summary>
    public const string DefaultRelayBaseUrl = MovementFormat.DefaultRelayBaseUrl;

    /// <summary> The relay base actually used (config override lands here on load). </summary>
    public static string RelayBaseUrl { get; set; } = DefaultRelayBaseUrl;

    private static readonly HttpClient Http =
        new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromMilliseconds(800) })
        {
            Timeout = TimeSpan.FromMilliseconds(1500),
        };

    /// <summary> One 2 s flush. </summary>
    private const int FlushEveryMs = 2000;

    /// <summary> Lines per POST cap (the relay refuses more; a full 10 Hz pull is ~40 lines/s). </summary>
    private const int MaxLinesPerPost = 600;

    private static readonly object BufferLock = new();
    private static readonly List<string> Buffer = new(256);
    private static long _lastFlush;
    private static int _inFlight;
    private static int _droppedBatches;

    /// <summary> How many batches were dropped since install (surfaced by the movelog status line). </summary>
    public static int DroppedBatches => _droppedBatches;

    /// <summary> Lines waiting (status line). </summary>
    public static int Pending
    {
        get { lock (BufferLock) return Buffer.Count; }
    }

    /// <summary> Adds one line to the current batch. Never throws; off when the relay URL is empty. </summary>
    public static void Enqueue(string line)
    {
        if (string.IsNullOrEmpty(RelayBaseUrl) || string.IsNullOrEmpty(line))
            return;
        lock (BufferLock)
        {
            if (Buffer.Count >= MaxLinesPerPost * 2)
                return; // the flush is stuck; drop rather than grow without bound
            Buffer.Add(line);
        }
    }

    /// <summary> Called from the recorder's tick: POSTs the batch when it is due. </summary>
    public static void Pump(long nowMs)
    {
        List<string>? batch = null;
        lock (BufferLock)
        {
            if (Buffer.Count == 0 || nowMs - _lastFlush < FlushEveryMs)
                return;
            _lastFlush = nowMs;
            batch = new List<string>(Buffer);
            Buffer.Clear();
        }

        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            // Previous POST still going: re-queue the lines so no pull data is lost.
            lock (BufferLock)
            {
                if (Buffer.Count + batch.Count <= MaxLinesPerPost * 2)
                    Buffer.InsertRange(0, batch);
                else
                    _droppedBatches++;
            }

            return;
        }

        var payload = batch;
        Task.Run(async () =>
        {
            try
            {
                var base_ = RelayBaseUrl.TrimEnd('/');
                using var content = new StringContent(string.Join('\n', payload), Encoding.UTF8, "text/plain");
                using var resp = await Http.PostAsync(base_ + "/move/ingest", content).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    _droppedBatches++;
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _droppedBatches);
            }
            finally
            {
                Interlocked.Exchange(ref _inFlight, 0);
            }
        });
    }

    /// <summary> One rate-limited warning when the relay keeps refusing, so a dead endpoint is visible. </summary>
    public static void WarnIfDown()
    {
        var dropped = Interlocked.Exchange(ref _droppedBatches, 0);
        if (dropped > 0)
            LalaTelemetry.Swallowed("movement.ingest",
                new Exception($"{dropped} movement batch(es) dropped - relay /move/ingest unreachable"));
    }
}
