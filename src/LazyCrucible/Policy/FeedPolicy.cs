using Lalalazy.Crucible;

namespace LazyCrucible;

/// <summary> A feed decision: which feed (and shop entry, -1 when already bought) goes to which familiar, and why. </summary>
public readonly record struct FeedChoice(int FeedRow, int StockIndex, int FamiliarRow, int FamiliarIndex, int Score, string Reason, int Raw = 0)
{
    /// <summary>
    ///     The feed on the shop's item scale: its unweighted value times 0.5 (no fight ahead needs the familiar) up to
    ///     2 (a horn pick for 3+ fights ahead), so one familiar's buff does not outbid gear that protects the player.
    /// </summary>
    internal int ShopScore(RunContext ctx) => (int)Math.Round(Raw * (0.5 + Math.Min(ctx.Demand(FamiliarRow), 3) / 2));
}

/// <summary>
///     Beast Feed: which feed to buy and which familiar eats it. PURE.
///     <para>Hard rules (a familiar that breaks one is never picked): the feed's kin list (sheet "Suitable for") and the
///     picker's own "cannot eat" flag; satiety below the familiar's cap; the same feed never twice; never a knocked-out
///     familiar. "Starve the Fever" as the score goal = no feeding at all.</para>
///     <para>Preference, survival first: max HP, heals, damage taken and vulnerability cuts, and resistance to what the
///     fights ahead inflict; then a little damage. Drawbacks (max HP -80%, Slow +200%, self damage over time, a chance
///     to inflict a status on itself) cost more than most feeds give. The value is weighted by how many fights ahead
///     the familiar is a horn pick for (<see cref="RunContext.HornDemand"/>), so feed goes to the familiars that will
///     fight. A campsite ahead discounts feeding a hurt familiar that may rest there (resting wipes feed) unless it
///     already ate Lily Simular; Lily and Lassi themselves gain value when a campsite is ahead.</para>
/// </summary>
internal static class FeedPolicy
{
    /// <summary> Weighted value below which no feed is bought. </summary>
    public const int Worthwhile = 20;

    /// <summary> Whether <paramref name="f"/> may eat <paramref name="feedRow"/>, with the rule that forbids it. </summary>
    public static bool CanEat(in FamiliarState f, int feedRow, out string whyNot)
    {
        var feed = CrucibleItems.Get(feedRow);
        whyNot = "";
        if (feed.Type != CrucibleItemType.Feed)
            whyNot = "not a feed";
        else if (f.KnockedOut)
            whyNot = "knocked out";
        else if (f.CannotEat == true)
            whyNot = "the screen says it cannot eat this";
        else if (BST_Beasts.ByRow(f.Row) is not { } beast || !feed.SuitsKin(beast.Kin))
            whyNot = "wrong kin";
        else if (!f.Hungry)
            whyNot = "full";
        else if (f.Ate(feedRow))
            whyNot = "already ate it";
        return whyNot.Length == 0;
    }

    /// <summary>
    ///     Cross-check of the feed the plugin believes is offered: the picker's own "cannot eat" marks must be exactly the
    ///     familiars whose kin the feed's sheet entry does not suit (matched every recorded picker). A knocked-out
    ///     familiar is always marked "cannot eat" whatever the feed, so its mark says nothing about the kin list.
    /// </summary>
    public static bool KinFlagsAgree(IReadOnlyList<FamiliarState> familiars, int feedRow)
    {
        var feed = CrucibleItems.Get(feedRow);
        foreach (var f in familiars)
        {
            if (f.KnockedOut || f.CannotEat is not { } cannot || BST_Beasts.ByRow(f.Row) is not { } beast)
                continue;
            if (cannot == feed.SuitsKin(beast.Kin))
                return false;
        }
        return true;
    }

