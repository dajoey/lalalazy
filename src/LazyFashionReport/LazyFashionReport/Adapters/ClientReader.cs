using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LazyFashionReport.Core;

namespace LazyFashionReport.Adapters;

/// <summary>
/// Live client reads. Every member used here was enumerated from omasky's INSTALLED
/// FFXIVClientStructs (Hooks/15.0.3.2, 2026-09-06) before this file was written:
/// - AgentFashion{static Instance(), OpenType, FashionCheckData{WeeklyTheme,Score,ItemThemes,ItemEvaluations}, Items}
///   (Items = FashionCheckItemDataStruct rows with ItemId/Stain0Id/Stain1Id — what AvantGarde
///   reads on the result screen; WeekNum = WeeklyTheme - 9u).
/// - AddonFashionCheck.AtkValues[2 + slot*11].String = live hint text (AvantGarde MainWindow.cs).
/// - InventoryManager{static Instance(), GetInventoryContainer(type), GetInventorySlot(type,i)};
///   InventoryContainer{Size, IsLoaded}; InventoryItem{ItemId, GlamourId}.
/// - MirageManager{static Instance(), PrismBoxItemIds (800), PrismBoxLoaded}.
/// - Cabinet via UIState.Instance()-&gt;Cabinet{IsItemInCabinet(id), IsCabinetLoaded}.
///
/// All calls run on the framework thread only (Service batches them into one pass).
/// </summary>
internal static unsafe class ClientReader
{
    public const int HintCount = 11;

