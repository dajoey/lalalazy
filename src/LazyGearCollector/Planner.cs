using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyGearCollector;

/// <summary>
/// Turns "what I own" plus "what the shops charge" into "what's left to do". All arithmetic is
/// derived from the shop graph, so the numbers are the game's, not a maintained table.
/// </summary>
public sealed class Planner
{
    private readonly OwnershipScanner _own;
    private readonly ShopGraph _shops;

    public Planner(OwnershipScanner own, ShopGraph shops)
    {
        _own = own;
        _shops = shops;
    }

    public PiecePlan Plan(PieceChain chain, int targetTier)
    {
        var plan = new PiecePlan { Piece = chain, TargetTier = targetTier, OwnedTier = -1 };

        // Highest tier of this piece we hold anywhere we can see.
        foreach (var tier in chain.Tiers.OrderByDescending(t => t.Tier))
        {
            if (_own.TotalCount(tier.ItemId) > 0) { plan.OwnedTier = tier.Tier; break; }
        }

        if (plan.Complete) return plan;

        // A free trade-up from another family can beat buying the base piece outright.
        var shortcutTier = DetectShortcut(chain, plan, targetTier);

        var startTier = Math.Max(plan.OwnedTier, shortcutTier);

        // Own nothing and no shortcut: buy the base piece first.
        if (startTier < 0)
        {
            var basePath = PurchasePath(chain.Tier(0));
            if (basePath != null) Add(plan.Remaining, basePath.MaterialCosts);
            startTier = 0;
        }

        // Then pay for each upgrade step up to the target.
        for (var t = startTier + 1; t <= targetTier; t++)
        {
            var node = chain.Tier(t);
            if (node == null) continue;
            var upgrade = node.Paths.FirstOrDefault(p => p.UpgradeFromItemId != 0);
            if (upgrade != null) Add(plan.Remaining, upgrade.MaterialCosts);
        }

        return plan;
    }

    /// <summary>
    /// Looks for a tier reachable by handing in equipment you already own from another family,
    /// including the two-step case where the trade-in item is itself an upgrade of something you hold.
    /// </summary>
    private int DetectShortcut(PieceChain chain, PiecePlan plan, int targetTier)
    {
        var best = -1;

        foreach (var node in chain.Tiers.Where(t => t.Tier <= targetTier).OrderByDescending(t => t.Tier))
        {
            foreach (var path in node.Paths.Where(p => p.ExchangeFromItemId != 0))
            {
                var sourceId = path.ExchangeFromItemId;
                var sourceName = _shops.ItemName(sourceId);

                // Direct: you already hold the trade-in item.
                if (_own.TotalCount(sourceId) > 0)
                {
                    if (node.Tier > plan.OwnedTier && node.Tier > best)
                    {
                        best = node.Tier;
                        plan.HasShortcut = true;
                        plan.Notes.Add($"Free trade-up: hand in your {sourceName} for +{node.Tier}.");
                    }
                    continue;
                }

                // Two-step: you hold the *predecessor* of the trade-in item, so upgrading that
                // first is cheaper than buying into this family from scratch.
                foreach (var srcOffer in _shops.OffersFor(sourceId))
                {
                    foreach (var cost in srcOffer.Costs)
                    {
                        if (_own.TotalCount(cost.ItemId) <= 0) continue;
                        if (cost.ItemId == sourceId) continue;

                        var mats = srcOffer.Costs
                            .Where(c => c.ItemId != cost.ItemId)
                            .Select(c => $"{c.Quantity}x {c.ItemName}")
                            .ToList();
                        var matText = mats.Count > 0 ? " using " + string.Join(" + ", mats) : "";

                        plan.Notes.Add(
                            $"Two-step: upgrade your {cost.ItemName} to {sourceName}{matText}, " +
                            $"then trade it in for +{node.Tier} free.");
                        plan.HasShortcut = true;
                    }
                }
            }
        }

        return best;
    }

    private static AcquisitionPath? PurchasePath(TierNode? baseNode) =>
        baseNode?.Paths.FirstOrDefault(p => p.UpgradeFromItemId == 0 && p.ExchangeFromItemId == 0);

    private static void Add(Dictionary<uint, long> into, IEnumerable<CostLine> costs)
    {
        foreach (var c in costs)
        {
            into.TryGetValue(c.ItemId, out var existing);
            into[c.ItemId] = existing + c.Quantity;
        }
    }

    /// <summary>Sums the per-piece plans for a set of chains.</summary>
    public (List<PiecePlan> Plans, Dictionary<uint, long> Total, int Done) PlanMany(
        IEnumerable<PieceChain> chains, int targetTier)
    {
        var plans = chains.Select(c => Plan(c, targetTier)).ToList();
        var total = new Dictionary<uint, long>();
        foreach (var p in plans)
            foreach (var kv in p.Remaining)
            {
                total.TryGetValue(kv.Key, out var existing);
                total[kv.Key] = existing + kv.Value;
            }
        return (plans, total, plans.Count(p => p.Complete));
    }
}
