using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free evaluator for live listing execution vs dry-run simulation (0.2.3.0).
public static class AutoMarketExecution
{
  public record ExecutionResult(
    IReadOnlyList<ListingOp> ExecutedOps,
    IReadOnlyList<string> SimulatedLines,
    string? ChatFeedback);

  public static ExecutionResult Evaluate(bool isDryRun, PlanResult plan, int simulatedPulls = 0)
  {
    if (isDryRun)
    {
      var simulatedLines = plan.Ops
        .Select(op => DryRunFormat.WouldList(op.ItemId, op.HQ, op.Quantity, op.Origin, op.TargetSlot))
        .ToList();
      var totalSimulated = plan.Ops.Count + simulatedPulls;
      var feedback = DryRunFormat.FormatPassFeedback(true, totalSimulated);
      return new ExecutionResult([], simulatedLines, feedback);
    }

    return new ExecutionResult(plan.Ops, [], null);
  }
}
