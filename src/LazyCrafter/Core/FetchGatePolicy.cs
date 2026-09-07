namespace LazyCrafter.Core;

/// <summary>
/// The queue-time decisions of the retainer fetch (0.1.6.13, card t_3161fa75), as pure data and sentences so
/// the offline harness can pin them. Three field states that the 0.1.6.12 gate all read as a green light:
///
/// <para>
/// <b>A thrown preflight.</b> Artisan's own bell scan (<c>GetReachableRetainerBell</c>) throws a
/// NullReferenceException while the character is still mid-teleport; <c>RetainerFetch.SessionPreflight</c>
/// catches it and returns it as the string "could not inspect Artisan's retainer state (...)". The gate used
/// to treat any non-bell refusal as Proceed and queued the batch session blind - the 2026-09-07 13:01 run
/// then idled at the market board, 0/7, until the player stopped it. An un-inspectable Artisan state is
/// exactly when nothing may be queued: the verdict is Hold (with the throttled bell walk and the heartbeat)
/// until the cap, and only then a refusal carrying the real reason.
/// </para>
///
/// <para>
/// <b>A Lifestream trip under way.</b> The walk fires <c>/li mb</c> and the next 400 ms poll can reach the
/// queue call while the character is still teleporting. While Lifestream is busy the gate holds without even
/// asking the preflight - the preflight is the completion signal, and mid-trip it has no answer that can be
/// trusted (the trip is what produced the throw above).
/// </para>
///
/// <para>
/// <b>A window that owns the client.</b> <see cref="ShouldHoldFetch"/> extends the craft path's
/// close-the-window hold (card t_ee6f7bf5) to the fetch phases - a bell session cannot run while a window
/// owns the client - while deliberately ignoring what a WORKING session opens or rides on: the retainer list,
/// a retainer's inventory, the quantity prompt, dialogue clicks, and the zone-change flag of the trip itself.
/// Gating on those would deadlock the dispatcher against its own session, the same lesson as the crafting
/// conditions in <see cref="ClientWaitPolicy"/>.
/// </para>
///
/// <para>
/// 0.1.6.15 (Helm t-joey-1788808881825): the walk itself now has two sentences. The old single
/// <see cref="TripStatus"/>/<see cref="TripHeartbeat"/> wording ("walking to a summoning bell ...") was
/// written when the walk WAS a market-board trip, and the 0.1.6.14 run showed what that ambiguity costs - the
/// character stood at the board while the status claimed a bell errand. The status now names the destination
/// ("the inn room"), and the two recovery states the walk can sit in get their own lines: a board the walk
/// itself opened (<see cref="BoardGateStatus"/>, never said when the plan has no market shopping to do - the
/// wrong-NPC case) and a board the player opened on top of the errand (<see cref="BoardHeldStatus"/>, the
/// plain close-it line, unchanged in shape). The bell-miss cap refusal (<see cref="GaveUp"/>) keeps its exact
/// 0.1.6.12 wording - it was checked against the harness before this card and the pin stays.
/// </para>
/// </summary>
public static class FetchGatePolicy
{
    /// <summary>The hold cap the queue-time bell gate shipped with in 0.1.6.12 - unchanged; only what counts as holdable widened.</summary>
    public static readonly TimeSpan HoldCap = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How long a batch retainer session may sit <c>Busy()</c> without moving anything into the bags before
    /// the dispatcher stops the run with the reason instead of idling until the player presses Stop.
    /// </summary>
    public static readonly TimeSpan BatchStallLimit = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The prefix <c>RetainerFetch.SessionPreflight</c> returns when its reflection into Artisan throws -
    /// classified by prefix because Core cannot reference the adapter. Change one, change both.
    /// </summary>
    public const string PreflightThrewPrefix = "could not inspect";

    /// <summary>What the queue-time gate decided. RefuseNoWalk is 0.1.6.12's press-time refusal; RefuseCap is the 3-minute give-up.</summary>
    public enum FetchVerdict { Queue, Hold, RefuseNoWalk, RefuseCap }

    /// <summary>The one blocker the automation can walk off - a reachable-bell miss (0.1.6.12's rule).</summary>
    public static bool BellOnly(string preflight) =>
        preflight.Contains("summoning bell", StringComparison.OrdinalIgnoreCase);

