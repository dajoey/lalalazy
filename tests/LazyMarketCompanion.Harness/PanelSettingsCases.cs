using LazyMarketCompanion;
using LazyMarketCompanion.AutoMarket;

// Offline cases 176-180: the docked Auto-Market panel lists ONLY the marketable stacks (0.2.8.25);
// everything else it used to draw lives in the main window's "Inventory panel" card.
//
// Written first, run against the 0.2.8.24 tree for the red run: the two model entry points
// (PanelRows, PlanSettingsBulk) are looked up by REFLECTION, so on the old tree a missing member
// FAILS its checks instead of failing the whole project to compile. 176 and 177 compare the new
// entry points against the OLD panel path (Visible/PlanBulk with the panel's default search and
// "show unmarketable" off), which stays in the model, so they are parity proofs and not
// restatements of the implementation.
internal static class PanelSettingsCases
{
  private const uint Ore1 = 5111, Ore2 = 5112, Herb = 5113, Plank = 5114, Junk = 9001, Quest = 9002;
  private const uint CatMetal = 47, CatPlant = 55, CatStale = 99;

  private static readonly AutoMarketPanelModel.StatusFilter[] Statuses =
  [
    AutoMarketPanelModel.StatusFilter.All,
    AutoMarketPanelModel.StatusFilter.OnList,
    AutoMarketPanelModel.StatusFilter.NotOnList,
    AutoMarketPanelModel.StatusFilter.Excluded,
  ];

  private static List<AutoMarketPanelModel.Row> Rows()
  {
    // Bags: two ore groups (one on the list and quick-excluded, one not listed), an HQ ore stack
    // (not listed), an on-list-but-disabled herb, an unlisted plank, and two unmarketable items.
    var stacks = new List<AutoMarketPanelModel.StackInput>
    {
      new(2000, 0, Ore1, false, 40), new(2000, 1, Ore1, false, 12), new(2001, 0, Ore1, true, 7),
      new(2000, 2, Ore2, false, 99), new(2000, 3, Herb, false, 30), new(2001, 1, Plank, false, 15),
      new(2000, 4, Junk, false, 3), new(2001, 2, Quest, false, 1),
    };
    var entries = new List<AutoMarketPanelModel.EntryInput>
    {
      new(Ore2, false, true, true, 99, 10),
      new(Herb, false, false, false, 0, 0),
    };
    var names = new Dictionary<uint, string> { [Ore1] = "Iron Ore", [Ore2] = "Silver Ore", [Herb] = "Mandrake", [Plank] = "Ash Lumber", [Junk] = "Broken Tool", [Quest] = "Letter" };
    var cats = new Dictionary<uint, uint> { [Ore1] = CatMetal, [Ore2] = CatMetal, [Herb] = CatPlant, [Plank] = CatPlant, [Junk] = 0, [Quest] = 0 };
    return AutoMarketPanelModel.Build(stacks, entries, id => names[id], id => cats[id], id => id is not (Junk or Quest));
  }

  private static string Ids(IEnumerable<AutoMarketPanelModel.Row> rows) => string.Join(",", rows.Select(r => $"{r.ItemId}{(r.Hq ? "h" : "n")}"));

  private static string Pairs(IEnumerable<(uint ItemId, bool Hq)> pairs) => string.Join(",", pairs.Select(p => $"{p.ItemId}{(p.Hq ? "h" : "n")}"));

  /// <summary>The panel's pre-0.2.8.25 list: the saved filters resolved as DrawFrame did, search empty, "show unmarketable" off.</summary>
  private static List<AutoMarketPanelModel.Row> OldPanelList(List<AutoMarketPanelModel.Row> all, AutoMarketPanelModel.StatusFilter status, uint category)
  {
    var s = AutoMarketPanelModel.ResolveVisibleStatus(status);
    var c = AutoMarketPanelModel.ResolveVisibleCategory(category, AutoMarketPanelModel.PresentCategories(all));
    return AutoMarketPanelModel.Visible(all, new AutoMarketPanelModel.Filters(s, string.Empty, c, false));
  }

