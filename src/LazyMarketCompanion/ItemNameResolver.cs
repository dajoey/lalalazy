using Dalamud.Game.Text.SeStringHandling;
using ECommons;
using ECommons.DalamudServices;
using LazyMarketCompanion.AutoMarket;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System;
using System.Linq;
using System.Text;

namespace LazyMarketCompanion;

/// <summary>
/// Game-side wrapper over <see cref="ItemNameMatch"/>: hands it the Item sheet and the cleaned-up text.
///
/// The matching RULES all live in <see cref="ItemNameMatch"/> so the harness can test them offline - notably
/// that text which cannot be pinned to exactly one item resolves to 0 rather than to a plausible-looking
/// neighbour. See that class for why: on 2026-09-05 a sell-list row whose label was clipped to
/// "Snow Cotton" was reported as item 44024 when the row actually held "Snow Cotton Ushanka of Scouting"
/// (41878), and that phantom identification vetoed an entire Auto-Market pass.
/// </summary>
internal static class ItemNameResolver
{
  private static readonly Lazy<ExcelSheet<Item>> Items = new(() => Svc.Data.GetExcelSheet<Item>());

  /// <summary>
  /// Every (id, name) pair, materialised once. The matcher makes a full pass per call, and the sheet is
  /// ~45k rows whose names allocate on every read, so this is built once rather than per lookup.
  /// </summary>
  private static readonly Lazy<(uint Id, string Name)[]> Catalogue = new(() =>
    Items.Value.Select(item => (item.RowId, item.Name.GetText())).ToArray());

  /// <summary>
  /// Resolve displayed item text to an id. Returns false - and 0 - when the text cannot be pinned to exactly
  /// one item; every caller treats that as "leave this alone", which is the safe direction.
  /// </summary>
  /// <param name="expectedItemId">
  /// What the game's container says is in this place, when the caller knows. Lets the resolver recognise a
  /// clipped rendering of that item instead of reporting the shorter item whose name it happens to contain.
  /// Pass 0 when there is nothing to compare against.
  /// </param>
  public static bool TryGetItemId(string itemName, string rawItemName, out uint itemId, uint expectedItemId = 0)
  {
    var normalizedItemName = NormalizeItemName(itemName);

    itemId = ItemNameMatch.Resolve(normalizedItemName, itemName, Catalogue.Value, expectedItemId);
    if (itemId == 0)
      itemId = ItemNameMatch.Resolve(normalizedItemName, rawItemName, Catalogue.Value, expectedItemId);

    return itemId != 0;
  }

  public static string GetItemName(uint itemId)
  {
    if (Items.Value.TryGetRow(itemId, out var item))
      return item.Name.GetText();

    return $"Unknown item ({itemId})";
  }

  public static bool CanBeHq(uint itemId)
  {
    return Items.Value.TryGetRow(itemId, out var item) && item.CanBeHq;
  }

  public static uint MaxStack(uint itemId)
  {
    return Items.Value.TryGetRow(itemId, out var item) ? Math.Max(item.StackSize, 1u) : 1u;
  }

  public static bool IsMarketable(uint itemId)
  {
    return Items.Value.TryGetRow(itemId, out var item) && item.ItemSearchCategory.RowId != 0 && !item.IsUntradable;
  }

  private static string NormalizeItemName(string itemName)
  {
    var normalizedItemName = itemName.Replace("\uE03C", string.Empty).Trim();

    try
    {
      var text = SeString.Parse(Encoding.UTF8.GetBytes(normalizedItemName)).GetText().Trim();
      if (!string.IsNullOrEmpty(text))
        normalizedItemName = text;
    }
    catch
    {
      // Plain text and malformed SeString both fall back to visible substring matching.
    }

    return normalizedItemName;
  }
}
