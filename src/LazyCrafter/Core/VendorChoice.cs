namespace LazyCrafter.Core;

/// <summary>
/// Where the player is standing, and what a teleport would cost, at the moment a vendor is picked.
/// Everything is optional: with <see cref="Unknown"/> the ranking degrades to the pre-0.1.6.2 behaviour
/// (nearest placement to a teleportable aetheryte, ties on lowest NPC id), which is what the offline
/// <c>tests/LazyCrafter.Probe</c> and any not-logged-in caller get.
/// </summary>
/// <param name="TerritoryId">The player's current TerritoryType row id; 0 when unknown.</param>
/// <param name="MapX">Player position in MAP units (same space as <see cref="VendorCandidate.MapX"/>).</param>
/// <param name="HasPosition">False when only the territory is known (or nothing is).</param>
/// <param name="TeleportCost">
/// aetheryteId -&gt; gil, from the client's own teleport list. <c>null</c> or empty = unknown, in which case every
/// candidate ties on cost and walk distance decides. An aetheryte the player is not attuned to is simply absent,
/// which correctly ranks it below every attuned one.
/// </param>
public sealed record VendorContext(
    uint TerritoryId,
    float MapX,
    float MapY,
    bool HasPosition,
    IReadOnlyDictionary<uint, uint>? TeleportCost)
{
    /// <summary>Nothing is known about the player: rank on walk distance from the aetheryte alone.</summary>
    public static readonly VendorContext Unknown = new(0, 0, 0, false, null);

    /// <summary>Gil to teleport to <paramref name="aetheryteId"/>, or <c>null</c> when unknown / not attuned.</summary>
    public uint? CostTo(uint aetheryteId) =>
        TeleportCost is { Count: > 0 } c && c.TryGetValue(aetheryteId, out var gil) ? gil : null;
}

/// <summary>One placement of one vendor NPC, already resolved to map coordinates and a teleportable aetheryte.</summary>
/// <param name="AetheryteDistance">Map units from <paramref name="AetheryteId"/> to the NPC - the walk after landing.</param>
public readonly record struct VendorCandidate(
    uint NpcId,
    uint TerritoryId,
    uint MapId,
    float MapX,
    float MapY,
    uint AetheryteId,
    float AetheryteMapX,
    float AetheryteMapY,
    float AetheryteDistance);

/// <summary>
/// How good a vendor is, smaller is better. Lexicographic: zone you are already in first, then what the
/// teleport costs, then how far you walk after landing, then the NPC id so the answer is stable.
/// </summary>
public readonly record struct VendorScore(int Tier, uint TeleportCost, float Walk, uint NpcId)
    : IComparable<VendorScore>
{
    public int CompareTo(VendorScore other)
    {
        var c = Tier.CompareTo(other.Tier);
        if (c != 0) return c;
        c = TeleportCost.CompareTo(other.TeleportCost);
        if (c != 0) return c;
        c = Walk.CompareTo(other.Walk);
        if (c != 0) return c;
        return NpcId.CompareTo(other.NpcId);
    }

    public static bool operator <(VendorScore a, VendorScore b) => a.CompareTo(b) < 0;
    public static bool operator >(VendorScore a, VendorScore b) => a.CompareTo(b) > 0;
    public static bool operator <=(VendorScore a, VendorScore b) => a.CompareTo(b) <= 0;
    public static bool operator >=(VendorScore a, VendorScore b) => a.CompareTo(b) >= 0;
}

/// <summary>
/// THE vendor ranking. Dalamud-free and Lumina-free on purpose so <c>tests/LazyCrafter.Harness</c> replays it
/// offline; <see cref="LazyCrafter.Adapters.VendorLocator"/> only builds <see cref="VendorCandidate"/>s from the
/// sheets and delegates every choice here.
/// <para>
/// <b>Why this class exists (card t_731ea0e7, LazyCrafter 0.1.6.2).</b> Up to 0.1.6.1 there were TWO selectors that
/// disagreed by construction and were used interchangeably:
/// <c>VendorLocator.Plan()</c> (the cart-run path) ranked by items-covered then <i>lowest NPC id</i>, so for a
/// single-item list distance was never consulted at all; <c>VendorLocator.Find()</c> (the per-item buttons) ranked by
/// <i>map distance to the nearest aetheryte</i>. On Joey's 2026-09-05 cart run the same item (Tallow Candle #5998)
/// resolved to Engerrand in Limsa Lominsa from one path and to a traveling material supplier in The Azim Steppe from
/// the other, minutes apart; <c>GoToVendor</c> re-flags the map on every call, so the LAST print won and his map flag
/// landed in Stormblood while the chat block said Limsa. Neither metric looked at where the player actually was, so
/// "nearest" could mean a vendor standing on top of a far-flung aetheryte instead of one a short walk from the city
/// he was standing in. There is now exactly one comparer, and both entry points route through it.
/// </para>
/// </summary>
public static class VendorChoice
{
    /// <summary>What an unknown / unattuned teleport costs for ranking purposes: worse than any real fare.</summary>
    public const uint UnknownTeleportCost = uint.MaxValue;

