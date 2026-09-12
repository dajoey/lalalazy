namespace LazyFashionReport.Core;

/// <summary>Outcome of one simulated apply step.</summary>
public enum StepOutcome
{
    WouldApply,
    WouldConsumeDye,
    AlreadyCorrect,
    SkippedUntouched,
    SkippedMissing,
    SkippedUnknown,
}

/// <summary>Per-slot dry-run result: exactly what WOULD happen, one pass, no retries.</summary>
public sealed record ApplyStepResult
{
    public required ApplyStep Step { get; init; }
    public required StepOutcome Outcome { get; init; }
    /// <summary>Plain sentence for the window and the log line.</summary>
    public string Line { get; init; } = "";
    /// <summary>Item id this slot would show after the step.</summary>
    public uint ResultingItemId { get; init; }
    /// <summary>Stain this slot would show after the step.</summary>
    public uint ResultingStain { get; init; }
}

/// <summary>The whole dry-run readout: one line per slot, counts, and the predicted after-total.</summary>
public sealed record ApplyDryRun
{
    public required IReadOnlyList<ApplyStepResult> Steps { get; init; }
    public int Applies => Steps.Count(s => s.Outcome is StepOutcome.WouldApply or StepOutcome.WouldConsumeDye);
    public int Dyes => Steps.Count(s => s.Outcome == StepOutcome.WouldConsumeDye);
    public int Skips => Steps.Count(s => s.Outcome is StepOutcome.SkippedMissing or StepOutcome.SkippedUnknown);
    public int Already => Steps.Count(s => s.Outcome == StepOutcome.AlreadyCorrect);
    /// <summary>The planner's predicted total, carried so the readout and the plan can be
    /// diffed in one place (the harness asserts they match).</summary>
    public required int PredictedTotal { get; init; }
    public bool Reaches80 => PredictedTotal >= 80;
}

/// <summary>
/// Dry-run simulator (P4 executor half, v0.5.0.0): runs an ApplyPlan against the CURRENT
/// live state and reports what WOULD happen per slot - one pass, no mutations, no retries.
/// This is the release Joey verifies in-game before the live mover ever ships: the readout
/// must match the character sheet exactly, or the live executor stays unwritten.
/// </summary>
public static class ApplySimulator
{
    public static ApplyDryRun Run(IReadOnlyList<ApplyStep> steps, int predictedTotal)
    {
        var results = new List<ApplyStepResult>(steps.Count);
        foreach (var s in steps)
        {
            StepOutcome outcome;
            string line;
            var resultingItem = s.ItemId;
            var resultingStain = s.StainAlreadyOn ? s.StainId : 0u;

            switch (s.Method)
            {
                case ObtainMethod.LeaveUntouched:
                    outcome = StepOutcome.SkippedUntouched;
                    line = $"{s.Slot.DisplayName()}: {s.Action}";
                    resultingItem = 0;
                    resultingStain = 0;
                    break;

                case ObtainMethod.Missing:
                    outcome = StepOutcome.SkippedMissing;
                    line = $"{s.Slot.DisplayName()}: SKIP - {s.Action}";
                    resultingItem = 0;
                    resultingStain = 0;
                    break;

                case ObtainMethod.AlreadyWorn when s.StainId != 0 && !s.StainAlreadyOn:
                    outcome = StepOutcome.WouldConsumeDye;
                    line = $"{s.Slot.DisplayName()}: already wearing, apply {s.StainName} (consumes 1)";
                    resultingStain = s.StainId;
                    break;

                case ObtainMethod.AlreadyWorn:
                    outcome = StepOutcome.AlreadyCorrect;
                    line = s.StainAlreadyOn
                        ? $"{s.Slot.DisplayName()}: {s.Action} (dye {s.StainName} already on)"
                        : $"{s.Slot.DisplayName()}: {s.Action}";
                    resultingStain = s.StainId;
                    break;

                case ObtainMethod.EquipFromBags when s.StainId != 0 && s.StainAlreadyOn:
                    // Pre-dyed bag copy: the move itself carries the dye; nothing consumed.
                    outcome = StepOutcome.WouldApply;
                    line = $"{s.Slot.DisplayName()}: {s.Action}";
                    resultingStain = s.StainId;
                    break;

                case ObtainMethod.EquipFromBags:
                case ObtainMethod.WithdrawFromDresser:
                case ObtainMethod.WithdrawFromArmoire:
                    outcome = s.StainId != 0 ? StepOutcome.WouldConsumeDye : StepOutcome.WouldApply;
                    line = $"{s.Slot.DisplayName()}: {s.Action}" +
                           (s.StainId != 0 ? $", then apply {s.StainName} (consumes 1)" : "");
                    resultingStain = s.StainId;
                    break;

                default:
                    outcome = StepOutcome.SkippedUnknown;
                    line = $"{s.Slot.DisplayName()}: SKIP - unknown state";
                    resultingItem = 0;
                    resultingStain = 0;
                    break;
            }

            results.Add(new ApplyStepResult
            {
                Step = s,
                Outcome = outcome,
                Line = line,
                ResultingItemId = resultingItem,
                ResultingStain = resultingStain,
            });
        }

        return new ApplyDryRun { Steps = results, PredictedTotal = predictedTotal };
    }
}
