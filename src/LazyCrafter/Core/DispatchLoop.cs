namespace LazyCrafter.Core;

/// <summary>
/// The run loop's decisions, kept pure so the harness can drive them (card t_efde145c, Joey's option A).
/// <para>
/// Before 0.1.4.0 a dispatch was ONE pass: the plan was built once, deferrals were decided at build time and never
/// revisited, and the run ended after the last queued craft even when a re-plan would have found more to do (Joey's
/// Alpine Chandelier: the ore was gathered, one nugget was crafted, and the three ingots + the chandelier that were
/// deferred only because the nugget did not exist yet were never attempted - and nothing said so).
/// </para>
/// <para>
/// Now: run a wave (retrieve -> ventures -> gather -> crafts), then re-assess the cart's <b>remaining</b> lines against
/// the live bags and re-plan. While the fresh plan has work the plugin can do on its own (a fetch, a gather, a craft)
/// and the last wave made progress, run the next wave. When nothing is runnable, stop in <see cref="Outcome.Blocked"/>
/// and name what the player has to buy / fetch; Resume re-plans from the bags and carries on with the same cart.
/// A wave that changes nothing (no bag delta, no craft finished) ends the run as Blocked too - never loop on it.
/// </para>
/// <para>
/// A cart line is finished when its root recipe has been crafted <c>Crafts</c> times (<see cref="CraftDone"/>, depth-0
/// crafts only); finished lines drop out of the re-plan, otherwise a completed craft would consume its own result as
/// "missing" and be planned again forever. Sub-crafts need no tracking: their results are in the bags and assess as
/// on-hand.
/// </para>
/// </summary>
public sealed class DispatchLoop
{
    /// <summary>Belt and braces: even with progress every pass, stop here and report.</summary>
    public const int MaxPasses = 12;

    public sealed record CartLine(uint RecipeId, uint ResultItemId, int Crafts);

    public enum Outcome { Wave, Done, Blocked }

    /// <param name="Plan">The plan for this wave (Wave), or the plan that had nothing runnable (Blocked / Done).</param>
    /// <param name="Why">Blocked: one sentence for the chat block and the snapshot.</param>
    public sealed record Decision(Outcome Outcome, DispatchPlan.Plan Plan, string? Why, IReadOnlyList<CartLine> Remaining);

    private readonly Func<IReadOnlyList<CartLine>, DispatchPlan.Plan?> _replan;
    private readonly Func<IEnumerable<uint>, string> _fingerprint;
    private readonly List<CartLine> _lines;
    private readonly Dictionary<uint, int> _doneByRecipe = new();
    private string? _fingerprintBefore;

    /// <param name="replan">Assess the given (remaining) cart lines against the LIVE inventory and build a plan; <c>null</c> when it cannot.</param>
    /// <param name="fingerprint">Bag counts of the given item ids, folded into one string - "did anything move?".</param>
    public DispatchLoop(IEnumerable<CartLine> lines, Func<IReadOnlyList<CartLine>, DispatchPlan.Plan?> replan, Func<IEnumerable<uint>, string> fingerprint)
    {
        _lines = lines.Where(l => l.Crafts > 0).ToList();
        _replan = replan;
        _fingerprint = fingerprint;
    }

    /// <summary>1-based; 0 before <see cref="Begin"/>.</summary>
    public int Pass { get; private set; }

    public DispatchPlan.Plan? Plan { get; private set; }

    public IReadOnlyList<CartLine> Lines => _lines;

    /// <summary>Cart lines with root crafts still to do.</summary>
    public IReadOnlyList<CartLine> Remaining =>
        _lines.Select(l => l with { Crafts = l.Crafts - _doneByRecipe.GetValueOrDefault(l.RecipeId) }).Where(l => l.Crafts > 0).ToList();

    public int CraftsDone(uint recipeId) => _doneByRecipe.GetValueOrDefault(recipeId);

    /// <summary>A depth-0 craft of <paramref name="recipeId"/> finished <paramref name="crafts"/> runs (measured from the bags, not assumed).</summary>
    public void CraftDone(uint recipeId, int crafts)
    {
        if (crafts <= 0) return;
        _doneByRecipe[recipeId] = _doneByRecipe.GetValueOrDefault(recipeId) + crafts;
    }

    /// <summary>First pass: plan from the bags as they are.</summary>
    public Decision Begin() => Advance(progressed: true);

    /// <summary>
    /// A wave finished. <paramref name="progressed"/> is what the executor measured (a craft finished, a fetch
    /// landed); on top of that the bag fingerprint is compared with the one taken when the wave was planned.
    /// </summary>
    public Decision Next(bool progressed) => Advance(progressed: progressed);

