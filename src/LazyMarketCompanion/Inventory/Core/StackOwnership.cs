using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.Inventory;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (Inventory cases).
//
// "Who handles this stack": every piece of automation that will move, sell, destroy or deliberately protect a
// stack of this item, resolved from facts the game side reads once (Item sheet, LMC config, AutoRetainer's
// config, gearsets, last-seen retainer holdings). The tooltip prints the list; the venture-loot rails and the
// gear mover use the SAME resolution, so the tooltip can never say "nothing handles this" about a stack a rail
// protects, or the other way round.

/// <summary>The Item-sheet facts every Inventory decision needs, read once per item id by the game side.</summary>
/// <param name="UiCategory">Item.ItemUICategory row id (what AutoRetainer entrust categories match).</param>
/// <param name="SearchCategory">Item.ItemSearchCategory row id (market-board section; 0 = cannot be listed).</param>
/// <param name="Rarity">Item.Rarity: 1 common (white), 2 uncommon (green), 3 rare (blue), 4 relic (purple), 7 aetherial (pink).</param>
/// <param name="ArmourySlot">Where the item goes in the Armoury Chest; None = not equippable gear.</param>
/// <param name="Desynth">Item.Desynth: non-zero when the item can be desynthesized.</param>
/// <param name="PriceLow">Item.PriceLow: the NPC vendor price the retainer sells at (0 = cannot be vendored).</param>
public sealed record ItemFacts(
  uint ItemId,
  uint UiCategory,
  uint SearchCategory,
  bool Untradable,
  bool Unique,
  byte Rarity,
  ArmourySlot ArmourySlot,
  uint Desynth,
  uint PriceLow,
  uint PriceMid)
{
  /// <summary>The same marketability test the bag markers and the context menu use.</summary>
  public bool Marketable => !Untradable && SearchCategory != 0;

  public bool Equippable => ArmourySlot != ArmourySlot.None;

  /// <summary>Blue (rare) and above.</summary>
  public bool Rare => Rarity >= 3;
}

/// <summary>One Auto-Market list entry, as the ownership resolver needs it (mirrors AutoMarketItem).</summary>
public sealed record AmEntry(uint ItemId, bool Hq, bool Enabled, bool ExcludeFromRouting);

public enum OwnerKind
{
  LmcAutoMarket,
  LmcAutoMarketUnticked,
  LmcCategoryRoute,
  ArEntrustItem,
  ArEntrustCategory,
  ArEntrustDuplicates,
  ArEntrustUnassignedPlan,
  ArVendorHard,
  ArVendorSoft,
  ArDiscard,
  ArDesynth,
  ArProtect,
  Gearset,
}

/// <summary>One line of "who handles this stack". <paramref name="Acts"/> = this owner moves, sells or destroys it.</summary>
public sealed record Owner(OwnerKind Kind, string Text, bool Acts);

/// <summary>Everything <see cref="StackOwnership.Resolve"/> reads.</summary>
/// <param name="RetainerHoldings">Last-seen item ids per retainer name (for AutoRetainer's "duplicates" rule); null/absent = unknown.</param>
/// <param name="GearsetIds">1-based gearset numbers whose slots reference this item id (either quality).</param>
public sealed record OwnershipInput(
  uint ItemId,
  bool Hq,
  ItemFacts? Item,
  IReadOnlyList<AmEntry> AutoMarket,
  IReadOnlyList<CategoryRetainerRule> Routes,
  ArSnapshot Ar,
  IReadOnlyDictionary<string, IReadOnlySet<uint>>? RetainerHoldings,
  IReadOnlyList<int> GearsetIds);

public static class StackOwnership
{
  public const string NothingText = "Nothing handles this stack.";

