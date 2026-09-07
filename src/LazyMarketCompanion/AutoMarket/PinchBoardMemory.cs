using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>One confirmed verdict the Auto Pinch pass stored about one listing.</summary>
/// <param name="ItemId">The item's sheet id, read from the market container - never from row text.</param>
/// <param name="Hq">High-quality listing. A verdict about an NQ row is never applied to an HQ row of the same item.</param>
/// <param name="Price">The asking price the compare window confirmed, exactly as it was read off the market container.</param>
/// <param name="ConfirmedUnixMs">When the verdict was stored, in unix milliseconds.</param>
public sealed record PinchMemoryEntry(uint ItemId, bool Hq, long Price, long ConfirmedUnixMs);

/// <summary>
/// The board's own verdicts, remembered between sweeps.
///
/// WHY THIS EXISTS. On 2026-09-06 21:22-21:26 a manual Auto Pinch pass walked 23 rows. 14 were real
/// undercuts; the other 9 came out at EXACTLY the price they already had - 12 to 12, 242 to 242, 5000 to
/// 5000, HQ 84 to 84, HQ 65 to 65, 2 to 2, 857 to 857, 40000 to 40000 twice - at about 10.5 s of
/// context-menu work per row. AllaganMarket coloured none of them because its cache has no row for an
/// item until somebody opens a market window for it, and the Universalis pre-flight had no usable quote
/// either: nobody uploads scans of slow long-tail items (a 40,000-gil rug, 2-gil HQ rings), so its
/// uncertainty rule - correctly - walks them. The structural gap: between sweeps, nobody has the board
/// answer for a slow item. The one source that does - the compare window Auto Pinch itself opens for
/// exactly such a row - was discarded at the end of every pass.
///
/// WHAT IS STORED. When a full pass opens a listing and the compare window agrees the price is still
/// the right one - the candidate the window produced equals the price already on the listing - the
/// (item, quality, price, time) verdict is remembered here. That is not a prediction about the board:
/// it IS the board, read by the same window the pass would otherwise run. Universalis is the
/// crowd-sourced approximation of it.
///
/// WHAT IS TRUSTED. Only two things: the price on the listing must be EXACTLY the confirmed price
/// (a changed price means the world moved and the verdict is void), and the verdict must be younger
/// than the configured window (default 12 h). Anything else walks as before. A row Universalis does
/// know about still follows the normal rules; memory is only consulted where the pre-flight would walk
/// for lack of a usable answer - see <see cref="PinchPreflight.CanMemorySettle"/>, deliberately a
/// negative list: a new walk reason added to <see cref="PinchPreflight.Decide"/> stays a walk until it
/// is added there.
///
/// The store survives restarts (one small JSON file next to the plugin config, atomic write, trimmed to
/// <see cref="MaxEntries"/>). A corrupt or missing file reads as an empty memory, which is the honest
/// state: the next pass walks and re-writes it.
/// </summary>
public sealed class PinchBoardMemory
{
  /// <summary>Store file name, kept next to the plugin's own config.</summary>
  public const string FileName = "LazyMarketCompanion.pinch-memory.json";

  /// <summary>
  /// Hard cap on stored verdicts. One retainer holds 20 listings, so even a large multi-retainer setup
  /// is two orders of magnitude under this; the cap exists so a runaway cannot grow the file forever.
  /// When it is exceeded the OLDEST verdict is dropped first.
  /// </summary>
  public const int MaxEntries = 500;

  private readonly Dictionary<(uint ItemId, bool Hq), PinchMemoryEntry> _entries = new();
  private readonly Func<long> _nowUnixMs;

  public PinchBoardMemory(Func<long>? nowUnixMs = null)
    => _nowUnixMs = nowUnixMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

  /// <summary>Number of stored verdicts. Zero on a fresh install or after a corrupt read.</summary>
  public int Count => _entries.Count;

  /// <summary>The stored verdicts (diagnostics and the offline harness; the plugin never iterates them).</summary>
  public IReadOnlyCollection<PinchMemoryEntry> Entries => _entries.Values;

