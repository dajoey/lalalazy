using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Catalog;

public enum CatalogTab { Now, Easy, SomeEffort, RealEffort, Leveling, LogCompletion, Undersupplied }

public enum SortKey { Name, Job, Level, Craftable, MarginCash, MarginMarket, PerDay, Velocity, Saturation, Scrip, Desynth, Tier, Missing, Exp, CashCost, Listings }

/// <summary>
/// What the window wants to see. A value type so the service can tell "same request, nothing to do" from a real
/// change without the UI keeping any bookkeeping; every filter from Plan §Phase 4 task 2 is a field here.
/// </summary>
public sealed record ViewRequest(
    CatalogTab Tab,
    uint JobFilter,
    bool HqOnly,
    float MinVelocity,
    bool HideUntradeable,
    string Search,
    SortKey Sort,
    bool Descending,
    uint LevelingJob,
    double UndersuppliedMinVelocity,
    int UndersuppliedMaxListings,
    bool ShowAboveLevel)
{
    public static SortKey DefaultSort(CatalogTab tab) => tab switch
    {
        CatalogTab.Leveling => SortKey.Exp,
        CatalogTab.LogCompletion => SortKey.CashCost,
        CatalogTab.Undersupplied => SortKey.Velocity,
        _ => SortKey.PerDay,
    };

    public static bool DefaultDescending(CatalogTab tab) => tab != CatalogTab.LogCompletion;
}

/// <summary>The rows for one <see cref="ViewRequest"/> against one snapshot generation. Immutable.</summary>
public sealed record CatalogView(ViewRequest Request, int Generation, IReadOnlyList<CatalogRow> Rows, IReadOnlyDictionary<uint, UndersuppliedItem>? Undersupplied)
{
    public static readonly CatalogView Empty = new(
        new ViewRequest(CatalogTab.Now, 0, false, 0, false, "", SortKey.PerDay, true, 0, 3, 2, false), -1, Array.Empty<CatalogRow>(), null);
}

