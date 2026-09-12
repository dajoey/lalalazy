using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using LazyFashionReport.Core;

namespace LazyFashionReport.Adapters;

/// <summary>
/// Executor-side live reads (P4 executor half, v0.5.0.0). Everything here is READ-ONLY: the
/// dry-run release must not move gear or consume dye. The member pins were enumerated from
/// omasky's INSTALLED FFXIVClientStructs (Hooks/15.0.3.4, 2026-09-12, dnfile probe with a
/// negative control) BEFORE this file was written:
/// - InventoryManager: i32 MoveItemSlot(InventoryType, u16, InventoryType, u16, bool),
///   bool CanEquip(u32,u8,u8,u16,u8,u8,u8,f64), GetInventoryContainer/GetInventorySlot,
///   i32 GetItemCountInContainer(u32, InventoryType, ...). [live mover: NEXT release]
/// - MirageManager: bool RestorePrismBoxItem(u32), PrismBoxItemIds (800), PrismBoxLoaded.
/// - Cabinet (UIState->Cabinet): bool WithdrawCabinetItem(u32), StoreCabinetItem,
///   IsItemInCabinet, IsCabinetLoaded.
/// - AgentFashion.Items: equipped appearance ids + stains (glamour wins) - the "already
///   wearing" check reads the SAME source the Fashion Report judges.
/// There is NO direct InventoryManager dye-apply call in this build: dye application is
/// staged through AgentMiragePrismMiragePlate.SetItemStain / SetSelectedItemStains (the
/// plate editor's pending-stain state), so the live dye leg needs its own in-game probe
/// session before any code is written for it.
/// </summary>
internal static unsafe class ApplyExecutor
{
    /// <summary>
    /// Snapshot everything the ApplyPlanBuilder needs, in ONE framework-thread pass:
    /// equipped appearance + stains, per-item location (bags coord preferred, then dresser,
    /// then armoire), and every dye-item coord in bags.
    /// </summary>
    public static ApplySnapshot Snapshot(IEnumerable<uint> itemIds, IEnumerable<uint> stainIds)
    {
        var equipped = new Dictionary<FashionSlot, uint>();
        var equippedStain = new Dictionary<FashionSlot, uint>();
        var rawEquipped = new List<RawContainerRow>();

        // Equipped appearance: same source the report reads (glamour wins over physical).
        try
        {
            var agent = AgentFashion.Instance();
            if (agent != null)
            {
                var items = agent->Items;
                for (var i = 0; i < items.Length && i < ClientReader.HintCount; i++)
                {
                    var slot = (FashionSlot)i;
                    equipped[slot] = items[i].ItemId;
                    equippedStain[slot] = items[i].Stain0Id;
                }
            }
        }
        catch { /* agent not loaded: empty maps = "unknown equipped"; builder reports honestly */ }

        // Per-item location with bags coord.
        var locations = new Dictionary<uint, (ItemStorage, InventoryCoord?, uint)>();
        var inv = InventoryManager.Instance();
        var wanted = new HashSet<uint>(itemIds);
        if (inv != null && wanted.Count > 0)
        {
            foreach (InventoryType type in Enum.GetValues<InventoryType>())
            {
                if (!IsSearchContainer(type)) continue;
                var cont = inv->GetInventoryContainer(type);
                if (cont == null || !cont->IsLoaded) continue;
                for (var i = 0; i < cont->Size; i++)
                {
                    var item = cont->GetInventorySlot(i);
                    if (item == null || item->ItemId == 0) continue;
                    if (!wanted.Contains(item->ItemId) && item->GlamourId == 0) continue;
                    if (type == InventoryType.EquippedItems)
                    {
                        // v0.5.1.0: raw container row for the mapping proof. READ-ONLY.
                        var rStains = item->Stains;
                        rawEquipped.Add(new RawContainerRow
                        {
                            Container = (int)type,
                            Slot = i,
                            ItemId = item->ItemId,
                            GlamourId = item->GlamourId,
                            Stain0 = rStains.Length > 0 ? rStains[0] : 0u,
                        });
                    }
                    var where = type == InventoryType.EquippedItems ? ItemStorage.Equipped : ItemStorage.Bags;
                    // A GlamourId riding on a bag item also provides the look - record the
                    // glamour id as present-in-bags (its carrier is movable to the slot).
                    locations.TryGetValue(item->ItemId, out var prev);
                    var coord = new InventoryCoord { Container = (int)type, Slot = i };
                    var stains = item->Stains;
                    var stain0 = stains.Length > 0 ? stains[0] : 0u;
                    if (where == ItemStorage.Bags)
                        locations[item->ItemId] = (prev.Item1 | ItemStorage.Bags, coord, stain0);
                    else if ((prev.Item1 & ItemStorage.Bags) == 0)
                        locations[item->ItemId] = (prev.Item1 | where, prev.Item2, prev.Item3);
                    if (item->GlamourId != 0 && wanted.Contains(item->GlamourId))
                    {
                        locations.TryGetValue(item->GlamourId, out var gprev);
                        locations[item->GlamourId] = (gprev.Item1 | ItemStorage.Bags, coord, stain0);
                    }
                }
            }
        }

        // Dresser + armoire presence (no coords - withdraw is by item id).
        var mirage = MirageManager.Instance();
        var uiState = UIState.Instance();
        foreach (var id in wanted)
        {
            if (locations.TryGetValue(id, out var cur) && (cur.Item1 & ItemStorage.Bags) != 0)
                continue; // bags copy wins: no trip needed
            var storage = cur.Item1;
            if (mirage != null && mirage->PrismBoxLoaded)
            {
                var ids = mirage->PrismBoxItemIds;
                for (var i = 0; i < ids.Length; i++)
                    if (ids[i] == id) { storage |= ItemStorage.Dresser; break; }
            }
            try
            {
                if (uiState != null && uiState->Cabinet.IsCabinetLoaded() && uiState->Cabinet.IsItemInCabinet(id))
                    storage |= ItemStorage.Armoire;
            }
            catch { }
            locations[id] = (storage, cur.Item2, cur.Item3);
        }

        // Dye item stock: every coord holding one of the wanted stains' dye items.
        var dyeLocs = new Dictionary<uint, IReadOnlyList<InventoryCoord>>();
        if (inv != null && stainIds.Any())
        {
            var dyeItems = new HashSet<uint>();
            foreach (var stain in stainIds)
                if (ClientReader.StainToDyeItem.TryGetValue(stain, out var di) && di != 0)
                    dyeItems.Add(di);
            if (dyeItems.Count > 0)
            {
                var found = new Dictionary<uint, List<InventoryCoord>>();
                foreach (InventoryType type in Enum.GetValues<InventoryType>())
                {
                    if (!IsSearchContainer(type) || type == InventoryType.EquippedItems) continue;
                    var cont = inv->GetInventoryContainer(type);
                    if (cont == null || !cont->IsLoaded) continue;
                    for (var i = 0; i < cont->Size; i++)
                    {
                        var item = cont->GetInventorySlot(i);
                        if (item == null || item->ItemId == 0) continue;
                        if (dyeItems.Contains(item->ItemId))
                        {
                            if (!found.TryGetValue(item->ItemId, out var list))
                                found[item->ItemId] = list = new List<InventoryCoord>();
                            list.Add(new InventoryCoord { Container = (int)type, Slot = i });
                        }
                    }
                }
                foreach (var (di, list) in found)
                    dyeLocs[di] = list;
            }
        }

        return new ApplySnapshot
        {
            RawEquipped = rawEquipped,
            Equipped = equipped,
            EquippedStain = equippedStain,
            Locations = locations,
            DyeItemLocations = dyeLocs,
        };
    }