    public static bool PreflightThrew(string preflight) =>
        preflight.StartsWith(PreflightThrewPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The gate decision. Order matters: a Lifestream trip under way holds BEFORE the preflight is asked
    /// (mid-trip its answer is not trustworthy), a preflight that neither threw nor names the bell is a dead
    /// end the queue call itself will surface (unchanged from 0.1.6.12), and every hold is bounded by
    /// <see cref="HoldCap"/>.
    /// </summary>
    public static FetchVerdict Decide(string? preflight, bool lifestreamBusy, TimeSpan held, bool walkPossible)
    {
        if (lifestreamBusy) return held >= HoldCap ? FetchVerdict.RefuseCap : FetchVerdict.Hold;
        if (preflight is null) return FetchVerdict.Queue;
        if (!PreflightThrew(preflight) && !BellOnly(preflight)) return FetchVerdict.Queue;
        if (BellOnly(preflight) && !walkPossible) return FetchVerdict.RefuseNoWalk;
        return held >= HoldCap ? FetchVerdict.RefuseCap : FetchVerdict.Hold;
    }

    /// <summary>The red refusal when the cap ends a preflight hold, carrying the live reason. The bell wording is byte-identical to 0.1.6.12's.</summary>
    public static string GaveUp(string? preflight, TimeSpan cap) =>
        PreflightThrew(preflight ?? "")
            ? $"gave up trying to start the retainer fetch after {(int)cap.TotalMinutes} minutes ({preflight})."
            : $"gave up waiting for a reachable summoning bell after {(int)cap.TotalMinutes} minutes ({preflight}).";

    /// <summary>The red refusal when the summoning-bell walk itself never finished inside the cap.</summary>
    public static string GaveUpOnTrip(TimeSpan cap) =>
        $"gave up waiting for the summoning-bell walk to finish after {(int)cap.TotalMinutes} minutes - walk to a bell by hand and press Dispatch again.";

    /// <summary>The blocked-run reason when a batch session sits busy with zero bag movement (0.1.6.13, defect 4).</summary>
    public static string BatchStallLine(TimeSpan limit) =>
        $"the retainer fetch ran for {(int)limit.TotalMinutes} minutes without moving anything into the bags (a dialogue may be waiting, or the bell was interrupted) - close it and press Resume (or /lcraft resume) to continue the same cart";

    /// <summary>Status while the walk to the bell is under way (0.1.6.15 wording: the destination is the inn room's bell; "the nearest market board" was the old destination and the bug).</summary>
    public static string TripStatus() => "walking to the summoning bell in the inn room";

    /// <summary>Heartbeat while held for the walk (0.1.6.15 wording; names the bell, not the board).</summary>
    public static string TripHeartbeat() => "walking to the summoning bell in the inn room so the retainer fetch can run";

    /// <summary>Status while the walk is waiting out a market board that the WALK ITSELF opened (0.1.6.15, Helm t-joey-1788808881825). Only valid when the plan has market shopping to do; the wrong-NPC case must never produce this line.</summary>
    public static string BoardGateStatus() => "waiting - the trip to the bell goes through the market board plaza; close the market board to continue";

    /// <summary>The one normal chat line for the same state.</summary>
    public static string BoardGateLine() => "waiting - the bell trip passes the market board plaza; close the market board to continue";

    /// <summary>Status while a board the PLAYER opened holds the fetch (the 0.1.6.13 close-the-window hold, unchanged shape).</summary>
    public static string BoardHeldStatus(TimeSpan held) => $"waiting - the market board ({held:m\\:ss})";

    /// <summary>Status while the walk is waiting out a board the plan has NO shopping for - the wrong-NPC state (0.1.6.15). The run recovers by itself; nothing is asked of the player.</summary>
    public static string WrongBoardStatus() => "waiting out a market board the run did not plan to open - closing it and carrying on";

    /// <summary>Labels - exactly as <c>ClientReadiness.BusyBecause()</c> renders them - that the fetch phases must NOT
    /// hold on. The first group is what a working retainer session opens or rides on: the session we drive
    /// opens the retainer list, works inside a retainer's inventory, types the quantity and clicks the
    /// dialogues. The last is the zone-change flag the summoning-bell trip itself raises (the trip is held on
    /// <c>Lifestream.IsBusy()</c> instead, and the cap bounds it). Anything else a player opens - the market
    /// board above all - holds the fetch with the close-it line.
    /// </summary>
    public static readonly string[] FetchHoldIgnoredLabels =
    [
        "the summoning bell",          // OccupiedSummoningBell - the session's own menu at the bell
        "the retainer bell",           // RetainerList - the session opens it itself
        "a retainer's inventory",      // InventoryRetainer - the session works inside it
        "a quantity input prompt",     // InputNumeric - the session types into it
        "a dialogue box",              // Talk / SelectOk - the session clicks through them
        "a dialogue choice",           // SelectString / SelectIconString - the session picks the retainer
        "a yes/no prompt",             // SelectYesno - the session confirms its own quit
        "a zone change",               // BetweenAreas - in transit, not closable
    ];

    /// <summary>True when the fetch phases should hold on this <c>BusyBecause()</c> sample.</summary>
    public static bool ShouldHoldFetch(string? busyBecause) =>
        busyBecause is not null && !FetchHoldIgnoredLabels.Contains(busyBecause);

    /// <summary>The blocked-run reason when a window held the fetch past the cap - worded for the fetch, not the craft.</summary>
    public static string FetchTimeoutReason(string? busyBecause, TimeSpan cap) =>
        $"{Label(busyBecause)} blocked the retainer fetch for {(int)cap.TotalMinutes} minutes - close it and press Resume (or /lcraft resume) to continue the same cart";

    /// <summary>The red chat line for the same timeout.</summary>
    public static string FetchTimeoutLine(string? busyBecause, TimeSpan cap) =>
        $"stopped - {FetchTimeoutReason(busyBecause, cap)}.";

    private static string Label(string? busyBecause) =>
        string.IsNullOrWhiteSpace(busyBecause) ? CraftDiagnosis.UnknownWindow : busyBecause;
}
