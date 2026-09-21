namespace LazyCrucible;

/// <summary> One shop stock entry as the screen lists it. </summary>
public readonly record struct ShopOffer(int Index, int Row, int Price, bool Bought, bool Discounted = false);

/// <summary> A purchase: stock index, item row, price, value, and (for feed) the familiar that will eat it. </summary>
public readonly record struct ShopChoice(int Index, int Row, int Price, int Score, string Reason, FeedChoice? Feed = null);

/// <summary> A treasure / spoils pick: the choice index on the screen, the item row and why. </summary>
public readonly record struct ItemPick(int Index, int Row, int Score, string Reason);

/// <summary> What to do with the spoils screen. </summary>
public sealed record SpoilsChoice(bool TakeAll, IReadOnlyList<ItemPick> Ranked, string Reason);

/// <summary>
///     Shop purchases, treasure pick and spoils pick. PURE, one decision at a time (the live layer re-reads the screen
///     after every purchase and asks again).
///     <para>Shop, survival first: (1) keep a healing reserve — while fewer heals are held than fights ahead (at least 2,
///     at most 4) the best affordable heal is bought first; (2) then whatever has the highest <see cref="ItemValue"/> —
///     resistance to what a fight ahead inflicts, max HP / damage-taken gear, revives, reraise, Temporal Sand — or a feed
///     with the familiar that will eat it (<see cref="FeedPolicy"/>), whichever is worth more; (3) stop when nothing left
///     is worth <see cref="ItemValue.Worthwhile"/> or affordable. Tokens are not saved for a later shop: what is bought now
///     serves the later fights too, and a later shop's stock is unknown. Never sells. Items that are full (10) or gear
///     already owned are worth 0.</para>
///     <para>Treasure: the choice with the highest value; when every choice is worth nothing it is left to the player.
///     Spoils: "Take all" when everything fits; otherwise the ranking is shown and the player chooses.</para>
/// </summary>
internal static class ItemPolicies
{
    /// <summary> Most purchases one shop visit makes on its own. </summary>
    public const int MaxPurchasesPerVisit = 8;

    public static int HealingReserve(RunContext ctx) => Math.Clamp(ctx.FightsAhead, 2, 4);

    public static ShopChoice? NextPurchase(
        IReadOnlyList<ShopOffer> stock, int tokens, Inventory inv, IReadOnlyList<FamiliarState>? familiars, RunContext ctx,
        IReadOnlySet<int>? skipIndices = null)
    {
        var open = stock.Where(o => !o.Bought && o.Row > 0 && o.Price <= tokens && skipIndices?.Contains(o.Index) != true).ToList();
        if (open.Count == 0)
            return null;

        // (1) Healing reserve.
        var reserve = HealingReserve(ctx);
        if (!inv.ItemsFull && inv.HealingHeld < reserve)
        {
            ShopChoice? heal = null;
            foreach (var o in open.Where(o => CrucibleItems.IsHealing(o.Row)))
            {
                var why = new List<string>(3);
                var v = ItemValue.Of(o.Row, ctx, inv, familiars, why);
                if (heal is null || v > heal.Value.Score || (v == heal.Value.Score && o.Price < heal.Value.Price))
                    heal = new ShopChoice(o.Index, o.Row, o.Price, v,
                        $"{ItemValue.Describe(o.Row, why)} (keeping {reserve} heals for {ctx.FightsAhead} fight{(ctx.FightsAhead == 1 ? "" : "s")} ahead, {inv.HealingHeld} held)");
            }
            if (heal is not null)
                return heal;
        }

        // (2) Best value: items and gear, or a feed with its familiar.
        ShopChoice? best = null;
        foreach (var o in open)
        {
            if (CrucibleItems.Get(o.Row).Type == CrucibleItemType.Feed)
                continue;
            var why = new List<string>(4);
            var v = ItemValue.Of(o.Row, ctx, inv, familiars, why);
            if (v < ItemValue.Worthwhile)
                continue;
            if (best is null || v > best.Value.Score || (v == best.Value.Score && o.Price < best.Value.Price))
                best = new ShopChoice(o.Index, o.Row, o.Price, v, ItemValue.Describe(o.Row, why));
        }

        if (familiars is { Count: > 0 })
        {
            var feedOffers = open.Where(o => CrucibleItems.Get(o.Row).Type == CrucibleItemType.Feed)
                                 .Select(o => (o.Index, o.Row, o.Price)).ToList();
            if (FeedPolicy.BestPurchase(feedOffers, tokens, familiars, ctx) is { } feed)
            {
                var feedScore = ctx.Goal == ScoreGoal.FeedTheBold ? Math.Max(feed.ShopScore(ctx), ItemValue.Worthwhile) : feed.ShopScore(ctx);
                var price = feedOffers.First(f => f.Index == feed.StockIndex).Price;
                if (best is null || feedScore > best.Value.Score)
                    best = new ShopChoice(feed.StockIndex, feed.FeedRow, price, feedScore, feed.Reason, feed);
            }
        }

        return best;
    }

    public static ItemPick? TreasurePick(IReadOnlyList<(int Index, int Row)> choices, Inventory inv, IReadOnlyList<FamiliarState>? familiars, RunContext ctx)
    {
        ItemPick? best = null;
        foreach (var (index, row) in choices)
        {
            if (row <= 0)
                continue;
            var why = new List<string>(4);
            var v = ItemValue.Of(row, ctx, inv, familiars, why);
            if (best is null || v > best.Value.Score)
                best = new ItemPick(index, row, v, ItemValue.Describe(row, why));
        }
        return best is { Score: > 0 } ? best : null;
    }

    public static SpoilsChoice Spoils(IReadOnlyList<(int Index, int Row)> loot, Inventory inv, IReadOnlyList<FamiliarState>? familiars, RunContext ctx)
    {
        var ranked = new List<ItemPick>(loot.Count);
        var items = 0;
        var gear = new List<int>();
        foreach (var (index, row) in loot)
        {
            if (row <= 0)
                continue;
            var why = new List<string>(4);
            var v = ItemValue.Of(row, ctx, inv, familiars, why);
            ranked.Add(new ItemPick(index, row, v, ItemValue.Describe(row, why)));
            switch (CrucibleItems.Get(row).Type)
            {
                case CrucibleItemType.Item:
                    items++;
                    break;
                case CrucibleItemType.Gear:
                    gear.Add(row);
                    break;
            }
        }
        ranked.Sort((a, b) => b.Score.CompareTo(a.Score));

        if (ranked.Count == 0)
            return new SpoilsChoice(false, ranked, "Nothing to take");
        var itemsFit = inv.Items.Count + items <= Inventory.ItemSlots;
        var dupGear = gear.Where(inv.Owns).ToList();
        var gearFits = inv.Gear.Count + gear.Count <= Inventory.GearSlots && dupGear.Count == 0 && gear.Distinct().Count() == gear.Count;
        if (itemsFit && gearFits)
            return new SpoilsChoice(true, ranked, $"Take all: {string.Join(", ", ranked.Select(r => CrucibleItems.NameOf(r.Row)))}");

        var blocker = !itemsFit ? $"only {Inventory.ItemSlots - inv.Items.Count} item slot(s) free"
            : dupGear.Count > 0 ? $"{CrucibleItems.NameOf(dupGear[0])} is already owned"
            : "gear slots full";
        return new SpoilsChoice(false, ranked, $"Not taking all ({blocker}). Best first: {string.Join(", ", ranked.Select(r => CrucibleItems.NameOf(r.Row)))}");
    }
}
