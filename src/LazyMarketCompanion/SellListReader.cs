using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LazyMarketCompanion.AutoMarket;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion;

/// <summary>
/// Reads the open RetainerSellList addon: for each UI row, which RetainerMarket slot it is showing, what item
/// name it displays and at what asking price.
///
/// This exists because 0.1.3.0 did the opposite - it computed a row from the market container on the
/// assumption that "the list shows occupied slots in ascending container order" and then verified the guess.
/// The guess was wrong on 5 of 5 measured runs on Joey's client (2026-09-05) and the failure path re-priced
/// every listing. Reading is not guessing, so this class replaces the assumption entirely.
///
/// TWO READINGS, deliberately, because they fail in different situations:
///
/// * <see cref="ReadSlots"/> - the addon's AtkValues. The RetainerSellList setup values carry one 13-value
///   block per listing starting at index 15, whose first field is the market slot shown at that row
///   (AllaganMarket 1.4.0.2 `AtkOrderService.GetCurrentOrder` reads exactly `AtkValues[15 + 13n].Int`, and
///   uses it for both its initial load and its post-listing refresh). This covers ALL rows including ones
///   scrolled out of view, and does not care about duplicate items. This is the reading that works; it
///   identified the right row even on the 20:37:48 run that was then thrown away for other reasons.
/// * <see cref="ReadRenderedRows"/> - the visible list-item renderers. The list virtualises, so only the
///   ~10 on-screen rows have one; that is why this cannot be the primary reading. Text node id 3 is the item
///   name and node id 7 the price, per the RetainerSellList.uld ListItem component (id 1011) read out of the
///   installed game files, and AllaganMarket reads name from that same node 3.
///
/// The name reading is the WEAKEST of the two and is treated that way. The sell list's name column is narrow
/// enough to clip a long item name, and a clipped name still contains shorter real item names: on 2026-09-05
/// a row holding "Snow Cotton Ushanka of Scouting" (41878) rendered as text that resolved to "Snow Cotton"
/// (44024). So every name lookup is given the item the CONTAINER says is in that row's slot, and
/// <see cref="ItemNameMatch"/> answers 0 - unknown - rather than naming a different item.
///
/// Both readings are combined into one <see cref="SellListRow"/> list; unavailable fields stay explicitly
/// unknown (slot <see cref="MarketRowMap.NoRow"/>, item 0, price 0) rather than being filled in with a guess.
/// </summary>
internal static unsafe class SellListReader
{
  /// <summary>Node id of the AtkComponentList holding the rows (RetainerSellList.uld widget node 11).</summary>
  private const uint ListNodeId = 11;

  /// <summary>Node id of the item-name text node inside a list-item renderer (uld component 1011, node 3).</summary>
  private const uint RowItemNameNodeId = 3;

  /// <summary>Node id of the unit-price text node inside a list-item renderer (uld component 1011, node 7).</summary>
  private const uint RowPriceNodeId = 7;

  /// <summary>AtkValue index of the first listing block.</summary>
  private const int FirstListingValue = 15;

  /// <summary>AtkValue stride between listing blocks.</summary>
  private const int ListingValueStride = 13;

  /// <summary>
  /// Read every row of the open sell list, or an empty list when it is not available.
  /// </summary>
  /// <param name="market">
  /// The market container snapshot, when the caller has one. Used ONLY to tell the name resolver what the
  /// game says is in each row's slot, so a clipped label is recognised as unreadable instead of being
  /// reported as the shorter item it happens to contain. Passing null simply means names get resolved with
  /// no ground truth to check against.
  /// </param>
  public static List<SellListRow> Read(IReadOnlyList<MarketSlot>? market = null)
  {
    var result = new List<SellListRow>();
    if (!(GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && GenericHelpers.IsAddonReady(addon)))
      return result;

    var rowCount = RowCount(addon);
    if (rowCount <= 0)
      return result;

    var slots = ReadSlots(addon, rowCount);
    var rendered = ReadRenderedRows(addon, slots, market);

    for (var row = 0; row < rowCount; row++)
    {
      var slot = row < slots.Count ? slots[row] : MarketRowMap.NoRow;
      rendered.TryGetValue(row, out var seen);
      result.Add(new SellListRow(row, slot, seen.ItemId, seen.Price));
    }

    return result;
  }