  public static void Run(StallCases.CheckFn check)
  {
    var all = Rows();
    var mRows = typeof(AutoMarketPanelModel).GetMethod("PanelRows", [typeof(IReadOnlyList<AutoMarketPanelModel.Row>), typeof(AutoMarketPanelModel.StatusFilter), typeof(uint)]);
    var mBulk = typeof(AutoMarketPanelModel).GetMethod("PlanSettingsBulk", [typeof(IReadOnlyList<AutoMarketPanelModel.Row>), typeof(AutoMarketPanelModel.StatusFilter), typeof(uint)]);

    // Control: the fixture really holds unmarketable rows, so "none of them reach the panel" is not vacuous.
    check("176 control: the fixture has 2 unmarketable and 5 marketable groups",
      all.Count(r => r.Kind == AutoMarketPanelModel.RowKind.NotMarketable) == 2
        && all.Count(r => r.Kind != AutoMarketPanelModel.RowKind.NotMarketable) == 5,
      $"rows={Ids(all)}");

    // ---- 176. PanelRows: marketable stacks only, filtered by the SAVED status and category ----
    check("176 the model exposes PanelRows(all, savedStatus, savedCategory)", mRows != null, "PanelRows not found on AutoMarketPanelModel");
    List<AutoMarketPanelModel.Row> PanelRows(AutoMarketPanelModel.StatusFilter s, uint c) =>
      mRows?.Invoke(null, [all, s, c]) as List<AutoMarketPanelModel.Row> ?? [];

    if (mRows != null)
    {
      var everything = PanelRows(AutoMarketPanelModel.StatusFilter.All, 0);
      check("176 PanelRows lists only marketable stacks - no unmarketable row, whatever the saved filters",
        everything.Count == 5 && Statuses.All(s => PanelRows(s, 0).All(r => r.Kind != AutoMarketPanelModel.RowKind.NotMarketable)),
        Ids(everything));
      check("176 PanelRows keeps the on-list rows (list order) before the not-listed ones",
        Ids(everything) == "5112n,5113n,5114n,5111n,5111h",
        Ids(everything));
      check("176 a saved 'Not on list' status shows only not-listed rows",
        Ids(PanelRows(AutoMarketPanelModel.StatusFilter.NotOnList, 0)) == "5114n,5111n,5111h",
        Ids(PanelRows(AutoMarketPanelModel.StatusFilter.NotOnList, 0)));
      check("176 a saved 'Excluded' status shows only the quick-excluded on-list row",
        Ids(PanelRows(AutoMarketPanelModel.StatusFilter.Excluded, 0)) == "5112n", Ids(PanelRows(AutoMarketPanelModel.StatusFilter.Excluded, 0)));
      check("176 a saved category shows only that category",
        PanelRows(AutoMarketPanelModel.StatusFilter.All, CatMetal).All(r => r.CategoryId == CatMetal) && PanelRows(AutoMarketPanelModel.StatusFilter.All, CatMetal).Count == 3,
        Ids(PanelRows(AutoMarketPanelModel.StatusFilter.All, CatMetal)));
      check("176 a saved category the bags do not hold shows every category - never an invisible filter",
        Ids(PanelRows(AutoMarketPanelModel.StatusFilter.All, CatStale)) == Ids(everything), Ids(PanelRows(AutoMarketPanelModel.StatusFilter.All, CatStale)));
      check("176 a saved status outside the enum (hand-edited config) shows as All",
        Ids(PanelRows((AutoMarketPanelModel.StatusFilter)99, 0)) == Ids(everything), Ids(PanelRows((AutoMarketPanelModel.StatusFilter)99, 0)));

      // Parity with the pre-0.2.8.25 panel for every saved-filter combination.
      var combos = 0;
      var mismatched = new List<string>();
      foreach (var s in Statuses.Append((AutoMarketPanelModel.StatusFilter)99))
        foreach (var c in new uint[] { 0, CatMetal, CatPlant, CatStale })
        {
          combos++;
          if (Ids(PanelRows(s, c)) != Ids(OldPanelList(all, s, c)))
            mismatched.Add($"{s}/{c}");
        }
      check($"176 PanelRows equals the old panel's list for all {combos} saved-filter combinations",
        mismatched.Count == 0, string.Join(" ", mismatched));
      check("176 PanelRows on empty bags is empty (no throw)",
        (mRows.Invoke(null, [new List<AutoMarketPanelModel.Row>(), AutoMarketPanelModel.StatusFilter.All, 0u]) as List<AutoMarketPanelModel.Row>)?.Count == 0, "");
    }

    // ---- 177. PlanSettingsBulk: the settings page plans exactly what the old panel's bulk buttons planned ----
    check("177 the model exposes PlanSettingsBulk(all, savedStatus, savedCategory)", mBulk != null, "PlanSettingsBulk not found on AutoMarketPanelModel");
    if (mBulk != null)
    {
      AutoMarketPanelModel.BulkPlan Bulk(AutoMarketPanelModel.StatusFilter s, uint c) =>
        (AutoMarketPanelModel.BulkPlan)mBulk.Invoke(null, [all, s, c])!;

      var mismatched = new List<string>();
      var combos = 0;
      foreach (var s in Statuses.Append((AutoMarketPanelModel.StatusFilter)99))
        foreach (var c in new uint[] { 0, CatMetal, CatPlant, CatStale })
        {
          combos++;
          var old = AutoMarketPanelModel.PlanBulk(OldPanelList(all, s, c));
          var now = Bulk(s, c);
          if (Pairs(old.Adds) != Pairs(now.Adds) || Pairs(old.Removes) != Pairs(now.Removes))
            mismatched.Add($"{s}/{c}");
        }
      check($"177 the settings page's bulk plan equals the old panel's for all {combos} saved-filter combinations",
        mismatched.Count == 0, string.Join(" ", mismatched));

      var plain = Bulk(AutoMarketPanelModel.StatusFilter.All, 0);
      check("177 add-all with no filter covers exactly the not-listed marketable stacks (iron ore HQ + NQ, ash lumber)",
        plain.Adds.Count == 3 && plain.Adds.All(a => a.ItemId is Ore1 or Plank), Pairs(plain.Adds));
      check("177 remove-all with no filter covers exactly the on-list stacks (silver ore, mandrake)",
        plain.Removes.Count == 2 && plain.Removes.All(r => r.ItemId is Ore2 or Herb), Pairs(plain.Removes));
      check("177 an unmarketable item is never planned for adding or removing",
        !plain.Adds.Concat(plain.Removes).Any(p => p.ItemId is Junk or Quest), Pairs(plain.Adds.Concat(plain.Removes)));
      var metal = Bulk(AutoMarketPanelModel.StatusFilter.All, CatMetal);
      check("177 the saved category narrows the bulk plan (metal only: adds iron ore x2, removes silver ore)",
        metal.Adds.Count == 2 && metal.Adds.All(a => a.ItemId == Ore1) && metal.Removes.Count == 1 && metal.Removes[0].ItemId == Ore2,
        $"adds={Pairs(metal.Adds)} removes={Pairs(metal.Removes)}");
    }

    // ---- 178. Source-scan wiring pins (case-46 control form: a missing source or marker FAILS) ----
    var roots = new[]
    {
      Path.Combine("..", "..", "..", "..", "..", "src", "LazyMarketCompanion"),
      Path.Combine("src", "LazyMarketCompanion"),
    };
    var root = roots.Where(Directory.Exists).FirstOrDefault() ?? "";
    var winPath = Path.Combine(root, "Windows", "AutoMarketPanelWindow.cs");
    var cfgWinPath = Path.Combine(root, "Windows", "ConfigWindow.cs");
    check("178 scan: the panel and main window sources are found",
      root.Length > 0 && File.Exists(winPath) && File.Exists(cfgWinPath), $"root={root}");
    if (!(root.Length > 0 && File.Exists(winPath) && File.Exists(cfgWinPath)))
      return;

    var win = File.ReadAllText(winPath);
    var cfgWin = File.ReadAllText(cfgWinPath);

    var frame = SliceMethod(win, "private void DrawFrame");
    check("178 scan: the panel's DrawFrame is found (control)", frame.Length > 0 && frame.Contains("HeaderBand("), "");
    check("178 scan: DrawFrame draws the title/close line and the stack table",
      frame.Contains("MarketChrome.HeaderBand(\"Auto-Market\"") && frame.Contains("closeClicked") && frame.Contains("DrawTable("), "");
    check("178 scan: DrawFrame takes its rows from PanelRows (marketable only, saved filters)",
      frame.Contains("AutoMarketPanelModel.PanelRows("), "");
    foreach (var gone in new[] { "PageHeader(", "StatCards(", "DrawHeader(", "DrawFilters(", "DrawBulkButtons(", "DrawBulkModals(", "DrawKnobs(", "DrawFooter(" })
      check($"178 scan: the panel no longer draws {gone.TrimEnd('(')}", !frame.Contains(gone), "");
    check("178 scan: the panel carries no confirmation modal, no search box, no unmarketable toggle and no Stop",
      !win.Contains("BeginPopupModal") && !win.Contains("##lmcPanelSearch") && !win.Contains("Show unmarketable") && !win.Contains("_cancelAutomation?.Invoke"), "");
    check("178 scan: the panel still draws the per-row controls (Add/Remove, Stack, Keep, Skip category routing, enabled tick)",
      win.Contains("\"Add\"") && win.Contains("\"Remove\"") && win.Contains("##stack") && win.Contains("##keep") && win.Contains("Skip category routing") && win.Contains("##on"), "");
    check("178 scan: dock placement logic is untouched (Watch still places via PanelDock)",
      win.Contains("PanelDock.Place(") && win.Contains("ToggleViaCommand"), "");

    // ---- 179. The main window's Inventory panel card ----
    check("179 scan: the main window has the 'Inventory panel' card", cfgWin.Contains("CardHeader(\"Inventory panel\""), "");
    var card = SliceMethod(cfgWin, "private void DrawInventoryPanelCard");
    check("179 scan: the card method is found (control)", card.Length > 0, "");
    check("179 scan: the card writes the dock toggle to the same saved key",
      card.Contains("\"Dock to inventory\"") && card.Contains("c.AutoMarketPanelDock = dock"), "");
    check("179 scan: the card writes the stay-open toggle to the same saved key",
      card.Contains("\"Stay open when the inventory closes\"") && card.Contains("c.AutoMarketPanelPinned = pinned"), "");
    check("179 scan: the card writes the status filter to the same saved key",
      card.Contains("c.AutoMarketPanelStatus ="), "");
    check("179 scan: the card writes the category filter to the same saved key (including every-category = 0)",
      card.Contains("c.AutoMarketPanelCategory = 0") && card.Contains("c.AutoMarketPanelCategory = catId"), "");
    check("179 scan: the card shows the On list / Not listed / Excluded counts",
      card.Contains("\"On list\"") && card.Contains("\"Not listed\"") && card.Contains("\"Excluded\""), "");
    check("179 scan: the card has both bulk actions, planned by PlanSettingsBulk",
      card.Contains("PlanSettingsBulk(") && card.Contains("Add all") && card.Contains("Remove all"), "");
    check("179 scan: both bulk confirmations are openable (OpenPopup id == BeginPopupModal id) and write through the same config calls",
      cfgWin.Contains("OpenPopup(\"Add all marketable stacks?\")") && cfgWin.Contains("BeginPopupModal(\"Add all marketable stacks?\"")
        && cfgWin.Contains("OpenPopup(\"Remove all marketable stacks?\")") && cfgWin.Contains("BeginPopupModal(\"Remove all marketable stacks?\"")
        && cfgWin.Contains("c.GetOrAddAutoMarketItem(id, hq)") && cfgWin.Contains("c.AutoMarketItems.Remove(entry)"), "");

    // ---- 180. Every control the panel's 'Auto-Market settings' card had is in the main window; config keys unchanged ----
    var knobs = new[]
    {
      "AutoMarketEnabled", "AutoMarketMarkersEnabled", "AutoMarketDuringAutoRetainer", "AutoMarketInPinchAllSweep", "AutoMarketSource",
      "AutoMarketPreferRetainerStockFirst", "AutoMarketListPartialStacks", "AutoMarketPriceMode", "AutoMarketReserveSlots",
      "AutoMarketPinchAllAfter", "ShowAutoMarketMessages", "AutoMarketPinchFallback", "AutoMarketSortMode",
      "AutoMarketValueGateEnabled", "AutoMarketValueGateThresholdGil", "AutoMarketGateFreshnessHours", "AutoAssignUnroutedCategories",
    };
    var missing = knobs.Where(k => !cfgWin.Contains($"c.{k} = ")).ToList();
    check($"180 scan: all {knobs.Length} controls of the old panel settings card are written by the main window",
      missing.Count == 0, string.Join(",", missing));
    var absentProps = knobs.Concat(["AutoMarketPanelDock", "AutoMarketPanelPinned", "AutoMarketPanelStatus", "AutoMarketPanelCategory", "AutoMarketPanelX", "AutoMarketPanelY"])
      .Where(k => typeof(Configuration).GetProperty(k) == null).ToList();
    check("180 every saved key the panel used is still a Configuration property (existing settings load as-is)",
      absentProps.Count == 0, string.Join(",", absentProps));
  }

  private static string SliceMethod(string src, string header)
  {
    var start = src.IndexOf(header, StringComparison.Ordinal);
    if (start < 0)
      return string.Empty;
    var next = src.IndexOf("\n  private ", start + header.Length, StringComparison.Ordinal);
    var end = next < 0 ? src.Length : next;
    return src[start..end];
  }
}
