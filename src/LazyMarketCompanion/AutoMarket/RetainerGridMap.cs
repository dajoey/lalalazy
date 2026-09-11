using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// Maps each live retainer-storage grid addon to the retainer inventory page it is showing, for the
/// duration a retainer's inventory window (InventoryRetainer / InventoryRetainerLarge) is open.
/// Parallel to GridMap.cs, which does the same job for the player's own four bags.
///
/// GROUND TRUTH CORRECTED (2026-09-10, kanban t_eeb284dd, Helm t-joey-1789056199442 follow-up): the
/// 0.1.34.0 "GROUND TRUTH" below this comment was WRONG. It assumed the retainer storage view reuses
/// the player's own grid addon NAMES ("InventoryGrid"/"InventoryGrid0"/"InventoryGrid1"). It does not.
///
/// Independent, multiply-corroborated evidence the retainer grid is a DISTINCT addon family:
/// - CriticalCommonLib (a mature, actively-maintained inventory library; decompiled from
///   InventoryTools 1.15.0.12, which is INSTALLED AND ACTIVE on omasky - Joey's own live client)
///   declares <c>WindowName.RetainerGrid</c> ("For normal retainer inventory") as a name distinct
///   from <c>WindowName.InventoryGrid</c> ("For normal inventory"), and
///   <c>WindowName.RetainerGrid0..RetainerGrid4</c> ("For expanded retainer inventory") distinct from
///   <c>WindowName.InventoryGrid0E..InventoryGrid3E</c> ("For open all inventory").
/// - <c>CriticalCommonLib.Services.GameUiManager</c> resolves every live addon's REAL name string via
///   <c>AtkUnitBase.NameString</c> and does <c>Enum.TryParse&lt;WindowName&gt;(name)</c> against it every
///   frame - so "RetainerGrid" is not a library-invented label, it is the literal addon name the game
///   assigns, proven by walking the live AtkUnitList (same technique documented in
///   references/offline-clientstructs-enumeration.md).
/// - <c>AtkInventoryRetainer.SetColor</c>/<c>SetColors</c> (normal retainer) resolve the grid via
///   <c>GetAtkUnitBase(WindowName.RetainerGrid)</c> and address DragDrop node ids with the SAME formula
///   LMC's own code uses for the player's grid (<c>position.X + position.Y*5 + 3</c>) - i.e. the
///   RetainerGrid addon is structurally the SAME AddonInventoryGrid-shaped component (35 DragDrop
///   slots) the player's bag uses, just under a different, retainer-specific addon name.
/// - <c>AtkRetainerLarge</c> (expanded retainer, <c>WindowName.InventoryRetainerLarge</c>) resolves
///   FIVE simultaneously-live grids by FIXED name identity (RetainerGrid0..RetainerGrid4), exactly
///   mirroring GridMap.cs's ExpandedGrids design for the player's own "open all" mode - never via the
///   parent's TabIndex.
///
/// This also explains Joey's exact symptom report better than the superseded "two same-named live
/// instances, ambiguous ownership" theory: there was never a collision. The retainer branch was
/// scanning for "InventoryGrid"/"InventoryGrid0"/"InventoryGrid1" - names that, while a retainer
/// window is open, belong ONLY to the PLAYER's own bag (which the game keeps open alongside a
/// retainer's storage so items can be dragged between them). The retainer branch found the PLAYER's
/// grid, bound it to a RETAINER page via RetainerGridMap, and painted the retainer's stock - sorted by
/// the RETAINER's own item order - onto the PLAYER's bag cells. Whether a given cell's dot looked
/// "right" was pure coincidence of slot-position overlap between two unrelated containers - exactly
/// "random color" and "some items but not others". And because this consumed the ONLY grid addon
/// instance that exists (the player's), the early-return in AutoMarketMarkers.Draw() then skipped the
/// real player-bag computation entirely for that frame - exactly "my own inventory didn't show".
///
/// PageCount stays 7 (RetainerPage1..7): live ffxivdb telemetry already proves a retainer reaching
/// RetainerPage6 ("RetainerPage6#5 -> market#12", 2026-09-10 20:03:48), so retainers can exceed
/// CriticalCommonLib's declared RetainerGrid0..RetainerGrid4 (5 grids). Rather than assume a page
/// count for the expanded name family, the code below PROBES for "RetainerGrid0".."RetainerGrid6"
/// (matching PageCount) and binds whichever are actually live this frame - unconfirmed pages 5/6 fail
/// closed (no name found -> no binding -> no dots) exactly as an unresolvable page always has here.
///
/// SUPERSEDED (0.1.34.0 GROUND TRUTH, kept for history - do not rely on this any more):
/// "MetadataLoadContext probe against omasky's live FFXIVClientStructs.dll ... AddonInventoryRetainer
/// (...) and AddonInventoryRetainerLarge (...) both expose only AtkAddonControl AddonControl, int
/// TabIndex and a SetTab(int) member function beyond the standard AtkUnitBase fields - the same
/// single-panel TABBED shape the player's own 'Inventory' addon has in GridMap's NORMAL mode ... There
/// is therefore no known 'retainer E-grid' mode ... a retainer's inventory is assumed to always be one
/// panel whose TabIndex (0-6) selects which ... page it is showing." This probe only inspected the
/// PARENT shell struct's own fields - it never established what name the CHILD grid addon receives,
/// which is the actual point of failure. A thin parent shell (matching AddonInventory's own shape for
/// the player) does not imply its children share the player's addon names.
/// </summary>
public static class RetainerGridMap
{
  /// <summary>Grid panel name for NORMAL (tabbed, single-panel) retainer inventory - distinct from the player's "InventoryGrid".</summary>
  private const string NormalGrid = "RetainerGrid";

