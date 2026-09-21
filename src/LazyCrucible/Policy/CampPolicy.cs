namespace LazyCrucible;

/// <summary> Who rests at a campsite (familiar rows and their list indices; empty = the player's self heal) and why. </summary>
public sealed record CampChoice(IReadOnlyList<int> Rows, IReadOnlyList<int> Indices, int HealPercent, string Reason);

/// <summary>
///     Campsite: which familiars rest. PURE.
///     <para>Heal per rest [guides, two sources]: boards 1-2 heal the player 90% alone, or the player and each resting
///     familiar 45% (one) / 30% (two); board 3 heals 100 / 60 / 40 / 30. Boards 4-5 publish no numbers, so board 3's
///     table is used there [inferred] (the chat line says so). A knocked-out familiar cannot rest. Resting wipes the
///     familiar's feed unless it ate Lily Simular; Lassi Simular restores it fully at a campsite [sheet].</para>
///     <para>Choice, survival first: for each possible number of resting familiars (0 up to the campsite's limit), add
///     the HP the player gets back (weighted double: the player's knock-out ends the run) and the HP each resting
///     familiar gets back weighted by how many fights ahead it is a horn pick for, minus the feed it would lose; the
///     best total wins, fewer familiars on a tie.</para>
/// </summary>
internal static class CampPolicy
{
    private const int PlayerWeight = 2;
    private const int FeedLossPerFeed = 12;

    /// <summary> Heal percent for the player and each resting familiar when <paramref name="resting"/> familiars rest. </summary>
    public static int HealPercent(int board, int resting, out bool inferred)
    {
        inferred = board is 4 or 5;
        int[] table = board is 1 or 2 ? [90, 45, 30] : [100, 60, 40, 30];
        return table[Math.Clamp(resting, 0, table.Length - 1)];
    }

    public static CampChoice Choose(IReadOnlyList<FamiliarState> familiars, int capacity, RunContext ctx)
    {
        var candidates = familiars.Where(f => !f.KnockedOut && f.HpPercent < 100).ToList();
        var playerMissing = 100 - ctx.PlayerHpPercent;
        var bestTotal = double.MinValue;
        var bestK = 0;
        List<(FamiliarState F, double Gain)> bestPicks = [];

        for (var k = 0; k <= Math.Min(capacity, candidates.Count); k++)
        {
            var heal = HealPercent(ctx.Board, k, out _);
            var total = PlayerWeight * (double)Math.Min(heal, playerMissing);
            var ranked = candidates
                .Select(f => (F: f, Gain: FamiliarGain(f, heal, ctx)))
                .OrderByDescending(x => x.Gain)
                .ThenBy(x => x.F.HpPercent)
                .ThenBy(x => x.F.Index)
                .Take(k)
                .ToList();
            if (ranked.Count < k || (k > 0 && ranked[^1].Gain <= 0))
                continue; // a pet that gains nothing is not worth the player's lost heal
            total += ranked.Sum(x => x.Gain);
            if (total > bestTotal + 0.001)
            {
                bestTotal = total;
                bestK = k;
                bestPicks = ranked;
            }
        }

        var healPct = HealPercent(ctx.Board, bestK, out var inferred);
        var note = inferred ? " (board 3 heal table assumed)" : "";
        if (bestK == 0)
        {
            var why = candidates.Count == 0 ? "familiars are full or knocked out" : "your heal is worth more";
            return new CampChoice([], [], healPct, $"No familiar rests: you heal {healPct}% ({why}; you {ctx.PlayerHpPercent}%){note}");
        }

        var names = string.Join(", ", bestPicks.Select(x => $"{x.F.Name} {x.F.HpPercent}%{(x.F.AteLassi ? " (Lassi: full heal)" : "")}"));
        var fed = bestPicks.Any(x => x.F.FeedsEaten.Count > 0 && !x.F.AteLily) ? "; its feed is lost" : "";
        return new CampChoice(
            bestPicks.Select(x => x.F.Row).ToList(),
            bestPicks.Select(x => x.F.Index).ToList(),
            healPct,
            $"Rest {names}: +{healPct}% each and you (you {ctx.PlayerHpPercent}%){fed}{note}");
    }

    private static double FamiliarGain(in FamiliarState f, int heal, RunContext ctx)
    {
        var missing = 100 - f.HpPercent;
        var gained = f.AteLassi ? missing : Math.Min(heal, missing);
        var weight = 0.5 + ctx.Demand(f.Row);
        var loss = f.AteLily ? 0 : FeedLossPerFeed * f.FeedsEaten.Count;
        return gained * weight - loss;
    }
}
