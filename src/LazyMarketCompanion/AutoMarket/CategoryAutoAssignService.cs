using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// 0.1.45.0: routing auto-fill (Helm t-joey-1789190796770, Joey 2026-09-12: "HOW DOES A WEAPON NOT
// HAVE A CATEGORY I'M NOT DOING THAT MANUALLY"). The 0.1.43.0 uncovered report named the Heavy
// Metal Culverin's category (Machinist's Arms) and offered hand-added rows; the answer to that ask
// is that hand-added rows are not acceptable, so the sweep fills the gap itself. Runs BEFORE the
// routing plan is built (MarketAutomation.BuildListingStepsNow), so stock the new rule just
// assigned is moved in the same pass, exactly as if the row had existed all along.
internal static class CategoryAutoAssignService
{
  /// <summary>
  /// One auto-fill attempt per call. Probes the mover for uncovered bags stock, derives the
  /// missing categories through the same Item-sheet lookup TryBuildRoutingLookups builds, asks
  /// CategoryRouter.AutoAssignMissing for least-loaded gap-fill among the retainers the sweep
  /// actually visits, appends the new rules to the live config (which this saves), and returns
  /// the additions for the caller's announce. An empty list means the switch is off, nothing was
  /// uncovered, or no safe retainer existed - all three are fail-open, nothing is forced.
  /// </summary>
  public static List<CategoryRetainerRule> AssignNow()
  {
    var config = Plugin.Configuration;
    if (!config.AutoAssignUnroutedCategories)
      return [];

    var candidates = EnabledSweepRetainers();
    if (candidates.Count == 0)
      return [];

    var probe = AutoMarketService.PlanRoutingMoves();
    if (probe.UnroutedBagsStacks.Count == 0)
      return [];

    var sheet = Svc.Data.GetExcelSheet<Item>();
    var categoryByKey = new Dictionary<string, ItemCategoryInfo>(probe.UnroutedBagsStacks.Count);
    if (sheet != null)
    {
      foreach (var (itemId, hq) in probe.UnroutedBagsStacks)
      {
        var key = $"{itemId}:{(hq ? "hq" : "nq")}";
        if (categoryByKey.ContainsKey(key) || !sheet.TryGetRow(itemId, out var row))
          continue;
        categoryByKey[key] = new ItemCategoryInfo(itemId, hq, row.ItemSearchCategory.RowId,
          row.ItemSearchCategory.RowId != 0 && !row.IsUntradable);
      }
    }

    var missing = CategoryRouter.UnroutedCategories(probe.UnroutedBagsStacks, categoryByKey);
    if (missing.Count == 0)
      return [];

    var added = CategoryRouter.AutoAssignMissing(missing, config.CategoryRetainerRules, candidates);
    if (added.Count > 0)
    {
      config.CategoryRetainerRules.AddRange(added);
      config.Save();
    }
    return added;
  }

  /// <summary>
  /// Retainers the sweep will actually visit: known names that are not disabled. A rule pointed at
  /// a retainer no session ever opens recreates the original invisible-stock failure - the gate
  /// blocks the category's stock on every visited retainer while no session ever deposits it -
  /// which is strictly worse than uncovered, so the candidate list stops here.
  /// </summary>
  private static List<string> EnabledSweepRetainers()
  {
    var config = Plugin.Configuration;
    if (config.EnabledRetainerNames.Contains(Configuration.ALL_DISABLED_SENTINEL))
      return [];
    if (config.EnabledRetainerNames.Count == 0)
      return [.. config.LastKnownRetainerNames];
    return config.LastKnownRetainerNames.Where(config.EnabledRetainerNames.Contains).ToList();
  }
}
