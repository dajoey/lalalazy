namespace LazyFashionReport.Core;

/// <summary>How the executor will obtain the planned piece (P4 executor half, v0.5.0.0).</summary>
public enum ObtainMethod
{
    /// <summary>Already worn with the right look; nothing to do.</summary>
    AlreadyWorn,
    /// <summary>Move from a bag/armoury container onto the equipped slot (InventoryManager.MoveItemSlot).</summary>
    EquipFromBags,
    /// <summary>Withdraw from the glamour dresser into bags first, then equip (MirageManager.RestorePrismBoxItem).</summary>
    WithdrawFromDresser,
    /// <summary>Withdraw from the armoire into bags first, then equip (UIState->Cabinet.WithdrawCabinetItem).</summary>
    WithdrawFromArmoire,
    /// <summary>The plan named no item (unhinted "any item" slot - touching it is pure risk; never moved).</summary>
    LeaveUntouched,
    /// <summary>The piece vanished between planning and applying (was consumed/moved/dropped).</summary>
    Missing,
}

/// <summary>Container + slot index pair for an inventory location.</summary>
public sealed record InventoryCoord
{
    public required int Container { get; init; }
    public required int Slot { get; init; }
}

/// <summary>One executable step of the apply plan.</summary>
public sealed record ApplyStep
{
    public required FashionSlot Slot { get; init; }
    public required ObtainMethod Method { get; init; }
    /// <summary>Planned appearance item id (the crowd-gold piece); 0 for untouched slots.</summary>
    public required uint ItemId { get; init; }
    public string ItemName { get; init; } = "";
    /// <summary>Source container + slot the piece moves FROM (EquipFromBags only).</summary>
    public InventoryCoord? Source { get; init; }
    /// <summary>Stain to apply after equipping (0 = none). Only ever a dye whose ITEM is in bags.</summary>
    public uint StainId { get; init; }
    public string StainName { get; init; } = "";
    /// <summary>True when the piece being worn already carries this stain (nothing to consume).</summary>
    public bool StainAlreadyOn { get; init; }
    /// <summary>Stain on the SOURCE copy in bags (a pre-dyed copy needs no dye item spent).</summary>
    public uint SourceStainId { get; init; }
    /// <summary>Plain-language action for the readout.</summary>
    public string Action { get; init; } = "";
}