  public static List<Owner> Resolve(OwnershipInput input)
  {
    var owners = new List<Owner>();
    var id = input.ItemId;
    if (id == 0)
      return owners;

    // --- LMC Auto-Market list (quality is part of the identity, exactly as the config stores it) ---
    var am = input.AutoMarket.FirstOrDefault(e => e.ItemId == id && e.Hq == input.Hq);
    if (am != null)
    {
      owners.Add(am.Enabled
        ? new Owner(OwnerKind.LmcAutoMarket, $"LMC Auto-Market lists it ({(input.Hq ? "HQ" : "NQ")} entry)", true)
        : new Owner(OwnerKind.LmcAutoMarketUnticked, $"LMC Auto-Market entry is unticked ({(input.Hq ? "HQ" : "NQ")}) - not listed", false));

      // Category routing moves only enabled, marketable, non-excluded Auto-Market stock (RoutingMove.cs).
      if (am.Enabled && !am.ExcludeFromRouting && input.Item is { Marketable: true } facts)
      {
        var route = input.Routes.FirstOrDefault(r => r.CategoryId == facts.SearchCategory);
        if (route != null && !string.IsNullOrWhiteSpace(route.RetainerName))
          owners.Add(new Owner(OwnerKind.LmcCategoryRoute, $"LMC routes it to retainer {route.RetainerName.Trim()} (category routing)", true));
      }
    }

    // --- AutoRetainer entrust plans (act on BAG stacks, move them into the retainer) ---
    var ar = input.Ar;
    if (ar.Available)
    {
      var uiCat = input.Item?.UiCategory ?? 0u;
      var protectedByAr = ar.Im.Protect.Contains(id);
      var namedByAssigned = false;
      foreach (var (retainer, _) in ar.PlanByRetainer.OrderBy(kv => kv.Key, StringComparer.Ordinal))
      {
        var plan = ar.PlanFor(retainer);
        if (plan == null)
          continue;
        if (plan.ExcludeProtected && protectedByAr)
          continue;
        var manual = plan.ManualPlan ? " - manual plan, runs from AutoRetainer's button only" : string.Empty;
        if (plan.Items.Contains(id))
        {
          owners.Add(new Owner(OwnerKind.ArEntrustItem, $"AutoRetainer entrusts it to {retainer} (plan item list{manual})", true));
          namedByAssigned = true;
        }
        else if (uiCat != 0 && plan.UiCategories.Contains(uiCat))
        {
          owners.Add(new Owner(OwnerKind.ArEntrustCategory, $"AutoRetainer entrusts it to {retainer} (plan category{manual})", true));
          namedByAssigned = true;
        }
        else if (plan.Duplicates && input.RetainerHoldings != null
                 && input.RetainerHoldings.TryGetValue(retainer, out var held) && held.Contains(id)
                 && (plan.DuplicatesMultiStack || input.Item is not { Unique: true }))
        {
          owners.Add(new Owner(OwnerKind.ArEntrustDuplicates, $"AutoRetainer entrusts copies to {retainer} (duplicates - {retainer} held it when last seen{manual})", true));
          namedByAssigned = true;
        }
      }

      if (!namedByAssigned)
      {
        // A plan that names the item but is assigned to no retainer of this character never runs; say so,
        // without claiming it acts. (An ASSIGNED plan that skipped the item - ExcludeProtected - is not this.)
        var assigned = new HashSet<string>(ar.PlanByRetainer.Values, StringComparer.OrdinalIgnoreCase);
        var loose = ar.Plans.FirstOrDefault(p => !assigned.Contains(p.Guid)
          && (p.Items.Contains(id) || (uiCat != 0 && p.UiCategories.Contains(uiCat))));
        if (loose != null)
          owners.Add(new Owner(OwnerKind.ArEntrustUnassignedPlan, $"On AutoRetainer entrust plan '{loose.Name}' (not assigned to a retainer)", false));
      }

      // --- AutoRetainer inventory cleanup (acts on BAG stacks; armoury only when its own switches allow) ---
      var im = ar.Im;
      if (im.VendorHard.Contains(id))
      {
        var limit = im.VendorHardIgnoreStack.Contains(id) ? "any stack size" : $"stacks under {im.VendorHardStackLimit}";
        owners.Add(new Owner(OwnerKind.ArVendorHard, $"AutoRetainer sells it from the bags (vendor list, {limit}){(im.AutoVendorEnabled ? string.Empty : " - auto-vendor is OFF")}", im.AutoVendorEnabled));
      }
      if (im.VendorSoft.Contains(id))
        owners.Add(new Owner(OwnerKind.ArVendorSoft, $"AutoRetainer sells it when a Quick Exploration brings it (soft list){(im.AutoVendorEnabled ? string.Empty : " - auto-vendor is OFF")}", im.AutoVendorEnabled));
      if (im.Discard.Contains(id))
      {
        var limit = im.DiscardIgnoreStack.Contains(id) ? "any stack size" : $"stacks under {im.DiscardStackLimit}";
        owners.Add(new Owner(OwnerKind.ArDiscard, $"AutoRetainer DISCARDS it from the bags (discard list, {limit})", true));
      }
      if (im.Desynth.Contains(id))
        owners.Add(new Owner(OwnerKind.ArDesynth, $"AutoRetainer desynthesizes it{(im.DesynthEnabled ? string.Empty : " - desynthesis is OFF")}", im.DesynthEnabled));
      if (protectedByAr)
        owners.Add(new Owner(OwnerKind.ArProtect, "AutoRetainer protect list (never sold or discarded by AutoRetainer)", false));
    }

    if (input.GearsetIds.Count > 0)
      owners.Add(new Owner(OwnerKind.Gearset, $"In gearset {string.Join(", ", input.GearsetIds.OrderBy(g => g).Select(g => "#" + g))}", false));

    return owners;
  }

  /// <summary>True when some owner moves, sells or destroys the stack.</summary>
  public static bool AnyActs(IReadOnlyList<Owner> owners) => owners.Any(o => o.Acts);

  /// <summary>Tooltip lines: every owner, then the "nothing handles this" line when no owner acts.</summary>
  public static List<string> Lines(IReadOnlyList<Owner> owners)
  {
    var lines = owners.Select(o => o.Text).ToList();
    if (!AnyActs(owners))
      lines.Add(NothingText);
    return lines;
  }
}
