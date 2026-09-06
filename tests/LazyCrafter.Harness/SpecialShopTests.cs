using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>
/// Currency (special) shop naming and routing - card t_b431de3a.
///
/// <para>
/// <b>The incident, and what it costs to reproduce.</b> Joey's 11:43:33 run on 0.1.6.6:
/// <code>
/// dispatch plan for cart pass 1: gathers=[5150x1] market=[7601x1] deferred=[r30406:needs market #7601]
/// [LazyCrafter] not crafting Iolite x1 yet - needs market Emery.
/// </code>
/// Emery (7601) is not sold for gil at all - it comes from currency shops - and the plugin was sending him to the
/// market board without ever saying so. The whole fixture below is that run: recipe r30406 Iolite, one Emery
/// short, one gatherable (5150) alongside it.
/// </para>
///
/// <para>
/// <b>Every check asserts on RENDERED text</b> (<see cref="PlanReport"/> / <see cref="RunReport"/>), not on an
/// internal value. That is deliberate and it is the lesson of the two previous defects on this thread: both were
/// renderer bugs where the right answer sat in memory and was dropped on the way to the player, so an
/// internal-value test stayed green through them.
/// </para>
///
/// <para>
/// <b>The most important check in this file is <c>D1</c></b>: a special-shop item that resolves to nothing must
/// still route to the market board, and manual must stay empty. That is the regression the "just move SpecialShop
/// above Market" fix would have caused - <c>SourceKind.SpecialShop</c> used to map to <c>Route.Manual</c>, so the
/// naive reorder would have turned an actionable market listing into a dead end.
/// </para>
/// </summary>
internal static class SpecialShopTests
{
    // The real ids from the incident, so a log line can be matched against a check by eye.
    private const uint Emery = 7601;
    private const uint Iolite = 30406;
    private const uint IxaliOaknot = 21073;
    private const uint FluoriteLens = 10103;
    private const uint StormSeal = 20;

    private static string Name(uint id) => id switch
    {
        Emery => "Emery",
        Iolite => "Iolite",
        IxaliOaknot => "Ixali Oaknot",
        FluoriteLens => "Fluorite Lens",
        StormSeal => "Storm Seal",
        _ => $"#{id}",
    };

    /// <summary>The game's own plurals, as LuminaGameData reads them from Item.Plural.</summary>
    private static string Plural(uint id) => id switch
    {
        IxaliOaknot => "Ixali Oaknots",
        FluoriteLens => "Fluorite Lenses",
        StormSeal => "Storm Seals",
        _ => "",
    };

    private static long? NoPrice(uint _) => null;
    private static long? Priced(uint _) => 900;

    /// <summary>A placed vendor in a teleportable zone - the shape the routing requires before it will act.</summary>
    private static SpecialShopCandidate Offer(
        string npc, string zone, uint npcId, uint territory, params (uint Item, int Qty)[] costs) =>
        new(Emery, 1769525 + npcId, $"{npc} Exchange", npc, zone, ReceiveQuantity: 1,
            costs.Select(c => new SpecialShopCost(c.Item, Name(c.Item), c.Qty, Plural(c.Item))).ToList(),
            new VendorCandidate(npcId, territory, 7, 24.9f, 22.7f, AetheryteId: 3, 20f, 20f, AetheryteDistance: 6f));

    private static SpecialShopCandidate Ixali => Offer("Ixali vendor", "North Shroud", 1009205, 154, (IxaliOaknot, 7));
    private static SpecialShopCandidate Quartermaster => Offer("Storm Quartermaster", "Limsa Lominsa", 1002389, 128, (StormSeal, 1500));

    /// <summary>Balances: rich enough for anything unless a check narrows it.</summary>
    private static Func<uint, int?> Wallet(params (uint Item, int? Held)[] held) =>
        id => held.FirstOrDefault(h => h.Item == id) is { Item: not 0 } hit ? hit.Held : 0;

    /// <summary>
    /// The 11:43 cart: Iolite needs one Emery (special-shop + marketable, as the live sheets classify it) and one
    /// gatherable. <paramref name="shops"/> decides what the plugin knows about currency vendors.
    /// </summary>
    private static DispatchPlan.Plan Plan(SpecialShopContext? shops, IReadOnlyList<SourceKind>? sources = null)
    {
        var leaf = new IngredientLeaf(Emery, Need: 1, Have: 0,
            sources ?? [SourceKind.SpecialShop, SourceKind.Market], EffortTier.SomeEffort);
        var data = new FakeGameData().Recipe(1, Iolite, 1, World.Bsm, 60, (Emery, 1));
        var graph = new RecipeGraph(data);
        var ventures = new VentureResolver(data);
        return DispatchPlan.Build([], [leaf], graph, ventures, [], null, null, shops);
    }

