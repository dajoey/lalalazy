using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>
/// One currency an offer costs: the item id, its name, and how many per trade.
/// <para>
/// "Currency" here is just an item - Grand Company seals, beast-tribe tokens, tomestones and Fluorite Lenses are
/// all ordinary <c>Item</c> rows, which is what makes <see cref="IInventory.CurrencyBalance"/> able to count them.
/// </para>
/// <param name="Plural">
/// The game's own plural for the item (<c>Item.Plural</c>), because "1,500 Storm Seal" reads like a bug report.
/// Taken from the sheet rather than derived with an English rule: the client already stores the correct plural for
/// every item and every language, and inventing one would be wrong for exactly the irregular names a currency list
/// is full of. Falls back to <see cref="Name"/> when the sheet has nothing, which is never worse than today.
/// </param>
/// </summary>
public sealed record SpecialShopCost(uint ItemId, string Name, int Quantity, string Plural = "")
{
    /// <summary>"7 Ixali Oaknots", "1 Fluorite Lens" - the amount with the right form of the name.</summary>
    public string Phrase => $"{Quantity:N0} {(Quantity == 1 ? Name : string.IsNullOrEmpty(Plural) ? Name : Plural)}";
}

/// <summary>
/// One <c>SpecialShop</c> entry that hands out an item: which shop, how many units per trade, and what it costs.
/// This is the data <c>LuminaGameData.LoadShops</c> used to read and throw away (only the item id survived, into a
/// <c>HashSet</c>), which is why the plugin could say "some special shop gives this out" and nothing more.
/// <para>
/// Shop identity is kept because the <b>placement</b> chain hangs off it: <c>ENpcBase.ENpcData</c> handlers name
/// SpecialShop rows exactly the way they name GilShop rows, so shop id -> NPC -> map coordinates reuses the gil
/// vendor machinery wholesale.
/// </para>
/// </summary>
public sealed record SpecialShopOffer(
    uint ShopId,
    string ShopName,
    uint ItemId,
    int ReceiveQuantity,
    IReadOnlyList<SpecialShopCost> Costs);

/// <summary>
/// An offer that has been resolved all the way to somewhere the player can actually stand: a named NPC, in a
/// territory with a teleportable aetheryte, with a known cost. <b>Only a fully resolved candidate may change
/// routing</b> (decision D1) - anything less falls through to the market board exactly as before.
/// </summary>
/// <param name="Where">The placement, in the same shape and space the gil-vendor ranking uses.</param>
public sealed record SpecialShopCandidate(
    uint ItemId,
    uint ShopId,
    string ShopName,
    string NpcName,
    string TerritoryName,
    int ReceiveQuantity,
    IReadOnlyList<SpecialShopCost> Costs,
    VendorCandidate Where)
{
    /// <summary>Trades needed to cover <paramref name="units"/> (a shop that hands out 3 at a time needs fewer).</summary>
    public int TradesFor(int units) => units <= 0 ? 0 : (units + Math.Max(1, ReceiveQuantity) - 1) / Math.Max(1, ReceiveQuantity);

    /// <summary>What <paramref name="units"/> costs in total, per currency.</summary>
    public IReadOnlyList<SpecialShopCost> CostFor(int units)
    {
        var trades = TradesFor(units);
        return Costs.Select(c => c with { Quantity = c.Quantity * trades }).ToList();
    }

    /// <summary>"7 Ixali Oaknot", "1500 Storm Seals + 200 Gil" - the price for <paramref name="units"/>, in words.</summary>
    public string PriceFor(int units) =>
        string.Join(" + ", CostFor(units).Select(c => c.Phrase));

    /// <summary>"Ixali vendor (North Shroud) for 7 Ixali Oaknot" - the whole offer in one clause.</summary>
    public string Describe(int units) => $"{NpcName} ({TerritoryName}) for {PriceFor(units)}";
}

