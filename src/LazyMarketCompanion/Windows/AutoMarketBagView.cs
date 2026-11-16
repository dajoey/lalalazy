using LazyMarketCompanion.AutoMarket;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace LazyMarketCompanion.Windows;

/// <summary>
/// The Auto-Market panel's live read (0.2.8.25), shared by the docked panel and the main window's
/// "Inventory panel" card so the two can never disagree: every group in the player's four bag
/// containers, graded against the Auto-Market list with the same marketability test the bag
/// markers use. Everything downstream (filters, counts, bulk plans) is the Dalamud-free
/// <see cref="AutoMarketPanelModel"/>.
/// </summary>
internal static class AutoMarketBagView
{
  private static readonly Dictionary<uint, bool> _marketableCache = [];

  /// <summary>Every grouped bag stack, sorted as the model sorts them; unmarketable groups included so they can be counted.</summary>
  public static List<AutoMarketPanelModel.Row> ReadRows(Inventory.InventoryService inventory, Configuration c)
  {
    var stacks = new List<AutoMarketPanelModel.StackInput>();
    foreach (var b in inventory.ReadBags())
      stacks.Add(new AutoMarketPanelModel.StackInput(b.Container, b.Slot, b.ItemId, b.Hq, b.Quantity));

    var entries = new List<AutoMarketPanelModel.EntryInput>(c.AutoMarketItems.Count);
    foreach (var e in c.AutoMarketItems)
      entries.Add(new AutoMarketPanelModel.EntryInput(e.ItemId, e.HQ, e.Enabled, e.ExcludeFromCategoryRouting, e.StackSize, e.KeepInBags));

    return AutoMarketPanelModel.Build(stacks, entries, ItemNameResolver.GetItemName, ItemNameResolver.SearchCategoryId, IsMarketable);
  }

  /// <summary>The same marketability test the bag markers use, cached per item id per session.</summary>
  public static bool IsMarketable(uint itemId)
  {
    if (_marketableCache.TryGetValue(itemId, out var cached))
      return cached;
    var result = false;
    var sheet = Plugin.DataManager?.GetExcelSheet<Item>();
    if (sheet != null && sheet.TryGetRow(itemId, out var item))
      result = !item.IsUntradable && item.ItemSearchCategory.RowId != 0;
    _marketableCache[itemId] = result;
    return result;
  }
}
