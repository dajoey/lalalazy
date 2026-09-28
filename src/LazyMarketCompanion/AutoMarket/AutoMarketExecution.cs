using System;
using System.Collections.Generic;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free evaluator for live listing execution and per-pass feedback (0.2.4.0).
// There is no in-game dry-run gate: every planned op executes. The offline harness's
// simulation of recorded inventory states is SC4's dry-run evidence (DryRunFormat).
public static class AutoMarketExecution
{
  public record ExecutionResult(
    IReadOnlyList<ListingOp> ExecutedOps,
    string? ChatFeedback);

  public static ExecutionResult Evaluate(PlanResult plan, int executedPulls = 0, int heldUnpricedCount = 0)
  {
    var feedback = FormatPassFeedback(plan.Ops.Count, executedPulls, heldUnpricedCount);
    return new ExecutionResult(plan.Ops, feedback);
  }

  /// <summary>
  /// Per-pass chat feedback (0.2.4.0, design §12). Exactly one line when the pass executed
  /// >= 1 listing/pull op and/or held >= 1 item unpriced, naming BOTH counts and the hold
  /// reason. Null when the pass was idle (nothing executed, nothing held). Never references
  /// a dry-run mode - listing is live by default.
  /// </summary>
  public static string? FormatPassFeedback(int executedListings, int executedPulls, int heldUnpriced)
  {
    if (executedListings <= 0 && executedPulls <= 0 && heldUnpriced <= 0)
      return null;

    var pulls = executedPulls > 0 ? $" + {executedPulls} pull(s)" : string.Empty;
    return $"Auto-Market pass: {executedListings} listing(s){pulls} executed live, {heldUnpriced} item(s) held unpriced (no confirmed market price; held in place)";
  }
}
