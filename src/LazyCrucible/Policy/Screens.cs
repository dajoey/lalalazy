using System.Globalization;
using System.Text.RegularExpressions;

namespace LazyCrucible;

/// <summary> One AtkValue as the selection code sees it: type letter (b i u f s, '\0' = undefined), number, text. </summary>
public readonly record struct AtkCell(char Type, long Number, string? Text)
{
    public bool IsDefined => Type != '\0';
    public int Int => (int)Number;
    public bool Bool => Number != 0;

    /// <summary> Number, or the first number in the text ("1,481" → 1481, "30 (-50%)" → 30). </summary>
    public int AsNumber => Type == 's' ? Screens.FirstNumber(Text) : (int)Number;

    public static AtkCell Parse(string type, string value) => type switch
    {
        "b" => new('b', value is "1" or "true" or "True" ? 1 : 0, null),
        "i" => new('i', long.Parse(value, CultureInfo.InvariantCulture), null),
        "u" => new('u', long.Parse(value, CultureInfo.InvariantCulture), null),
        "f" => new('f', (long)double.Parse(value, CultureInfo.InvariantCulture), null),
        "s" => new('s', 0, value),
        _ => default,
    };
}

/// <summary> An addon's AtkValues by index (missing = undefined). </summary>
public sealed class AtkCells(IReadOnlyDictionary<int, AtkCell> cells, int count)
{
    public int Count { get; } = count;
    public AtkCell this[int index] => cells.TryGetValue(index, out var c) ? c : default;

    /// <summary> From a fixture's <c>{"idx": ["type", "value"]}</c> map. </summary>
    public static AtkCells FromFixture(IReadOnlyDictionary<string, string[]> values, int count) =>
        new(values.ToDictionary(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture), kv => AtkCell.Parse(kv.Value[0], kv.Value[1])), count);
}

/// <summary> The shop as its AtkValues list it. </summary>
public sealed record ShopScreen(int Tokens, IReadOnlyList<ShopOffer> Stock, Inventory Inventory);

/// <summary> The treasure coffer: up to four choices (choice index, XBMItem row) and what is carried. </summary>
public sealed record TreasureScreen(IReadOnlyList<(int Index, int Row)> Choices, Inventory Inventory);

/// <summary> The spoils screen: loot (index, row, taken) and what is carried. </summary>
public sealed record BootyScreen(int Tokens, int TokensOffered, IReadOnlyList<(int Index, int Row, bool Taken)> Loot, Inventory Inventory);

/// <summary> The familiar screen (XBMPetParty) in feed or campsite mode. </summary>
public sealed record PetPartyScreen(int Mode, int SubMode, IReadOnlyList<FamiliarState> Familiars);

/// <summary>
///     Readers for the selection screens' AtkValues. PURE. Layouts verified against recorded snapshots (2026-09-17..21,
///     GluttonyCombo XB| collector) and the replay fixtures in tests/LazyCrucible.Harness/Fixtures:
///     <list type="bullet">
///         <item>XBMContentsItemShop (n=269): [1] tokens text, [2] stock count; stock k at 3+5k: +0 listed, +1 XBMItem row,
///             +2 price text ("30 (-50%)" when discounted), +3 discount flag, +4 bought flag. Carried items 154+5s, gear
///             205+5s (item row at +3; 0 = empty slot).</item>
///         <item>XBMContentsTreasure (n=144): choice k present 3+5k, row 6+5k; carried items 24+5s, gear 75+5s.</item>
///         <item>XBMContentsBooty (n=147): [2] tokens, [4] tokens offered; loot k present 6+5k, row 9+5k, taken 129+k;
///             carried items 27+5s, gear 78+5s.</item>
///         <item>XBMPetParty (n=1188): [2] mode (3 feed, 4 campsite), [3] submode, [5] familiar count; familiar k block
///             B=6+77k: +1 242000+XBMPet row, +2 cannot eat the offered feed, +5/+6 current/max HP, +7..+16 feeds eaten as
///             (icon, row) pairs, +72/+73 satiety used/max, +75 selected to rest, +76 XBMPet row.</item>
///     </list>
/// </summary>
internal static class Screens
{
    public const int ShopStockStart = 3, ShopStride = 5, ShopItemsStart = 154, ShopGearStart = 205;
    public const int TreasureChoiceStart = 3, TreasureItemsStart = 24, TreasureGearStart = 75;
    public const int BootyLootStart = 6, BootyTakenStart = 129, BootyItemsStart = 27, BootyGearStart = 78;
    public const int PetBlockStart = 6, PetBlockSize = 77;
    public const int SlotRow = 3;

