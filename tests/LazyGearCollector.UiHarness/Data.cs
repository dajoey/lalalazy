namespace LazyGearCollector.UiHarness;

/// <summary>
/// A representative Phantom Vision style collection: eight roles, four tiers per piece, upgrade and trade-up
/// shop paths, wallet currencies. Names follow the plugin's own "Family Piece of Role +N" convention and its
/// shop wording; the numbers are fixed so every render is deterministic.
/// </summary>
internal static class Data
{
    internal const string Family = "Phantom Vision";
    internal const string Display = "Phantom Vision (North Horn)";
    internal const string Note = "Occult Crescent: North Horn. Base pieces from the expedition antiquarian, upgrades from the expedition armorer.";

    private static readonly string[] Armor = ["Head", "Body", "Hands", "Legs", "Feet"];
    private static readonly string[] Accessory = ["Ears", "Neck", "Wrists", "Ring"];

    private static readonly (string Role, string[] Slots, string[] Pieces)[] Roles =
    [
        ("Fending", Armor, ["Visor", "Cuirass", "Gauntlets", "Cuisses", "Sabatons"]),
        ("Maiming", Armor, ["Barbut", "Haubergeon", "Vambraces", "Poleyns", "Greaves"]),
        ("Striking", Armor, ["Hood", "Jacket", "Fingerless Gloves", "Hose", "Sandals"]),
        ("Scouting", Armor, ["Mask", "Doublet", "Armguards", "Brais", "Boots"]),
        ("Aiming", Armor, ["Tricorne", "Coat", "Gloves", "Trousers", "Shoes"]),
        ("Healing", Armor, ["Circlet", "Robe", "Halfgloves", "Skirt", "Pumps"]),
        ("Casting", Armor, ["Hat", "Doublet Coat", "Cuffs", "Tights", "Thighboots"]),
        ("Slaying", Accessory, ["Earrings", "Choker", "Bracelet", "Ring"]),
    ];

    // Wallet currencies, first-seen order as the provider would list them.
    internal const uint SilverObol = 9001, GoldObol = 9002, Fixative = 9003, LunarResin = 9004, Aethersand = 9005;

    internal static readonly (uint Id, string Name)[] Currencies =
    [
        (SilverObol, "Silver Obol"),
        (GoldObol, "Gold Obol"),
        (Fixative, "Enlightenment Fixative"),
        (LunarResin, "Enlightenment Lunar Resin"),
        (Aethersand, "Phantom Aethersand Concentrate"),
    ];

    // An upgrade material that is not part of the wallet list and is never owned in the samples.
    private const uint ThreadId = 9100;

    private static uint ChainBase(int piece) => 10000u + (uint)piece * 100;
    internal static uint Item(int piece, int tier) => ChainBase(piece) + (uint)tier;
    private static uint Arcanaut(int piece) => ChainBase(piece) + 20;
    private static uint Lunar(int piece) => ChainBase(piece) + 30;

    internal static int PieceCount => Roles.Sum(r => r.Pieces.Length);