    /// <summary>Rank one placement. Smaller is better; see <see cref="VendorScore"/> for the ordering.</summary>
    public static VendorScore Score(VendorCandidate c, VendorContext? context)
    {
        var ctx = context ?? VendorContext.Unknown;

        // Tier 0 - already in this zone: no teleport at all. Prefer the shortest walk from where the player IS
        // standing when we know that, otherwise fall back to the walk from the zone's aetheryte.
        if (ctx.TerritoryId != 0 && c.TerritoryId == ctx.TerritoryId)
            return new VendorScore(0, 0, ctx.HasPosition ? Distance(ctx.MapX, ctx.MapY, c.MapX, c.MapY) : c.AetheryteDistance, c.NpcId);

        // Tier 1 - somewhere else: what the trip costs, then the walk after landing.
        return new VendorScore(1, ctx.CostTo(c.AetheryteId) ?? UnknownTeleportCost, c.AetheryteDistance, c.NpcId);
    }

    /// <summary>Best of a set of placements, or <c>null</c> when the set is empty.</summary>
    public static VendorCandidate? Best(IEnumerable<VendorCandidate> candidates, VendorContext? context)
    {
        VendorCandidate? best = null;
        var bestScore = default(VendorScore);
        foreach (var c in candidates)
        {
            var s = Score(c, context);
            if (best is null || s < bestScore) { best = c; bestScore = s; }
        }
        return best;
    }

    /// <summary>The best placement of each NPC, keyed by NPC id. An NPC stands in several places; this is where we send you.</summary>
    public static Dictionary<uint, VendorCandidate> BestPlacementPerNpc(IEnumerable<VendorCandidate> candidates, VendorContext? context)
    {
        var byNpc = new Dictionary<uint, VendorCandidate>();
        foreach (var c in candidates)
        {
            if (!byNpc.TryGetValue(c.NpcId, out var incumbent)) { byNpc[c.NpcId] = c; continue; }
            if (Score(c, context) < Score(incumbent, context)) byNpc[c.NpcId] = c;
        }
        return byNpc;
    }

    /// <summary>One stop of a shopping trip: which NPC, where, and what to buy there.</summary>
    public sealed record Stop(VendorCandidate Where, IReadOnlyList<(uint ItemId, int Quantity)> Items);

    /// <summary>
    /// Group a shopping list into as few stops as possible: the vendor covering the most remaining items wins each
    /// round, and <b>ties are broken by <see cref="Score"/></b> - not by NPC id, which was the 0.1.6.1 bug. For a
    /// single-item list every candidate covers exactly one item, so the tie-break decides outright and the answer is
    /// necessarily the same NPC <see cref="Find"/> returns. That identity is the regression test.
    /// </summary>
    /// <param name="candidatesFor">Every placed, teleportable vendor placement selling that item; empty = unlocatable.</param>
    public static IReadOnlyList<Stop> Plan(
        IReadOnlyList<(uint ItemId, int Quantity)> wanted,
        Func<uint, IReadOnlyList<VendorCandidate>> candidatesFor,
        VendorContext? context,
        out IReadOnlyList<(uint ItemId, int Quantity)> unlocated)
    {
        var remaining = wanted.Where(w => w.Quantity > 0).ToList();
        var stops = new List<Stop>();
        var missing = new List<(uint ItemId, int Quantity)>();

        // itemId -> the npcs that sell it; plus, across all of them, each npc's own best placement.
        var npcsByItem = new Dictionary<uint, HashSet<uint>>();
        var all = new List<VendorCandidate>();
        foreach (var (itemId, _) in remaining)
        {
            if (npcsByItem.ContainsKey(itemId)) continue;
            var set = new HashSet<uint>();
            foreach (var c in candidatesFor(itemId)) { set.Add(c.NpcId); all.Add(c); }
            npcsByItem[itemId] = set;
        }
        var placement = BestPlacementPerNpc(all, context);

        while (remaining.Count > 0)
        {
            var coverage = new Dictionary<uint, List<(uint ItemId, int Quantity)>>();
            foreach (var w in remaining)
                foreach (var npc in npcsByItem[w.ItemId])
                    (coverage.TryGetValue(npc, out var l) ? l : coverage[npc] = new List<(uint, int)>()).Add(w);
            if (coverage.Count == 0) { missing.AddRange(remaining); break; }

            uint bestNpc = 0;
            List<(uint ItemId, int Quantity)>? bestItems = null;
            var bestScore = default(VendorScore);
            foreach (var (npc, items) in coverage)
            {
                if (!placement.TryGetValue(npc, out var where)) continue;
                var score = Score(where, context);
                // Most items covered wins the round; a tie is settled by the SAME metric Find() uses.
                if (bestItems is null || items.Count > bestItems.Count || (items.Count == bestItems.Count && score < bestScore))
                {
                    bestNpc = npc;
                    bestItems = items;
                    bestScore = score;
                }
            }
            if (bestItems is null) { missing.AddRange(remaining); break; }

            stops.Add(new Stop(placement[bestNpc], bestItems));
            remaining.RemoveAll(bestItems.Contains);
        }

        unlocated = missing;
        return stops;
    }

    /// <summary>
    /// Where to buy one item. A thin wrapper over <see cref="Best"/> so it can never drift from <see cref="Plan"/>
    /// again; both are the same comparer over the same candidates.
    /// </summary>
    public static VendorCandidate? Find(uint itemId, Func<uint, IReadOnlyList<VendorCandidate>> candidatesFor, VendorContext? context) =>
        Best(candidatesFor(itemId), context);

    private static float Distance(float ax, float ay, float bx, float by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }
}
