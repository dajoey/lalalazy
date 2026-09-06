namespace LazyCrafter.Core;

/// <summary>
/// Why a craft that produced nothing produced nothing, and how to say it truthfully (card t_0b4d8b2c).
///
/// <para><b>The defect this exists to prevent.</b> Joey's 2026-09-06 11:58 run, 0.1.6.6. He had just finished a
/// shopping cart at the market board, so the board window still owned the client's input. Artisan bounced every
/// craft instantly:</para>
/// <code>
/// 11:58:19 [LazyCrafter] Artisan: crafting Adamantite Nugget x98 (1/2).
/// 11:58:21 [Artisan]     Error Warnings [4]: Unable to execute command while occupied.
/// 11:58:21 [LazyCrafter] Artisan: Adamantite Nugget - expected 98, made 0.
/// </code>
/// <para>
/// <c>WaitCraftEnd</c> filed that as a bare <c>expected 98, made 0</c> - <b>the cause was discarded</b> - so on the
/// next pass <c>DispatchPlan.BagsShortfall</c> observed the intermediates were simply absent and emitted its
/// perfectly correct "not in your bags / retrieve from elsewhere" branch. Correct code, poisoned input: the run
/// told him to go and fetch two materials that had never existed and were never listed for sale, and the bell walk
/// took him there. The materials were absent because <i>the plugin had blocked itself</i>.
/// </para>
///
/// <para><b>Scope.</b> This fixes the DIAGNOSIS only. Nothing here waits, retries, polls or auto-stops on the
/// client being busy - that is a behaviour decision that belongs to Joey and is deliberately not taken here. The
/// craft is still attempted, still fails, still counts as failed; only what we then <i>say and conclude</i> changes.
/// </para>
///
/// <para><b>Pure Core on purpose.</b> This is a reporting bug end to end, so every check in the harness asserts on
/// the RENDERED text. An assertion on the internal shortfall list would have stayed green right through the defect
/// - the list was always correct, the sentence built from it was the lie.</para>
/// </summary>
public static class CraftDiagnosis
{
    /// <summary>Fallback wording when the client reported itself busy but no window we know by name was open.</summary>
    public const string UnknownWindow = "a game window";

    /// <summary>Why a craft produced fewer units than expected.</summary>
    public enum Cause
    {
        /// <summary>Genuinely short: the craft ran and did not deliver (bags full, materials consumed elsewhere, aborted).</summary>
        Shortfall,

        /// <summary>The game refused the command outright because a UI window owned the client's input. Nothing was consumed and nothing is missing.</summary>
        ClientBusy,
    }

    /// <summary>
    /// The result items of crafts that were refused because the client was busy, with the window that was open.
    /// <para>
    /// Per run, reset by <c>StartRun</c>. Membership is what lets a later bags shortfall tell "this material is
    /// sitting on a retainer" apart from "this material was never made because we blocked ourselves" - two states
    /// that are indistinguishable from the bags alone, which is exactly how the defect happened.
    /// </para>
    /// </summary>
    public sealed class BlockedCrafts
    {
        private readonly Dictionary<uint, string> _windows = new();

        /// <summary>Record that a craft of <paramref name="resultItemId"/> was refused with <paramref name="window"/> open. Last one wins.</summary>
        public void Note(uint resultItemId, string? window) =>
            _windows[resultItemId] = string.IsNullOrWhiteSpace(window) ? UnknownWindow : window!;

        /// <summary>The window that was open when this item's craft was refused, or <c>null</c> when it was never refused that way.</summary>
        public string? Window(uint itemId) => _windows.TryGetValue(itemId, out var w) ? w : null;

        public bool Contains(uint itemId) => _windows.ContainsKey(itemId);

        public bool IsEmpty => _windows.Count == 0;

        /// <summary>The distinct windows recorded this run, in no particular order - for the end-of-run line.</summary>
        public IReadOnlyList<string> Windows => _windows.Values.Distinct(StringComparer.Ordinal).ToList();

        public void Clear() => _windows.Clear();
    }

    /// <summary>
    /// The step's failure reason. The <see cref="Cause.ClientBusy"/> wording is deliberately unmistakable: the
    /// bare <c>expected N, made 0</c> it replaces is what made a blocked window and a genuine shortage read the
    /// same to every downstream consumer.
    /// </summary>
    public static string StepReason(Cause cause, int expected, int made, string? window) => cause switch
    {
        Cause.ClientBusy =>
            $"the game refused the craft command while {window ?? UnknownWindow} was open - nothing was made and nothing is missing (expected {expected}, made {made})",
        _ => $"expected {expected}, made {made}",
    };

    /// <summary>The chat line for a craft the game would not accept. Names the window, and says what to do.</summary>
    public static string BusyCraftLine(string itemName, int expected, int made, string? window) =>
        $"{itemName} - the game would not accept the craft command while {window ?? UnknownWindow} was open" +
        $" (expected {expected}, made {made}). Nothing is missing from your bags - close it, then press Resume (or /lcraft resume).";

