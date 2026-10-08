using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Exercised by tests/LazyMarketCompanion.Harness (cases 163-168).
//
// Retainer-dialog stall guard (0.2.8.14). The task manager gives every step a hard 10 s limit and,
// when one step overruns, throws away the WHOLE remaining chain. Before this file nothing sat in
// front of that limit: a row step that never saw its dialog (or never got a market price) waited
// out the full 10 s, the chain was cleared with the price dialog still on screen, and the
// AutoRetainer postprocess was released into a game state it could not continue from.
//
// Three small pieces, each used by MarketAutomation and by the harness simulation:
//   StepStall       - a soft deadline in front of the hard limit, per step, so one bad row is skipped
//                     (and its dialogs closed) instead of aborting the chain.
//   DialogRecovery  - what to close, in what order, before a chain that DID abort hands the game back.
//   SweepProgress   - which retainers of a sweep finished, so an interrupted sweep names the ones it
//                     skipped and a re-run completes only those.

/// <summary>What one tick of a bounded retry step decided.</summary>
public enum StallVerdict
{
  /// <summary>The step is still waiting inside its budget: retry next tick.</summary>
  Pending,
  /// <summary>The step finished on its own.</summary>
  Done,
  /// <summary>The step's soft budget ran out: the caller skips the row and cleans up.</summary>
  Stalled,
}

/// <summary>Tunables, kept next to the decision code so the harness grades the real numbers.</summary>
public static class StallPolicy
{
  /// <summary>The task manager's hard per-step limit (TaskManager.TimeLimitMS in MarketAutomation).</summary>
  public const int HardStepLimitMs = 10000;

  /// <summary>Soft budget for a row step that waits on a dialog to open (context menu, price dialog).</summary>
  public const int RowStepSoftMs = 7000;

  /// <summary>
  /// Soft budget for the step that waits on the row's price. Longer than <see cref="RowStepSoftMs"/>
  /// because the sale-history lookup behind it has its own 8 s HTTP timeout, and still 1.5 s under the hard limit.
  /// </summary>
  public const int PriceStepSoftMs = 8500;

  /// <summary>How long the recovery keeps trying to close dialogs before it hands the game back as it is.</summary>
  public const int RecoveryGiveUpMs = 4000;

  /// <summary>Re-ask a dialog to close only this often (a close is applied on a later frame).</summary>
  public const int RecoveryReaskMs = 600;

  /// <summary>
  /// The AutoRetainer session cap scales with the rows the session walks: the flat 5 minutes it had
  /// was sized for a short list, and a 20-row pinch at the configured market-board delays runs
  /// about 11 s per row before any listing or gate work.
  /// </summary>
  public static long SessionCapMs(long baseMs, int rowsWalked, int perRowMs)
    => baseMs + (long)Math.Max(rowsWalked, 0) * Math.Max(perRowMs, 0);

  /// <summary>One row's budget: its fixed delays plus slack for the three waits around them.</summary>
  public static int PerRowBudgetMs(int getMbPricesDelayMs, int marketBoardKeepOpenMs)
    => Math.Max(getMbPricesDelayMs, 0) + Math.Max(marketBoardKeepOpenMs, 0) + 200 + 3000;
}

/// <summary>
/// Soft deadline for a retry step (a step that returns false until its dialog is there). One instance
/// serves the whole chain because only one step runs at a time; the key (stage + row) restarts the
/// window when the chain moves on, and a finished or stalled step clears it.
/// </summary>
public sealed class StepStall
{
  private string? _key;
  private long _startedAt;

  /// <param name="key">Identity of the running step, e.g. "SetNewPrice7".</param>
  /// <param name="now">Monotonic milliseconds (Environment.TickCount64).</param>
  /// <param name="stepResult">What the step itself returned: true = finished, anything else = still waiting.</param>
  /// <param name="limitMs">Soft budget for this step.</param>
  /// <param name="waitedMs">How long the step had been waiting when this verdict was made.</param>
  public StallVerdict Evaluate(string key, long now, bool? stepResult, int limitMs, out long waitedMs)
  {
    if (stepResult == true)
    {
      waitedMs = _key == key ? now - _startedAt : 0;
      Clear();
      return StallVerdict.Done;
    }

    if (!string.Equals(_key, key, StringComparison.Ordinal))
    {
      _key = key;
      _startedAt = now;
    }

    waitedMs = now - _startedAt;
    if (waitedMs < limitMs)
      return StallVerdict.Pending;

    Clear();
    return StallVerdict.Stalled;
  }

  public void Clear()
  {
    _key = null;
    _startedAt = 0;
  }
}

/// <summary>The one log/chat line shape for a stalled stage, so every stage names itself the same way.</summary>
public static class StallLine
{
  public static string Skipped(string stage, int row, long waitedMs, string waitingFor)
    => $"stall: stage={stage} row={row} waited {waitedMs} ms for {waitingFor}; skipping this row, closing its dialogs, the chain continues";

  public static string Recovered(string reason, string lastStage, IReadOnlyList<string> closed, IReadOnlyList<string> stillOpen)
  {
    var closedText = closed.Count == 0 ? "nothing needed closing" : "closed " + string.Join(", ", closed);
    var openText = stillOpen.Count == 0 ? "" : "; still open: " + string.Join(", ", stillOpen);
    return $"recovery after '{reason}' (last stage: {lastStage}): {closedText}{openText}";
  }
}