    /// <summary>The player pressed Resume: re-plan from the bags and treat it as progress (a still-blocked cart reports the same block again, not "no progress").</summary>
    public Decision Resume()
    {
        _fingerprintBefore = null;
        return Advance(progressed: true);
    }

    private Decision Advance(bool progressed)
    {
        var remaining = Remaining;
        if (remaining.Count == 0)
        {
            Pass++;
            return new Decision(Outcome.Done, Plan ?? EmptyPlan, null, remaining);
        }

        var fresh = _replan(remaining);
        if (fresh is null)
        {
            Pass++;
            return new Decision(Outcome.Blocked, Plan ?? EmptyPlan, "could not rebuild the plan (game data still loading?)", remaining);
        }

        var fp = _fingerprint(ItemsOf(fresh, remaining));
        if (!progressed && _fingerprintBefore is not null && fp != _fingerprintBefore) progressed = true;
        _fingerprintBefore = fp;
        Plan = fresh;
        Pass++;

        if (Pass > MaxPasses)
            return new Decision(Outcome.Blocked, fresh, $"{MaxPasses} passes and the cart is still not finished - something keeps needing another round", remaining);

        var runnable = fresh.Gathers.Count + fresh.Crafts.Count + fresh.Retrievals.Count > 0;
        if (!runnable)
            return new Decision(Outcome.Blocked, fresh, Describe(fresh), remaining);
        if (!progressed)
            return new Decision(Outcome.Blocked, fresh, "no progress this pass - the last round changed nothing in your bags", remaining);
        return new Decision(Outcome.Wave, fresh, null, remaining);
    }

    /// <summary>Every item id whose bag count tells us whether the next wave did anything.</summary>
    public static IEnumerable<uint> ItemsOf(DispatchPlan.Plan plan, IReadOnlyList<CartLine> remaining)
    {
        var ids = new HashSet<uint>();
        foreach (var l in remaining) ids.Add(l.ResultItemId);
        foreach (var g in plan.Gathers) ids.Add(g.ItemId);
        foreach (var c in plan.Crafts) ids.Add(c.ResultItemId);
        foreach (var r in plan.Retrievals) ids.Add(r.ItemId);
        foreach (var d in plan.Deferred) ids.Add(d.ResultItemId);
        foreach (var p in plan.Vendor) ids.Add(p.ItemId);
        foreach (var p in plan.Market) ids.Add(p.ItemId);
        // Currency-shop items count for the same reason market items do: the player can go and get one between
        // waves, and if this set missed them the stall guard would read "nothing changed" and end the run as
        // blocked while the material was in fact arriving in the bags (card t_b431de3a).
        foreach (var c in plan.CurrencyShop) ids.Add(c.ItemId);
        foreach (var m in plan.Manual) ids.Add(m.ItemId);
        foreach (var v in plan.Ventures) ids.Add(v.ItemId);
        return ids;
    }

    /// <summary>One line: what a plan with nothing runnable is waiting on. Item ids as <c>#id</c>; the adapter swaps names in.</summary>
    public static string Describe(DispatchPlan.Plan plan)
    {
        var parts = new List<string>();
        if (plan.Market.Count > 0) parts.Add($"{plan.Market.Count} market-board item{(plan.Market.Count == 1 ? "" : "s")}");
        if (plan.Vendor.Count > 0) parts.Add($"{plan.Vendor.Count} vendor item{(plan.Vendor.Count == 1 ? "" : "s")}");
        if (plan.CurrencyShop.Count > 0) parts.Add($"{plan.CurrencyShop.Count} currency-shop item{(plan.CurrencyShop.Count == 1 ? "" : "s")}");
        if (plan.Manual.Count > 0) parts.Add($"{plan.Manual.Count} item{(plan.Manual.Count == 1 ? "" : "s")} with no automatic source");
        if (plan.Ventures.Count > 0) parts.Add($"{plan.Ventures.Count} venture item{(plan.Ventures.Count == 1 ? "" : "s")} still out with the retainers");
        var crafts = plan.Deferred.Count;
        var head = crafts > 0 ? $"{crafts} craft{(crafts == 1 ? "" : "s")} still blocked" : "nothing left the plugin can do on its own";
        return parts.Count == 0 ? head : $"{head} - waiting on {string.Join(", ", parts)}";
    }

    private static readonly DispatchPlan.Plan EmptyPlan = new([], [], [], [], [], [], []);
}
