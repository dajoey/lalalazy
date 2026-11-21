namespace LazyCrafter.Core;

/// <summary>
/// What the plan may hand to WahTools (0.1.7.14): fish to its Feesh module, monster drops to its Hunter module.
/// <para>
/// Built fresh per plan by the adapter, like <see cref="SpecialShopContext"/>. <see cref="None"/> (or <c>null</c>)
/// reproduces the routing that existed before WahTools support, byte for byte: fish go to GBR and drops fall
/// through to the market board or a manual line.
/// </para>
/// <para>
/// <see cref="MobsFor"/> lists the monsters (BNpcName ids) known to drop an item, best candidate first.
/// <see cref="Excluded"/> holds items WahTools already refused or gave up on in this run, so a re-plan sends
/// them back down the old routes instead of offering them to WahTools again.
/// </para>
/// </summary>
public sealed record FarmContext(
    bool CatchFish,
    bool HuntDrops,
    Func<uint, IReadOnlyList<uint>> MobsFor,
    IReadOnlySet<uint> Excluded)
{
    public static readonly FarmContext None = new(false, false, _ => Array.Empty<uint>(), new HashSet<uint>());

    public bool Catches(uint itemId) => CatchFish && !Excluded.Contains(itemId);

    public IReadOnlyList<uint> Hunts(uint itemId) =>
        HuntDrops && !Excluded.Contains(itemId) ? MobsFor(itemId) : Array.Empty<uint>();

    /// <summary>
    /// The order to offer an item's monsters to WahTools' Hunter (0.1.7.15, WahTools finding 4): monsters the
    /// character can already beat first, the highest-level of those first so the strongest spawn goes first, then
    /// monsters with no known level, then monsters above the character - the lowest of those first. Ties keep the
    /// input order. A combat level of 0 (not logged in / unknown) leaves the input unchanged.
    /// </summary>
    public static IReadOnlyList<uint> OrderMobs(IReadOnlyList<uint> mobs, Func<uint, (int Low, int High)?> level, int combatLevel)
    {
        if (combatLevel <= 0) return mobs;
        var below = new List<(uint Mob, int High, int Order)>();
        var unknown = new List<uint>();
        var above = new List<(uint Mob, int Low, int Order)>();
        var order = 0;
        foreach (var m in mobs)
        {
            if (level(m) is not { } lv) unknown.Add(m);
            else if (lv.High <= combatLevel) below.Add((m, lv.High, order));
            else above.Add((m, lv.Low, order));
            order++;
        }
        var ordered = new List<uint>(mobs.Count);
        ordered.AddRange(below.OrderByDescending(x => x.High).ThenBy(x => x.Order).Select(x => x.Mob));
        ordered.AddRange(unknown);
        ordered.AddRange(above.OrderBy(x => x.Low).ThenBy(x => x.Order).Select(x => x.Mob));
        return ordered;
    }
}

/// <summary>
/// The hunt-until-enough decision (0.1.7.14). WahTools' Hunter counts kills, not items, and a drop is a chance, so
/// LazyCrafter hunts in batches and recounts the bags between them. Pure: counts in, next step out.
/// </summary>
public static class HuntLoop
{
    /// <summary>Stop after this many batches in total for one item.</summary>
    public const int MaxBatches = 8;

    /// <summary>Stop after this many batches in a row that added nothing to the bags.</summary>
    public const int MaxDryBatches = 2;

    public const int MinKills = 3;
    public const int MaxKills = 30;

    public enum Step { Done, HuntMore, GiveUp }

    /// <summary>Kills to ask for when <paramref name="remaining"/> items are still missing: one per item, clamped.</summary>
    public static int KillsFor(int remaining) => Math.Clamp(remaining, MinKills, MaxKills);

    /// <summary>
    /// After a batch ends: <paramref name="need"/> items were wanted, <paramref name="gained"/> have landed since the
    /// first batch, <paramref name="batches"/> batches have run, the last <paramref name="dryBatches"/> of them in a
    /// row added nothing.
    /// </summary>
    public static Step Next(int need, int gained, int batches, int dryBatches)
    {
        if (gained >= need) return Step.Done;
        if (batches >= MaxBatches || dryBatches >= MaxDryBatches) return Step.GiveUp;
        return Step.HuntMore;
    }
}
