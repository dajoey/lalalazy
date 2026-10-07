namespace ArmoireAutoFill.Data.Shopping;

// Pure selection logic: which not-owned armoire-eligible items can Knightshopper buy,
// grouped per currency, with per-item reasons for everything excluded.
//
// Owned-state treatment (documented decision, task 2026-10-07):
//   * InArmoire  — already stored; never a shopping candidate.
//   * InInventory — in bags, armoury chest, saddlebags or a gearset. Storing these is
//     AutoStore's job, not Knightshopper's; they are excluded from the shopping list
//     so the list never prompts buying something already in the inventory.
//   * NotOwned — the only state that produces a shopping candidate.
//
// Pure file: no Dalamud types (also compiled by the offline harness).
public static class ShoppingListBuilder
{
    public sealed record Input(
        IReadOnlyList<ArmouryEntry> ArmoireItems,
        IReadOnlySet<uint> OwnedItemIds,
        IReadOnlyList<ShopEntry> Catalog,
        IReadOnlyDictionary<uint, string> ItemNames,
        int MaxItemsPerCurrency = KnightshopperShare.MaxItems);

    // One armoire-eligible item with its current owned state.
    public sealed record ArmouryEntry(uint ItemId, Ownership Owned);

    public enum Ownership : byte
    {
        NotOwned = 0,
        InInventory = 1,
        InArmoire = 2,
    }

    public static ShoppingResult Build(Input input)
    {
        // Index the catalog by item id; an item may be sold by many shops and families.
        var catalogByItem = new Dictionary<uint, List<ShopEntry>>();
        foreach (var entry in input.Catalog)
        {
            if (!catalogByItem.TryGetValue(entry.ItemId, out var list))
                catalogByItem[entry.ItemId] = list = [];
            list.Add(entry);
        }

        var candidates = new List<ShoppingCandidate>();
        var excluded = new List<ExcludedItem>();
        var missingNotBuyable = 0;

        foreach (var item in input.ArmoireItems)
        {
            var name = input.ItemNames.GetValueOrDefault(item.ItemId, $"item {item.ItemId}");

            switch (item.Owned)
            {
                case Ownership.InArmoire:
                    continue; // stored — no mention needed

                case Ownership.InInventory:
                    excluded.Add(new ExcludedItem(item.ItemId, name,
                        "in inventory/armoury chest — AutoStore stores it, no purchase needed"));
                    continue;
            }

            // NotOwned: shop for it if any entry exists.
            if (!catalogByItem.TryGetValue(item.ItemId, out var entries))
            {
                missingNotBuyable++;
                continue;
            }

            // One candidate per item per currency: cheapest then lowest shop id, for
            // stable output. Multiple families legitimately produce separate candidates.
            var picked = entries
                .OrderBy(e => e.Price ?? uint.MaxValue)
                .ThenBy(e => e.ShopId)
                .ThenBy(e => e.VendorId)
                .GroupBy(e => e.CurrencyId)
                .Select(g => g.First());
            foreach (var entry in picked)
                candidates.Add(new ShoppingCandidate(item.ItemId, name, entry.CurrencyId,
                    CurrencyNames.For(entry.CurrencyId), entry));
        }

        var currencies = new List<CurrencyCandidates>();
        foreach (var group in candidates.GroupBy(c => c.CurrencyId).OrderBy(g => g.Key))
        {
            var ordered = group
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var truncated = ordered.Count > input.MaxItemsPerCurrency;
            var kept = truncated ? ordered.Take(input.MaxItemsPerCurrency).ToList() : ordered;
            var total = kept.Sum(c => c.Entry.Price ?? 0);
            currencies.Add(new CurrencyCandidates(group.Key, group.First().CurrencyName, kept, truncated, total));
        }

        return new ShoppingResult(currencies, excluded, missingNotBuyable);
    }
}

public static class CurrencyNames
{
    // Knightshopper's currency enum ordering, fixed in its share format.
    public static string For(byte currencyId) => currencyId switch
    {
        0 => "Bicolor Gemstone",
        1 => "Company Seal",
        2 => "Gil",
        3 => "Hunt",
        4 => "MGP",
        5 => "PvP",
        6 => "Scrip",
        7 => "Tomestone",
        8 => "Firmament",
        9 => "Cosmocredits",
        10 => "Occult Crescent",
        _ => $"Currency {currencyId}",
    };
}