/// <summary>
/// Builds the executable apply plan from the planner's OutfitAssembly plus live snapshots
/// (P4 executor half, v0.5.0.0). PURE: no game calls, fully offline-harness-tested. The
/// simulator (ApplySimulator) turns this into the "what WOULD happen" readout; the live
/// mover (MoveItemSlot / RestorePrismBoxItem / WithdrawCabinetItem) is a later, separately
/// in-game-verified release.
///
/// Hard rules encoded here:
/// - An unhinted "any item" slot is NEVER touched (it already scores base; moving gear there
///   is pure risk).
/// - A dye step is only emitted when the planner's exact dye is owned AND its dye item can be
///   located in bags right now (it would be consumed); a dye already on the equipped piece is
///   reported as already-on, never re-applied.
/// - A piece that cannot be located becomes Missing - reported, never guessed at.
/// </summary>
public static class ApplyPlanBuilder
{
    /// <param name="assembly">The planner's outfit (unhinted slots are "any item").</param>
    /// <param name="plusTwoStain">Left-side slot -> this week's exact +2 stain id (0 = none).</param>
    /// <param name="equipped">Live equipped appearance id per slot (glamour wins, as the report reads it).</param>
    /// <param name="equippedStain">Live stain id per equipped slot (0 = undyed).</param>
    /// <param name="locate">item id -> where one copy physically sits (with bags coord + that copy's stain).</param>
    /// <param name="dyeItemForStain">stain id -> the dye item id that applies it (0 = unknown dye item).</param>
    /// <param name="dyeItemLocations">dye item id -> every coord where a copy sits in bags (multi-use stock).</param>
    /// <param name="itemName">item id -> display name.</param>
    /// <param name="stainName">stain id -> display name.</param>
    public static IReadOnlyList<ApplyStep> Build(
        OutfitAssembly assembly,
        IReadOnlyDictionary<FashionSlot, uint>? plusTwoStain,
        IReadOnlyDictionary<FashionSlot, uint>? equipped,
        IReadOnlyDictionary<FashionSlot, uint>? equippedStain,
        Func<uint, (ItemStorage Storage, InventoryCoord? Coord, uint Stain)?>? locate,
        Func<uint, uint>? dyeItemForStain,
        IReadOnlyDictionary<uint, IReadOnlyList<InventoryCoord>>? dyeItemLocations,
        Func<uint, string> itemName,
        IReadOnlyDictionary<uint, string>? stainName)
    {
        var steps = new List<ApplyStep>(ScoreMath.TotalSlots);

        // Reserve one physical dye item copy per consumer slot so the readout's consumption
        // math is honest when several slots want the same dye (Jet Black on two slots = 2 items).
        var reserved = new Dictionary<uint, List<InventoryCoord>>();

        foreach (var piece in assembly.Pieces)
        {
            var method = ObtainMethod.LeaveUntouched;
            InventoryCoord? src = null;
            var action = "";
            var stain = 0u;
            var stainNm = "";
            var stainOn = false;
            var sourceStain = 0u;

            if (piece.ItemId != 0)
            {
                var where = locate?.Invoke(piece.ItemId);
                if (where is { } w && w.Storage != ItemStorage.None)
                {
                    var already = equipped != null
                        && equipped.TryGetValue(piece.Slot, out var cur) && cur == piece.ItemId;
                    if (already || w.Storage.HasFlag(ItemStorage.Equipped) && !w.Storage.HasFlag(ItemStorage.Bags))
                    {
                        method = ObtainMethod.AlreadyWorn;
                        action = already
                            ? $"already wearing {itemName(piece.ItemId)}"
                            : $"{itemName(piece.ItemId)} already worn for this look";
                    }
                    else
                    {
                        // Bags beats dresser beats armoire when several copies exist (fewest trips).
                        if (w.Storage.HasFlag(ItemStorage.Bags) && w.Coord is { } bag)
                        {
                            method = ObtainMethod.EquipFromBags;
                            src = bag;
                            sourceStain = w.Stain;
                            action = $"equip {itemName(piece.ItemId)} from bags";
                        }
                        else if (w.Storage.HasFlag(ItemStorage.Dresser))
                        {
                            method = ObtainMethod.WithdrawFromDresser;
                            action = $"withdraw {itemName(piece.ItemId)} from the glamour dresser, then equip";
                        }
                        else if (w.Storage.HasFlag(ItemStorage.Armoire))
                        {
                            method = ObtainMethod.WithdrawFromArmoire;
                            action = $"withdraw {itemName(piece.ItemId)} from the armoire, then equip";
                        }
                        else
                        {
                            method = ObtainMethod.Missing;
                            action = $"{itemName(piece.ItemId)} not reachable (stored: {w.Storage})";
                        }
                    }
                }
                else
                {
                    method = ObtainMethod.Missing;
                    action = $"{itemName(piece.ItemId)} not found in bags, dresser, or armoire";
                }
            }
            else
            {
                action = $"leave untouched (any item scores {ScoreMath.BaseFor(piece.Slot.IsAccessory())})";
            }

            // Dye leg: left-side slots only, only the planner's exact +2 stain, only when the
            // dye ITEM is physically present in bags (it is consumed on apply). The planner's
            // DyeNote is the human-facing truth; this is the executable truth built from the
            // same stain map, so the two can never disagree about what gets applied.
            if (piece.Slot.IsLeftSide()
                && plusTwoStain != null
                && plusTwoStain.TryGetValue(piece.Slot, out var want) && want != 0
                && method is not (ObtainMethod.LeaveUntouched or ObtainMethod.Missing))
            {
                var nm = stainName?.GetValueOrDefault(want, $"stain {want}") ?? $"stain {want}";
                var equippedNow = equippedStain?.GetValueOrDefault(piece.Slot, 0u) ?? 0u;
                var itemWorn = method == ObtainMethod.AlreadyWorn
                    || method == ObtainMethod.EquipFromBags; // stain rides the piece being worn after the move

                if (equippedNow == want && itemWorn)
                {
                    stain = want;
                    stainNm = nm;
                    stainOn = true;
                }
                else if (sourceStain == want && method == ObtainMethod.EquipFromBags)
                {
                    // The bag copy is already dyed exactly right: equipping it carries the
                    // dye, nothing is consumed, nothing is reserved.
                    stain = want;
                    stainNm = nm;
                    stainOn = true;
                    action += $" (dye {nm} already on the copy)";
                }
                else
                {
                    // Reserve one physical dye item per consumer: when N slots want the same
                    // dye, exactly min(N, stock) get a dye step. Dye items are unstacked
                    // one-per-coord, so each coord is one consumable.
                    var dyeItem = dyeItemForStain?.Invoke(want) ?? 0u;
                    var coords = dyeItemLocations?.GetValueOrDefault(dyeItem);
                    if (dyeItem != 0 && coords is { Count: > 0 })
                    {
                        var used = reserved.GetValueOrDefault(dyeItem);
                        var taken = used?.Count ?? 0;
                        if (taken < coords.Count)
                        {
                            if (used is null)
                                reserved[dyeItem] = used = new List<InventoryCoord>();
                            used.Add(coords[taken]);
                            stain = want;
                            stainNm = nm;
                        }
                    }
                }
            }

            steps.Add(new ApplyStep
            {
                Slot = piece.Slot,
                Method = method,
                ItemId = piece.ItemId,
                ItemName = piece.ItemName,
                Source = src,
                StainId = stain,
                StainName = stainNm,
                StainAlreadyOn = stainOn,
                SourceStainId = sourceStain,
                Action = action,
            });
        }
        return steps;
    }
}