    /// <summary>A context that resolves the given offers and can pay for everything unless told otherwise.</summary>
    private static SpecialShopContext Ctx(
        IReadOnlyList<SpecialShopCandidate> offers, bool enabled = true, Func<uint, int?>? wallet = null) =>
        new(_ => offers, wallet ?? (_ => 10_000), enabled);

    /// <summary>Render a plan's blocked list the way /lcraft status and the Run tab do.</summary>
    private static string Rendered(DispatchPlan.Plan plan)
    {
        var blocked = PlanReport.BlockedFrom(plan, Name, NoPrice);
        var snap = RunSnapshot.Empty with { State = RunState.Blocked, Blocked = blocked };
        return string.Join("\n", RunReport.BlockedLines(snap));
    }

    public static IEnumerable<(string Name, Func<bool> Check)> Tests => new (string, Func<bool>)[]
    {
        // ---------------------------------------------------------------- D1: never make it worse

        ("D1 a special-shop item with NO resolvable vendor still routes to market, manual stays empty", () =>
        {
            var plan = Plan(Ctx([]));
            return plan.Market.Count == 1 && plan.Market[0].ItemId == Emery
                && plan.Manual.Count == 0 && plan.CurrencyShop.Count == 0;
        }),

        ("D1 the unresolvable case renders the SAME market line as before the feature existed", () =>
            Rendered(Plan(Ctx([]))) == Rendered(Plan(null))),

        ("D1 with no context at all the plan is byte-identical to the pre-feature one", () =>
        {
            var before = Plan(null);
            var after = Plan(SpecialShopContext.None);
            return before.Market.Count == after.Market.Count
                && before.Market[0].Where == "" && after.Market[0].Where == ""
                && after.CurrencyShop.Count == 0 && after.Manual.Count == 0;
        }),

        ("D1 an offer with a vendor but NO cost never reroutes (nothing to charge)", () =>
        {
            var free = Ixali with { Costs = Array.Empty<SpecialShopCost>() };
            var plan = Plan(Ctx([free]));
            return plan.CurrencyShop.Count == 0 && plan.Market.Count == 1 && plan.Manual.Count == 0;
        }),

        ("D1 an offer-resolver that THROWS leaves the item on the market board rather than killing the plan", () =>
        {
            var exploding = new SpecialShopContext(_ => throw new InvalidOperationException("sheet blew up"), _ => 10_000, true);
            var plan = Plan(exploding);
            return plan.Market.Count == 1 && plan.CurrencyShop.Count == 0 && plan.Manual.Count == 0;
        }),

        ("D1 a special-shop item that is NOT marketable still falls to manual, exactly as before", () =>
        {
            // The pre-feature fall-through: no Market source, so Manual is correct and is NOT a regression.
            var plan = Plan(Ctx([]), [SourceKind.SpecialShop]);
            return plan.Manual.Count == 1 && plan.Market.Count == 0 && plan.CurrencyShop.Count == 0;
        }),

        // ---------------------------------------------------------------- D2: the affordability gate

        ("D2 an affordable vendor wins the route", () =>
        {
            var plan = Plan(Ctx([Ixali], wallet: Wallet((IxaliOaknot, 7))));
            return plan.CurrencyShop.Count == 1 && plan.CurrencyShop[0].ItemId == Emery && plan.Market.Count == 0;
        }),

        ("D2 one token SHORT and the item stays on the market board", () =>
        {
            var plan = Plan(Ctx([Ixali], wallet: Wallet((IxaliOaknot, 6))));
            return plan.CurrencyShop.Count == 0 && plan.Market.Count == 1 && plan.Manual.Count == 0;
        }),

        ("D2 an UNREADABLE balance stays on the market board (null is not 'plenty')", () =>
        {
            var plan = Plan(Ctx([Ixali], wallet: _ => null));
            return plan.CurrencyShop.Count == 0 && plan.Market.Count == 1;
        }),

        ("D2 the vendor is still NAMED on the market line when it was too expensive", () =>
        {
            var plan = Plan(Ctx([Ixali], wallet: Wallet((IxaliOaknot, 6))));
            var rendered = Rendered(plan);
            // It must be the MARKET line that carries the name: under the naive reorder the item would fall to
            // Manual, whose line also names the vendor, and this check would pass while the routing regressed.
            return plan.Market.Count == 1 && plan.Manual.Count == 0
                && rendered.Contains("buy on the market board: Emery x1")
                && rendered.Contains("or Ixali vendor (North Shroud) for 7 Ixali Oaknots");
        }),

        ("D2 exactly enough currency is enough (the boundary is >=, not >)", () =>
            Plan(Ctx([Ixali], wallet: Wallet((IxaliOaknot, 7)))).CurrencyShop.Count == 1),

        ("D2 cost scales with quantity, so 2 units of a 7-token item needs 14", () =>
        {
            bool Routes(int held) => SpecialShopChoice.Affordable(Ixali, 2, Wallet((IxaliOaknot, held)));
            return Routes(14) && !Routes(13);
        }),

        ("D2 a shop handing out 3 per trade charges once for 3 units, not three times", () =>
        {
            var bulk = Ixali with { ReceiveQuantity = 3 };
            return bulk.TradesFor(3) == 1 && bulk.TradesFor(4) == 2
                && SpecialShopChoice.TotalCost(bulk, 3) == 7 && SpecialShopChoice.TotalCost(bulk, 4) == 14;
        }),

        ("D2 every currency of a multi-currency price must be affordable, not just one", () =>
        {
            var both = Offer("Trader", "Ul'dah", 1001, 130, (IxaliOaknot, 7), (StormSeal, 100));
            return SpecialShopChoice.Affordable(both, 1, Wallet((IxaliOaknot, 7), (StormSeal, 100)))
                && !SpecialShopChoice.Affordable(both, 1, Wallet((IxaliOaknot, 7), (StormSeal, 99)));
        }),

        // ---------------------------------------------------------------- cheapest-affordable ranking

        ("cheapest affordable offer wins: 7 Ixali Oaknot beats 1500 Storm Seals", () =>
        {
            var plan = Plan(Ctx([Quartermaster, Ixali], wallet: Wallet((IxaliOaknot, 7), (StormSeal, 99_999))));
            return plan.CurrencyShop.Count == 1 && plan.CurrencyShop[0].Offer.NpcName == "Ixali vendor";
        }),

        ("the cheap offer being UNAFFORDABLE hands the route to the expensive one the player can pay", () =>
        {
            var plan = Plan(Ctx([Quartermaster, Ixali], wallet: Wallet((IxaliOaknot, 0), (StormSeal, 99_999))));
            return plan.CurrencyShop.Count == 1 && plan.CurrencyShop[0].Offer.NpcName == "Storm Quartermaster";
        }),

        ("neither affordable = market board, and BOTH are still named", () =>
        {
            var ctx = Ctx([Quartermaster, Ixali], wallet: Wallet((IxaliOaknot, 0), (StormSeal, 0)));
            var plan = Plan(ctx);
            var rendered = Rendered(plan);
            return plan.CurrencyShop.Count == 0 && plan.Market.Count == 1 && plan.Manual.Count == 0
                && rendered.Contains("or Ixali vendor (North Shroud) for 7 Ixali Oaknots")
                && rendered.Contains("or Storm Quartermaster (Limsa Lominsa) for 1,500 Storm Seals");
        }),

        // ---------------------------------------------------------------- D3: the setting

        ("D3 with the reroute OFF the item stays on the market board", () =>
        {
            var plan = Plan(Ctx([Ixali], enabled: false));
            // "Stays on the market board" means the MARKET channel, not merely "not the currency channel" -
            // a plan that silently fell to Manual would pass the weaker assertion, and Manual is the dead end
            // this whole card exists to keep special-shop items out of.
            return plan.CurrencyShop.Count == 0 && plan.Market.Count == 1 && plan.Manual.Count == 0;
        }),

        ("D3 with the reroute OFF the vendor is STILL named - turning it off must not restore the silence", () =>
        {
            var plan = Plan(Ctx([Ixali], enabled: false));
            var rendered = Rendered(plan);
            return plan.Market.Count == 1 && plan.Manual.Count == 0
                && rendered.Contains("buy on the market board: Emery x1")
                && rendered.Contains("or Ixali vendor (North Shroud) for 7 Ixali Oaknots");
        }),

        // ---------------------------------------------------------------- part C: the rendered text

        ("C the market line names the vendor and the price, in the sentence the player reads", () =>
        {
            var plan = Plan(Ctx([Ixali], wallet: Wallet((IxaliOaknot, 6))));
            var line = PlanReport.MarketLine(plan, Name, NoPrice);
            return line is not null
                && line.Contains("Emery x1")
                && line.Contains("or Ixali vendor (North Shroud) for 7 Ixali Oaknots");
        }),

        ("C the market line keeps its gil estimate when the vendor clause is added", () =>
        {
            var line = PlanReport.MarketLine(Plan(Ctx([Ixali], wallet: Wallet((IxaliOaknot, 6)))), Name, Priced);
            return line is not null && line.Contains("(~900)") && line.Contains("est. 900 gil")
                && line.Contains("or Ixali vendor");
        }),

        ("C a market item with no currency vendor renders exactly as it did before (no stray dash)", () =>
        {
            var line = PlanReport.MarketLine(Plan(Ctx([])), Name, Priced);
            return line == "Market board list (1 item, est. 900 gil): Emery x1 (~900)";
        }),

        ("C the routed currency line names item, quantity, NPC, zone and price", () =>
        {
            var lines = PlanReport.CurrencyLines(Plan(Ctx([Ixali], wallet: Wallet((IxaliOaknot, 7)))), Name);
            return lines.Count == 1
                && lines[0] == "trade for Emery x1 at Ixali vendor (North Shroud) for 7 Ixali Oaknots";
        }),

        ("C the blocked report gives a currency shop its own line, not the generic tail", () =>
        {
            var rendered = Rendered(Plan(Ctx([Ixali], wallet: Wallet((IxaliOaknot, 7)))));
            return rendered.Contains("trade for at a currency shop: Emery x1 - Ixali vendor (North Shroud) for 7 Ixali Oaknots")
                && !rendered.Contains("buy on the market board");
        }),

        ("C the manual line appends real vendor names instead of only SourceKind enum names", () =>
        {
            // Unmarketable special-shop item whose vendor is known but unaffordable: manual, and it says who.
            var plan = Plan(Ctx([Ixali], wallet: Wallet((IxaliOaknot, 0))), [SourceKind.SpecialShop]);
            var line = PlanReport.ManualLine(plan, Name);
            return plan.Manual.Count == 1 && line is not null
                && line.Contains("Emery x1 (SpecialShop)")
                && line.Contains("or Ixali vendor (North Shroud) for 7 Ixali Oaknots");
        }),

        ("C naming folds duplicate placements of one NPC into a single clause", () =>
        {
            // Same NPC, same shop, two aetherytes - the resolver emits one candidate per aetheryte.
            var twice = new[] { Ixali, Ixali with { Where = Ixali.Where with { AetheryteId = 9, AetheryteDistance = 30f } } };
            var named = SpecialShopChoice.Named(twice, 1);
            return named.Count == 1;
        }),

        ("C the naming clause is empty, not a stray 'or', when nothing resolved", () =>
            SpecialShopChoice.NameClause(Array.Empty<SpecialShopCandidate>(), 1) == ""),

        ("C a price uses the game's PLURAL above one - '1,500 Storm Seals', never '1,500 Storm Seal'", () =>
            Quartermaster.PriceFor(1) == "1,500 Storm Seals"),

        ("C a price of exactly one uses the singular - '1 Fluorite Lens', never 'Fluorite Lenses'", () =>
        {
            var talan = Offer("Talan", "Mor Dhona", 1006972, 156, (FluoriteLens, 1));
            return talan.PriceFor(1) == "1 Fluorite Lens";
        }),

        ("C an item with NO plural in the sheet falls back to its singular rather than rendering blank", () =>
            new SpecialShopCost(999, "Mystery Token", 5).Phrase == "5 Mystery Token"),

        ("C large prices keep their thousands separator", () =>
            Quartermaster.PriceFor(2) == "3,000 Storm Seals"),

        // ---------------------------------------------------------------- the craft deferral

        ("the deferred craft says 'currency shop', not 'market', when that is where the material comes from", () =>
        {
            var leaf = new IngredientLeaf(Emery, 1, 0, [SourceKind.SpecialShop, SourceKind.Market], EffortTier.SomeEffort);
            var data = new FakeGameData().Recipe(1, Iolite, 1, World.Bsm, 60, (Emery, 1));
            var graph = new RecipeGraph(data);
            var assessment = new RecipeAssessment(1, EffortTier.SomeEffort, 0, [leaf]);
            var plan = DispatchPlan.Build([new DispatchPlan.Line(assessment, 1)], [leaf], graph,
                new VentureResolver(data), [], null, null, Ctx([Ixali], wallet: Wallet((IxaliOaknot, 7))));
            return plan.Deferred.Count == 1 && plan.Deferred[0].Reason == $"needs currency shop #{Emery}";
        }),

        ("the same craft still says 'market' when the vendor did not resolve - the 11:43 line, unchanged", () =>
        {
            var leaf = new IngredientLeaf(Emery, 1, 0, [SourceKind.SpecialShop, SourceKind.Market], EffortTier.SomeEffort);
            var data = new FakeGameData().Recipe(1, Iolite, 1, World.Bsm, 60, (Emery, 1));
            var graph = new RecipeGraph(data);
            var assessment = new RecipeAssessment(1, EffortTier.SomeEffort, 0, [leaf]);
            var plan = DispatchPlan.Build([new DispatchPlan.Line(assessment, 1)], [leaf], graph,
                new VentureResolver(data), [], null, null, Ctx([]));
            return plan.Deferred.Count == 1 && plan.Deferred[0].Reason == $"needs market #{Emery}";
        }),

        // ---------------------------------------------------------------- the wave loop

        ("a currency-shop item counts as progress-visible, so the stall guard cannot end the run early", () =>
            DispatchLoop.ItemsOf(Plan(Ctx([Ixali], wallet: Wallet((IxaliOaknot, 7)))), []).Contains(Emery)),

        ("a currency-shop-only plan says what it is waiting on, instead of a bare 'nothing to do'", () =>
            DispatchLoop.Describe(Plan(Ctx([Ixali], wallet: Wallet((IxaliOaknot, 7))))).Contains("1 currency-shop item")),

        ("a plan holding only a currency-shop item is not IsEmpty", () =>
            !Plan(Ctx([Ixali], wallet: Wallet((IxaliOaknot, 7)))).IsEmpty),

        // ---------------------------------------------------------------- routing order

        ("a gil vendor still outranks a currency shop - gil is the cheaper currency to spend", () =>
        {
            var leaf = new IngredientLeaf(Emery, 1, 0, [SourceKind.GilVendor, SourceKind.SpecialShop, SourceKind.Market], EffortTier.Easy);
            var data = new FakeGameData();
            var plan = DispatchPlan.Build([], [leaf], new RecipeGraph(data), new VentureResolver(data), [], null, null,
                Ctx([Ixali], wallet: Wallet((IxaliOaknot, 7))));
            return plan.Vendor.Count == 1 && plan.CurrencyShop.Count == 0;
        }),

        ("a gather still outranks a currency shop - the 5150 in the same cart pass is unaffected", () =>
        {
            var leaf = new IngredientLeaf(5150, 1, 0, [SourceKind.SpecialShop, SourceKind.RegularNode, SourceKind.Market], EffortTier.Easy);
            var data = new FakeGameData();
            var plan = DispatchPlan.Build([], [leaf], new RecipeGraph(data), new VentureResolver(data), [], null, null,
                Ctx([Ixali], wallet: Wallet((IxaliOaknot, 7))));
            return plan.Gathers.Count == 1 && plan.CurrencyShop.Count == 0;
        }),

        ("the per-leaf button offers the same channel the plan chose", () =>
        {
            var leaf = new IngredientLeaf(Emery, 1, 0, [SourceKind.SpecialShop, SourceKind.Market], EffortTier.SomeEffort);
            var data = new FakeGameData();
            var ctx = Ctx([Ixali], wallet: Wallet((IxaliOaknot, 7)));
            var (channel, _) = DispatchPlan.RouteLeaf(leaf, World.Bsm, new RecipeGraph(data), new VentureResolver(data), [], null, ctx);
            var (fallback, _) = DispatchPlan.RouteLeaf(leaf, World.Bsm, new RecipeGraph(data), new VentureResolver(data), [], null, Ctx([]));
            return channel == SourceKind.SpecialShop && fallback == SourceKind.Market;
        }),

        // ---------------------------------------------------------------- an item already in hand

        ("an item we already have is never routed to a currency shop", () =>
        {
            var leaf = new IngredientLeaf(Emery, 1, 1, [SourceKind.OnHand], EffortTier.Now);
            var data = new FakeGameData();
            var plan = DispatchPlan.Build([], [leaf], new RecipeGraph(data), new VentureResolver(data), [], null, null,
                Ctx([Ixali], wallet: Wallet((IxaliOaknot, 7))));
            return plan.CurrencyShop.Count == 0 && plan.Market.Count == 0 && plan.IsEmpty;
        }),
    };
}
