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
    /// <summary>Retainer bags and crystals. Market-board listings are NOT counted - see <see cref="InventorySources.RetainerMarket"/>.</summary>
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
    /// <summary>
    /// Retainer containers that count as stock you HAVE: bags 10000-10006 plus the retainer crystal pouch 12001.
    /// <para>
    /// <see cref="RetainerMarket"/> (12002) is deliberately absent, and that one id used to dead-end a whole cart:
    /// an item you had listed for sale made <c>Count()</c> report it as owned, so <c>IngredientLeaf.Missing</c> fell
    /// to 0, <c>DispatchPlan.RouteFor</c> returned <c>Route.Have</c> before classifying it, and the item turned into
    /// a "retrieve by hand" step no summoning bell can satisfy - which blocked every craft above it (Hardsilver
    /// Nugget 12520, 2026-09-05). Stock you have listed for sale is stock you do not have: it is Missing, and it
    /// routes normally - craft, gather or buy.
    /// </para>
    /// </summary>
    public static readonly uint[] RetainerTypes =
        [10000, 10001, 10002, 10003, 10004, 10005, 10006, 12001];                    // RetainerBag0..6, RetainerCrystal
    /// <summary>
    /// A retainer's market-board listings. NOT part of <see cref="RetainerTypes"/> and never counted as owned: a
    /// summoning bell hands over bag/crystal stock only, and Artisan's retainer count reads 10000-10006 + 12001,
    /// never this container. <c>AllaganInventory.StoredWhere</c> still queries it explicitly, so a listing can be
    /// NAMED ("1 on the market board, listed by retainer X") without that count inflating <c>Have</c>.
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
