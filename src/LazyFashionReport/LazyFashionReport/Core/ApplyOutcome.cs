namespace LazyFashionReport.Core;

/// <summary>What one live-apply step ended up doing (P4 executor live half, v0.6.0.0).</summary>
public enum ApplyStepStatus
{
    /// <summary>Moved from bags/armoury onto the equipped slot.</summary>
    Equipped,
    /// <summary>Withdrew from dresser/armoire into bags, then equipped - all in one pass.</summary>
    WithdrewThenEquipped,
    /// <summary>Withdraw accepted; waiting (bounded) for the piece to land in bags.</summary>
    WithdrawPending,
    /// <summary>Already wearing the planned piece; nothing to move.</summary>
    AlreadyWorn,
    /// <summary>Unhinted "any item" slot - deliberately never touched.</summary>
    Untouched,
    /// <summary>The planned piece is nowhere reachable; reported, never guessed at.</summary>
    Missing,
    /// <summary>The piece moved between planning and applying; skipped rather than forcing it.</summary>
    SkippedChanged,
    /// <summary>A game call rejected the move/withdraw; the raw result code is on the step.</summary>
    Failed,
}

/// <summary>
/// One live-apply step's outcome plus its human-facing line (window + log). The line is built
/// here, PURE, so the offline harness can assert the exact wording the player will read; the
/// mover (Adapters/ApplyMover) only picks the status and the raw move result.
/// </summary>
public sealed record ApplyLiveStep
{
    public required ApplyStep Step { get; init; }
    public required ApplyStepStatus Status { get; init; }
    public string Line { get; init; } = "";
    /// <summary>Raw MoveItemSlot return code when Status == Failed (0 otherwise).</summary>
    public int MoveResult { get; init; }

    public static ApplyLiveStep For(ApplyStep step, ApplyStepStatus status, int moveResult = 0)
    {
        var line = status switch
        {
            ApplyStepStatus.Equipped =>
                $"{step.Slot.DisplayName()}: equipped {step.ItemName}",
            ApplyStepStatus.WithdrewThenEquipped =>
                $"{step.Slot.DisplayName()}: withdrew {step.ItemName} from {(step.Method == ObtainMethod.WithdrawFromDresser ? "the glamour dresser" : "the armoire")}, then equipped it",
            ApplyStepStatus.WithdrawPending =>
                $"{step.Slot.DisplayName()}: withdrawing {step.ItemName} from {(step.Method == ObtainMethod.WithdrawFromDresser ? "the glamour dresser" : "the armoire")}...",
            ApplyStepStatus.AlreadyWorn =>
                $"{step.Slot.DisplayName()}: already wearing {step.ItemName}",
            ApplyStepStatus.Untouched =>
                $"{step.Slot.DisplayName()}: {step.Action}",
            ApplyStepStatus.Missing =>
                $"{step.Slot.DisplayName()}: SKIP - {step.Action}",
            ApplyStepStatus.SkippedChanged =>
                $"{step.Slot.DisplayName()}: SKIP - {step.ItemName} moved since the plan was built; press Apply again",
            ApplyStepStatus.Failed =>
                $"{step.Slot.DisplayName()}: FAILED - {step.Action} (game rejected it, code {moveResult})",
            _ => $"{step.Slot.DisplayName()}: unknown",
        };
        return new ApplyLiveStep { Step = step, Status = status, Line = line, MoveResult = moveResult };
    }

    /// <summary>Dye the player must apply by hand after this step ("" when nothing to do). The
    /// live executor never touches dye: no verified dye-apply call exists in this ClientStructs
    /// build, so a planned dye is always a manual reminder, never a silent skip.</summary>
    public string DyeReminder =>
        Step.StainId != 0 && !Step.StainAlreadyOn && Status is ApplyStepStatus.Equipped
            or ApplyStepStatus.WithdrewThenEquipped or ApplyStepStatus.AlreadyWorn
            or ApplyStepStatus.WithdrawPending
                ? $"{Step.Slot.DisplayName()}: dye manually - apply {Step.StainName}"
                : "";
}

/// <summary>
/// The FashionSlot -> destination-slot mapping for the game's equipped-gear container
/// (InventoryType.EquippedItems), in the container's own order: main hand, off hand, head,
/// body, hands, [belt - removed from the game years ago, the container slot stays and stays
/// empty], legs, feet, ears, neck, wrists, ring 1, ring 2, soul crystal. The plugin's
/// FashionSlot enum follows the FashionCheck addon order, so the mapping skips the belt gap.
/// Pure (no game references) so the offline harness can pin it exactly; verified against the
/// FFXIVClientStructs 15.0.3.4 layout, and the apply log's per-slot before/after lines
/// confirm it live on the first real apply.
/// </summary>
public static class ApplyDestinations
{
    public static int EquippedDest(FashionSlot slot) => slot switch
    {
        FashionSlot.Weapon => 0,
        FashionSlot.Head => 2,
        FashionSlot.Body => 3,
        FashionSlot.Hands => 4,
        FashionSlot.Legs => 6,
        FashionSlot.Feet => 7,
        FashionSlot.Ears => 8,
        FashionSlot.Neck => 9,
        FashionSlot.Wrist => 10,
        FashionSlot.RingL => 11,
        FashionSlot.RingR => 12,
        _ => -1,
    };
}

/// <summary>The whole live-apply result: one entry per slot, counts, and the predicted after-total.
/// Steps is a live list: withdraw continuations replace their entry in place when they resolve.</summary>
public sealed record ApplyLiveResult
{
    public required List<ApplyLiveStep> Steps { get; init; }
    /// <summary>The planner's predicted total, carried so the readout and the plan can be
    /// diffed in one place (the harness asserts they match).</summary>
    public required int PredictedTotal { get; init; }
    public bool Reaches80 => PredictedTotal >= 80;
    public int Moved => Steps.Count(s => s.Status is ApplyStepStatus.Equipped or ApplyStepStatus.WithdrewThenEquipped);
    public int Pending => Steps.Count(s => s.Status == ApplyStepStatus.WithdrawPending);
    public int Failed => Steps.Count(s => s.Status == ApplyStepStatus.Failed);
    /// <summary>Manual dye reminders for every planned dye the executor did not (and cannot) apply.</summary>
    public IEnumerable<string> DyeReminders => Steps.Select(s => s.DyeReminder).Where(d => d.Length > 0);
}
