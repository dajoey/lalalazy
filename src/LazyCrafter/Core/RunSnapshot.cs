namespace LazyCrafter.Core;

/// <summary>Where a run is, coarsely. <see cref="Blocked"/> is terminal-but-resumable: nothing the plugin can hand off is left, the player has to buy / fetch something, then press Resume.</summary>
public enum RunState { Idle, Running, Blocked, Done, Failed }

public enum StepKind { Retrieve, Venture, Gather, Craft, Vendor, Market, Manual }

public enum StepState { Pending, Running, Done, Failed, Blocked }

/// <summary>
/// One row of a run, for the Run tab and <c>/lcraft status</c>. <see cref="Name"/> is already resolved (item name or
/// <c>#id</c>); <see cref="Reason"/> is plain English with item ids already swapped for names.
/// </summary>
/// <param name="Reason">"needs market Titanium Ore x15", "GBR made no progress for 10 min", "Artisan did not start within 15 s" - only on Failed / Blocked rows.</param>
/// <param name="ExternalStatus">Only on the Running row: GBR's own status text, "Artisan busy 1:12", "retainer session 0:40".</param>
/// <param name="RecipeId">Crafts only.</param>
public sealed record RunStep(
    StepKind Kind,
    uint ItemId,
    string Name,
    int Quantity,
    StepState State,
    string? Reason,
    string? ExternalStatus,
    uint RecipeId = 0);

/// <summary>Something the player has to do before Resume can make progress.</summary>
/// <param name="EstimatedGil">Market only: unit cost x quantity from the price cache; <c>null</c> when unpriced.</param>
/// <param name="Where">Vendor: "NPC name (Zone X, Y)"; Manual: the source kinds; Venture: the retainer's name.</param>
public sealed record BlockedItem(
    StepKind Kind,
    uint ItemId,
    string Name,
    int Quantity,
    long? EstimatedGil,
    string? Where);

/// <summary>
/// Immutable picture of the current (or last) dispatch run (card t_efde145c, contract v1 agreed with t_c360953f).
/// Replaced wholesale by the dispatcher on every phase change and poll; the UI reads it per draw without touching
/// game state. Lives in Core so the offline probes can render it.
/// </summary>
/// <param name="Phase">The dispatcher's raw phase name ("WaitGather", "Blocked", ...).</param>
/// <param name="PhaseLabel">Human label: Retrieving / Gathering / Crafting / Blocked / Done / Failed / Idle.</param>
/// <param name="Status">The dispatcher's one-line status.</param>
/// <param name="What">"cart" or the single item's name - what was dispatched.</param>
/// <param name="CartNames">Result item names of the cart lines this run came from (empty for single-item runs).</param>
/// <param name="StartedAt">UTC; <see cref="DateTime.MinValue"/> when idle.</param>
/// <param name="Pass">1-based re-plan pass count.</param>
/// <param name="Blocked">Empty unless <see cref="State"/> is <see cref="RunState.Blocked"/> (or Done with leftovers the plugin could not act on).</param>
/// <param name="StoppedReason">Failed / Blocked: why, one sentence.</param>
/// <param name="CanResume">Blocked, or Failed with the cart still held.</param>
public sealed record RunSnapshot(
    RunState State,
    string Phase,
    string PhaseLabel,
    string Status,
    string What,
    IReadOnlyList<string> CartNames,
    DateTime StartedAt,
    DateTime? EndedAt,
    TimeSpan Elapsed,
    int Pass,
    IReadOnlyList<RunStep> Steps,
    IReadOnlyList<BlockedItem> Blocked,
    string? StoppedReason,
    bool CanResume)
{
    public static RunSnapshot Empty { get; } = new(
        RunState.Idle, "Idle", "Idle", "idle", "", Array.Empty<string>(), DateTime.MinValue, null, TimeSpan.Zero, 0,
        Array.Empty<RunStep>(), Array.Empty<BlockedItem>(), null, false);

    /// <summary>
    /// The plain-text report (<c>/lcraft status</c>, the Run tab's Copy report button). One line per step, then the
    /// blocked list. Deterministic so the Probe can assert on it.
    /// </summary>
    public string Report()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("[LazyCrafter] run ").Append(PhaseLabel.ToLowerInvariant());
        if (State != RunState.Idle)
        {
            sb.Append(" - ").Append(What);
            if (CartNames.Count > 0) sb.Append(" (").Append(string.Join(", ", CartNames)).Append(')');
            sb.Append(", pass ").Append(Pass).Append(", ").Append(FormatElapsed(Elapsed)).Append(" elapsed");
            if (Status.Length > 0) sb.Append(": ").Append(Status);
        }
        sb.AppendLine();
        foreach (var s in Steps)
        {
            sb.Append("  ").Append(StateMark(s.State)).Append(' ').Append(KindLabel(s.Kind)).Append(' ')
              .Append(s.Name).Append(" x").Append(s.Quantity);
            if (!string.IsNullOrEmpty(s.ExternalStatus)) sb.Append(" - ").Append(s.ExternalStatus);
            if (!string.IsNullOrEmpty(s.Reason)) sb.Append(" - ").Append(s.Reason);
            sb.AppendLine();
        }
        if (Blocked.Count > 0)
        {
            sb.AppendLine("  needs you:");
            foreach (var b in Blocked)
            {
                sb.Append("    ").Append(KindLabel(b.Kind)).Append(' ').Append(b.Name).Append(" x").Append(b.Quantity);
                if (b.EstimatedGil is { } gil) sb.Append(" (~").Append(gil.ToString("N0")).Append(" gil)");
                if (!string.IsNullOrEmpty(b.Where)) sb.Append(" - ").Append(b.Where);
                sb.AppendLine();
            }
        }
        if (!string.IsNullOrEmpty(StoppedReason)) sb.Append("  ").Append(StoppedReason).AppendLine();
        if (CanResume) sb.AppendLine("  press Resume (or /lcraft resume) once that is done.");
        return sb.ToString().TrimEnd();
    }

    public static string FormatElapsed(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    public static string KindLabel(StepKind k) => k switch
    {
        StepKind.Retrieve => "retrieve",
        StepKind.Venture => "venture",
        StepKind.Gather => "gather",
        StepKind.Craft => "craft",
        StepKind.Vendor => "vendor",
        StepKind.Market => "market",
        _ => "manual",
    };

    private static string StateMark(StepState s) => s switch
    {
        StepState.Pending => "[ ]",
        StepState.Running => "[>]",
        StepState.Done => "[x]",
        StepState.Failed => "[!]",
        _ => "[-]",
    };
}