    internal static GearCollection Build(ShopGraph shops)
    {
        shops.Names.Clear();
        shops.Offers.Clear();
        foreach (var (id, name) in Currencies)
            shops.Names[id] = name;
        shops.Names[ThreadId] = "Voidmoon Thread";

        var collection = new GearCollection { Id = "phantom-vision", DisplayName = Display, SourceNote = Note };
        foreach (var (id, _) in Currencies)
            collection.Currencies.Add(id);

        var index = 0;
        foreach (var (role, slots, pieces) in Roles)
        {
            for (var i = 0; i < pieces.Length; i++, index++)
            {
                var accessory = slots == Accessory;
                var weight = accessory ? 1 : i is 1 or 3 ? 2 : 1;
                var chain = new PieceChain
                {
                    Role = role,
                    PieceName = pieces[i],
                    SlotName = slots[i],
                    EquipSlot = (uint)(3 + i),
                };
                for (var tier = 0; tier <= 3; tier++)
                {
                    var name = tier == 0 ? $"{Family} {pieces[i]} of {role}" : $"{Family} {pieces[i]} of {role} +{tier}";
                    shops.Names[Item(index, tier)] = name;
                    var node = new TierNode { Tier = tier, ItemId = Item(index, tier), Name = name };
                    if (tier == 0)
                    {
                        node.Paths.Add(Path("Expedition Antiquarian", 1, [Cost(SilverObol, 60u * (uint)weight)]));
                    }
                    else
                    {
                        var materials = new List<CostLine>();
                        switch (tier)
                        {
                            case 1:
                                materials.Add(Cost(GoldObol, 8u * (uint)weight));
                                materials.Add(Cost(Fixative, 2u * (uint)weight));
                                break;
                            case 2:
                                materials.Add(Cost(GoldObol, 16u * (uint)weight));
                                materials.Add(Cost(Fixative, 4u * (uint)weight));
                                materials.Add(Cost(LunarResin, 2u * (uint)weight));
                                break;
                            default:
                                materials.Add(Cost(GoldObol, 30u * (uint)weight));
                                materials.Add(Cost(Fixative, 8u * (uint)weight));
                                materials.Add(Cost(LunarResin, 6u * (uint)weight));
                                materials.Add(Cost(Aethersand, 1u));
                                break;
                        }
                        var costs = new List<CostLine> { new(Item(index, tier - 1), shops.Names[Item(index, tier - 1)], 1) };
                        costs.AddRange(materials);
                        var upgrade = Path("Expedition Armorer", 2, costs);
                        upgrade.UpgradeFromItemId = Item(index, tier - 1);
                        node.Paths.Add(upgrade);
                    }

                    if (tier == 1)
                    {
                        // Trade-up from an older family's piece, as the plugin's shortcut detection expects.
                        var arcanaut = Arcanaut(index);
                        shops.Names[arcanaut] = $"Arcanaut's {pieces[i]} of {role}";
                        var exchange = Path("Expedition Armorer", 2,
                            [new CostLine(arcanaut, shops.Names[arcanaut], 1), Cost(Fixative, 1)]);
                        exchange.ExchangeFromItemId = arcanaut;
                        node.Paths.Add(exchange);

                        var lunar = Lunar(index);
                        shops.Names[lunar] = $"Lunar {pieces[i]} of {role}";
                        shops.Offers[arcanaut] =
                        [
                            new ShopOffer(3, "Expedition Armorer", arcanaut, 1,
                                [new CostLine(lunar, shops.Names[lunar], 1), new CostLine(ThreadId, "Voidmoon Thread", 40)]),
                        ];
                    }
                    chain.Tiers.Add(node);
                }
                collection.Pieces.Add(chain);
            }
        }
        return collection;
    }

    internal static (uint ArcanautId, uint LunarId) TradeIds(int pieceIndex) => (Arcanaut(pieceIndex), Lunar(pieceIndex));

    internal static int IndexOf(string role, string slot)
    {
        var index = 0;
        foreach (var (name, slots, _) in Roles)
        {
            for (var i = 0; i < slots.Length; i++, index++)
                if (name == role && slots[i] == slot)
                    return index;
        }
        throw new InvalidOperationException($"{role}/{slot} is not part of the sample data.");
    }

    internal static IEnumerable<(string Role, string Slot, int Index)> AllPieces()
    {
        var index = 0;
        foreach (var (role, slots, _) in Roles)
            for (var i = 0; i < slots.Length; i++, index++)
                yield return (role, slots[i], index);
    }

    private static CostLine Cost(uint id, uint quantity) =>
        new(id, Currencies.First(c => c.Id == id).Name, quantity);

    private static AcquisitionPath Path(string shop, uint shopId, IEnumerable<CostLine> costs) =>
        new() { ShopId = shopId, ShopName = shop, Costs = costs.ToList() };
}
