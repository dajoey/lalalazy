using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

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
        var owned = new HashSet<uint>();

        var inv = InventoryManager.Instance();
        if (inv != null)
        {
            foreach (InventoryType type in Enum.GetValues<InventoryType>())
            {
                if (!IsOwnContainer(type)) continue;
                var cont = inv->GetInventoryContainer(type);
                if (cont == null || !cont->IsLoaded) continue;
                for (var i = 0; i < cont->Size; i++)
                {
                    var item = cont->GetInventorySlot(i);
                    if (item == null || item->ItemId == 0) continue;
                    owned.Add(item->ItemId);
                    if (item->GlamourId != 0) owned.Add(item->GlamourId);
                }
            }
        }

        // Glamour dresser (fixed 800 slots; PrismBoxLoaded distinguishes empty from not-loaded).
        var mirage = MirageManager.Instance();
        if (mirage != null && mirage->PrismBoxLoaded)
        {
            var ids = mirage->PrismBoxItemIds;
            for (var var_i = 0; var_i < ids.Length; var_i++)
                if (ids[var_i] != 0) owned.Add(ids[var_i]);
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
                            owned.Add(id);
                }
            }
            catch { }
        }

        return owned;
    }

    /// <summary>True for containers the player can wear/glamour from at the Gold Saucer.</summary>
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