  /// <summary>Number of rows the open list is showing, or -1 when it is not available.</summary>
  public static int RowCount()
  {
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && GenericHelpers.IsAddonReady(addon))
      return RowCount(addon);
    return -1;
  }

  private static int RowCount(AtkUnitBase* addon)
  {
    var list = addon->GetComponentListById(ListNodeId);
    return list == null ? -1 : list->ListLength;
  }

  /// <summary>
  /// The market slot each row is showing, straight out of the addon's setup values. Stops at the first block
  /// the addon has not filled in, so a short read yields fewer entries rather than zeroes that look like slot 0.
  /// </summary>
  private static List<int> ReadSlots(AtkUnitBase* addon, int rowCount)
  {
    var slots = new List<int>();
    if (addon->AtkValues == null)
      return slots;

    for (var row = 0; row < rowCount; row++)
    {
      var index = FirstListingValue + (row * ListingValueStride);
      if (index >= addon->AtkValuesCount)
        break;

      var value = addon->AtkValues[index];
      if (value.Type != AtkValueType.Int && value.Type != AtkValueType.UInt)
        break;

      var slot = value.Type == AtkValueType.Int ? value.Int : (int)value.UInt;
      if (slot is < 0 or >= AutoMarketService.MarketSlotCount)
        break;

      slots.Add(slot);
    }

    return slots;
  }

  /// <summary>
  /// Item id + asking price for the rows that currently have a live renderer. The list virtualises, so this
  /// is a partial reading by design - absent rows simply do not appear in the dictionary. A row whose label
  /// cannot be pinned to exactly one item is recorded as item 0 (unknown), never as a best guess.
  /// </summary>
  private static Dictionary<int, (uint ItemId, long Price)> ReadRenderedRows(
    AtkUnitBase* addon, List<int> slots, IReadOnlyList<MarketSlot>? market)
  {
    var seen = new Dictionary<int, (uint ItemId, long Price)>();
    var list = addon->GetComponentListById(ListNodeId);
    if (list == null || list->ItemRendererList == null)
      return seen;

    for (var i = 0; i < list->ListLength; i++)
    {
      var renderer = list->ItemRendererList[i].AtkComponentListItemRenderer;
      if (renderer == null)
        continue;

      var row = renderer->ListItemIndex;
      if (row < 0 || seen.ContainsKey(row))
        continue;

      var nameNode = renderer->GetTextNodeById(RowItemNameNodeId);
      if (nameNode == null)
        continue;

      var display = nameNode->NodeText.GetText();
      var raw = nameNode->NodeText.ToString();
      if (!ItemNameResolver.TryGetItemId(display, raw, out var itemId, ExpectedItemAt(row, slots, market)))
        itemId = 0;

      long price = 0;
      var priceNode = renderer->GetTextNodeById(RowPriceNodeId);
      if (priceNode != null)
        price = ParsePrice(priceNode->NodeText.GetText());

      seen[row] = (itemId, price);
    }

    return seen;
  }

  /// <summary>
  /// What the market container holds in the slot this row says it is showing, or 0 when either the slot
  /// reading or the snapshot is unavailable. Ground truth for spotting a clipped label - never used as the
  /// row's identity on its own.
  /// </summary>
  private static uint ExpectedItemAt(int row, List<int> slots, IReadOnlyList<MarketSlot>? market)
  {
    if (market == null || row < 0 || row >= slots.Count)
      return 0;
    var slot = slots[row];
    if (slot == MarketRowMap.NoRow)
      return 0;
    return market.FirstOrDefault(m => m.Slot == slot)?.ItemId ?? 0u;
  }

  /// <summary>
  /// A displayed price to a number. The client renders thousands separators (and the separator character is
  /// locale-dependent), so every non-digit is dropped rather than trying to match a format. Returns 0 when
  /// there is no digit at all, which callers treat as "unknown", never as "free".
  /// </summary>
  private static long ParsePrice(string text)
  {
    long value = 0;
    var any = false;
    foreach (var ch in text)
    {
      if (ch is < '0' or > '9')
        continue;
      any = true;
      value = (value * 10) + (ch - '0');
      if (value > 999_999_999_999L)
        return 0; // nonsense; treat as unreadable rather than pass a bogus number to the matcher
    }
    return any ? value : 0;
  }
}
