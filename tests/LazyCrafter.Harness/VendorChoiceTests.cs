using LazyCrafter.Core;

namespace LazyCrafter.Harness;

/// <summary>
/// The vendor ranking (card t_731ea0e7). The headline case is Joey's 2026-09-05 cart run: the SAME item resolved to
/// two different vendors minutes apart because <c>Plan()</c> ranked on lowest NPC id while <c>Find()</c> ranked on
/// distance to the nearest aetheryte. Every fixture below is built so those two old metrics DISAGREE, so a
/// re-introduction of either one fails here rather than in game.
/// </summary>
internal static class VendorChoiceTests
{
    // ---- the Tallow Candle fixture -------------------------------------------------------------------------
    // Engerrand: LOW npc id, in Limsa Lominsa Lower Decks, a long-ish walk from the Limsa aetheryte.
    // Traveling supplier: HIGH npc id, in The Azim Steppe, parked essentially on top of its aetheryte.
    // Lowest-npc-id  -> Engerrand.       Nearest-aetheryte -> the Azim Steppe supplier.  They disagree by design.
    private const uint TallowCandle = 5998;
    private const uint Engerrand = 1000236, Supplier = 1027847;
    private const uint LimsaTerritory = 129, AzimTerritory = 622;
    private const uint LimsaAetheryte = 8, AzimAetheryte = 111;

    private static readonly VendorCandidate EngerrandLimsa =
        new(Engerrand, LimsaTerritory, 12, 8.6f, 11.8f, LimsaAetheryte, 11.4f, 11.0f, 2.9f);
    private static readonly VendorCandidate SupplierAzim =
        new(Supplier, AzimTerritory, 400, 32.7f, 29.0f, AzimAetheryte, 32.5f, 29.1f, 0.22f);

    private static IReadOnlyList<VendorCandidate> Candles() => new[] { EngerrandLimsa, SupplierAzim };

    private static Func<uint, IReadOnlyList<VendorCandidate>> Only(params VendorCandidate[] cs) =>
        _ => cs;

    /// <summary>Fares as the client reports them: Limsa is cheap from anywhere in ARR, the Steppe is a long haul.</summary>
    private static VendorContext At(uint territory, params (uint Aetheryte, uint Gil)[] fares) =>
        new(territory, 0, 0, false, fares.ToDictionary(f => f.Aetheryte, f => f.Gil));

    private static readonly (uint, uint)[] NormalFares = [(LimsaAetheryte, 216u), (AzimAetheryte, 999u)];