    private static readonly Regex Number = new("[0-9][0-9,]*", RegexOptions.Compiled);

    /// <summary> SeString payloads (0x02 type length data 0x03), e.g. the colour an unaffordable price is wrapped in. </summary>
    private static readonly Regex Payload = new("\u0002[^\u0003]*\u0003", RegexOptions.Compiled);

    public static int FirstNumber(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        var m = Number.Match(Payload.Replace(text, ""));
        return m.Success && int.TryParse(m.Value.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary> Ten 5-wide carried slots from <paramref name="start"/>, row at +3 (0 = empty). </summary>
    private static List<int> Slots(AtkCells v, int start)
    {
        var rows = new List<int>(10);
        for (var s = 0; s < 10; s++)
        {
            var row = v[start + s * 5 + SlotRow].Int;
            if (row > 0)
                rows.Add(row);
        }
        return rows;
    }

    public static ShopScreen ReadShop(AtkCells v)
    {
        var count = Math.Clamp(v[2].Int, 0, 32);
        var stock = new List<ShopOffer>(count);
        for (var k = 0; k < count; k++)
        {
            var at = ShopStockStart + k * ShopStride;
            if (!v[at].Bool)
                continue;
            var row = v[at + 1].Int;
            if (row <= 0)
                continue;
            stock.Add(new ShopOffer(k, row, v[at + 2].AsNumber, v[at + 4].Bool, v[at + 3].Bool));
        }
        return new ShopScreen(v[1].AsNumber, stock, new Inventory(Slots(v, ShopItemsStart), Slots(v, ShopGearStart)));
    }

    public static TreasureScreen ReadTreasure(AtkCells v)
    {
        var choices = new List<(int, int)>(4);
        for (var k = 0; k < 4; k++)
        {
            if (!v[TreasureChoiceStart + 5 * k].Bool)
                continue;
            var row = v[TreasureChoiceStart + 5 * k + 3].Int;
            if (row > 0)
                choices.Add((k, row));
        }
        return new TreasureScreen(choices, new Inventory(Slots(v, TreasureItemsStart), Slots(v, TreasureGearStart)));
    }

    public static BootyScreen ReadBooty(AtkCells v)
    {
        var loot = new List<(int, int, bool)>(4);
        for (var k = 0; k < 4; k++)
        {
            if (!v[BootyLootStart + 5 * k].Bool)
                continue;
            var row = v[BootyLootStart + 5 * k + 3].Int;
            if (row > 0)
                loot.Add((k, row, v[BootyTakenStart + k].Bool));
        }
        return new BootyScreen(v[2].AsNumber, v[4].AsNumber, loot, new Inventory(Slots(v, BootyItemsStart), Slots(v, BootyGearStart)));
    }

    public static PetPartyScreen ReadPetParty(AtkCells v)
    {
        var count = Math.Clamp(v[5].Int, 0, 15);
        var list = new List<FamiliarState>(count);
        for (var k = 0; k < count; k++)
        {
            var b = PetBlockStart + k * PetBlockSize;
            var row = v[b + 76].Int;
            if (row <= 0)
                row = v[b + 1].Int - 242000;
            if (row <= 0)
                continue;
            var cur = v[b + 5].Int;
            var max = v[b + 6].Int;
            var hp = !v[b + 6].IsDefined ? 100 // not shown: unknown, treated as healthy (never as knocked out)
                : max <= 0 || cur <= 0 ? 0
                : Math.Clamp((int)Math.Round(100.0 * cur / max), 1, 100);
            var satietyMax = v[b + 73].IsDefined ? v[b + 73].Int
                : row < CrucibleItems.SatietyMax.Length ? CrucibleItems.SatietyMax[row] : 0;
            var eaten = new List<int>(5);
            for (var p = 0; p < 5; p++)
            {
                var feed = v[b + 8 + 2 * p].Int;
                if (feed > 0)
                    eaten.Add(feed);
            }
            var cannot = v[b + 2].IsDefined ? v[b + 2].Bool : (bool?)null;
            list.Add(new FamiliarState(row, k, hp, v[b + 72].Int, satietyMax, eaten, cannot, v[b + 75].Bool));
        }
        return new PetPartyScreen(v[2].IsDefined ? v[2].Int : -1, v[3].IsDefined ? v[3].Int : -1, list);
    }
}
