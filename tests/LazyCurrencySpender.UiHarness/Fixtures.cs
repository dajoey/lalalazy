// Deterministic fixture data for the render harness. Currency ids and ranges mirror the plugin's own
// TrackedCurrency.GenerateCurrencyList; names, shops and zones are hand-written representative game strings
// (the plugin reads them from game data sheets at runtime, which are not available here).
using CurrencySpender;
using CurrencySpender.Classes;
using CurrencySpender.Data;
using CurrencySpender.Helpers;
using Lala.UiRender;

namespace LazyCurrencySpender.UiHarness;

internal static class Fixtures
{
    internal static readonly List<AtlasTexture> IconTextures = [];
    private static readonly Dictionary<uint, string> ItemNames = new();

    // id, name, max, current balance, child?, children
    private sealed record Cur(uint Id, string Name, int Max, int Count, bool Child = false, uint[]? Children = null,
        CurrencyType Type = CurrencyType.Item, bool Enabled = true);

    private static readonly Cur[] Currencies =
    [
        new(20, "Storm Seal", 90000, 43120),
        new(21, "Serpent Seal", 90000, 12480),
        new(22, "Flame Seal", 90000, 81200),
        new(29, "MGP", 9999999, 1203450),
        new(28, "Allagan Tomestone of Poetics", 2000, 1742),
        new(47, "Allagan Tomestone of Mnemonics", 2000, 640, Type: CurrencyType.NonLimitedTomestone),
        new(48, "Allagan Tomestone of Heliometry", 2000, 0, Type: CurrencyType.LimitedTomestone, Enabled: false),
        new(25, "Wolf Mark", 20000, 6412),
        new(36656, "Trophy Crystal", 20000, 1150),
        new(27, "Allied Seal", 4000, 2310),
        new(10307, "Centurio Seal", 4000, 3480, Children: [13625, 20308, 21103]),
        new(13625, "Centurio Clan Mark", 0, 0, Child: true),
        new(20308, "Veteran's Clan Mark", 0, 0, Child: true),
        new(21103, "Mythic Clan Mark", 0, 0, Child: true),
        new(26533, "Sack of Nuts", 4000, 0),
        new(30341, "Faux Leaf", 1000, 612),
        new(21172, "Achievement Certificate", 999, 120),
        new(26807, "Bicolor Gemstone", 1500, 1386, Children: [43961, 35833]),
        new(43961, "Turali Gemstone Voucher", 0, 4, Child: true),
        new(35833, "Gemstone Voucher", 0, 7, Child: true),
        new(33913, "Purple Crafters' Scrip", 4000, 2890, Children: [12839]),
        new(12839, "Crafters' Scrip Voucher", 0, 0, Child: true),
        new(41784, "Orange Crafters' Scrip", 4000, 1530),
        new(33914, "Purple Gatherers' Scrip", 4000, 3720),
        new(41785, "Orange Gatherers' Scrip", 4000, 410),
        new(28063, "Skybuilders' Scrip", 10000, 7590),
        new(37549, "Seafarer's Cowrie", 9999999, 15840),
        new(37550, "Islander's Cowrie", 9999999, 9210),
        new(45690, "Cosmocredit", 30000, 21450),
        new(45691, "Lunar Credit", 10000, 8460),
        new(48146, "Phaenna Credit", 10000, 2750),
        new(48147, "Oizys Credit", 10000, 0),
        new(48148, "Auxesia Credit", 10000, 130),
        new(21076, "Steel Amalj'ok", 999, 214),
        new(21075, "Sylphic Goldleaf", 999, 905),
        new(21078, "Titan Cobaltpiece", 999, 87),
        new(22525, "Namazu Koban", 999, 640),
        new(38952, "Loporrit Carat", 999, 330),
        new(44472, "Pelu Pelplume", 999, 41),
        new(48084, "Mamool Ja Nanook", 999, 12),
        new(46178, "Yok Huy Ward", 999, 18),
    ];

    internal static List<TrackedCurrency> BuildCurrencies()
    {
        EnsureIcons();
        var list = new List<TrackedCurrency>();
        for (var i = 0; i < Currencies.Length; i++)
        {
            var c = Currencies[i];
            ItemNames[c.Id] = c.Name;
            list.Add(new TrackedCurrency
            {
                Type = c.Type,
                ItemId = c.Id,
                Name = c.Name,
                MaxCount = c.Max,
                CurrentCount = c.Count,
                Child = c.Child,
                Enabled = c.Enabled,
                Children = c.Children?.ToList(),
                Icon = new HarnessIcon { Handle = new Dalamud.Bindings.ImGui.ImTextureID(new IntPtr(2000 + i)) },
            });
        }
        return list;
    }

    internal static IReadOnlyList<uint> Selected(params uint[] ids) => ids;