  /// <summary>A retainer has at most seven storage pages (InventoryType.RetainerPage1..7); RetainerGrid5/6 are probed, not assumed.</summary>
  public const int PageCount = 7;

  /// <summary>Expanded-mode grids in page order; index == page index. Confirmed live for 0..4; 5/6 probed defensively (see class remarks).</summary>
  private static readonly string[] ExpandedGrids =
    Enumerable.Range(0, PageCount).Select(i => $"RetainerGrid{i}").ToArray();

  /// <summary>True when the given grid addon name is one of the expanded-mode RetainerGridN grids.</summary>
  public static bool IsExpandedGrid(string name) => ExpandedGrids.Contains(name);

  /// <summary>One resolved grid-to-page pairing: the grid addon name and the retainer page index (0-based) it shows.</summary>
  public sealed record GridBinding(string GridName, int PageIndex);

  /// <summary>
  /// Resolve every live retainer-grid addon to the page it is showing this frame. Expanded mode
  /// (RetainerGrid0..RetainerGridN live) wins outright and binds each by fixed name identity,
  /// mirroring GridMap.Resolve's expanded-vs-normal split for the player's own bags. Otherwise the
  /// single "RetainerGrid" panel (normal, tabbed retainer) binds to the retainer addon's own TabIndex.
  /// An out-of-range tab, a missing tab (retainer addon not resolved), or an unknown name contributes
  /// NOTHING - honest absence, never a guess.
  /// </summary>
  /// <param name="liveAddonNames">Names of grid addons the caller found live and ready this frame.</param>
  /// <param name="tabIndex">The retainer inventory addon's TabIndex (0-based retainer page); used only in normal mode.</param>
  public static IReadOnlyList<GridBinding> Resolve(IReadOnlyList<string> liveAddonNames, int? tabIndex)
  {
    var result = new List<GridBinding>();

    var expandedLive = new List<string>();
    foreach (var name in liveAddonNames)
      if (ExpandedGrids.Contains(name))
        expandedLive.Add(name);
    if (expandedLive.Count > 0)
    {
      for (var page = 0; page < ExpandedGrids.Length; page++)
        if (expandedLive.Contains(ExpandedGrids[page]))
          result.Add(new GridBinding(ExpandedGrids[page], page));
      return result;
    }

    if (tabIndex is not int tab || tab < 0 || tab >= PageCount)
      return result;

    foreach (var name in liveAddonNames)
      if (name == NormalGrid)
        result.Add(new GridBinding(name, tab));

    return result;
  }
}