/// <summary>What the dialog recovery wants done next.</summary>
public enum RecoveryKind
{
  /// <summary>Nothing to do this tick (a close is in flight).</summary>
  Wait,
  /// <summary>Ask the named addon to close.</summary>
  Close,
  /// <summary>Everything the automation opened is closed; hand the game back.</summary>
  Done,
  /// <summary>Time is up with something still open: hand the game back as it is and say so.</summary>
  GaveUp,
}

public readonly record struct RecoveryStep(RecoveryKind Kind, string? Addon = null);

/// <summary>
/// The close order for an aborted chain. Innermost first: the market-board result, the item's
/// context menu, the price dialog, then the sell list, then the retainer inventory panel the vendor
/// leg opens. The retainer menu (SelectString) is never in this list - AutoRetainer continues from it
/// - and the buyback-abandon confirm (SelectYesno) is protected: automation never answers it.
/// </summary>
public sealed class DialogRecovery
{
  public static readonly string[] CloseOrder =
  [
    "ItemSearchResult", "ContextMenu", "RetainerSell", "RetainerSellList", "InventoryRetainerLarge", "InventoryRetainer",
  ];

  /// <summary>Left alone on purpose, whatever state it is in.</summary>
  public const string ProtectedConfirm = "SelectYesno";

  private readonly long _startedAt;
  private readonly Dictionary<string, long> _askedAt = new(StringComparer.Ordinal);
  private readonly List<string> _closed = [];

  public DialogRecovery(long now) { _startedAt = now; }

  /// <summary>Addons this recovery asked to close, in order, for the end-of-recovery log line.</summary>
  public IReadOnlyList<string> Closed => _closed;

  public IReadOnlyList<string> StillOpen(Func<string, bool> isOpen) => CloseOrder.Where(isOpen).ToList();

  public RecoveryStep Next(long now, Func<string, bool> isOpen)
  {
    var open = CloseOrder.Where(isOpen).ToList();
    if (open.Count == 0)
      return new RecoveryStep(RecoveryKind.Done);

    if (now - _startedAt >= StallPolicy.RecoveryGiveUpMs)
      return new RecoveryStep(RecoveryKind.GaveUp);

    foreach (var name in open)
    {
      if (_askedAt.TryGetValue(name, out var at) && now - at < StallPolicy.RecoveryReaskMs)
        return new RecoveryStep(RecoveryKind.Wait);
      _askedAt[name] = now;
      if (!_closed.Contains(name))
        _closed.Add(name);
      return new RecoveryStep(RecoveryKind.Close, name);
    }
    return new RecoveryStep(RecoveryKind.Wait);
  }
}

/// <summary>
/// Which retainers of a sweep finished. When a chain aborts partway, the retainers that never ran
/// are named in the report, and the next sweep inside <see cref="ResumeWindowMs"/> runs only those -
/// a retainer that finished is never re-priced.
/// </summary>
public sealed class SweepProgress
{
  /// <summary>How long after an interruption a new sweep resumes instead of starting over.</summary>
  public const long ResumeWindowMs = 30L * 60 * 1000;

  private readonly List<string> _planned = [];
  private readonly HashSet<string> _done = new(StringComparer.Ordinal);
  private bool _interrupted;
  private long _interruptedAt;
  private HashSet<string> _resumeSkip = new(StringComparer.Ordinal);
  // Finished in an earlier interrupted run and left out of this one; a second interruption must keep them out too.
  private HashSet<string> _carried = new(StringComparer.Ordinal);

  public IReadOnlyList<string> Planned => _planned;

  /// <summary>Retainers planned this sweep that have not finished, in plan order.</summary>
  public IReadOnlyList<string> NotRun => _planned.Where(n => !_done.Contains(n)).ToList();

  /// <summary>
  /// Starts a sweep over <paramref name="names"/>. Returns the names a resumed sweep leaves out
  /// (finished in the interrupted run); empty for a normal start or when the window has passed.
  /// </summary>
  public IReadOnlyList<string> Begin(IEnumerable<string> names, long now)
  {
    var skip = _interrupted && now - _interruptedAt <= ResumeWindowMs ? _resumeSkip : new HashSet<string>(StringComparer.Ordinal);
    var all = names.ToList();
    var skipped = all.Where(skip.Contains).ToList();
    _planned.Clear();
    _planned.AddRange(all.Where(n => !skip.Contains(n)));
    _done.Clear();
    _carried = new HashSet<string>(skipped, StringComparer.Ordinal);
    _interrupted = false;
    _resumeSkip = new HashSet<string>(StringComparer.Ordinal);
    return skipped;
  }

  public void MarkDone(string name) => _done.Add(name);

  /// <summary>The sweep ended without finishing: remember who finished so the next sweep can skip them.</summary>
  public void Interrupt(long now)
  {
    _resumeSkip = new HashSet<string>(_done, StringComparer.Ordinal);
    _resumeSkip.UnionWith(_carried);
    _interrupted = true;
    _interruptedAt = now;
  }

  /// <summary>The sweep ended cleanly: nothing to resume.</summary>
  public void Complete()
  {
    _carried = new HashSet<string>(StringComparer.Ordinal);
    _interrupted = false;
    _resumeSkip = new HashSet<string>(StringComparer.Ordinal);
  }
}