  /// <summary>
  /// Records a compare window's confirm: the pass opened this item's listing and the window agreed the
  /// price already on it is the right one. A re-confirm of the same key overwrites the older verdict -
  /// latest wins, so the window of trust restarts from the newest evidence.
  /// </summary>
  /// <returns>True when something was stored. Item 0 or a non-positive price is never stored.</returns>
  public bool Remember(uint itemId, bool hq, long price)
  {
    if (itemId == 0 || price <= 0)
      return false;

    _entries[(itemId, hq)] = new PinchMemoryEntry(itemId, hq, price, _nowUnixMs());

    if (_entries.Count > MaxEntries)
    {
      var oldest = _entries.Values.OrderBy(e => e.ConfirmedUnixMs).First();
      _entries.Remove((oldest.ItemId, oldest.Hq));
    }

    return true;
  }

  public bool TryGet(uint itemId, bool hq, out PinchMemoryEntry entry)
    => _entries.TryGetValue((itemId, hq), out entry!);

  /// <summary>
  /// Drops the verdict for one listing. Called when a compare window produces a DIFFERENT price than the
  /// memory held: the world moved, so the remembered answer is not just stale but wrong, and keeping it
  /// would let the next pass skip a row whose price no longer matches anything confirmed.
  /// </summary>
  public bool Forget(uint itemId, bool hq)
    => itemId != 0 && _entries.Remove((itemId, hq));

  /// <summary>
  /// The verdict, or null when it must not be trusted. The item and quality are re-checked here even
  /// though every caller reaches the entry through the (item, quality) key - a directly-passed entry
  /// whose identity disagrees must never settle a row. The price check is EXACT equality on purpose: a
  /// listing at any other price is a changed world, and skipping it on an old verdict is precisely the
  /// wrong-skip this feature must never commit. <c>BoardMemoryHours</c> of 0 (or less) means OFF -
  /// nothing is ever trusted, whatever the store holds; only a positive window can justify a skip.
  /// </summary>
  public static PinchVerdict? Decide(PinchRow row, PinchMemoryEntry entry, PinchPreflightOptions options, long nowUnixMs)
  {
    if (options.BoardMemoryHours <= 0)
      return null;
    var windowMs = (long)options.BoardMemoryHours * 3_600_000L;
    if (nowUnixMs - entry.ConfirmedUnixMs > windowMs)
      return null;
    if (entry.ItemId != row.ItemId || entry.Hq != row.HQ)
      return null;
    if (row.CurrentPrice != entry.Price)
      return null;
    return PinchVerdict.SkipBoardMemory;
  }

  /// <summary>
  /// Rewrites the uncertainty walks a remembered verdict can settle into
  /// <see cref="PinchVerdict.SkipBoardMemory"/> skips. Every other verdict - placeholder rows, real
  /// undercuts, threshold skips, mirror skips, rows with a usable Universalis answer - is returned
  /// untouched, and a walk with no matching fresh verdict stays a walk.
  /// </summary>
  /// <param name="usedLog">One line per applied verdict, for the pass to log.</param>
  public static List<PinchDecision> ApplyToDecisions(
    IReadOnlyList<PinchDecision> decisions,
    PinchBoardMemory memory,
    PinchPreflightOptions options,
    long nowUnixMs,
    ICollection<string>? usedLog = null)
  {
    var result = new List<PinchDecision>(decisions.Count);
    if (options.BoardMemoryHours <= 0)
    {
      result.AddRange(decisions);
      return result;
    }

    foreach (var decision in decisions)
    {
      if (decision.Verdict == PinchVerdict.Walk
          && PinchPreflight.CanMemorySettle(decision.Reason)
          && memory.TryGet(decision.Row.ItemId, decision.Row.HQ, out var entry)
          && Decide(decision.Row, entry, options, nowUnixMs) is { } verdict)
      {
        var age = AgeText(nowUnixMs - entry.ConfirmedUnixMs);
        usedLog?.Add(
          $"pinch board memory: row {decision.Row.Row} (slot #{decision.Row.Slot}, item {decision.Row.ItemId}{(decision.Row.HQ ? " HQ" : "")}) at {decision.Row.CurrentPrice} gil - confirmed {age} ago, skipping without a walk");
        result.Add(decision with
        {
          Verdict = verdict,
          Candidate = decision.Row.CurrentPrice,
          Reason = $"board memory: this exact price was confirmed by a compare window {age} ago",
        });
        continue;
      }

      result.Add(decision);
    }

    return result;
  }