    public static IEnumerable<(string Name, Func<bool> Check)> Tests => new (string, Func<bool>)[]
    {
        // ---------------------------------------------------------------- the regression that was shipped
        ("t_731ea0e7: Plan() and Find() agree on a single-item list (Joey's Tallow Candle run)", () =>
        {
            var ctx = At(LimsaTerritory, NormalFares);
            var find = VendorChoice.Find(TallowCandle, Only(Candles().ToArray()), ctx);
            var plan = VendorChoice.Plan([(TallowCandle, 7)], Only(Candles().ToArray()), ctx, out var unlocated);
            return unlocated.Count == 0 && plan.Count == 1 && find is { } f && f.NpcId == plan[0].Where.NpcId;
        }),

        ("t_731ea0e7: they still agree with the candidate list in the other order", () =>
        {
            var ctx = At(LimsaTerritory, NormalFares);
            Func<uint, IReadOnlyList<VendorCandidate>> reversed = _ => new[] { SupplierAzim, EngerrandLimsa };
            var find = VendorChoice.Find(TallowCandle, reversed, ctx);
            var plan = VendorChoice.Plan([(TallowCandle, 7)], reversed, ctx, out _);
            return find is { } f && plan.Count == 1 && f.NpcId == plan[0].Where.NpcId;
        }),

        ("t_731ea0e7: they agree even with no context at all (offline probe / not logged in)", () =>
        {
            var find = VendorChoice.Find(TallowCandle, Only(Candles().ToArray()), null);
            var plan = VendorChoice.Plan([(TallowCandle, 7)], Only(Candles().ToArray()), null, out _);
            return find is { } f && plan.Count == 1 && f.NpcId == plan[0].Where.NpcId;
        }),

        // ---------------------------------------------------------------- and they agree on the RIGHT vendor
        ("standing in Limsa picks Engerrand, not the Azim Steppe supplier", () =>
        {
            var ctx = At(LimsaTerritory, NormalFares);
            return VendorChoice.Find(TallowCandle, Only(Candles().ToArray()), ctx) is { } f && f.NpcId == Engerrand;
        }),

        ("standing in the Azim Steppe picks the supplier, not Engerrand", () =>
        {
            var ctx = At(AzimTerritory, NormalFares);
            return VendorChoice.Find(TallowCandle, Only(Candles().ToArray()), ctx) is { } f && f.NpcId == Supplier;
        }),

        ("the zone you are in beats a cheaper teleport elsewhere", () =>
        {
            // In the Steppe, with a FREE trip to Limsa on offer: staying put still wins.
            var ctx = At(AzimTerritory, (LimsaAetheryte, 0u), (AzimAetheryte, 999u));
            return VendorChoice.Find(TallowCandle, Only(Candles().ToArray()), ctx) is { } f && f.NpcId == Supplier;
        }),

        ("standing somewhere else entirely, the cheaper teleport wins", () =>
        {
            // Ul'dah (140): neither vendor is local, Limsa is 216 gil and the Steppe 999.
            var ctx = At(140, NormalFares);
            return VendorChoice.Find(TallowCandle, Only(Candles().ToArray()), ctx) is { } f && f.NpcId == Engerrand;
        }),

        ("flip the fares and the other vendor wins - cost is really read, not assumed", () =>
        {
            var ctx = At(140, (LimsaAetheryte, 999u), (AzimAetheryte, 100u));
            return VendorChoice.Find(TallowCandle, Only(Candles().ToArray()), ctx) is { } f && f.NpcId == Supplier;
        }),

        ("an unattuned aetheryte (absent from the fare table) loses to an attuned one", () =>
        {
            // Only the Steppe is attuned, and it is the expensive one; Limsa is not in the list at all.
            var ctx = At(140, (AzimAetheryte, 999u));
            return VendorChoice.Find(TallowCandle, Only(Candles().ToArray()), ctx) is { } f && f.NpcId == Supplier;
        }),

        ("with no fares known at all, the shorter walk from the aetheryte wins", () =>
        {
            var ctx = new VendorContext(140, 0, 0, false, null);
            return VendorChoice.Find(TallowCandle, Only(Candles().ToArray()), ctx) is { } f && f.NpcId == Supplier;
        }),

        // ---------------------------------------------------------------- position inside the zone
        ("in-zone, the nearer of two local vendors wins on the player's own position", () =>
        {
            var near = new VendorCandidate(2000, LimsaTerritory, 12, 10f, 10f, LimsaAetheryte, 11.4f, 11.0f, 1.7f);
            var far  = new VendorCandidate(1000, LimsaTerritory, 12, 30f, 30f, LimsaAetheryte, 11.4f, 11.0f, 0.5f);
            var ctx = new VendorContext(LimsaTerritory, 10.2f, 10.1f, true, null);
            // NOTE: `far` has the shorter walk FROM THE AETHERYTE and the lower npc id - both old metrics pick it.
            return VendorChoice.Best([near, far], ctx) is { } b && b.NpcId == 2000;
        }),

        ("in-zone without a position, fall back to walk-from-aetheryte", () =>
        {
            var near = new VendorCandidate(2000, LimsaTerritory, 12, 10f, 10f, LimsaAetheryte, 11.4f, 11.0f, 1.7f);
            var far  = new VendorCandidate(1000, LimsaTerritory, 12, 30f, 30f, LimsaAetheryte, 11.4f, 11.0f, 0.5f);
            var ctx = new VendorContext(LimsaTerritory, 0, 0, false, null);
            return VendorChoice.Best([near, far], ctx) is { } b && b.NpcId == 1000;
        }),

        ("an NPC standing in two zones is placed in the one you are in", () =>
        {
            var here = new VendorCandidate(777, LimsaTerritory, 12, 9f, 9f, LimsaAetheryte, 11.4f, 11.0f, 3.4f);
            var away = new VendorCandidate(777, AzimTerritory, 400, 32f, 29f, AzimAetheryte, 32.5f, 29.1f, 0.5f);
            var best = VendorChoice.BestPlacementPerNpc([here, away], At(LimsaTerritory, NormalFares));
            return best.Count == 1 && best[777].TerritoryId == LimsaTerritory;
        }),

        // ---------------------------------------------------------------- grouping still works
        ("a vendor covering two items beats a nearer vendor covering one", () =>
        {
            var both = new VendorCandidate(9000, AzimTerritory, 400, 32f, 29f, AzimAetheryte, 32.5f, 29.1f, 0.5f);
            var one  = new VendorCandidate(1, LimsaTerritory, 12, 9f, 9f, LimsaAetheryte, 11.4f, 11.0f, 0.1f);
            var stops = VendorChoice.Plan([(1u, 1), (2u, 1)],
                id => id == 1 ? [both, one] : [both], At(LimsaTerritory, NormalFares), out var unlocated);
            return unlocated.Count == 0 && stops.Count == 1 && stops[0].Where.NpcId == 9000 && stops[0].Items.Count == 2;
        }),

        ("two items with no shared vendor become two stops", () =>
        {
            var a = new VendorCandidate(1, LimsaTerritory, 12, 9f, 9f, LimsaAetheryte, 11.4f, 11.0f, 0.1f);
            var b = new VendorCandidate(2, AzimTerritory, 400, 32f, 29f, AzimAetheryte, 32.5f, 29.1f, 0.5f);
            var stops = VendorChoice.Plan([(1u, 1), (2u, 1)],
                id => id == 1 ? [a] : [b], At(LimsaTerritory, NormalFares), out var unlocated);
            return unlocated.Count == 0 && stops.Count == 2 && stops.Sum(s => s.Items.Count) == 2;
        }),

        ("an item nobody sells comes back unlocated, and does not eat the rest of the list", () =>
        {
            var a = new VendorCandidate(1, LimsaTerritory, 12, 9f, 9f, LimsaAetheryte, 11.4f, 11.0f, 0.1f);
            var stops = VendorChoice.Plan([(1u, 1), (99u, 4)],
                id => id == 1 ? [a] : [], At(LimsaTerritory, NormalFares), out var unlocated);
            return stops.Count == 1 && stops[0].Items.Count == 1
                && unlocated.Count == 1 && unlocated[0] == (99u, 4);
        }),

        ("nothing sellable anywhere: no stops, everything unlocated", () =>
        {
            var stops = VendorChoice.Plan([(1u, 1), (2u, 2)], _ => [], At(LimsaTerritory, NormalFares), out var unlocated);
            return stops.Count == 0 && unlocated.Count == 2;
        }),

        ("zero-quantity lines are dropped, not planned and not reported unlocated", () =>
        {
            var a = new VendorCandidate(1, LimsaTerritory, 12, 9f, 9f, LimsaAetheryte, 11.4f, 11.0f, 0.1f);
            var stops = VendorChoice.Plan([(1u, 0)], _ => [a], At(LimsaTerritory, NormalFares), out var unlocated);
            return stops.Count == 0 && unlocated.Count == 0;
        }),

        ("an empty list plans nothing", () =>
            VendorChoice.Plan([], _ => [], null, out var u).Count == 0 && u.Count == 0),

        ("Best of an empty candidate set is null, not a throw", () =>
            VendorChoice.Best([], At(LimsaTerritory, NormalFares)) is null),

        // ---------------------------------------------------------------- ordering is total and stable
        ("ranking is stable: equal candidates tie-break on npc id, lowest first", () =>
        {
            var a = new VendorCandidate(50, AzimTerritory, 400, 1f, 1f, AzimAetheryte, 1f, 1f, 0f);
            var b = new VendorCandidate(20, AzimTerritory, 400, 1f, 1f, AzimAetheryte, 1f, 1f, 0f);
            return VendorChoice.Best([a, b], At(LimsaTerritory, NormalFares)) is { } w && w.NpcId == 20;
        }),

        ("in-zone always outranks out-of-zone regardless of walk distance", () =>
        {
            var hereFar = new VendorCandidate(1, LimsaTerritory, 12, 40f, 40f, LimsaAetheryte, 11.4f, 11.0f, 40f);
            var awayClose = new VendorCandidate(2, AzimTerritory, 400, 32f, 29f, AzimAetheryte, 32.5f, 29.1f, 0.01f);
            var ctx = At(LimsaTerritory, (LimsaAetheryte, 216u), (AzimAetheryte, 0u));
            return VendorChoice.Best([awayClose, hereFar], ctx) is { } w && w.NpcId == 1;
        }),

        ("score ordering: tier, then cost, then walk, then npc id", () =>
        {
            var s = new VendorScore(0, 100, 5f, 9);
            return s < new VendorScore(1, 0, 0f, 0)
                && s < new VendorScore(0, 101, 0f, 0)
                && s < new VendorScore(0, 100, 5.1f, 0)
                && s < new VendorScore(0, 100, 5f, 10)
                && s.CompareTo(new VendorScore(0, 100, 5f, 9)) == 0;
        }),

        // ---------------------------------------------------------------- negative controls: these fixtures MUST
        // discriminate, or the tests above prove nothing.
        ("negative control: the fixture really does split the two old metrics", () =>
        {
            var byLowestNpcId = Candles().OrderBy(c => c.NpcId).First();
            var byNearestAetheryte = Candles().OrderBy(c => c.AetheryteDistance).First();
            return byLowestNpcId.NpcId == Engerrand
                && byNearestAetheryte.NpcId == Supplier
                && byLowestNpcId.NpcId != byNearestAetheryte.NpcId;
        }),

        ("negative control: the ranking is not constant - the same list yields both winners", () =>
        {
            var inLimsa = VendorChoice.Find(TallowCandle, Only(Candles().ToArray()), At(LimsaTerritory, NormalFares));
            var inAzim  = VendorChoice.Find(TallowCandle, Only(Candles().ToArray()), At(AzimTerritory, NormalFares));
            return inLimsa is { } a && inAzim is { } b && a.NpcId != b.NpcId;
        }),
    };
}