/// <summary>Pure filter + sort. Runs off the framework thread; nothing here touches ImGui or the game.</summary>
public static class ViewBuilder
{
    public static CatalogView Build(CatalogSnapshot snap, ViewRequest req, IGameData data, RecipeGraph graph, IPriceSource prices)
    {
        IEnumerable<CatalogRow> rows = snap.Rows;
        IReadOnlyDictionary<uint, UndersuppliedItem>? under = null;

        switch (req.Tab)
        {
            case CatalogTab.Now: rows = rows.Where(r => r.Tier == EffortTier.Now); break;
            case CatalogTab.Easy: rows = rows.Where(r => r.Tier == EffortTier.Easy); break;
            case CatalogTab.SomeEffort: rows = rows.Where(r => r.Tier == EffortTier.SomeEffort); break;
            case CatalogTab.RealEffort: rows = rows.Where(r => r.Tier >= EffortTier.RealEffort); break;
            case CatalogTab.Leveling:
                rows = rows.Where(r => r.JobId == req.LevelingJob && r.Tier <= EffortTier.Easy && r.ExpPerCraft > 0);
                break;
            case CatalogTab.LogCompletion: rows = rows.Where(r => !r.LogComplete); break;
            case CatalogTab.Undersupplied:
                var finder = new UndersuppliedFinder(data, graph) { MinVelocity = req.UndersuppliedMinVelocity, MaxListings = req.UndersuppliedMaxListings };
                var hits = finder.Find(snap.Rows.Select(r => r.ResultItemId), prices, craftableOnly: true)
                                 .GroupBy(h => h.ItemId).ToDictionary(g => g.Key, g => g.First());
                under = hits;
                rows = rows.Where(r => hits.ContainsKey(r.ResultItemId));
                break;
        }

        if (req.JobFilter != 0 && req.Tab != CatalogTab.Leveling) rows = rows.Where(r => r.JobId == req.JobFilter);
        // Scope §3.1: the recipe universe is the jobs you have at >= recipe level unless the toggle says otherwise.
        // When not logged in (no job levels at all) everything shows, so the window is still useful at the title screen.
        if (!req.ShowAboveLevel && snap.Jobs.Count > 0) rows = rows.Where(r => r.JobLevel > 0 && r.Level <= r.JobLevel);
        if (req.HqOnly) rows = rows.Where(r => r.CanBeHq);
        if (req.HideUntradeable) rows = rows.Where(r => r.Marketable);
        if (req.MinVelocity > 0) rows = rows.Where(r => (r.Est(req.HqOnly)?.Velocity ?? 0) >= req.MinVelocity);
        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var s = req.Search.Trim();
            rows = rows.Where(r => r.Name.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        var list = rows.ToList();
        Sort(list, req, under);
        return new CatalogView(req, snap.Generation, list, under);
    }

    private static void Sort(List<CatalogRow> list, ViewRequest req, IReadOnlyDictionary<uint, UndersuppliedItem>? under)
    {
        var hq = req.HqOnly;
        var desc = req.Descending;
        // Numeric keys: nulls always sink to the bottom regardless of direction; ties by name for stability.
        int Cmp(double? a, double? b)
        {
            if (a is null && b is null) return 0;
            if (a is null) return 1;
            if (b is null) return -1;
            var c = a.Value.CompareTo(b.Value);
            return desc ? -c : c;
        }
        int CmpS(string a, string b) { var c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase); return desc ? -c : c; }

        Comparison<CatalogRow> cmp = req.Sort switch
        {
            SortKey.Name => (x, y) => CmpS(x.Name, y.Name),
            SortKey.Job => (x, y) => { var c = CmpS(x.Job, y.Job); return c != 0 ? c : Cmp(x.Level, y.Level); },
            SortKey.Level => (x, y) => Cmp(x.Level, y.Level),
            SortKey.Craftable => (x, y) => Cmp(x.HowMany, y.HowMany),
            SortKey.MarginCash => (x, y) => Cmp(x.Est(hq)?.MarginCash, y.Est(hq)?.MarginCash),
            SortKey.MarginMarket => (x, y) => Cmp(x.Est(hq)?.MarginMarket, y.Est(hq)?.MarginMarket),
            SortKey.PerDay => (x, y) => Cmp(PerDay(x, hq), PerDay(y, hq)),
            SortKey.Velocity => (x, y) => Cmp(under is not null ? Under(under, x)?.Velocity : x.Est(hq)?.Velocity, under is not null ? Under(under, y)?.Velocity : y.Est(hq)?.Velocity),
            SortKey.Saturation => (x, y) => Cmp(Sat(x, hq), Sat(y, hq)),
            SortKey.Scrip => (x, y) => Cmp(x.Scrip > 0 ? x.Scrip : null, y.Scrip > 0 ? y.Scrip : null),
            SortKey.Desynth => (x, y) => Cmp(x.Desynth, y.Desynth),
            SortKey.Tier => (x, y) => Cmp(TierRank(x.Tier), TierRank(y.Tier)),
            SortKey.Missing => (x, y) => Cmp(x.MissingCount, y.MissingCount),
            SortKey.Exp => (x, y) => Cmp(x.ExpPerCraft, y.ExpPerCraft),
            SortKey.CashCost => (x, y) => Cmp(CashCost(x, hq), CashCost(y, hq)),
            SortKey.Listings => (x, y) => Cmp(under is not null ? Under(under, x)?.Listings : x.Est(hq)?.Listings, under is not null ? Under(under, y)?.Listings : y.Est(hq)?.Listings),
            _ => (x, y) => CmpS(x.Name, y.Name),
        };
        list.Sort((x, y) =>
        {
            var c = cmp(x, y);
            return c != 0 ? c : string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static UndersuppliedItem? Under(IReadOnlyDictionary<uint, UndersuppliedItem> d, CatalogRow r) => d.TryGetValue(r.ResultItemId, out var u) ? u : null;
    private static double? PerDay(CatalogRow r, bool hq) => r.Est(hq) is { RevenueKnown: true } e ? e.PerDay : null;
    private static double? Sat(CatalogRow r, bool hq) => r.Est(hq) is { } e && e.Velocity > 0 ? e.SaturationDays : null;
    /// <summary>Cost is only meaningful when every material was priced (or on hand); a lower bound sorts last.</summary>
    private static double? CashCost(CatalogRow r, bool hq) => r.Est(hq) is { CostComplete: true } e ? e.CashCost : null;
    private static int TierRank(EffortTier t) => t == EffortTier.Blocked ? 4 : (int)t;
}