  /// <summary>Ages under a minute read as seconds, under an hour as minutes, otherwise in hours.</summary>
  public static string AgeText(long milliseconds)
  {
    if (milliseconds < 60_000)
      return $"{Math.Max(milliseconds, 0) / 1000.0:0}s";
    if (milliseconds < 3_600_000)
      return $"{milliseconds / 60_000.0:0.#}m";
    return $"{milliseconds / 3_600_000.0:0.##}h";
  }

  /// <summary>
  /// The store format, versioned so a future change can be recognised instead of misread. Hand-rolled on
  /// purpose: the fields are four primitives and the reader below tolerates junk instead of throwing.
  /// </summary>
  public static string ToJson(IReadOnlyCollection<PinchMemoryEntry> entries)
  {
    var sb = new StringBuilder(64 * Math.Max(entries.Count, 1) + 64);
    sb.Append("{\"version\":1,\"entries\":[");
    var first = true;
    foreach (var entry in entries)
    {
      if (!first)
        sb.Append(',');
      first = false;
      sb.Append("{\"itemId\":").Append(entry.ItemId)
        .Append(",\"hq\":").Append(entry.Hq ? "true" : "false")
        .Append(",\"price\":").Append(entry.Price)
        .Append(",\"confirmedUnixMs\":").Append(entry.ConfirmedUnixMs)
        .Append('}');
    }

    sb.Append("]}");
    return sb.ToString();
  }

  /// <summary>
  /// Reads a store. Anything that cannot be parsed - wrong root, broken JSON, malformed entries - is
  /// skipped or empties the result; a memory that reads as empty is the correct fallback, because the
  /// next pass re-writes it from real confirmations.
  /// </summary>
  public static List<PinchMemoryEntry> FromJson(string json)
  {
    var result = new List<PinchMemoryEntry>();
    if (string.IsNullOrWhiteSpace(json))
      return result;

    try
    {
      using var doc = JsonDocument.Parse(json);
      var root = doc.RootElement;
      if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("entries", out var entries)
          || entries.ValueKind != JsonValueKind.Array)
        return result;

      foreach (var element in entries.EnumerateArray())
      {
        if (element.ValueKind != JsonValueKind.Object)
          continue;
        if (!element.TryGetProperty("itemId", out var idElement) || !idElement.TryGetUInt32(out var itemId) || itemId == 0)
          continue;
        if (!element.TryGetProperty("price", out var priceElement) || !priceElement.TryGetInt64(out var price) || price <= 0)
          continue;
        if (!element.TryGetProperty("confirmedUnixMs", out var timeElement) || !timeElement.TryGetInt64(out var confirmed) || confirmed <= 0)
          continue;
        var hq = element.TryGetProperty("hq", out var hqElement) && hqElement.ValueKind == JsonValueKind.True;
        result.Add(new PinchMemoryEntry(itemId, hq, price, confirmed));
      }
    }
    catch (JsonException)
    {
      // Broken JSON reads as an empty memory; see the class remarks.
      return [];
    }

    return result;
  }

  /// <summary>Atomic write (temp file, then move) so a crash mid-save cannot leave a half file behind.</summary>
  public void Save(string directory)
  {
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, FileName);
    var tmp = path + ".tmp";
    File.WriteAllText(tmp, ToJson(_entries.Values));
    File.Move(tmp, path, overwrite: true);
  }

  /// <summary>Loads the store from the plugin's config directory. No file, or an unreadable one, is an empty memory.</summary>
  public static PinchBoardMemory Load(string? directory, Func<long>? nowUnixMs = null)
  {
    var memory = new PinchBoardMemory(nowUnixMs);
    if (string.IsNullOrWhiteSpace(directory))
      return memory;

    try
    {
      var path = Path.Combine(directory, FileName);
      if (!File.Exists(path))
        return memory;

      foreach (var entry in FromJson(File.ReadAllText(path)))
        memory._entries[(entry.ItemId, entry.Hq)] = entry;
    }
    catch (Exception)
    {
      // A store the OS will not give us reads as empty - same fallback as a corrupt file. The next
      // confirm re-writes it.
    }

    return memory;
  }
}
