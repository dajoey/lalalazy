using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.Inventory;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (Inventory cases).
//
// Bags -> Armoury Chest gear mover. Moves equippable gear the player carries in the four bag pages into the
// Armoury Chest page for its slot. A container move (InventoryManager.MoveItemSlot) - nothing is sold,
// destroyed or merged, and every move is logged with both slots, so it is reversible (Undo, below).
//
// RAILS (a stack is skipped, with the reason shown, when any holds):
//   - it is not equippable gear (no Armoury slot), or it is not in Inventory1-4;
//   - anything ACTS on it (StackOwnership: the LMC Auto-Market list / category routing, an AutoRetainer
//     entrust plan, vendor, discard or desynth list). AutoRetainer and LMC only ever take stock from the
//     bags, so moving such a stack to the armoury would silently take it away from them - two movers
//     disagreeing is the known failure mode, and the mover never starts that;
//   - it is referenced by a gearset;
//   - the Armoury page for its slot has no free slot left (counting the moves already planned).
// The plan never merges, never splits, and never touches the armoury page itself.

/// <summary>An Armoury Chest page. None = not equippable gear (also waist, which the game no longer uses).</summary>
public enum ArmourySlot { None, MainHand, OffHand, Head, Body, Hands, Legs, Feet, Ears, Neck, Wrists, Rings, SoulCrystal }

public static class ArmourySlots
{
  /// <summary>
  /// The Armoury page an item goes to, from its EquipSlotCategory row. Columns in sheet order: MainHand,
  /// OffHand, Head, Body, Gloves, Waist, Legs, Feet, Ears, Neck, Wrists, FingerL, FingerR, SoulCrystal.
  /// A value of 1 means "occupies this slot"; -1 means "blocks it" (a two-handed weapon blocks the off hand,
  /// a robe blocks the legs). The FIRST occupied column decides - that is the slot the game equips it to and
  /// the Armoury page that holds it. Rings occupy both finger columns and go to the single Rings page.
  /// Anything with no occupied column (or only Waist) is not Armoury gear.
  /// </summary>
  public static ArmourySlot FromEquipSlotCategory(ReadOnlySpan<sbyte> columns)
  {
    if (columns.Length < 14)
      return ArmourySlot.None;
    ArmourySlot[] map =
    [
      ArmourySlot.MainHand, ArmourySlot.OffHand, ArmourySlot.Head, ArmourySlot.Body, ArmourySlot.Hands,
      ArmourySlot.None /* waist */, ArmourySlot.Legs, ArmourySlot.Feet, ArmourySlot.Ears, ArmourySlot.Neck,
      ArmourySlot.Wrists, ArmourySlot.Rings, ArmourySlot.Rings, ArmourySlot.SoulCrystal,
    ];
    for (var i = 0; i < 14; i++)
    {
      if (columns[i] == 1 && map[i] != ArmourySlot.None)
        return map[i];
    }
    return ArmourySlot.None;
  }

  public static string Label(ArmourySlot s) => s switch
  {
    ArmourySlot.MainHand => "Main hand",
    ArmourySlot.OffHand => "Off hand",
    ArmourySlot.SoulCrystal => "Soul crystals",
    ArmourySlot.None => "-",
    _ => s.ToString(),
  };
}

/// <summary>One occupied bag slot. Container is the game InventoryType value (Inventory1-4 = 0-3).</summary>
public sealed record BagStack(int Container, int Slot, uint ItemId, bool Hq, int Quantity);

/// <summary>One planned bags -> armoury move. The destination SLOT is chosen at execution time, fresh.</summary>
public sealed record GearMoveOp(int SrcContainer, int SrcSlot, uint ItemId, bool Hq, ArmourySlot Target);

/// <summary>A gear stack the mover saw and left alone, with why.</summary>
public sealed record GearSkip(BagStack Stack, ArmourySlot Target, string Reason);

public sealed record GearMovePlan(IReadOnlyList<GearMoveOp> Ops, IReadOnlyList<GearSkip> Skipped);

/// <summary>One executed move, kept so it can be undone: where the item came from and where it landed.</summary>
public sealed record GearMoveRecord(uint ItemId, bool Hq, int BagContainer, int BagSlot, ArmourySlot Target, int ArmouryContainer, int ArmouryIndex);

/// <summary>One planned undo: move the item back from its armoury slot to the bags.</summary>
public sealed record GearUndoOp(GearMoveRecord Record, int DstContainer, int DstSlot);

public sealed record GearUndoPlan(IReadOnlyList<GearUndoOp> Ops, IReadOnlyList<string> Notes);