    private static bool IsSearchContainer(InventoryType type) => type switch
    {
        InventoryType.EquippedItems => true,
        InventoryType.Inventory1 => true,
        InventoryType.Inventory2 => true,
        InventoryType.Inventory3 => true,
        InventoryType.Inventory4 => true,
        InventoryType.ArmoryMainHand => true,
        InventoryType.ArmoryOffHand => true,
        InventoryType.ArmoryHead => true,
        InventoryType.ArmoryBody => true,
        InventoryType.ArmoryHands => true,
        InventoryType.ArmoryLegs => true,
        InventoryType.ArmoryFeets => true,
        InventoryType.ArmoryEar => true,
        InventoryType.ArmoryNeck => true,
        InventoryType.ArmoryWrist => true,
        InventoryType.ArmoryRings => true,
        _ => false,
    };
}

/// <summary>One physical row of a game inventory container, as the live executor will address it (v0.5.1.0).</summary>
public sealed record RawContainerRow
{
    public required int Container { get; init; }
    public required int Slot { get; init; }
    public required uint ItemId { get; init; }
    public required uint GlamourId { get; init; }
    public required uint Stain0 { get; init; }
}

/// <summary>Immutable one-pass snapshot the builder consumes on any thread.</summary>
internal sealed record ApplySnapshot
{
    /// <summary>Raw container rows for the equipped container (and bag slots holding wanted
    /// pieces), logged by the dry run so the live mover's MoveItemSlot destination indices
    /// can be proven offline from ffxivdb without a debugger.</summary>
    public required IReadOnlyList<RawContainerRow> RawEquipped { get; init; }
    public required IReadOnlyDictionary<FashionSlot, uint> Equipped { get; init; }
    public required IReadOnlyDictionary<FashionSlot, uint> EquippedStain { get; init; }
    public required IReadOnlyDictionary<uint, (ItemStorage Storage, InventoryCoord? Coord, uint Stain)> Locations { get; init; }
    public required IReadOnlyDictionary<uint, IReadOnlyList<InventoryCoord>> DyeItemLocations { get; init; }
}