    /// <summary> Unweighted survival value of one feed for one familiar (can be negative). </summary>
    public static int Value(int feedRow, in FamiliarState f, RunContext ctx, List<string>? why = null)
    {
        var feed = CrucibleItems.Get(feedRow);
        var v = 0;
        if (feed.MaxHp > 0)
        {
            v += feed.MaxHp * 8 / 10;
            why?.Add($"max HP +{feed.MaxHp}%");
        }
        var missing = Math.Max(0, 100 - f.HpPercent);
        if (feed.Heal > 0 && missing > 0)
        {
            v += Math.Min(feed.Heal, missing) * 8 / 10;
            why?.Add($"heals {Math.Min(feed.Heal, missing)}%");
        }
        if (feed.DamageTaken > 0)
        {
            v += feed.DamageTaken * 3 / 2;
            why?.Add($"damage taken -{feed.DamageTaken}%");
        }
        if (feed.PhysVuln + feed.MagicVuln > 0)
        {
            v += (feed.PhysVuln + feed.MagicVuln) * 8 / 10;
            why?.Add(feed.PhysVuln > 0 ? $"physical damage taken -{feed.PhysVuln}%" : $"magic damage taken -{feed.MagicVuln}%");
        }
        foreach (var s in RunContext.Each(feed.Resists))
        {
            var level = ctx.ThreatLevel(RunContext.ThreatOf(s));
            v += level >= 2 ? 30 : level == 1 ? 15 : 3;
            if (level > 0)
                why?.Add($"{RunContext.StatusName(s)} resist for a fight ahead");
        }
        if ((feed.Flags & ItemFlags.KeepsFeedAtCamp) != 0 && ctx.CampAhead)
        {
            v += 12;
            why?.Add("keeps feed through a campsite rest");
        }
        if ((feed.Flags & ItemFlags.FullHealAtCamp) != 0 && ctx.CampAhead)
        {
            v += 10;
            why?.Add("full heal at a campsite");
        }

        var magic = f.Row is >= 1 and <= BST_Beasts.Count && BST_CrucibleData.BeastProfiles[f.Row].AutoMagic;
        var dmg = feed.DamageDealt + (magic ? feed.MagicDamage : feed.PhysDamage);
        if (dmg > 0)
        {
            v += dmg * 3 / 10;
            if (why is { Count: 0 })
                why.Add($"damage +{dmg}%");
        }

        var cost = 0;
        if ((feed.Hazards & ItemHazard.MaxHpDown) != 0)
            cost += 2 * Math.Abs(Math.Min(feed.MaxHp, 0));
        if ((feed.Hazards & ItemHazard.Slow) != 0)
            cost += 40;
        if ((feed.Hazards & ItemHazard.SelfDot) != 0)
            cost += 25;
        if ((feed.Hazards & ItemHazard.SelfStatus) != 0)
            cost += 12;
        if (cost > 0)
            why?.Add("has a drawback");
        return v - cost;
    }

    /// <summary> Value weighted by how much the familiar is needed ahead; the number the choices compare. </summary>
    public static int Weighted(int feedRow, in FamiliarState f, RunContext ctx, List<string>? why = null)
    {
        var raw = Value(feedRow, f, ctx, why);
        if (raw <= 0)
            return raw;
        var weighted = raw * (1.0 + ctx.Demand(f.Row));
        // Resting wipes feed (unless it ate Lily Simular): a hurt familiar is likely to rest at a campsite ahead.
        if (ctx.CampAhead && f.HpPercent < 60 && !f.AteLily && (CrucibleItems.Get(feedRow).Flags & ItemFlags.KeepsFeedAtCamp) == 0)
            weighted *= 0.75;
        return (int)Math.Round(weighted);
    }

    /// <summary>
    ///     Which familiar should eat an already-bought feed (the picker is open). Null when nobody may eat it, or the
    ///     score goal is "Starve the Fever". Ties: more demand, then lower HP, then list order.
    /// </summary>
    public static FeedChoice? BestTarget(int feedRow, IReadOnlyList<FamiliarState> familiars, RunContext ctx)
    {
        if (ctx.Goal == ScoreGoal.StarveTheFever)
            return null;
        FeedChoice? best = null;
        var bestDemand = -1.0;
        var bestHp = int.MaxValue;
        foreach (var f in familiars)
        {
            if (!CanEat(f, feedRow, out _))
                continue;
            var why = new List<string>(4);
            var score = Weighted(feedRow, f, ctx, why);
            var demand = ctx.Demand(f.Row);
            var better = best is null || score > best.Value.Score
                         || (score == best.Value.Score && (demand > bestDemand || (demand == bestDemand && f.HpPercent < bestHp)));
            if (!better)
                continue;
            best = new FeedChoice(feedRow, -1, f.Row, f.Index, score, Reason(feedRow, f, ctx, why), Value(feedRow, f, ctx));
            bestDemand = demand;
            bestHp = f.HpPercent;
        }
        return best;
    }

    /// <summary>
    ///     The best feed to buy from the shop's feed entries (and who eats it), or null when none is worth it.
    ///     <paramref name="offers"/> are (stock index, feed row, price) not yet bought.
    /// </summary>
    public static FeedChoice? BestPurchase(
        IReadOnlyList<(int Index, int Row, int Price)> offers, int tokens, IReadOnlyList<FamiliarState> familiars, RunContext ctx)
    {
        if (ctx.Goal == ScoreGoal.StarveTheFever)
            return null;
        var floor = ctx.Goal == ScoreGoal.FeedTheBold ? 0 : Worthwhile; // Feed the Bold: anything that does no harm
        FeedChoice? best = null;
        foreach (var (index, row, price) in offers)
        {
            if (price > tokens || CrucibleItems.Get(row).Type != CrucibleItemType.Feed)
                continue;
            if (BestTarget(row, familiars, ctx) is not { } target || target.Score < floor)
                continue;
            if (best is null || target.Score > best.Value.Score || (target.Score == best.Value.Score && price < PriceOf(offers, best.Value.StockIndex)))
                best = target with { StockIndex = index };
        }
        return best;
    }

    private static int PriceOf(IReadOnlyList<(int Index, int Row, int Price)> offers, int index)
    {
        foreach (var o in offers)
            if (o.Index == index)
                return o.Price;
        return int.MaxValue;
    }

    private static string Reason(int feedRow, in FamiliarState f, RunContext ctx, List<string> why) =>
        $"{CrucibleItems.NameOf(feedRow)} → {f.Name} ({(why.Count == 0 ? "no survival effect" : string.Join(", ", why.Take(3)))}; {ctx.DemandPhrase(f.Row)})";
}