public static class GearMover
{
  public static bool IsBagContainer(int container) => container is >= 0 and <= 3;

  /// <param name="factsOf">Item-sheet facts per id (null = sheet miss: never moved).</param>
  /// <param name="freeSlots">Free slots per Armoury page right now.</param>
  /// <param name="ownersOf">StackOwnership.Resolve for (item, hq).</param>
  /// <param name="gearsetItems">Item ids any gearset references (quality-less).</param>
  /// <param name="maxOps">Cap on moves in one plan (0 = no cap).</param>
  public static GearMovePlan Plan(
    IReadOnlyList<BagStack> bags,
    Func<uint, ItemFacts?> factsOf,
    IReadOnlyDictionary<ArmourySlot, int> freeSlots,
    Func<uint, bool, IReadOnlyList<Owner>> ownersOf,
    IReadOnlySet<uint> gearsetItems,
    int maxOps)
  {
    var ops = new List<GearMoveOp>();
    var skipped = new List<GearSkip>();
    var budget = new Dictionary<ArmourySlot, int>();
    foreach (var (slot, free) in freeSlots)
      budget[slot] = Math.Max(free, 0);

    foreach (var s in bags.OrderBy(b => b.Container).ThenBy(b => b.Slot))
    {
      if (s.ItemId == 0 || s.Quantity <= 0 || !IsBagContainer(s.Container))
        continue;
      var facts = factsOf(s.ItemId);
      if (facts == null || !facts.Equippable)
        continue; // not gear: not the mover's business, not listed

      var target = facts.ArmourySlot;
      var owners = ownersOf(s.ItemId, s.Hq);
      var acting = owners.Where(o => o.Acts).ToList();
      if (acting.Count > 0)
      {
        skipped.Add(new GearSkip(s, target, "handled elsewhere: " + acting[0].Text));
        continue;
      }
      if (gearsetItems.Contains(s.ItemId))
      {
        skipped.Add(new GearSkip(s, target, "in a gearset - left where it is"));
        continue;
      }
      if (!budget.TryGetValue(target, out var left) || left <= 0)
      {
        skipped.Add(new GearSkip(s, target, $"Armoury {ArmourySlots.Label(target)} is full"));
        continue;
      }
      if (maxOps > 0 && ops.Count >= maxOps)
      {
        skipped.Add(new GearSkip(s, target, "left for the next pass (per-pass cap)"));
        continue;
      }

      budget[target] = left - 1;
      ops.Add(new GearMoveOp(s.Container, s.Slot, s.ItemId, s.Hq, target));
    }

    return new GearMovePlan(ops, skipped);
  }

  /// <summary>
  /// Plans undoing a batch of moves. A record is undone only when its armoury slot STILL holds the same
  /// item and quality (anything else - equipped since, moved by hand, sold - is left alone with a note).
  /// The destination is the original bag slot when it is still empty, otherwise the first empty bag slot not
  /// already claimed; with no empty bag slot the rest of the batch stays in the armoury, with a note.
  /// </summary>
  /// <param name="armouryAt">Reads (itemId, hq) at an armoury (container, slot); null = empty/unreadable.</param>
  /// <param name="emptyBagSlots">Empty bag slots right now, in bag order: (container, slot).</param>
  public static GearUndoPlan PlanUndo(
    IReadOnlyList<GearMoveRecord> batch,
    Func<int, int, (uint ItemId, bool Hq)?> armouryAt,
    IReadOnlyList<(int Container, int Slot)> emptyBagSlots)
  {
    var ops = new List<GearUndoOp>();
    var notes = new List<string>();
    var free = new List<(int Container, int Slot)>(emptyBagSlots);

    foreach (var r in batch)
    {
      var now = armouryAt(r.ArmouryContainer, r.ArmouryIndex);
      if (now == null || now.Value.ItemId != r.ItemId || now.Value.Hq != r.Hq)
      {
        notes.Add($"item {r.ItemId}{(r.Hq ? " HQ" : "")} is no longer in its armoury slot - left alone");
        continue;
      }

      var home = free.FindIndex(f => f.Container == r.BagContainer && f.Slot == r.BagSlot);
      var pick = home >= 0 ? home : (free.Count > 0 ? 0 : -1);
      if (pick < 0)
      {
        notes.Add($"no empty bag slot for item {r.ItemId}{(r.Hq ? " HQ" : "")} - it stays in the armoury");
        continue;
      }

      var dst = free[pick];
      free.RemoveAt(pick);
      ops.Add(new GearUndoOp(r, dst.Container, dst.Slot));
    }

    return new GearUndoPlan(ops, notes);
  }
}