/// <summary>
/// The special-shop half of <see cref="DispatchPlan.Build"/>'s inputs. Everything the plan needs to name a
/// currency vendor and decide whether to prefer it, with no adapter type in sight so the harness can drive it.
/// <para>
/// <see cref="None"/> is the pre-0.1.6.7 behaviour <b>exactly</b>: no offers, no balances, no reroute. Every
/// existing caller that does not pass a context keeps the old routing, which is what makes this change additive.
/// </para>
/// </summary>
/// <param name="Offers">Placed, teleportable, costed offers for an item; empty = nothing resolved (D1 fallback).</param>
/// <param name="Balance">
/// The player's readable balance of a currency item, or <c>null</c> when it cannot be read at all. A currency the
/// player simply has none of returns 0, not null - see <see cref="SpecialShopChoice.Affordable"/> for why the
/// difference does not matter to the outcome (both keep the item on the market board).
/// </param>
/// <param name="RerouteEnabled">The user setting (D3). When false the offers are still NAMED, never taken.</param>
/// <param name="Where">Where the player is standing, for ranking equally-priced offers.</param>
public sealed record SpecialShopContext(
    Func<uint, IReadOnlyList<SpecialShopCandidate>> Offers,
    Func<uint, int?> Balance,
    bool RerouteEnabled,
    VendorContext? Where = null)
{
    public static readonly SpecialShopContext None =
        new(_ => Array.Empty<SpecialShopCandidate>(), _ => null, false);

    /// <summary>Offers for an item, never throwing - a locator that blows up must not take the plan with it.</summary>
    public IReadOnlyList<SpecialShopCandidate> For(uint itemId)
    {
        try { return Offers(itemId) ?? Array.Empty<SpecialShopCandidate>(); }
        catch { return Array.Empty<SpecialShopCandidate>(); }
    }

    private int? Read(uint currencyItemId)
    {
        try { return Balance(currencyItemId); }
        catch { return null; }
    }

    /// <summary>The balance reader with its exception handling, for <see cref="SpecialShopChoice"/>.</summary>
    public Func<uint, int?> SafeBalance => Read;
}

/// <summary>
/// Which currency vendor to send the player to, and - the part that makes this safe to ship - <b>whether to send
/// them at all</b> (card t_b431de3a, decisions D1 and D2).
/// <para>
/// <b>D1, never make it worse.</b> Before this class, <c>SourceKind.SpecialShop</c> mapped to
/// <c>Route.Manual</c>: "needs a manual source: Emery x1", a dead end. The market board, whatever its faults, is
/// at least actionable, so the naive "move SpecialShop above Market" would have been a regression, not a fix. A
/// special-shop route is therefore taken ONLY when the item resolves to a named, placed NPC in a teleportable
/// zone with a known cost. Any miss - no offer, no handler NPC, no placement, no aetheryte, no readable cost -
/// falls through to Market exactly as it did before. Measured on the live sheets: 12886 items are receivable from
/// a special shop, 6364 have any handler NPC, and only 668 have a PLACED one. <b>A miss is the normal case</b>,
/// so the fallback is the hot path and has to be the safe one.
/// </para>
/// <para>
/// <b>D2, the affordability gate.</b> A currency vendor is preferred only when the player can already pay for it.
/// The alternative - routing to a shop the player cannot afford - trades an actionable market listing for a trip
/// to a counter that will refuse them. And the plugin cannot weigh 1500 Grand Company seals against a few thousand
/// gil; it has no exchange rate and no business inventing one. So: readable balance that already covers the cost,
/// or the vendor is NAMED and the item stays on the market board.
/// </para>
/// <para>
/// <b>Why an unreadable balance and a zero balance behave identically.</b> Both refuse. That is deliberate, and it
/// is what makes the currency read safe to get wrong: the only way a balance read can fail is by returning too
/// LITTLE (a container that was not summed, a not-logged-in client), which can only ever move an item back to the
/// market board. There is no misread that spends the player's seals.
/// </para>
/// </summary>
public static class SpecialShopChoice
{
    /// <summary>
    /// Can the player pay for <paramref name="units"/> at this offer right now? False when any currency's balance
    /// is unreadable or short. <paramref name="balance"/> returns <c>null</c> for "cannot tell".
    /// </summary>
    public static bool Affordable(SpecialShopCandidate offer, int units, Func<uint, int?> balance)
    {
        if (units <= 0 || offer.Costs.Count == 0) return false;
        foreach (var cost in offer.CostFor(units))
        {
            if (cost.Quantity <= 0) return false;
            if (balance(cost.ItemId) is not { } have || have < cost.Quantity) return false;
        }
        return true;
    }