    /// <summary>
    /// A bags shortfall split into the two things it can mean.
    /// </summary>
    /// <param name="Genuine">Materials that really are somewhere else (or really are short): the retrieve/unlist path is correct for these.</param>
    /// <param name="Phantom">
    /// Materials that are absent ONLY because a craft that would have produced them was refused while the client
    /// was busy. These are not "elsewhere", are not on the market board, and must never reach the retrieve or
    /// unlist paths - saying they are is a factual error.
    /// </param>
    public sealed record Shortfall(
        IReadOnlyList<DispatchPlan.Retrieve> Genuine,
        IReadOnlyList<(DispatchPlan.Retrieve Item, string Window)> Phantom)
    {
        public bool HasGenuine => Genuine.Count > 0;
        public bool HasPhantom => Phantom.Count > 0;

        /// <summary>The blocked-craft windows named in this shortfall, deduplicated, for one sentence.</summary>
        public IReadOnlyList<string> Windows => Phantom.Select(p => p.Window).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Split a <see cref="DispatchPlan.BagsShortfall"/> result against the crafts we know were refused.
    /// <para>
    /// <b>Both conditions are required</b> for an entry to be a phantom: the item is one a refused craft would have
    /// produced, AND nothing the plugin can see is holding any of it (<c>Where.Count == 0</c>, i.e. the retrieve
    /// line would have said the invented word "elsewhere"). If a retainer really is holding some, the retrieval is
    /// real and stays - suppressing it would trade one wrong answer for another. That pair is exactly the shape of
    /// the 11:58 evidence: <c>PlacesFor</c> returned an empty list and <c>Detail</c> rendered "98 not in your bags".
    /// </para>
    /// </summary>
    public static Shortfall SplitShortfall(IReadOnlyList<DispatchPlan.Retrieve> shortfall, BlockedCrafts blocked)
    {
        var genuine = new List<DispatchPlan.Retrieve>();
        var phantom = new List<(DispatchPlan.Retrieve, string)>();
        foreach (var s in shortfall)
        {
            var window = s.Where.Count == 0 ? blocked.Window(s.ItemId) : null;
            if (window is null) genuine.Add(s);
            else phantom.Add((s, window));
        }
        return new Shortfall(genuine, phantom);
    }

    /// <summary>
    /// The chat block printed when a craft is refused at the bags guard. Genuine shortfalls keep the existing
    /// "not in your bags / retrieve from elsewhere" wording; phantoms get the truth instead, and are explicitly
    /// told NOT to be retrieved so the sentence cannot be misread as a shorter version of the same instruction.
    /// </summary>
    public static IReadOnlyList<string> RefusalLines(string recipeName, Shortfall split, Func<uint, string> name)
    {
        var lines = new List<string>();
        if (split.HasGenuine)
        {
            lines.Add($"Artisan craft of {recipeName} refused: " +
                      string.Join(", ", split.Genuine.Select(s => $"{name(s.ItemId)} x{s.Quantity} is not in your bags ({s.Detail})")) + ".");
            lines.Add("retrieve before crafting: " +
                      string.Join("; ", split.Genuine.Select(s => $"{name(s.ItemId)} x{s.Quantity} from {s.Places}")) + ".");
        }
        if (split.HasPhantom)
        {
            var windows = split.Windows;
            var where = windows.Count == 0 ? UnknownWindow : string.Join(" / ", windows);
            lines.Add($"Artisan craft of {recipeName} refused: " +
                      string.Join(", ", split.Phantom.Select(p => $"{name(p.Item.ItemId)} x{p.Item.Quantity} was never made")) +
                      $" - the game would not accept the craft command while {where} was open.");
            lines.Add("nothing to retrieve for " +
                      string.Join(", ", split.Phantom.Select(p => name(p.Item.ItemId))) +
                      $": it is not on a retainer and it is not on the market board. Close {where}, then press Resume (or /lcraft resume).");
        }
        return lines;
    }

    /// <summary>
    /// The deferral / step reason for a refused craft.
    /// <para>
    /// The <c>retrieve #id</c> token is load-bearing: <see cref="RetainerBatch.Queue"/> selects deferrals whose
    /// reason contains it and queues those recipes into an Artisan retainer bell session. A phantom must therefore
    /// NOT carry it, or the next wave opens a bell session to fetch a material that does not exist anywhere.
    /// </para>
    /// </summary>
    public static string DeferralReason(Shortfall split)
    {
        var parts = new List<string>();
        foreach (var s in split.Genuine) parts.Add($"retrieve #{s.ItemId} x{s.Quantity} (from {s.Places})");
        foreach (var (item, window) in split.Phantom) parts.Add($"a blocked craft of #{item.ItemId} x{item.Quantity} ({window} was open)");
        return "needs " + string.Join(", ", parts);
    }

    /// <summary>
    /// The guard that keeps a phantom out of the unlist path for good (<c>BlockedListings.Summarise</c> ->
    /// "pull these off sale" -> the Lifestream summoning-bell walk).
    /// <para>
    /// Applied to the dispatcher's <c>_unfetched</c> list before it is summarised. A material a refused craft never
    /// produced, with no place known to hold any of it, is not listed for sale by anybody - putting it in that list
    /// would name a retainer that is not holding it, and would fire a bell walk for a problem that does not exist.
    /// This is belt and braces: the routing today cannot produce such an entry, and this makes sure a future edit
    /// still cannot.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(DispatchPlan.Retrieve Item, string Why)> WithoutPhantoms(
        IEnumerable<(DispatchPlan.Retrieve Item, string Why)> unfetched, BlockedCrafts blocked)
    {
        var kept = new List<(DispatchPlan.Retrieve, string)>();
        foreach (var entry in unfetched)
        {
            if (entry.Item is null) continue;
            if (entry.Item.Where.Count == 0 && blocked.Contains(entry.Item.ItemId)) continue;
            kept.Add(entry);
        }
        return kept;
    }

    /// <summary>
    /// The end-of-run summary line for crafts the game refused, or <c>null</c> when there were none.
    /// Printed instead of leaving the player with a retrieve errand he cannot perform.
    /// </summary>
    public static string? EndOfRunLine(BlockedCrafts blocked)
    {
        if (blocked.IsEmpty) return null;
        var windows = blocked.Windows;
        var where = windows.Count == 0 ? UnknownWindow : string.Join(" / ", windows);
        return $"some crafts never ran: the game refused the craft command while {where} was open. " +
               "That is not a missing material - close it and press Resume (or /lcraft resume).";
    }
}
