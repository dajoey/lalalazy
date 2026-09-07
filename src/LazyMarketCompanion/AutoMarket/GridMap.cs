using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (case 46).

/// <summary>
/// Maps each live bag-grid addon to the inventory container it is actually showing.
///
/// The bag UI exists in two mutually exclusive modes (ground truth: CriticalCommonLib
/// (InventoryTools) 1.15.0.12, `AtkInventoryExpansion.SetColors` / `InventoryGridOverlay.Draw`,
/// decompiled from the live install 2026-09-07 - a library in production for years):
///
/// - EXPANDED armoire-chest mode: the "InventoryExpansion" window shows four E-grids at once.
///   Here the mapping is a fixed NAME identity: InventoryGrid0E always shows Bag0 (Inventory1),
///   Grid1E -> Bag1 (Inventory2), Grid2E -> Bag2 (Inventory3), Grid3E -> Bag3 (Inventory4).
/// - NORMAL tabbed mode: the "Inventory" window shows ONE panel at a time (any of
///   "InventoryGrid", "InventoryGrid0", "InventoryGrid1") and which container that panel draws
///   is decided by the selected TAB, not by the panel's name. Tab 0 -> Bag0, tab 1 -> Bag1,
///   tab 2 -> Bag2, tab 3 -> Bag3. The tab is read from AddonInventory.TabIndex (officially
///   generated ClientStructs field on the parent "Inventory" window); CCL reads the same value
///   as a byte at +0x350 in its own struct.
///
/// The 0.1.17.0 markers bug paired grid names with containers by fixed index in BOTH modes
/// (0=>Grid0/Inventory1 .. 3=>Grid1E/Inventory4), which is wrong in expanded mode (Grid0E shows
/// Inventory1, not Inventory3) and meaningless in normal mode (the panel follows the tab). This
/// map replaces that guess: no live addon, or a tab outside 0..3, draws NOTHING - honest
/// absence, never a guess.
/// </summary>
public static class GridMap
{
  /// <summary>Expanded-mode E-grids in bag order; index == bag index == container minus Inventory1.</summary>
  private static readonly string[] ExpandedGrids = ["InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E"];

  /// <summary>Normal-mode panels; the container comes from the parent window's tab, not the name.</summary>
  private static readonly string[] NormalGrids = ["InventoryGrid", "InventoryGrid0", "InventoryGrid1"];

  /// <summary>Parent window names, one per mode.</summary>
  private const string ExpandedParent = "InventoryExpansion";
  private const string NormalParent = "Inventory";

  /// <summary>One resolved grid-to-container pairing: the grid addon name and the container it shows.</summary>
  public sealed record GridBinding(string GridName, int BagIndex);

  /// <summary>True when the given grid addon name is one of the four expanded-mode E-grids.</summary>
  public static bool IsExpandedGrid(string name) => ExpandedGrids.Contains(name);

  /// <summary>
  /// Resolve every live grid addon to the container it is showing. Expanded mode wins outright
  /// (the E-grids only exist in it); otherwise normal mode binds every live panel to the tab's
  /// bag. An unknown name, an unlive addon, or an out-of-range tab contributes NOTHING - the
  /// caller draws no marker for it rather than guessing a container.
  /// </summary>
  /// <param name="liveAddonNames">Names of grid addons the caller found live and ready this frame.</param>
  /// <param name="parentTabIndex">The parent "Inventory" window's TabIndex (0-based bag index); unused in expanded mode.</param>
  public static IReadOnlyList<GridBinding> Resolve(IReadOnlyList<string> liveAddonNames, int? parentTabIndex)
  {
    var result = new List<GridBinding>();

    var expandedLive = new List<string>();
    foreach (var name in liveAddonNames)
      if (ExpandedGrids.Contains(name))
        expandedLive.Add(name);
    if (expandedLive.Count > 0)
    {
      // Expanded mode: fixed name-identity mapping, each E-grid drawn independently of the others.
      for (var bag = 0; bag < ExpandedGrids.Length; bag++)
        if (expandedLive.Contains(ExpandedGrids[bag]))
          result.Add(new GridBinding(ExpandedGrids[bag], bag));
      return result;
    }

    if (parentTabIndex is int tab && tab >= 0 && tab <= 3)
    {
      foreach (var name in liveAddonNames)
        if (NormalGrids.Contains(name))
          result.Add(new GridBinding(name, tab));
    }

    // Unknown names, no live E-grid, or a tab outside 0..3: nothing. Honest absence, never a guess.
    return result;
  }
}