    /// <summary>
    /// Total currency units <paramref name="units"/> costs at this offer, summed across every currency it charges.
    /// <para>
    /// This is the "cheapest" metric, and it is raw units on purpose: 7 Ixali Oaknot beats 1500 Storm Seals, which
    /// is the comparison the card asked for. There is no exchange rate between seals, tokens and tomestones, and
    /// inventing one would be a fiction the player never agreed to. Because the ranking only ever chooses among
    /// offers the player can ALREADY afford, the worst case of a crude metric is picking a differently-cheap
    /// affordable vendor - never an unaffordable or unreachable one.
    /// </para>
    /// </summary>
    public static long TotalCost(SpecialShopCandidate offer, int units) =>
        offer.CostFor(units).Sum(c => (long)c.Quantity);

    /// <summary>
    /// The offer to actually route to, or <c>null</c> to leave the item where it was. Cheapest affordable offer
    /// wins; ties are broken by the same <see cref="VendorChoice.Score"/> the gil vendors use, so an equally
    /// priced shop in the zone the player is standing in beats one across the world, and the answer is stable.
    /// </summary>
    public static SpecialShopCandidate? Best(
        IEnumerable<SpecialShopCandidate> offers, int units, Func<uint, int?> balance, VendorContext? context)
    {
        SpecialShopCandidate? best = null;
        var bestCost = long.MaxValue;
        var bestScore = default(VendorScore);
        foreach (var offer in offers)
        {
            if (!Affordable(offer, units, balance)) continue;
            var cost = TotalCost(offer, units);
            var score = VendorChoice.Score(offer.Where, context);
            if (best is null || cost < bestCost || (cost == bestCost && score < bestScore))
            {
                best = offer;
                bestCost = cost;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>
    /// Every offer worth NAMING for an item, cheapest first - what the market and manual lines print whether or
    /// not the reroute fires (part C, and the half that ships even with the setting off).
    /// <para>
    /// Unlike <see cref="Best"/> this does <b>not</b> apply the affordability gate: telling the player "the Ixali
    /// vendor sells this for 7 Ixali Oaknot" is useful even when they have six, and hiding it would be the same
    /// "we know but will not say" behaviour that started this card. It is capped so one line cannot become a wall.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SpecialShopCandidate> Named(
        IEnumerable<SpecialShopCandidate> offers, int units, int max = 3)
    {
        var byNpc = new Dictionary<(uint Npc, uint Shop), SpecialShopCandidate>();
        foreach (var o in offers)
            byNpc.TryAdd((o.Where.NpcId, o.ShopId), o);
        return byNpc.Values
            .OrderBy(o => TotalCost(o, units))
            .ThenBy(o => o.Where.NpcId)
            .Take(Math.Max(0, max))
            .ToList();
    }

    /// <summary>
    /// "or Ixali vendor (North Shroud) for 7 Ixali Oaknot, or Talan (Mor Dhona) for 1 Fluorite Lens" - the clause
    /// appended to a market or manual line so the player is TOLD who sells it. Empty string when nothing resolved,
    /// so callers can concatenate unconditionally.
    /// </summary>
    public static string NameClause(IReadOnlyList<SpecialShopCandidate> named, int units) =>
        named.Count == 0 ? "" : string.Join(", ", named.Select(o => "or " + o.Describe(units)));
}