    // Procedural placeholder icons (the real ones are game textures): a ringed coin in a per-currency hue.
    private static void EnsureIcons()
    {
        if (IconTextures.Count > 0)
            return;
        for (var i = 0; i < Currencies.Length; i++)
        {
            const int size = 40;
            var rgba = new byte[size * size * 4];
            var hue = (i * 47) % 360;
            var (r, g, b) = Hsv(hue, 0.55f, 0.95f);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = x - 19.5f; var dy = y - 19.5f;
                    var d = MathF.Sqrt((dx * dx) + (dy * dy));
                    byte pr = 0, pg = 0, pb = 0, pa = 0;
                    if (d <= 19) { pr = (byte)(r * 0.55f); pg = (byte)(g * 0.55f); pb = (byte)(b * 0.55f); pa = 255; }
                    if (d <= 16) { pr = r; pg = g; pb = b; pa = 255; }
                    if (d <= 8) { pr = (byte)(r * 0.7f); pg = (byte)(g * 0.7f); pb = (byte)(b * 0.7f); pa = 255; }
                    var o = ((y * size) + x) * 4;
                    rgba[o] = pr; rgba[o + 1] = pg; rgba[o + 2] = pb; rgba[o + 3] = pa;
                }
            IconTextures.Add(new AtlasTexture(new IntPtr(2000 + i), rgba, size, size));
        }
    }

    private static (byte, byte, byte) Hsv(float h, float s, float v)
    {
        var c = v * s; var x = c * (1 - MathF.Abs((h / 60 % 2) - 1)); var m = v - c;
        (float r, float g, float b) = (int)(h / 60) switch
        {
            0 => (c, x, 0f), 1 => (x, c, 0f), 2 => (0f, c, x), 3 => (0f, x, c), 4 => (x, 0f, c), _ => (c, 0f, x),
        };
        return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private static Shop MakeShop(uint shopId, uint npcId, string npc, string zone, ShopType type = ShopType.SpecialShop, bool noLocation = false)
        => new()
        {
            Type = type, ShopId = shopId, NpcId = npcId, NpcName = npc, ShopName = npc,
            Location = new Location { NpcId = npcId, Zone = zone, TerritoryId = 128 },
        };

    private static ShopItem Add(uint currency, uint id, string name, uint price, ItemType type, CollectableType ctype,
        Shop shop, uint sales = 0, uint marketPrice = 0)
    {
        ItemNames[id] = name;
        var item = new ShopItem
        {
            Id = id, Name = name, Price = price, Currency = currency, Type = type, CollectableType = ctype,
            Shop = shop, ShopId = shop.ShopId, CurrentPrice = marketPrice, HasSoldWeek = sales,
        };
        Generator.items.Add(item);
        return item;
    }

    internal static void BuildItems()
    {
        Generator.items.Clear();
        Generator.shops.Clear();
        ItemHelper.Unlocked.Clear();
        var gcFlame = MakeShop(1, 1001379, "Flame Quartermaster", "Ul'dah - Steps of Nald", ShopType.GCShop);
        var gcStorm = MakeShop(2, 1009552, "Storm Quartermaster", "Limsa Lominsa Lower Decks", ShopType.GCShop);
        var saucer = MakeShop(3, 1011039, "Gold Saucer Attendant", "The Gold Saucer");
        var tt = MakeShop(4, 1010478, "Triple Triad Trader", "The Gold Saucer");
        var poetics = MakeShop(5, 1003633, "Scrip Exchange", "Limsa Lominsa Lower Decks");
        var gem = MakeShop(6, 1049079, "Zircon", "Solution Nine");
        var gemOld = MakeShop(7, 1032254, "Gemstone Trader", "Rhalgr's Reach");
        var scrips = MakeShop(8, 1045069, "Scrip Exchange", "Old Sharlayan");
        var cowrie = MakeShop(9, 1052588, "Mesouaidonque", "Tuliyollal");
        var credits = MakeShop(10, 1052612, "Orbitingway Vendor", "Orbitingway");

        // Flame Seals: ventures, collectables, general gear.
        Add(22, 22001, "Venture", 200, ItemType.Venture, CollectableType.None, gcFlame);
        Add(22, 22002, "Flame Elite Barding", 8000, ItemType.Collectable, CollectableType.Barding, gcFlame);
        Add(22, 22003, "Flame Private Barding", 2500, ItemType.Collectable, CollectableType.Barding, gcFlame);
        Add(22, 22004, "Wind-up Warrior of Light", 12000, ItemType.Collectable, CollectableType.Minion, gcFlame);
        Add(22, 22005, "Ink Black Facewear", 6000, ItemType.Collectable, CollectableType.Facewear, gcFlame);
        Add(22, 22006, "Company Chocobo Whistle", 20000, ItemType.Collectable, CollectableType.Mount, gcFlame);
        Add(22, 22007, "Grade 7 Dark Matter", 4000, ItemType.None, CollectableType.None, gcFlame);
        Add(22, 22008, "Replica Lakeland Ichor Cleaver", 1500, ItemType.None, CollectableType.None, gcFlame);
        Add(22, 22009, "Rarefied Chimeric Felt Hat of Fending", 17500, ItemType.None, CollectableType.None, gcFlame);
        Add(22, 22010, "Flame Sergeant's Hallway Robe of Striking", 3750, ItemType.None, CollectableType.None, gcFlame);
        Add(22, 22011, "Storm Corporal's Initiate Ring", 1200, ItemType.None, CollectableType.None, gcStorm);
        // The Flame collectables above stay locked; one is already owned (hidden from the missing list).
        ItemHelper.Unlocked.Add(22003);

        // MGP.
        Add(29, 29001, "Cloud Mallow", 15000, ItemType.Collectable, CollectableType.Minion, saucer);
        Add(29, 29002, "Wind-up Cursor", 300000, ItemType.Collectable, CollectableType.Minion, saucer);
        Add(29, 29003, "Magic Carpet", 3000000, ItemType.Collectable, CollectableType.Mount, saucer);
        Add(29, 29004, "Triple Triad Card Pack", 1000, ItemType.None, CollectableType.None, tt);
        Add(29, 29005, "Gold Saucer Cactpot Ticket", 6000, ItemType.None, CollectableType.None, saucer);

        // Poetics: market-board sellables, collectables, items of interest.
        Add(28, 28001, "Rarefied Moonbeam Silk Hat of Casting", 350, ItemType.Tradeable, CollectableType.None, poetics, 14, 128000);
        Add(28, 28002, "Augmented Radiant's Ring of Casting", 375, ItemType.Tradeable, CollectableType.None, poetics, 3, 41000);
        Add(28, 28003, "Vitality Materia XII", 500, ItemType.Tradeable, CollectableType.None, poetics, 33, 22500);
        Add(28, 28004, "Cordial Tincture of Strength", 100, ItemType.Tradeable, CollectableType.None, poetics, 0, 0);
        Add(28, 28005, "Metal Weapon Drawing", 1000, ItemType.Collectable, CollectableType.FramersKit, poetics);
        Add(28, 28006, "Orchestrion Roll: Torn from the Heavens", 1000, ItemType.Collectable, CollectableType.Scroll, poetics);
        Add(28, 28007, "Pagos Gloves of Fending", 550, ItemType.None, CollectableType.None, poetics);

        // Wolf Marks.
        Add(25, 25001, "Wolf's Pack Mount Whistle", 15000, ItemType.Collectable, CollectableType.Mount, MakeShop(11, 1005244, "Wolf Quartermaster", "Wolves' Den Pier"));
        Add(25, 25002, "Frontline Thaumaturge's Cap", 4000, ItemType.None, CollectableType.None, Generator.items[^1].Shop);

        // Bicolor Gemstones, directly and through vouchers (child currencies).
        Add(26807, 43961, "Turali Gemstone Voucher", 100, ItemType.None, CollectableType.None, gem);
        Add(26807, 35833, "Gemstone Voucher", 100, ItemType.None, CollectableType.None, gemOld);
        Add(26807, 26001, "Gemdraught of Dexterity", 100, ItemType.Tradeable, CollectableType.None, gem, 61, 3400);
        Add(26807, 26002, "Rarefied Grade 4 Gemsap of Vitality", 50, ItemType.Tradeable, CollectableType.None, gem, 22, 8200);
        Add(26807, 26003, "Wind-up Garuda", 500, ItemType.Collectable, CollectableType.Minion, gem);
        Add(26807, 26004, "Mirror of the Gilded Aetherial Lotus", 3000, ItemType.Collectable, CollectableType.Mount, gem);
        Add(35833, 26005, "Wind-up Raubahn", 3, ItemType.Collectable, CollectableType.Minion, gemOld);
        Add(43961, 26006, "Archaeopteryx Barding", 3, ItemType.Collectable, CollectableType.Barding, gem);
        Add(26807, 26007, "Augmented Credendum Gloves of Aiming", 4000, ItemType.None, CollectableType.None, gem);
        Add(26807, 26008, "Rarefied Grade 4 Gemdraught of Intelligence", 800, ItemType.None, CollectableType.None, gem);

        // Scrips.
        Add(33913, 33001, "Rarefied Gemdraught of Intelligence", 250, ItemType.Tradeable, CollectableType.None, scrips, 12, 9600);
        Add(33913, 33002, "Master Recipes Tome", 450, ItemType.Collectable, CollectableType.MasterRecipes, scrips);

        // Cowries, credits.
        Add(37549, 37001, "Seafarer's Relic Triangle", 1200, ItemType.None, CollectableType.None, cowrie);
        Add(45691, 45001, "Lunar Wrap-up Fabric", 600, ItemType.None, CollectableType.None, credits);
        Add(48146, 48001, "Orbital Hatchling", 1500, ItemType.Collectable, CollectableType.Minion, credits);

        Generator.shops.AddRange(new[] { gcFlame, gcStorm, saucer, tt, poetics, gem, gemOld, scrips, cowrie, credits });
        CurrencySpender.HarnessSheet<Lumina.Excel.Sheets.Item>.ItemNames = id => ItemNames.TryGetValue(id, out var n) ? n : $"Item {id}";
    }
}
