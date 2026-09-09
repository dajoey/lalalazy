// 0.1.7.0 (card t_5191608a): pins for the sequential resume-mode stage machine.
using LazyCrafter.Core;

namespace LazyCrafter.Harness;

/// <summary>
/// The stage-machine contract (card t_5191608a): a stage that needs the user emits exactly ONE
/// popup, Resume advances without restarting, no re-emit on later ticks, a Done stage
/// auto-continues, and a Failed reason is recorded once. The empty order is the unattended run:
/// current behavior, no popups ever.
/// </summary>
public static class RunStageTests
{
    public static IEnumerable<(string Name, Func<bool> Check)> Tests
    {
        get
        {
            // ---- one popup per blocked stage ----
            yield return ("a NeedsUser stage surfaces exactly one popup",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan, RunStage.CraftQueue);
                    c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "buy 3x Coal at the vendor, then press Resume");
                    return c.Popup == "buy 3x Coal at the vendor, then press Resume";
                });

            yield return ("the popup does NOT re-emit on later ticks once surfaced",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan, RunStage.CraftQueue);
                    c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "buy at the vendor");
                    var first = c.Popup;
                    c.PopupSurfaced();
                    return first is not null && c.Popup is null;
                });

            yield return ("re-recording the SAME blocked stage every tick never surfaces a second popup",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan, RunStage.CraftQueue);
                    c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "buy at the vendor");
                    c.PopupSurfaced();
                    for (var i = 0; i < 100; i++)
                        c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "buy at the vendor");
                    return c.Popup is null && c.PopupShowing == "buy at the vendor";
                });

            yield return ("a DIFFERENT message on re-block surfaces a new popup (the state changed)",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan, RunStage.CraftQueue);
                    c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "buy at the vendor");
                    c.PopupSurfaced();
                    c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "buy 5x Coal at the vendor");
                    return c.Popup == "buy 5x Coal at the vendor";
                });

            // ---- resume advances, does not restart ----
            yield return ("Resume advances to the next stage without restarting the order",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan, RunStage.CraftQueue);
                    c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "buy at the vendor");
                    var after = c.Advance();
                    return after == RunStage.GatherPlan && c.Popup is null && c.PopupShowing is null;
                });

            yield return ("a Resume that changed nothing can honestly re-block the stage it just left",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan, RunStage.CraftQueue);
                    c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "buy at the vendor");
                    c.Advance();
                    c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "2 items still missing - buy them, then press Resume");
                    return c.Popup == "2 items still missing - buy them, then press Resume";
                });

            // ---- auto-continue: no human needed ----
            yield return ("a Done stage auto-continues to the next stage with no popup",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan, RunStage.CraftQueue);
                    c.Record(RunStage.ShoppingTrip, StageOutcome.Done);
                    return c.Current == RunStage.GatherPlan && c.Popup is null;
                });

            yield return ("the stage after a resumed stage auto-continues when it needs no human",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan, RunStage.CraftQueue);
                    c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "buy at the vendor");
                    c.Advance();
                    c.Record(RunStage.GatherPlan, StageOutcome.Done);
                    return c.Current == RunStage.CraftQueue && c.Popup is null;
                });

            yield return ("recording the craft queue done lands the run in Unattended",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan, RunStage.CraftQueue, RunStage.Unattended);
                    c.Record(RunStage.ShoppingTrip, StageOutcome.Done);
                    c.Record(RunStage.GatherPlan, StageOutcome.Done);
                    c.Record(RunStage.CraftQueue, StageOutcome.Done);
                    return c.Current == RunStage.Unattended && c.IsUnattended;
                });

            yield return ("a cart with no shopping work is unattended from the first wave",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan, RunStage.CraftQueue, RunStage.Unattended);
                    c.Record(RunStage.ShoppingTrip, StageOutcome.Done);
                    c.Record(RunStage.GatherPlan, StageOutcome.Done);
                    return c.Current == RunStage.CraftQueue && c.IsUnattended;
                });

            // ---- failed records once ----
            yield return ("a Failed reason is recorded once and never duplicated",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan, RunStage.CraftQueue);
                    c.Record(RunStage.GatherPlan, StageOutcome.Failed, "GBR refused the list");
                    c.Record(RunStage.GatherPlan, StageOutcome.Failed, "GBR refused the list");
                    return c.Failure == "GBR refused the list";
                });

            yield return ("a Failed stage shows no popup (the chat block carries the reason)",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan, RunStage.CraftQueue);
                    c.Record(RunStage.GatherPlan, StageOutcome.Failed, "GBR refused the list");
                    return c.Popup is null && c.PopupShowing is null;
                });

            // ---- the off switch: an empty order is the unattended run ----
            yield return ("an empty order is unattended from the first tick (the off switch restores today's run)",
                () =>
                {
                    var c = new RunStageController(Array.Empty<RunStage>());
                    c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "buy at the vendor");
                    return c.IsUnattended && c.Popup is null && c.Current == RunStage.Unattended;
                });

            // ---- out-of-order records are ignored ----
            yield return ("a late record for a stage two back is ignored (the run has moved on)",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan, RunStage.CraftQueue);
                    c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "buy");
                    c.Advance();
                    c.Record(RunStage.GatherPlan, StageOutcome.NeedsUser, "gather needs the player");
                    c.Advance();
                    c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "buy AGAIN");
                    return c.Popup is null && c.Current == RunStage.CraftQueue;
                });

            yield return ("a stage not in this run's order is ignored",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.CraftQueue);
                    c.Record(RunStage.GatherPlan, StageOutcome.NeedsUser, "gather");
                    return c.Popup is null;
                });

            yield return ("the modal message names what to do and the Resume button",
                () =>
                {
                    var c = new RunStageController(RunStage.ShoppingTrip, RunStage.GatherPlan);
                    c.Record(RunStage.ShoppingTrip, StageOutcome.NeedsUser, "buy 3x Coal at Audun, then press Resume");
                    return c.PopupShowing!.Contains("press Resume", StringComparison.Ordinal);
                });
        }
    }
}
