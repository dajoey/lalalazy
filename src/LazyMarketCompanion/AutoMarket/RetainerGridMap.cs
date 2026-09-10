using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// Maps each live bag-grid addon to the RETAINER inventory page it is showing, for the duration a
/// retainer's inventory window (InventoryRetainer / InventoryRetainerLarge) is open. Parallel to
/// GridMap.cs, which does the same job for the player's own four bags.
///
/// GROUND TRUTH (2026-09-10, MetadataLoadContext probe against omasky's live
/// FFXIVClientStructs.dll - see references/offline-clientstructs-enumeration.md for the probe
/// pattern): <c>AddonInventoryRetainer</c> (<c>[Addon("InventoryRetainer")]</c>) and
/// <c>AddonInventoryRetainerLarge</c> (<c>[Addon("InventoryRetainerLarge")]</c>) both expose only
/// <c>AtkAddonControl AddonControl</c>, <c>int TabIndex</c> and a <c>SetTab(int)</c> member function
/// beyond the standard AtkUnitBase fields - the same single-panel TABBED shape the player's own
/// "Inventory" addon has in GridMap's NORMAL mode, NOT the four-grid-at-once EXPANDED shape. There
/// is therefore no known "retainer E-grid" mode: a retainer's inventory is assumed to always be one
/// panel whose TabIndex (0-6) selects which of the retainer's up to seven storage pages
/// (InventoryType.RetainerPage1..7) it is showing.
///
/// THIS ASSUMPTION IS UNCONFIRMED IN GAME (no game client from the build host; DAJOEYROG builds,
/// omasky runs the live client) - specifically, whether "InventoryRetainerLarge" ever shows more
/// than one page's grid at once for a retainer with expanded capacity. The design fails closed on
/// exactly that uncertainty: if an E-grid addon is ever live while a retainer window is open (not
/// expected per the reflection above), this map draws NOTHING for it rather than guessing a mapping
/// nobody has observed. A wrong dot on someone else's stock is worse than no dot.
/// </summary>
public static class RetainerGridMap
{
  /// <summary>Grid panel names that bind 1:1 to the retainer window's own TabIndex.</summary>
  private static readonly string[] NormalGrids = ["InventoryGrid", "InventoryGrid0", "InventoryGrid1"];

  /// <summary>A retainer has at most seven storage pages (InventoryType.RetainerPage1..7).</summary>
  public const int PageCount = 7;

  /// <summary>One resolved grid-to-page pairing: the grid addon name and the retainer page index (0-based) it shows.</summary>
  public sealed record GridBinding(string GridName, int PageIndex);

  /// <summary>
  /// Resolve every live grid addon to the retainer page it is showing this frame. An out-of-range
  /// tab, a missing tab (retainer addon not resolved), or an unknown/E-grid name contributes
  /// NOTHING - honest absence, never a guess, exactly as GridMap.Resolve does for the player's bags.
  /// </summary>
  /// <param name="liveAddonNames">Names of grid addons the caller found live and ready this frame.</param>
  /// <param name="tabIndex">The retainer inventory addon's TabIndex (0-based retainer page); null when neither retainer addon resolved.</param>
  public static IReadOnlyList<GridBinding> Resolve(IReadOnlyList<string> liveAddonNames, int? tabIndex)
  {
    var result = new List<GridBinding>();
    if (tabIndex is not int tab || tab < 0 || tab >= PageCount)
      return result;

    foreach (var name in liveAddonNames)
      if (NormalGrids.Contains(name))
        result.Add(new GridBinding(name, tab));

    return result;
  }
}
