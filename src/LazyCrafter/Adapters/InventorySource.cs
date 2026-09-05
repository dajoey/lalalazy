namespace LazyCrafter.Adapters;

/// <summary>
/// Where AllaganTools may look when counting an item (Scope §0 "Inventory scope"). Each source is one
/// toggle in the settings; <see cref="InventorySources.Defaults"/> is everything on except the FC chest.
/// </summary>
public enum InventorySource
{
    /// <summary>The character's four bags plus the crystal pouch.</summary>
    Bags,
    /// <summary>Armoury chest and equipped gear.</summary>
    ArmouryChest,
    /// <summary>Chocobo saddlebag (both halves, incl. the premium pages).</summary>
    Saddlebag,
    /// <summary>Retainer bags, crystals and market listings.</summary>
    Retainers,
    /// <summary>Pool every character AllaganTools knows about, not just the one logged in.</summary>
    AltCharacters,
    /// <summary>Free company chest. Off by default - it is shared property.</summary>
    FCChest,
    /// <summary>Glamour dresser and armoire.</summary>
    GlamourDresser,
}

public static class InventorySources
{
    // AllaganTools (CriticalCommonLib) InventoryType ids. Verified against
    // CriticalCommonLib/Enums/InventoryType.cs on 2026-09-03; these are what the
    // `AllaganTools.ItemCountOwned` IPC compares `item.SortedContainer` to.
    public static readonly uint[] BagTypes = [0, 1, 2, 3, 2001];                      // Bag0..3, Crystal
    public static readonly uint[] ArmouryTypes =
        [1000, 1001, 3200, 3201, 3202, 3203, 3204, 3205, 3206, 3207, 3208, 3209, 3300, 3400, 3500];
    public static readonly uint[] SaddlebagTypes = [4000, 4001, 4100, 4101];
    public static readonly uint[] RetainerTypes =
        [10000, 10001, 10002, 10003, 10004, 10005, 10006, 12001, 12002];             // RetainerBag0..6, RetainerCrystal, RetainerMarket
    /// <summary>
    /// A retainer's market-board listings. Counted as owned (Scope 0) but NOT fetchable: a summoning bell hands over
    /// bag/crystal stock only, and Artisan's retainer count reads 10000-10006 + 12001, never this container.
    /// </summary>
    public const uint RetainerMarket = 12002;
    public static readonly uint[] FcChestTypes =
        [20000, 20001, 20002, 20003, 20004, 20005, 20006, 20007, 20008, 20009, 20010, 22001];
    public static readonly uint[] GlamourTypes = [2500, 2501];                         // Armoire, GlamourChest

    public static bool DefaultFor(InventorySource source) => source != InventorySource.FCChest;

    public static Dictionary<string, bool> Defaults()
    {
        var d = new Dictionary<string, bool>();
        foreach (var s in Enum.GetValues<InventorySource>()) d[s.ToString()] = DefaultFor(s);
        return d;
    }

    /// <summary>Container ids for one source; <see cref="InventorySource.AltCharacters"/> is a scope flag, not a container set.</summary>
    public static uint[] TypesFor(InventorySource source) => source switch
    {
        InventorySource.Bags => BagTypes,
        InventorySource.ArmouryChest => ArmouryTypes,
        InventorySource.Saddlebag => SaddlebagTypes,
        InventorySource.Retainers => RetainerTypes,
        InventorySource.FCChest => FcChestTypes,
        InventorySource.GlamourDresser => GlamourTypes,
        _ => [],
    };
}