    /// <summary>
    /// Live hint text per slot from the FashionCheck addon (AtkValues[2 + slot*11], AvantGarde's
    /// verified layout). Returns null when the addon is not open; empty string = no hint this week.
    /// </summary>
    public static string?[]? ReadAddonHints(AddonFashionCheck* addon)
    {
        if (addon == null) return null;
        try
        {
            var values = addon->AtkValues;
            var hints = new string?[HintCount];
            for (var i = 0; i < HintCount; i++)
            {
                // AvantGarde's exact read: AtkValues[2 + i*11].String.ToString() (no null check needed;
                // String is a value struct whose ToString yields "" for unset slots).
                hints[i] = values[2 + i * 11].String.ToString();
            }
            return hints;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Equipped items as the Fashion Report sees them: glamour appearance wins over the physical
    /// item. Read from AgentFashion.Items (the submission itself), falling back to the equip
    /// container when the agent is not loaded.
    /// </summary>
    public static List<Core.EquippedItem>? ReadEquipped()
    {
        try
        {
            var agent = AgentFashion.Instance();
            if (agent == null) return null;
            var items = agent->Items;
            if (items.Length == 0) return null;

            var result = new List<Core.EquippedItem>(items.Length);
            for (var i = 0; i < items.Length && i < HintCount; i++)
            {
                var it = items[i];
                result.Add(new Core.EquippedItem
                {
                    Slot = (Core.FashionSlot)i,
                    ItemId = it.ItemId,
                    Stain0Id = it.Stain0Id,
                    Stain1Id = it.Stain1Id,
                });
            }
            return result;
        }
        catch { return null; }
    }

    /// <summary>The game's own judged result, when the result screen is open.</summary>
    public static (int Week, int Score)? ReadJudgedResult()
    {
        try
        {
            var agent = AgentFashion.Instance();
            if (agent == null) return null;
            if (agent->OpenType != AgentFashionOpenType.Result) return null;
            var data = agent->FashionCheckData;
            return ((int)data.WeeklyTheme - 9, (int)data.Score);
        }
        catch { return null; }
    }

    /// <summary>
    /// Glamour-usable item ids the player owns: bags + armoury + equipped (incl. glamour ids
    /// riding on items), the glamour dresser, and the armoire (queried per candidate id —
    /// cheap for a few hundred ids; avoids enumerating the whole Cabinet sheet).
    /// Snapshot; call on the framework thread.
    /// </summary>
    public static HashSet<uint> ReadOwnedItems(IEnumerable<uint>? candidateIds = null)
    {
        var cat = ReadOwnedCatalog(candidateIds);
        return new HashSet<uint>(cat.ByItem.Keys);
    }

    /// <summary>
    /// The P3 get-to catalog: item id -> EVERY place it was found (bags / dresser / armoire /
    /// equipped). Same single pass as <see cref="ReadOwnedItems"/>; an item found in several
    /// places carries all the flags. Retainer-held items are NOT glamour-usable in the Gold
    /// Saucer, so they are out of scope here (the crowd data only suggests gear the player
    /// could wear now).
    /// </summary>
    public static OwnedCatalog ReadOwnedCatalog(IEnumerable<uint>? candidateIds = null)
    {
        var byItem = new Dictionary<uint, ItemStorage>();

        void Add(uint id, ItemStorage where)
        {
            byItem[id] = byItem.GetValueOrDefault(id, ItemStorage.None) | where;
        }

        var inv = InventoryManager.Instance();
        if (inv != null)
        {
            foreach (InventoryType type in Enum.GetValues<InventoryType>())
            {
                if (!IsOwnContainer(type)) continue;
                var where = type == InventoryType.EquippedItems ? ItemStorage.Equipped : ItemStorage.Bags;
                var cont = inv->GetInventoryContainer(type);
                if (cont == null || !cont->IsLoaded) continue;
                for (var i = 0; i < cont->Size; i++)
                {
                    var item = cont->GetInventorySlot(i);
                    if (item == null || item->ItemId == 0) continue;
                    Add(item->ItemId, where);
                    if (item->GlamourId != 0) Add(item->GlamourId, where);
                }
            }
        }

        // Glamour dresser (fixed 800 slots; PrismBoxLoaded distinguishes empty from not-loaded).
        var mirage = MirageManager.Instance();
        if (mirage != null && mirage->PrismBoxLoaded)
        {
            var ids = mirage->PrismBoxItemIds;
            for (var var_i = 0; var_i < ids.Length; var_i++)
                if (ids[var_i] != 0) Add(ids[var_i], ItemStorage.Dresser);
        }

        // Armoire: per-candidate-id query against the loaded cabinet.
        if (candidateIds != null)
        {
            try
            {
                var ui = UIState.Instance();
                if (ui != null && ui->Cabinet.IsCabinetLoaded())
                {
                    foreach (var id in candidateIds)
                        if (ui->Cabinet.IsItemInCabinet(id))
                            Add(id, ItemStorage.Armoire);
                }
            }
            catch { }
        }

        return new OwnedCatalog { ByItem = byItem };
    }

    /// <summary>Stain ids the player can apply right now: every bag/armoury slot holding a
    /// dye-capable item resolves to its stain via the sheet's stain->item links. Framework
    /// thread. P4 planner half: the dye instruction must never tell the player to apply a
    /// dye they do not own.</summary>
    public static HashSet<uint> ReadOwnedStains()
    {
        var stains = new HashSet<uint>();
        var inv = InventoryManager.Instance();
        if (inv == null) return stains;
        foreach (InventoryType type in Enum.GetValues<InventoryType>())
        {
            if (type is not (InventoryType.Inventory1 or InventoryType.Inventory2
                or InventoryType.Inventory3 or InventoryType.Inventory4
                or InventoryType.ArmoryMainHand or InventoryType.ArmoryOffHand
                or InventoryType.ArmoryHead or InventoryType.ArmoryBody or InventoryType.ArmoryHands
                or InventoryType.ArmoryLegs or InventoryType.ArmoryFeets
                or InventoryType.ArmoryEar or InventoryType.ArmoryNeck or InventoryType.ArmoryWrist
                or InventoryType.ArmoryRings)) continue;
            var cont = inv->GetInventoryContainer(type);
            if (cont == null || !cont->IsLoaded) continue;
            for (var i = 0; i < cont->Size; i++)
            {
                var item = cont->GetInventorySlot(i);
                if (item == null || item->ItemId == 0) continue;
                if (DyeItemToStain.TryGetValue(item->ItemId, out var stain) && stain != 0)
                    stains.Add(stain);
            }
        }
        return stains;
    }

    /// <summary>Dye item id -> stain id it applies, filled by the host from the live Stain
    /// sheet (each stain row links its Items). Static so the framework-thread read can map
    /// without holding a sheet reference.</summary>
    public static Dictionary<uint, uint> DyeItemToStain { get; } = new();
    private static bool IsOwnContainer(InventoryType type) => type switch
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
