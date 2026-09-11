using System.Collections.Generic;
using System.Linq;
using LazyMarketCompanion.AutoMarket;

namespace LazyMarketCompanion;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.
//
// NOTE on the namespace (PriceMath/MarketGate precedent): this file sits under AutoMarket/ with the
// rest of the Dalamud-free logic, but declares the PARENT namespace LazyMarketCompanion on purpose.
// CategoryRetainerRule is a Configuration property type (Configuration.cs, also LazyMarketCompanion),
// so keeping it in the parent namespace means Configuration.cs needs no new using directive, and the
// AutoMarket namespace still sees everything here because C# searches enclosing namespaces.
//
// Category routing (Helm thread t-joey-1789092103262, Joey chose "category-routing"): a whole
// market-board search category (Item.ItemSearchCategory.RowId - the market board "section" grouping,
// NOT the finer ItemUICategory) can be assigned to one retainer; Auto-Market then only lists an item of
// that category on the retainer it is assigned to. A per-item "skip routing" checkbox
// (AutoMarketItem.ExcludeFromCategoryRouting) opts an item out entirely - it sells normally from
// wherever it already sits, on every retainer, exactly like before this feature existed.
//
// BINDING NOTE FROM JOEY (must hold): "Make sure to only handle marketable items." A non-marketable
// item is ALWAYS eligible everywhere - routing is a no-op for it, the exact same test the two-dot
// marker system already uses (ItemNameResolver.IsMarketable: !item.IsUntradable &&
// item.ItemSearchCategory.RowId != 0). It could never have listed anyway, but the routing filter must
// never be the reason it silently stops - fail OPEN, never closed, on any ambiguity, including a sheet
// miss (no categoryByKey entry for the rule's key).
//
// This only reliably divides items that start out in the BAGS (SellFromBags): the filter runs on the
// per-retainer rule list handed to AutoMarketPlanner.Plan, before a free market slot is claimed - it
// does not move an item that is already sitting in the wrong retainer's own inventory.

/// <summary>
/// Routes a whole market-board search category (Item.ItemSearchCategory's RowId - the market board
/// "section" grouping, NOT the finer ItemUICategory) to one named retainer. Auto-Market treats an item
/// whose category maps here, and is not on the retainer named, as ineligible to list on this retainer -
/// it is left exactly where it is, never vendored, never touched.
/// </summary>
public sealed class CategoryRetainerRule
{
  public uint CategoryId { get; set; }

  public string RetainerName { get; set; } = string.Empty;
}

/// <summary>
/// What the game/config say about one Auto-Market rule's item, resolved once per pass by the game-side
/// caller (AutoMarketService, which has the Item sheet). Key = <c>$"{ItemId}:{(HQ ? "hq" : "nq")}"</c>,
/// matching <see cref="AutoMarketItem.Key"/> and <see cref="ItemRule"/>'s own (ItemId, HQ) pair.
/// </summary>
public sealed record ItemCategoryInfo(uint ItemId, bool HQ, uint CategoryId, bool Marketable);

public static class CategoryRouter
{
  /// <summary>
  /// True when this item may list on the retainer currently being processed. Exclusion (either the
  /// per-item checkbox or "not marketable") always wins; a categoryRules entry for the item's category
  /// restricts it to exactly the one retainer named there, and having no mapped rule at all means no
  /// restriction (list everywhere, today's behaviour).
  /// </summary>
  public static bool EligibleForRetainer(ItemCategoryInfo info, bool excludeFromRouting,
    IReadOnlyList<CategoryRetainerRule> categoryRules, string retainerName)
  {
    if (excludeFromRouting || !info.Marketable)
      return true;

    var mapped = categoryRules.FirstOrDefault(r => r.CategoryId == info.CategoryId);
    return mapped == null || mapped.RetainerName == retainerName;
  }

  /// <summary>
  /// Filters an already-built enabled-rule list down to the ones eligible for ONE retainer. Called at
  /// each per-retainer entry point (see MarketAutomation) BEFORE the rules reach AutoMarketPlanner.Plan,
  /// so an item routed elsewhere never consumes this retainer's scarce free slots.
  ///
  /// A rule whose key is missing from categoryByKey (a sheet miss - the Item row could not be resolved)
  /// fails OPEN: it is treated exactly like "not marketable" and stays eligible everywhere, on the same
  /// reasoning as the marketable gate above - an ambiguous item must never be the one routing silently
  /// stops from listing.
  /// </summary>
  public static List<ItemRule> FilterForRetainer(IReadOnlyList<ItemRule> rules,
    IReadOnlyDictionary<string, ItemCategoryInfo> categoryByKey,
    IReadOnlyDictionary<string, bool> excludeByKey,
    IReadOnlyList<CategoryRetainerRule> categoryRules,
    string retainerName)
  {
    var result = new List<ItemRule>(rules.Count);
    foreach (var rule in rules)
    {
      var key = $"{rule.ItemId}:{(rule.HQ ? "hq" : "nq")}";
      var excludeFromRouting = excludeByKey.TryGetValue(key, out var ex) && ex;

      if (!categoryByKey.TryGetValue(key, out var info))
      {
        // Sheet miss: fail open, same direction as "not marketable".
        result.Add(rule);
        continue;
      }

      if (EligibleForRetainer(info, excludeFromRouting, categoryRules, retainerName))
        result.Add(rule);
    }
    return result;
  }
}
