using System.Collections.Generic;
using System.Numerics;

namespace LazySightseeing;

public class SightInfo
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint TerritoryType { get; set; }
    public string Aetheryte { get; set; } = string.Empty;
    public Vector3 Position { get; set; }
    public string Emote { get; set; } = string.Empty;
    public string TimeWindow { get; set; } = string.Empty;
    public List<string> Weathers { get; set; } = [];
}

public static class SightseeingDatabase
{
    public static readonly List<SightInfo> Sights = new()
    {
        new SightInfo
        {
            Id = 1,
            Name = "001: Barracuda Piers",
            TerritoryType = 128,
            Aetheryte = "Limsa Lominsa",
            Position = new Vector3(-83.4304f, 42.3934f, -171.201f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 2,
            Name = "002: The Astalicia",
            TerritoryType = 129,
            Aetheryte = "Limsa Lominsa",
            Position = new Vector3(-209.079f, 24.4977f, 194.536f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 3,
            Name = "003: Seasong Grotto",
            TerritoryType = 134,
            Aetheryte = "Middle La Noscea",
            Position = new Vector3(-59.0412f, 27.2827f, -118.42f),
            Emote = "pray",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Rain" }
        },
        new SightInfo
        {
            Id = 4,
            Name = "004: The Skylift",
            TerritoryType = 134,
            Aetheryte = "Middle La Noscea",
            Position = new Vector3(-269.647f, 29.38f, -206.136f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 5,
            Name = "005: La Thagran Eastroad",
            TerritoryType = 134,
            Aetheryte = "Middle La Noscea",
            Position = new Vector3(194.549f, 73.7878f, 302.671f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Clouds" }
        },
        new SightInfo
        {
            Id = 6,
            Name = "006: The Salt Strand",
            TerritoryType = 135,
            Aetheryte = "Lower La Noscea",
            Position = new Vector3(80.7812f, 61.0769f, 932.08f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 7,
            Name = "007: Red Rooster Stead",
            TerritoryType = 135,
            Aetheryte = "Lower La Noscea",
            Position = new Vector3(597.168f, 73.9819f, -111.752f),
            Emote = "lookout",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Fog" }
        },
        new SightInfo
        {
            Id = 8,
            Name = "008: The Brewer's Beacon",
            TerritoryType = 138,
            Aetheryte = "Western La Noscea",
            Position = new Vector3(424.973f, 15.0224f, 464.633f),
            Emote = "lookout",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 9,
            Name = "009: The Leatherworkers' Guild",
            TerritoryType = 133,
            Aetheryte = "Gridania",
            Position = new Vector3(81.5907f, 12.7001f, -167.375f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Clouds" }
        },
        new SightInfo
        {
            Id = 10,
            Name = "010: Apkallu Falls",
            TerritoryType = 133,
            Aetheryte = "Gridania",
            Position = new Vector3(-21.067f, 16.2384f, -240.544f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 11,
            Name = "011: Bentbranch Meadows",
            TerritoryType = 148,
            Aetheryte = "Central Shroud",
            Position = new Vector3(16.7607f, -0.550099f, 20.893f),
            Emote = "lounge",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 12,
            Name = "012: The Sanctum of the Twelve",
            TerritoryType = 152,
            Aetheryte = "East Shroud",
            Position = new Vector3(-190.631f, 58.9523f, -162.709f),
            Emote = "pray",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 13,
            Name = "013: Little Solace",
            TerritoryType = 152,
            Aetheryte = "East Shroud",
            Position = new Vector3(41.3789f, 11.7149f, 239.872f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 14,
            Name = "014: Royal Promenade",
            TerritoryType = 131,
            Aetheryte = "Ul'dah",
            Position = new Vector3(-4.83453f, 30.0499f, 18.1656f),
            Emote = "salute",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 15,
            Name = "015: The Gold Court",
            TerritoryType = 131,
            Aetheryte = "Ul'dah",
            Position = new Vector3(15.1989f, 19.7971f, -0.00536507f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Clouds" }
        },
        new SightInfo
        {
            Id = 16,
            Name = "016: The Jewel of the Desert",
            TerritoryType = 140,
            Aetheryte = "Western Thanalan",
            Position = new Vector3(44.8042f, 60.8472f, 43.5448f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 17,
            Name = "017: The Ruins of Sil'dih",
            TerritoryType = 141,
            Aetheryte = "Central Thanalan",
            Position = new Vector3(-275.645f, -14.9422f, 74.5446f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Fog" }
        },
        new SightInfo
        {
            Id = 18,
            Name = "018: The Lonely Giant",
            TerritoryType = 145,
            Aetheryte = "Eastern Thanalan",
            Position = new Vector3(-96.5187f, -56.442f, 161.164f),
            Emote = "comfort",
            TimeWindow = "17:00-18:00",
            Weathers = new List<string> { "Rain" }
        },
        new SightInfo
        {
            Id = 19,
            Name = "019: The Invisible City",
            TerritoryType = 145,
            Aetheryte = "Eastern Thanalan",
            Position = new Vector3(-357.332f, -3.45619f, -147.22f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Clouds" }
        },
        new SightInfo
        {
            Id = 20,
            Name = "020: Highbridge",
            TerritoryType = 145,
            Aetheryte = "Eastern Thanalan",
            Position = new Vector3(-21.8034f, -31f, -32.9587f),
            Emote = "pray",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 21,
            Name = "021: Woad Whisper Canyon",
            TerritoryType = 134,
            Aetheryte = "Middle La Noscea",
            Position = new Vector3(-72.2032f, 11.9733f, -416.303f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 22,
            Name = "022: Summerford Farms",
            TerritoryType = 134,
            Aetheryte = "Middle La Noscea",
            Position = new Vector3(213.045f, 117.651f, -222.447f),
            Emote = "lookout",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 23,
            Name = "023: The Grey Fleet",
            TerritoryType = 135,
            Aetheryte = "Lower La Noscea",
            Position = new Vector3(503.149f, 106.738f, -434.714f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Rain" }
        },
        new SightInfo
        {
            Id = 24,
            Name = "024: Hidden Falls",
            TerritoryType = 137,
            Aetheryte = "Eastern La Noscea",
            Position = new Vector3(557.435f, 15.8653f, 102.053f),
            Emote = "groundsit",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 25,
            Name = "025: Gullperch Tower",
            TerritoryType = 137,
            Aetheryte = "Eastern La Noscea",
            Position = new Vector3(406.93f, 79.126f, 617.192f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Rain" }
        },
        new SightInfo
        {
            Id = 26,
            Name = "026: The Navigator",
            TerritoryType = 138,
            Aetheryte = "Western La Noscea",
            Position = new Vector3(274.611f, -25f, 257.307f),
            Emote = "pray",
            TimeWindow = "17:00-18:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 27,
            Name = "027: The Ship Graveyard",
            TerritoryType = 138,
            Aetheryte = "Western La Noscea",
            Position = new Vector3(-215.829f, -40.6253f, 740.102f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Gales" }
        },
        new SightInfo
        {
            Id = 28,
            Name = "028: Camp Skull Valley",
            TerritoryType = 138,
            Aetheryte = "Western La Noscea",
            Position = new Vector3(67.4838f, 1.95755f, 47.7749f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 29,
            Name = "029: Tidegate",
            TerritoryType = 138,
            Aetheryte = "Western La Noscea",
            Position = new Vector3(-103.038f, -14.0954f, 81.1342f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 30,
            Name = "030: Camp Bronze Lake",
            TerritoryType = 139,
            Aetheryte = "Upper La Noscea",
            Position = new Vector3(468.962f, 27.7825f, 49.69f),
            Emote = "lookout",
            TimeWindow = "17:00-18:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 31,
            Name = "031: Thalaos",
            TerritoryType = 139,
            Aetheryte = "Upper La Noscea",
            Position = new Vector3(-428.468f, 70.6805f, 28.4012f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 32,
            Name = "032: Jijiroon's Trading Post",
            TerritoryType = 139,
            Aetheryte = "Upper La Noscea",
            Position = new Vector3(381.605f, 5.17995f, 198.848f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Thunderstorms" }
        },
        new SightInfo
        {
            Id = 33,
            Name = "033: The Floating City of Nym",
            TerritoryType = 180,
            Aetheryte = "Outer La Noscea",
            Position = new Vector3(-440.191f, 50.8393f, -319.955f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 34,
            Name = "034: Camp Overlook",
            TerritoryType = 180,
            Aetheryte = "Outer La Noscea",
            Position = new Vector3(-218.508f, 75.0642f, -254.042f),
            Emote = "lookout",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Clouds" }
        },
        new SightInfo
        {
            Id = 35,
            Name = "035: U'Ghamaro Mines",
            TerritoryType = 180,
            Aetheryte = "Outer La Noscea",
            Position = new Vector3(96.696f, 70.2372f, -486.023f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 36,
            Name = "036: The Hermit's Hovel",
            TerritoryType = 180,
            Aetheryte = "Outer La Noscea",
            Position = new Vector3(-302.948f, 10.2696f, -570.584f),
            Emote = "lounge",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Rain" }
        },
        new SightInfo
        {
            Id = 37,
            Name = "037: The Carline Canopy",
            TerritoryType = 132,
            Aetheryte = "Gridania",
            Position = new Vector3(150.648f, -9.49056f, 154.847f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 38,
            Name = "038: The Lancers' Guild",
            TerritoryType = 133,
            Aetheryte = "Gridania",
            Position = new Vector3(153.003f, 17.8214f, -264.589f),
            Emote = "lookout",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Rain" }
        },
        new SightInfo
        {
            Id = 39,
            Name = "039: The Bannock",
            TerritoryType = 148,
            Aetheryte = "Central Shroud",
            Position = new Vector3(97.9296f, 3.56932f, -75.1908f),
            Emote = "lookout",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Rain" }
        },
        new SightInfo
        {
            Id = 40,
            Name = "040: Haukke Manor",
            TerritoryType = 148,
            Aetheryte = "Central Shroud",
            Position = new Vector3(-392.178f, 63.9114f, 82.0779f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 41,
            Name = "041: The Guardian Tree",
            TerritoryType = 148,
            Aetheryte = "Central Shroud",
            Position = new Vector3(-254.943f, 55.3501f, 44.3531f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 42,
            Name = "042: Rainbow Bridge",
            TerritoryType = 148,
            Aetheryte = "Central Shroud",
            Position = new Vector3(252.006f, -6.96364f, -130.171f),
            Emote = "lookout",
            TimeWindow = "11:00-14:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 43,
            Name = "043: The Seedbed",
            TerritoryType = 152,
            Aetheryte = "East Shroud",
            Position = new Vector3(-25.5261f, -35.2182f, -538.839f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Thunder" }
        },
        new SightInfo
        {
            Id = 44,
            Name = "044: Buscarron's Druthers",
            TerritoryType = 153,
            Aetheryte = "South Shroud",
            Position = new Vector3(-182.434f, 14.8842f, -67.8689f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Thunderstorms" }
        },
        new SightInfo
        {
            Id = 45,
            Name = "045: South Shroud Landing",
            TerritoryType = 153,
            Aetheryte = "South Shroud",
            Position = new Vector3(-338.277f, 21.1198f, 622.259f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 46,
            Name = "046: Urth's Gift",
            TerritoryType = 153,
            Aetheryte = "South Shroud",
            Position = new Vector3(588.012f, 23.8626f, 124.749f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Fog" }
        },
        new SightInfo
        {
            Id = 47,
            Name = "047: Quarrymill",
            TerritoryType = 153,
            Aetheryte = "South Shroud",
            Position = new Vector3(196.65f, 17.1241f, -17.4916f),
            Emote = "lookout",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 48,
            Name = "048: Ixali Logging Grounds",
            TerritoryType = 154,
            Aetheryte = "North Shroud",
            Position = new Vector3(-151.736f, -4.4811f, -94.5486f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 49,
            Name = "049: Fallen Neurolink",
            TerritoryType = 154,
            Aetheryte = "North Shroud",
            Position = new Vector3(-275.055f, -77.6295f, 526.373f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 50,
            Name = "050: Alder Springs",
            TerritoryType = 154,
            Aetheryte = "North Shroud",
            Position = new Vector3(-300.775f, -32.5675f, 293.477f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Clouds" }
        },
        new SightInfo
        {
            Id = 51,
            Name = "051: Castrum Marinum",
            TerritoryType = 140,
            Aetheryte = "Western Thanalan",
            Position = new Vector3(-636.701f, 65.7551f, -812.003f),
            Emote = "lookout",
            TimeWindow = "17:00-18:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 52,
            Name = "052: Vesper Bay",
            TerritoryType = 140,
            Aetheryte = "Western Thanalan",
            Position = new Vector3(-451.674f, 32.6f, -330.641f),
            Emote = "point",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 53,
            Name = "053: Black Brush Station",
            TerritoryType = 141,
            Aetheryte = "Central Thanalan",
            Position = new Vector3(-3.69171f, 8.57371f, -194.9f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Dust Storms" }
        },
        new SightInfo
        {
            Id = 54,
            Name = "054: Gate of Nald",
            TerritoryType = 141,
            Aetheryte = "Central Thanalan",
            Position = new Vector3(-146.756f, 7.9358f, 235.584f),
            Emote = "groundsit",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 55,
            Name = "055: The Burning Wall",
            TerritoryType = 145,
            Aetheryte = "Eastern Thanalan",
            Position = new Vector3(465.308f, -57.7803f, 252.109f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 56,
            Name = "056: The Golden Bazaar",
            TerritoryType = 145,
            Aetheryte = "Eastern Thanalan",
            Position = new Vector3(-572.522f, 12.8983f, -238.74f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 57,
            Name = "057: Thal's Respite",
            TerritoryType = 145,
            Aetheryte = "Eastern Thanalan",
            Position = new Vector3(183.443f, 3.431f, -339.986f),
            Emote = "pray",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Showers" }
        },
        new SightInfo
        {
            Id = 58,
            Name = "058: Nald's Reflection",
            TerritoryType = 146,
            Aetheryte = "Southern Thanalan",
            Position = new Vector3(-463.209f, -2.6253f, 69.3779f),
            Emote = "pray",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Fog" }
        },
        new SightInfo
        {
            Id = 59,
            Name = "059: Zahar'ak",
            TerritoryType = 146,
            Aetheryte = "Southern Thanalan",
            Position = new Vector3(-108.376f, 8.0871f, -44.5041f),
            Emote = "lookout",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 60,
            Name = "060: The Sagolii Desert",
            TerritoryType = 146,
            Aetheryte = "Southern Thanalan",
            Position = new Vector3(-7.4631f, 13.2381f, 858.614f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Heat Waves" }
        },
        new SightInfo
        {
            Id = 61,
            Name = "061: The Sunken Temple of Qarn",
            TerritoryType = 146,
            Aetheryte = "Southern Thanalan",
            Position = new Vector3(115.804f, 21.6682f, -481.666f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 62,
            Name = "062: Minotaur Malm",
            TerritoryType = 146,
            Aetheryte = "Southern Thanalan",
            Position = new Vector3(-340.838f, 2.29643f, 254.479f),
            Emote = "psych",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Heat Waves" }
        },
        new SightInfo
        {
            Id = 63,
            Name = "063: East Watchtower",
            TerritoryType = 147,
            Aetheryte = "Northern Thanalan",
            Position = new Vector3(40.0928f, 41.0952f, 213.034f),
            Emote = "salute",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 64,
            Name = "064: Ceruleum Pipeline",
            TerritoryType = 147,
            Aetheryte = "Northern Thanalan",
            Position = new Vector3(-40.5522f, 20.0044f, 403.776f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 65,
            Name = "065: Bluefog",
            TerritoryType = 147,
            Aetheryte = "Northern Thanalan",
            Position = new Vector3(-32.3776f, 47.6336f, 51.1474f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 66,
            Name = "066: Raubahn's Push",
            TerritoryType = 147,
            Aetheryte = "Northern Thanalan",
            Position = new Vector3(-73.9073f, 81.3849f, -188.685f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Clouds" }
        },
        new SightInfo
        {
            Id = 67,
            Name = "067: Abandoned Amajina Mythril Mine",
            TerritoryType = 147,
            Aetheryte = "Northern Thanalan",
            Position = new Vector3(247.298f, 30.936f, 64.8202f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Fog" }
        },
        new SightInfo
        {
            Id = 68,
            Name = "068: The Nail",
            TerritoryType = 155,
            Aetheryte = "Coerthas Central Highlands",
            Position = new Vector3(200.794f, 310.837f, 420.147f),
            Emote = "lookout",
            TimeWindow = "17:00-18:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 69,
            Name = "069: The Observatorium",
            TerritoryType = 155,
            Aetheryte = "Coerthas Central Highlands",
            Position = new Vector3(197.633f, 283.288f, 416.308f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Fog" }
        },
        new SightInfo
        {
            Id = 70,
            Name = "070: The Frozen Fang",
            TerritoryType = 155,
            Aetheryte = "Coerthas Central Highlands",
            Position = new Vector3(-484.655f, 209.488f, -280.476f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Blizzards" }
        },
        new SightInfo
        {
            Id = 71,
            Name = "071: The Holy See of Ishgard",
            TerritoryType = 155,
            Aetheryte = "Coerthas Central Highlands",
            Position = new Vector3(-433.024f, 276.417f, -208.352f),
            Emote = "lookout",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 72,
            Name = "072: Boulder Downs",
            TerritoryType = 155,
            Aetheryte = "Coerthas Central Highlands",
            Position = new Vector3(-682.333f, 315.567f, 373.028f),
            Emote = "lookout",
            TimeWindow = "17:00-18:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 73,
            Name = "073: The Fury's Gaze",
            TerritoryType = 155,
            Aetheryte = "Coerthas Central Highlands",
            Position = new Vector3(-674.652f, 254.458f, 494.997f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Blizzards" }
        },
        new SightInfo
        {
            Id = 74,
            Name = "074: Snowcloak",
            TerritoryType = 155,
            Aetheryte = "Coerthas Central Highlands",
            Position = new Vector3(-964.235f, 283.944f, -8.69034f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 75,
            Name = "075: Camp Dragonhead",
            TerritoryType = 155,
            Aetheryte = "Coerthas Central Highlands",
            Position = new Vector3(254.004f, 328.526f, -186.976f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 76,
            Name = "076: The Steel Vigil",
            TerritoryType = 155,
            Aetheryte = "Coerthas Central Highlands",
            Position = new Vector3(340.512f, 362.396f, -555.496f),
            Emote = "lookout",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 77,
            Name = "077: Castrum Centri",
            TerritoryType = 156,
            Aetheryte = "Mor Dhona",
            Position = new Vector3(-580.639f, 4.10024f, -396.35f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Gloom" }
        },
        new SightInfo
        {
            Id = 78,
            Name = "078: The Crystal Tower",
            TerritoryType = 156,
            Aetheryte = "Mor Dhona",
            Position = new Vector3(299.557f, 36.8305f, -669.613f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 79,
            Name = "079: Rathefrost",
            TerritoryType = 156,
            Aetheryte = "Mor Dhona",
            Position = new Vector3(-141.547f, 49.0607f, -188.824f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Clear Skies", "Fair Skies" }
        },
        new SightInfo
        {
            Id = 80,
            Name = "080: The Keeper of the Lake",
            TerritoryType = 156,
            Aetheryte = "Mor Dhona",
            Position = new Vector3(234.31f, -4.77774f, -510.81f),
            Emote = "groundsit",
            TimeWindow = "17:00-18:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 81,
            Name = "081: Falcon's Nest",
            TerritoryType = 397,
            Aetheryte = "Coerthas Western Highlands",
            Position = new Vector3(474.852f, 302.036f, 683.598f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 82,
            Name = "082: Camp Riversmeet",
            TerritoryType = 397,
            Aetheryte = "Coerthas Western Highlands",
            Position = new Vector3(-88.6154f, 229.039f, 29.6999f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 83,
            Name = "083: The Dreaming Dragon",
            TerritoryType = 397,
            Aetheryte = "Coerthas Western Highlands",
            Position = new Vector3(-610.653f, 232.144f, -227.043f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 84,
            Name = "084: The Dusk Vigil",
            TerritoryType = 397,
            Aetheryte = "Coerthas Western Highlands",
            Position = new Vector3(-122.104f, 144.588f, -795.967f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 85,
            Name = "085: Gorgagne Mills",
            TerritoryType = 397,
            Aetheryte = "Coerthas Western Highlands",
            Position = new Vector3(458.258f, 185.584f, -881.995f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 86,
            Name = "086: Hemlock",
            TerritoryType = 397,
            Aetheryte = "Coerthas Western Highlands",
            Position = new Vector3(686.539f, 219.386f, -165.722f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 87,
            Name = "087: The Bed of Bones",
            TerritoryType = 397,
            Aetheryte = "Coerthas Western Highlands",
            Position = new Vector3(-99.3005f, 243.25f, 697.031f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 88,
            Name = "088: Loth ast Gnath",
            TerritoryType = 398,
            Aetheryte = "The Dravanian Forelands",
            Position = new Vector3(246.864f, 13.937f, 688.739f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 89,
            Name = "089: Anyx Minor",
            TerritoryType = 398,
            Aetheryte = "The Dravanian Forelands",
            Position = new Vector3(-526.514f, -53.6236f, 845.34f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 90,
            Name = "090: Anyx Trine",
            TerritoryType = 398,
            Aetheryte = "The Dravanian Forelands",
            Position = new Vector3(-298.218f, 406.622f, 40.5729f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 91,
            Name = "091: The Hundred Throes",
            TerritoryType = 398,
            Aetheryte = "The Dravanian Forelands",
            Position = new Vector3(353.92f, 84.4961f, -819.031f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 92,
            Name = "092: Halo",
            TerritoryType = 398,
            Aetheryte = "The Dravanian Forelands",
            Position = new Vector3(-714.214f, 8.86739f, -820.994f),
            Emote = "pray",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 93,
            Name = "093: Tailfeather",
            TerritoryType = 398,
            Aetheryte = "The Dravanian Forelands",
            Position = new Vector3(570.948f, -15.1548f, 47.7415f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 94,
            Name = "094: Sohm Al",
            TerritoryType = 398,
            Aetheryte = "The Dravanian Forelands",
            Position = new Vector3(-555.015f, 506.323f, -454.422f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 95,
            Name = "095: Moghome",
            TerritoryType = 400,
            Aetheryte = "The Churning Mists",
            Position = new Vector3(342.288f, -54.3293f, 629.302f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 96,
            Name = "096: The Aery",
            TerritoryType = 400,
            Aetheryte = "The Churning Mists",
            Position = new Vector3(334.625f, 223.001f, -477.044f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 97,
            Name = "097: Tharl Oom Khash",
            TerritoryType = 400,
            Aetheryte = "The Churning Mists",
            Position = new Vector3(-196.628f, 279.198f, -804.726f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 98,
            Name = "098: Zenith",
            TerritoryType = 400,
            Aetheryte = "The Churning Mists",
            Position = new Vector3(-739.417f, 460.912f, 224.482f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 99,
            Name = "099: The Lost Landlord",
            TerritoryType = 400,
            Aetheryte = "The Churning Mists",
            Position = new Vector3(-247.717f, -26.372f, 734.599f),
            Emote = "pray",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 100,
            Name = "100: The House of Letters",
            TerritoryType = 400,
            Aetheryte = "The Churning Mists",
            Position = new Vector3(627.733f, 51.1456f, -104.085f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 101,
            Name = "101: The Rookery",
            TerritoryType = 400,
            Aetheryte = "The Churning Mists",
            Position = new Vector3(34.1741f, 78.733f, -193.55f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 102,
            Name = "102: Camp Cloudtop",
            TerritoryType = 401,
            Aetheryte = "The Sea of Clouds",
            Position = new Vector3(-367.799f, -138.56f, 758.797f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 103,
            Name = "103: The Nidifice",
            TerritoryType = 401,
            Aetheryte = "The Sea of Clouds",
            Position = new Vector3(738.519f, -71.7644f, 878.257f),
            Emote = "doze",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 104,
            Name = "104: Voor Sian Siran",
            TerritoryType = 401,
            Aetheryte = "The Sea of Clouds",
            Position = new Vector3(876.071f, 47.4145f, -28.5232f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 105,
            Name = "105: Mok Oogl Island",
            TerritoryType = 401,
            Aetheryte = "The Sea of Clouds",
            Position = new Vector3(-472.446f, -27.1825f, -676.207f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 106,
            Name = "106: Hall of the Fallen Plume",
            TerritoryType = 401,
            Aetheryte = "The Sea of Clouds",
            Position = new Vector3(-204.332f, 313.082f, 225.997f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 107,
            Name = "107: Ok' Vundu Vana",
            TerritoryType = 401,
            Aetheryte = "The Sea of Clouds",
            Position = new Vector3(122.972f, -161.936f, 67.6746f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 108,
            Name = "108: Hengr's Crucible",
            TerritoryType = 401,
            Aetheryte = "The Sea of Clouds",
            Position = new Vector3(777.618f, -39.9797f, -539.084f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 109,
            Name = "109: The Arkhitekton",
            TerritoryType = 399,
            Aetheryte = "The Dravanian Hinterlands",
            Position = new Vector3(879.168f, 231.337f, -37.0409f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 110,
            Name = "110: The Answering Quarter",
            TerritoryType = 399,
            Aetheryte = "The Dravanian Hinterlands",
            Position = new Vector3(-232.195f, 87.4851f, 34.5417f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 111,
            Name = "111: The Cenotaph",
            TerritoryType = 399,
            Aetheryte = "The Dravanian Hinterlands",
            Position = new Vector3(-20.5516f, 215.108f, 256.772f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 112,
            Name = "112: The Tipped Ewer",
            TerritoryType = 399,
            Aetheryte = "The Dravanian Hinterlands",
            Position = new Vector3(-622.921f, 158.725f, 672.806f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 113,
            Name = "113: Great Gubal Library",
            TerritoryType = 399,
            Aetheryte = "The Dravanian Hinterlands",
            Position = new Vector3(315.005f, 403.422f, 766.363f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 114,
            Name = "114: The Orn Wild",
            TerritoryType = 399,
            Aetheryte = "The Dravanian Hinterlands",
            Position = new Vector3(510.807f, 135.713f, -532.79f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 115,
            Name = "115: Saint Mocianne's Arboretum",
            TerritoryType = 399,
            Aetheryte = "The Dravanian Hinterlands",
            Position = new Vector3(-474.431f, 196.497f, -47.25f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 116,
            Name = "116: The Gration",
            TerritoryType = 402,
            Aetheryte = "Azys Lla",
            Position = new Vector3(833.547f, 88.9901f, -276.477f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 117,
            Name = "117: The Fractal Continuum",
            TerritoryType = 402,
            Aetheryte = "Azys Lla",
            Position = new Vector3(546.877f, 219.767f, 651.81f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 118,
            Name = "118: Antithesis",
            TerritoryType = 402,
            Aetheryte = "Azys Lla",
            Position = new Vector3(-830.242f, 2.64947f, 406.939f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 119,
            Name = "119: Aetherochemical Research Facility",
            TerritoryType = 402,
            Aetheryte = "Azys Lla",
            Position = new Vector3(-581.6f, -13.8407f, 656.312f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 120,
            Name = "120: Helix",
            TerritoryType = 402,
            Aetheryte = "Azys Lla",
            Position = new Vector3(-817.86f, 102.086f, -628.471f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 121,
            Name = "121: Quarantine Block",
            TerritoryType = 402,
            Aetheryte = "Azys Lla",
            Position = new Vector3(-653.877f, 110.562f, -50.7766f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 122,
            Name = "122: Recombination Labs",
            TerritoryType = 402,
            Aetheryte = "Azys Lla",
            Position = new Vector3(394.956f, 90.5541f, -554.083f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 123,
            Name = "123: The Pike",
            TerritoryType = 397,
            Aetheryte = "Coerthas Western Highlands",
            Position = new Vector3(504.719f, 195.48f, 284.801f),
            Emote = "rally",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 124,
            Name = "124: Black Iron Bridge",
            TerritoryType = 397,
            Aetheryte = "Coerthas Western Highlands",
            Position = new Vector3(369.832f, 205.726f, 62.0945f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 125,
            Name = "125: Dragonspit",
            TerritoryType = 397,
            Aetheryte = "Coerthas Western Highlands",
            Position = new Vector3(-674.539f, 99f, -605.267f),
            Emote = "groundsit",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 126,
            Name = "126: The Slate Mountains",
            TerritoryType = 397,
            Aetheryte = "Coerthas Western Highlands",
            Position = new Vector3(-488.428f, 144.239f, -714.01f),
            Emote = "me",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 127,
            Name = "127: Whilom River",
            TerritoryType = 398,
            Aetheryte = "The Dravanian Forelands",
            Position = new Vector3(45.365f, -133.864f, 843.267f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 128,
            Name = "128: Loth ast Vath",
            TerritoryType = 398,
            Aetheryte = "The Dravanian Forelands",
            Position = new Vector3(85.7617f, -34.0312f, -186.625f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 129,
            Name = "129: The Hissing Cobbles",
            TerritoryType = 398,
            Aetheryte = "The Dravanian Forelands",
            Position = new Vector3(589.955f, -22.2755f, -336.516f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 130,
            Name = "130: The Danneroad ",
            TerritoryType = 398,
            Aetheryte = "The Dravanian Forelands",
            Position = new Vector3(-196.638f, -78.0916f, 502.568f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 131,
            Name = "131: Statue of the Unsung",
            TerritoryType = 400,
            Aetheryte = "The Churning Mists",
            Position = new Vector3(564.649f, 186.474f, 493.283f),
            Emote = "pray",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 132,
            Name = "132: Landlord Colony",
            TerritoryType = 400,
            Aetheryte = "The Churning Mists",
            Position = new Vector3(740.541f, 202.987f, -405.95f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 133,
            Name = "133: The Old Father",
            TerritoryType = 400,
            Aetheryte = "The Churning Mists",
            Position = new Vector3(-392.1f, 113.008f, 122.388f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 134,
            Name = "134: Coldwind",
            TerritoryType = 401,
            Aetheryte = "The Sea of Clouds",
            Position = new Vector3(-805.472f, -89.4253f, -824.851f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 135,
            Name = "135: The Shattered Back",
            TerritoryType = 401,
            Aetheryte = "The Sea of Clouds",
            Position = new Vector3(207.516f, -12.7969f, -779.469f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 136,
            Name = "136: Provenance",
            TerritoryType = 401,
            Aetheryte = "The Sea of Clouds",
            Position = new Vector3(-629.845f, -163.335f, 308.732f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 137,
            Name = "137: The Sage's Cataract",
            TerritoryType = 399,
            Aetheryte = "The Dravanian Hinterlands",
            Position = new Vector3(-515.021f, 150.299f, -477.84f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 138,
            Name = "138: The Path of Knowing",
            TerritoryType = 399,
            Aetheryte = "The Dravanian Hinterlands",
            Position = new Vector3(-141.94f, 187.337f, 779.134f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 139,
            Name = "139: The Daggers",
            TerritoryType = 399,
            Aetheryte = "The Dravanian Hinterlands",
            Position = new Vector3(397.561f, 203.027f, 441.191f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 140,
            Name = "140: Centrifugal Crystal Engine",
            TerritoryType = 402,
            Aetheryte = "Azys Lla",
            Position = new Vector3(-625.377f, -168.824f, -383.315f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 141,
            Name = "141: Biomass Incubation Complex",
            TerritoryType = 402,
            Aetheryte = "Azys Lla",
            Position = new Vector3(651.968f, -50.186f, -807.028f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 142,
            Name = "142: The Cathedral",
            TerritoryType = 402,
            Aetheryte = "Azys Lla",
            Position = new Vector3(168.109f, 8.49612f, 304.277f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 143,
            Name = "143: Castellum Velodyna",
            TerritoryType = 612,
            Aetheryte = "The Fringes",
            Position = new Vector3(24.0338f, 98.3409f, 274.329f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 144,
            Name = "144: Gyr Kehim",
            TerritoryType = 612,
            Aetheryte = "The Fringes",
            Position = new Vector3(137.541f, 84.6628f, -257.796f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 145,
            Name = "145: Schism",
            TerritoryType = 612,
            Aetheryte = "The Fringes",
            Position = new Vector3(87.9197f, 80.5189f, -709.338f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 146,
            Name = "146: Castrum Oriens",
            TerritoryType = 612,
            Aetheryte = "The Fringes",
            Position = new Vector3(-605.632f, 184.012f, -530.526f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 147,
            Name = "147: Dimwold",
            TerritoryType = 612,
            Aetheryte = "The Fringes",
            Position = new Vector3(-640.44f, 105.157f, 250.174f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 148,
            Name = "148: Djanan Qhat",
            TerritoryType = 612,
            Aetheryte = "The Fringes",
            Position = new Vector3(753.328f, 225.749f, -249.632f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 149,
            Name = "149: The Peering Stones",
            TerritoryType = 612,
            Aetheryte = "The Fringes",
            Position = new Vector3(427.054f, 254.995f, 189.569f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 150,
            Name = "150: Hidden Tear",
            TerritoryType = 620,
            Aetheryte = "The Peaks",
            Position = new Vector3(587.659f, 271.56f, -562.683f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 151,
            Name = "151: Coldhearth",
            TerritoryType = 620,
            Aetheryte = "The Peaks",
            Position = new Vector3(279.751f, 340.982f, 770.994f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 152,
            Name = "152: Nyunkrepf's Hope",
            TerritoryType = 620,
            Aetheryte = "The Peaks",
            Position = new Vector3(27.4919f, 398.185f, 569.952f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 153,
            Name = "153: Ala Gannha",
            TerritoryType = 620,
            Aetheryte = "The Peaks",
            Position = new Vector3(182.305f, 165.415f, -781.453f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 154,
            Name = "154: Specula Imperatoris",
            TerritoryType = 620,
            Aetheryte = "The Peaks",
            Position = new Vector3(-75.5661f, 372.291f, 99.033f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 155,
            Name = "155: The Arms of Meed",
            TerritoryType = 620,
            Aetheryte = "The Peaks",
            Position = new Vector3(-664.932f, 307.681f, 805.406f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 156,
            Name = "156: The Ziggurat",
            TerritoryType = 620,
            Aetheryte = "The Peaks",
            Position = new Vector3(-157.618f, 135.793f, -357.868f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 157,
            Name = "157: Emprise",
            TerritoryType = 620,
            Aetheryte = "The Peaks",
            Position = new Vector3(-697.473f, 59.9885f, -693.026f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 158,
            Name = "158: Ala Mhigo",
            TerritoryType = 621,
            Aetheryte = "The Lochs",
            Position = new Vector3(101.852f, 172.954f, 612.187f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 159,
            Name = "159: The Ala Mhigan Quarter",
            TerritoryType = 621,
            Aetheryte = "The Lochs",
            Position = new Vector3(689.262f, 150.724f, 586.841f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 160,
            Name = "160: Sothwatch",
            TerritoryType = 621,
            Aetheryte = "The Lochs",
            Position = new Vector3(-382.053f, 284f, 701.468f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 161,
            Name = "161: The Divine Audience",
            TerritoryType = 621,
            Aetheryte = "The Lochs",
            Position = new Vector3(-43.4882f, 85.8502f, -244.821f),
            Emote = "pray",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 162,
            Name = "162: The Hidden Tunnel",
            TerritoryType = 621,
            Aetheryte = "The Lochs",
            Position = new Vector3(621.783f, 50f, 438.905f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 163,
            Name = "163: Porta Praetoria",
            TerritoryType = 621,
            Aetheryte = "The Lochs",
            Position = new Vector3(-777.757f, 240.431f, 28.0452f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 164,
            Name = "164: The Sekiseigumi Barracks",
            TerritoryType = 628,
            Aetheryte = "Kugane",
            Position = new Vector3(153.473f, 20.7505f, -80.3655f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 165,
            Name = "165: Bokairo Inn",
            TerritoryType = 628,
            Aetheryte = "Kugane",
            Position = new Vector3(-91.7026f, 56.814f, -196.417f),
            Emote = "groundsit",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 166,
            Name = "166: Kogane Dori",
            TerritoryType = 628,
            Aetheryte = "Kugane",
            Position = new Vector3(99.2801f, 11.5f, 81.0459f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 167,
            Name = "167: Kogane Alleyways",
            TerritoryType = 628,
            Aetheryte = "Kugane",
            Position = new Vector3(36.046f, 10.6582f, 23.4598f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 168,
            Name = "168: Shiokaze Hostelry",
            TerritoryType = 628,
            Aetheryte = "Kugane",
            Position = new Vector3(-47.9968f, 109.016f, -59.1397f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 169,
            Name = "169: Tamamizu",
            TerritoryType = 613,
            Aetheryte = "The Ruby Sea",
            Position = new Vector3(222.658f, -91.8284f, -422.329f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 170,
            Name = "170: Shoal Rock",
            TerritoryType = 613,
            Aetheryte = "The Ruby Sea",
            Position = new Vector3(575.1f, 107.308f, -638.601f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 171,
            Name = "171: Heaven-on-High",
            TerritoryType = 613,
            Aetheryte = "The Ruby Sea",
            Position = new Vector3(130.555f, 45.6131f, -790.931f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 172,
            Name = "172: Sakazuki",
            TerritoryType = 613,
            Aetheryte = "The Ruby Sea",
            Position = new Vector3(505.869f, 58.9899f, 790.208f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 173,
            Name = "173: The Isle of Zekki",
            TerritoryType = 613,
            Aetheryte = "The Ruby Sea",
            Position = new Vector3(-565.897f, 12.924f, 263.349f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 174,
            Name = "174: Isari",
            TerritoryType = 613,
            Aetheryte = "The Ruby Sea",
            Position = new Vector3(-751.046f, 9.53925f, -531.426f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 175,
            Name = "175: The Swallow's Compass",
            TerritoryType = 614,
            Aetheryte = "Yanxia",
            Position = new Vector3(-453.11f, 109.676f, 264.357f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 176,
            Name = "176: Castrum Fluminis",
            TerritoryType = 614,
            Aetheryte = "Yanxia",
            Position = new Vector3(445.415f, 81.0676f, 571.434f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 177,
            Name = "177: Namai",
            TerritoryType = 614,
            Aetheryte = "Yanxia",
            Position = new Vector3(642.79f, 110.336f, -155.603f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 178,
            Name = "178: Prism Lake",
            TerritoryType = 614,
            Aetheryte = "Yanxia",
            Position = new Vector3(446.661f, 46.7262f, -763.548f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 179,
            Name = "179: Doma Castle",
            TerritoryType = 614,
            Aetheryte = "Yanxia",
            Position = new Vector3(-327.712f, 95.2907f, -753.873f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 180,
            Name = "180: Dairyu Moon Gates",
            TerritoryType = 614,
            Aetheryte = "Yanxia",
            Position = new Vector3(-94.5898f, 148.406f, -44.7855f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 181,
            Name = "181: Yuzuka Manor",
            TerritoryType = 614,
            Aetheryte = "Yanxia",
            Position = new Vector3(-313.892f, 61.0826f, 508.244f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 182,
            Name = "182: Ceol Aen",
            TerritoryType = 622,
            Aetheryte = "The Azim Steppe",
            Position = new Vector3(-360.572f, 111.276f, -583.2f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 183,
            Name = "183: Dotharl Khaa",
            TerritoryType = 622,
            Aetheryte = "The Azim Steppe",
            Position = new Vector3(-459.685f, 33.9357f, 527.162f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 184,
            Name = "184: The Dusk Throne",
            TerritoryType = 622,
            Aetheryte = "The Azim Steppe",
            Position = new Vector3(-79.602f, 76.8576f, 613.155f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 185,
            Name = "185: Reunion",
            TerritoryType = 622,
            Aetheryte = "The Azim Steppe",
            Position = new Vector3(649.37f, 28.0002f, 521.128f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 186,
            Name = "186: Chakha Zoh",
            TerritoryType = 622,
            Aetheryte = "The Azim Steppe",
            Position = new Vector3(-71.2841f, 101.723f, -442.454f),
            Emote = "pray",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 187,
            Name = "187: The Dawn Throne",
            TerritoryType = 622,
            Aetheryte = "The Azim Steppe",
            Position = new Vector3(62.7058f, 177.156f, -12.8876f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 188,
            Name = "188: The Destroyer",
            TerritoryType = 635,
            Aetheryte = "Rhalgr's Reach",
            Position = new Vector3(9.45903f, 4.1386f, 137.444f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 189,
            Name = "189: Bloodstorm",
            TerritoryType = 635,
            Aetheryte = "Rhalgr's Reach",
            Position = new Vector3(-34.165f, 15.6814f, -75.9668f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 190,
            Name = "190: The Yawn",
            TerritoryType = 612,
            Aetheryte = "The Fringes",
            Position = new Vector3(302.437f, 154.118f, 684.418f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 191,
            Name = "191: Ala Ghiri",
            TerritoryType = 620,
            Aetheryte = "The Peaks",
            Position = new Vector3(-357.652f, 315.489f, 759.869f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 192,
            Name = "192: Specula Imperatoris #2",
            TerritoryType = 620,
            Aetheryte = "The Peaks",
            Position = new Vector3(-53.8283f, 315.133f, 71.9961f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 193,
            Name = "193: The Sunken Destroyer",
            TerritoryType = 621,
            Aetheryte = "The Lochs",
            Position = new Vector3(-218.12f, -283.105f, -113.249f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 194,
            Name = "194: The Ala Mhigan Quarter #2",
            TerritoryType = 621,
            Aetheryte = "The Lochs",
            Position = new Vector3(728.046f, 80.9657f, 602.288f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 195,
            Name = "195: The Statue of Zuiko",
            TerritoryType = 628,
            Aetheryte = "Kugane",
            Position = new Vector3(-4.37274f, 5.2779f, -64.0847f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 196,
            Name = "196: Rakuza District",
            TerritoryType = 628,
            Aetheryte = "Kugane",
            Position = new Vector3(-71.5357f, 26.1413f, -143.151f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 197,
            Name = "197: Tenkonto",
            TerritoryType = 628,
            Aetheryte = "Kugane",
            Position = new Vector3(64.4344f, 15.7567f, -30.3241f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 198,
            Name = "198: Kugane Ofunakura",
            TerritoryType = 628,
            Aetheryte = "Kugane",
            Position = new Vector3(-67.3715f, 4.26844f, 57.9105f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 199,
            Name = "199: Shisui of the Violet Tides",
            TerritoryType = 613,
            Aetheryte = "The Ruby Sea",
            Position = new Vector3(-821.625f, -858.011f, 749.037f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 200,
            Name = "200: East Othard Coastline",
            TerritoryType = 613,
            Aetheryte = "The Ruby Sea",
            Position = new Vector3(-597.893f, 11.0963f, -120.059f),
            Emote = "lookout",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 201,
            Name = "201: Crick",
            TerritoryType = 613,
            Aetheryte = "The Ruby Sea",
            Position = new Vector3(1.39211f, 33.4709f, -474.697f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 202,
            Name = "202: Imperial Hypersonic Assault Craft L-XXIII",
            TerritoryType = 614,
            Aetheryte = "Yanxia",
            Position = new Vector3(706.341f, -120.255f, 866.73f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 203,
            Name = "203: Mol Iloh",
            TerritoryType = 622,
            Aetheryte = "The Azim Steppe",
            Position = new Vector3(495.494f, 67.7263f, -498.119f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 204,
            Name = "204: Moai Statue",
            TerritoryType = 622,
            Aetheryte = "The Azim Steppe",
            Position = new Vector3(19.4963f, -46.2176f, -54.5222f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 205,
            Name = "205: The Rotunda",
            TerritoryType = 819,
            Aetheryte = "The Crystarium",
            Position = new Vector3(-129.264f, 15.0301f, -0.0118323f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 206,
            Name = "206: Musica Universalis",
            TerritoryType = 819,
            Aetheryte = "The Crystarium",
            Position = new Vector3(-38.1833f, 1.45778f, 94.7464f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 207,
            Name = "207: The Cabinet of Curiosity",
            TerritoryType = 819,
            Aetheryte = "The Crystarium",
            Position = new Vector3(-63.632f, -25.6744f, -267.448f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 208,
            Name = "208: Rapture",
            TerritoryType = 819,
            Aetheryte = "The Crystarium",
            Position = new Vector3(-10.2317f, 37.8173f, -326.29f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 209,
            Name = "209: Temenos Rookery",
            TerritoryType = 819,
            Aetheryte = "The Crystarium",
            Position = new Vector3(-193.267f, 12.5008f, -73.1311f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 210,
            Name = "210: The Glory Gate",
            TerritoryType = 820,
            Aetheryte = "Eulmore",
            Position = new Vector3(24.4197f, -4.13403f, -139.329f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 211,
            Name = "211: The Derelicts",
            TerritoryType = 820,
            Aetheryte = "Eulmore",
            Position = new Vector3(63.1812f, -3.79091f, 138.48f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 212,
            Name = "212: Eulmoran Army Headquarters",
            TerritoryType = 820,
            Aetheryte = "Eulmore",
            Position = new Vector3(-3.53046f, 27.8914f, 8.06374f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 213,
            Name = "213: The Beehive",
            TerritoryType = 820,
            Aetheryte = "Eulmore",
            Position = new Vector3(55.7358f, 82.9802f, -39.8242f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 214,
            Name = "214: Fort Jobb",
            TerritoryType = 813,
            Aetheryte = "Lakeland",
            Position = new Vector3(796.138f, 73.41f, -27.5451f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 215,
            Name = "215: Radisca's Round",
            TerritoryType = 813,
            Aetheryte = "Lakeland",
            Position = new Vector3(-153.88f, 49.3223f, -138.092f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 216,
            Name = "216: Laxan Loft",
            TerritoryType = 813,
            Aetheryte = "Lakeland",
            Position = new Vector3(33.2413f, 224.664f, -318.346f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 217,
            Name = "217: The Ostall Imperative",
            TerritoryType = 813,
            Aetheryte = "Lakeland",
            Position = new Vector3(-756.578f, 200.736f, -312.303f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 218,
            Name = "218: The Hour of Certain Durance",
            TerritoryType = 813,
            Aetheryte = "Lakeland",
            Position = new Vector3(-634.167f, 68.3391f, 74.9973f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 219,
            Name = "219: Sullen",
            TerritoryType = 813,
            Aetheryte = "Lakeland",
            Position = new Vector3(3.2347f, 34.246f, 736.575f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 220,
            Name = "220: Cracked Shell Beach",
            TerritoryType = 814,
            Aetheryte = "Kholusia",
            Position = new Vector3(587.969f, 38.6003f, 371.187f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 221,
            Name = "221: White Oil Falls",
            TerritoryType = 814,
            Aetheryte = "Kholusia",
            Position = new Vector3(368.596f, 38.8814f, 32.7692f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 222,
            Name = "222: Gatetown",
            TerritoryType = 814,
            Aetheryte = "Kholusia",
            Position = new Vector3(110.714f, 58.0247f, 834.788f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 223,
            Name = "223: Wright",
            TerritoryType = 814,
            Aetheryte = "Kholusia",
            Position = new Vector3(-161.836f, 43.9691f, 392.419f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 224,
            Name = "224: The Ladder",
            TerritoryType = 814,
            Aetheryte = "Kholusia",
            Position = new Vector3(-464.001f, 362.548f, 32.1987f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 225,
            Name = "225: Tomra",
            TerritoryType = 814,
            Aetheryte = "Kholusia",
            Position = new Vector3(-391.401f, 464.034f, -582.062f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 226,
            Name = "226: The Duergar's Tewel",
            TerritoryType = 814,
            Aetheryte = "Kholusia",
            Position = new Vector3(783.239f, 286.104f, -498.971f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 227,
            Name = "227: The Red Serai",
            TerritoryType = 815,
            Aetheryte = "Amh Araeng",
            Position = new Vector3(587.55f, -43.8068f, -384.681f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 228,
            Name = "228: Mord Souq",
            TerritoryType = 815,
            Aetheryte = "Amh Araeng",
            Position = new Vector3(195.032f, 133.475f, -241.599f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 229,
            Name = "229: The Pristine Palace of Amh Malik",
            TerritoryType = 815,
            Aetheryte = "Amh Araeng",
            Position = new Vector3(355.218f, -36.293f, 523.226f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 230,
            Name = "230: Mount Biran Mines",
            TerritoryType = 815,
            Aetheryte = "Amh Araeng",
            Position = new Vector3(34.5416f, 52.3809f, -602.121f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 231,
            Name = "231: Twine",
            TerritoryType = 815,
            Aetheryte = "Amh Araeng",
            Position = new Vector3(-515.393f, 57.3515f, -227.762f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 232,
            Name = "232: Kelk",
            TerritoryType = 815,
            Aetheryte = "Amh Araeng",
            Position = new Vector3(-49.5601f, 2.66181f, -7.09251f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 233,
            Name = "233: Lydha Lran",
            TerritoryType = 816,
            Aetheryte = "Il Mheg",
            Position = new Vector3(-332.226f, 99.5098f, 525.093f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 234,
            Name = "234: The Bookman's Shelves",
            TerritoryType = 816,
            Aetheryte = "Il Mheg",
            Position = new Vector3(-637.664f, 62.6652f, -230.005f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 235,
            Name = "235: Pla Enni",
            TerritoryType = 816,
            Aetheryte = "Il Mheg",
            Position = new Vector3(-62.7993f, 125.429f, -843.179f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 236,
            Name = "236: Deepwood Swim",
            TerritoryType = 816,
            Aetheryte = "Il Mheg",
            Position = new Vector3(-1.69895f, -66.5782f, -28.6864f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 237,
            Name = "237: Lyhe Ghiah",
            TerritoryType = 816,
            Aetheryte = "Il Mheg",
            Position = new Vector3(-30.8217f, 171.032f, -259.103f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 238,
            Name = "238: Saint Fathric's Temple",
            TerritoryType = 816,
            Aetheryte = "Il Mheg",
            Position = new Vector3(713.553f, 192.912f, 166.82f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 239,
            Name = "239: Fort Gohn",
            TerritoryType = 817,
            Aetheryte = "The Rak'tika Greatwood",
            Position = new Vector3(-394.526f, 41.0661f, 549.073f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 240,
            Name = "240: Fruit of the Protector",
            TerritoryType = 817,
            Aetheryte = "The Rak'tika Greatwood",
            Position = new Vector3(-627.996f, 17.9868f, 182.262f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 241,
            Name = "241: The Covered Halls of Dwatl",
            TerritoryType = 817,
            Aetheryte = "The Rak'tika Greatwood",
            Position = new Vector3(-852.625f, -82.2965f, 290.264f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 242,
            Name = "242: Lozatl's Conquest",
            TerritoryType = 817,
            Aetheryte = "The Rak'tika Greatwood",
            Position = new Vector3(-365.765f, 19.6282f, -154.975f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 243,
            Name = "243: Fanow",
            TerritoryType = 817,
            Aetheryte = "The Rak'tika Greatwood",
            Position = new Vector3(382.639f, 34.6857f, -119.978f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 244,
            Name = "244: The Morning Stars",
            TerritoryType = 817,
            Aetheryte = "The Rak'tika Greatwood",
            Position = new Vector3(247.803f, 29.9049f, -577.755f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 245,
            Name = "245: The Ondo Cups",
            TerritoryType = 818,
            Aetheryte = "The Tempest",
            Position = new Vector3(580.413f, 400.246f, -259.702f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 246,
            Name = "246: The Workbench",
            TerritoryType = 818,
            Aetheryte = "The Tempest",
            Position = new Vector3(651.748f, 404.421f, 200.704f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 247,
            Name = "247: Where the Dry Return",
            TerritoryType = 818,
            Aetheryte = "The Tempest",
            Position = new Vector3(781.84f, 449.421f, -741.657f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 248,
            Name = "248: Purpure",
            TerritoryType = 818,
            Aetheryte = "The Tempest",
            Position = new Vector3(643.832f, -235.135f, 460.098f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 249,
            Name = "249: Amaurot",
            TerritoryType = 818,
            Aetheryte = "The Tempest",
            Position = new Vector3(-387.305f, 140.893f, 769.211f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 250,
            Name = "250: The Leveilleur Estate",
            TerritoryType = 962,
            Aetheryte = "Old Sharlayan",
            Position = new Vector3(222.898f, 37.8362f, -158.754f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 251,
            Name = "251: Scholar's Harbor",
            TerritoryType = 962,
            Aetheryte = "Old Sharlayan",
            Position = new Vector3(31.8668f, -15.1187f, 229.496f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 252,
            Name = "252: The Forum",
            TerritoryType = 962,
            Aetheryte = "Old Sharlayan",
            Position = new Vector3(-19.8521f, 43.869f, -218.708f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 253,
            Name = "253: Noumenon",
            TerritoryType = 962,
            Aetheryte = "Old Sharlayan",
            Position = new Vector3(-334.851f, 19.5969f, 3.9433f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 254,
            Name = "254: The Rostra",
            TerritoryType = 962,
            Aetheryte = "Old Sharlayan",
            Position = new Vector3(0.0759f, 7.7262f, -59.4558f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 255,
            Name = "255: Journey's End",
            TerritoryType = 962,
            Aetheryte = "Old Sharlayan",
            Position = new Vector3(197.449f, 19.2766f, -80.3918f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 256,
            Name = "256: Learners' Meet",
            TerritoryType = 962,
            Aetheryte = "Old Sharlayan",
            Position = new Vector3(-397.36f, 21.245f, -110.013f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 257,
            Name = "257: Ruveydah Fibers",
            TerritoryType = 963,
            Aetheryte = "Radz-at-Han",
            Position = new Vector3(-164.562f, 29.6039f, 106.951f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 258,
            Name = "258: Balshahn Bazaar",
            TerritoryType = 963,
            Aetheryte = "Radz-at-Han",
            Position = new Vector3(-27.2171f, 4.81032f, -87.2737f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 259,
            Name = "259: Nilopala Nourishments",
            TerritoryType = 963,
            Aetheryte = "Radz-at-Han",
            Position = new Vector3(-174.585f, 43.4861f, 146.424f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 260,
            Name = "260: Mehryde's Meyhane",
            TerritoryType = 963,
            Aetheryte = "Radz-at-Han",
            Position = new Vector3(2.24017f, 3.28024f, -201.44f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 261,
            Name = "261: Meghaduta",
            TerritoryType = 963,
            Aetheryte = "Radz-at-Han",
            Position = new Vector3(-305.287f, 37.6871f, 100.206f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 262,
            Name = "262: Alzadaal's Peace",
            TerritoryType = 963,
            Aetheryte = "Radz-at-Han",
            Position = new Vector3(-13.082f, 25f, 44.4617f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 263,
            Name = "263: Ruveydah Fibers Rooftop Garden",
            TerritoryType = 963,
            Aetheryte = "Radz-at-Han",
            Position = new Vector3(-128.136f, 49.7376f, 96.3277f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 264,
            Name = "264: The Path of Artifice",
            TerritoryType = 956,
            Aetheryte = "Labyrinthos",
            Position = new Vector3(-53.4698f, 170.809f, -694.308f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 265,
            Name = "265: Thaumazein",
            TerritoryType = 956,
            Aetheryte = "Labyrinthos",
            Position = new Vector3(-322.982f, -220.203f, 192.295f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 266,
            Name = "266: Meryall Agronomics",
            TerritoryType = 956,
            Aetheryte = "Labyrinthos",
            Position = new Vector3(469.81f, 78.0158f, -137.041f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 267,
            Name = "267: Troglophile's Deep",
            TerritoryType = 956,
            Aetheryte = "Labyrinthos",
            Position = new Vector3(849.929f, 178.899f, -389f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 268,
            Name = "268: Sharlayan Hamlet",
            TerritoryType = 956,
            Aetheryte = "Labyrinthos",
            Position = new Vector3(50.5206f, 50.9477f, -198.859f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 269,
            Name = "269: Yedlihmad",
            TerritoryType = 957,
            Aetheryte = "Thavnair",
            Position = new Vector3(240.686f, 24.8357f, 606.714f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 270,
            Name = "270: Kadjaya's Footsteps",
            TerritoryType = 957,
            Aetheryte = "Thavnair",
            Position = new Vector3(53.0861f, 117.77f, -90.9717f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 271,
            Name = "271: Giantsgall Grounds",
            TerritoryType = 957,
            Aetheryte = "Thavnair",
            Position = new Vector3(-76.6738f, 121.968f, -727.625f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 272,
            Name = "272: The Shroud of the Samgha",
            TerritoryType = 957,
            Aetheryte = "Thavnair",
            Position = new Vector3(391.287f, 18.4537f, 175.158f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 273,
            Name = "273: The Font of Maya",
            TerritoryType = 957,
            Aetheryte = "Thavnair",
            Position = new Vector3(252.713f, 12.006f, 133.564f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 274,
            Name = "274: The Great Work",
            TerritoryType = 957,
            Aetheryte = "Thavnair",
            Position = new Vector3(-582.686f, 100.454f, 86.8281f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 275,
            Name = "275: Tertium",
            TerritoryType = 958,
            Aetheryte = "Garlemald",
            Position = new Vector3(544.355f, -36.5981f, -147.717f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 276,
            Name = "276: Juturna Platform G",
            TerritoryType = 958,
            Aetheryte = "Garlemald",
            Position = new Vector3(410.98f, 71.9f, 635.551f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 277,
            Name = "277: The Runaway Train",
            TerritoryType = 958,
            Aetheryte = "Garlemald",
            Position = new Vector3(405.38f, 32.2814f, 93.1496f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 278,
            Name = "278: Senaculum Imperialis",
            TerritoryType = 958,
            Aetheryte = "Garlemald",
            Position = new Vector3(-224.022f, 44.1875f, -613.167f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 279,
            Name = "279: Regio Urbanissima",
            TerritoryType = 958,
            Aetheryte = "Garlemald",
            Position = new Vector3(-106.542f, 48.4604f, -229.412f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 280,
            Name = "280: Forum Solius",
            TerritoryType = 958,
            Aetheryte = "Garlemald",
            Position = new Vector3(381.918f, 19.6132f, -642.908f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 281,
            Name = "281: The Chthonic Horns",
            TerritoryType = 961,
            Aetheryte = "Elpis",
            Position = new Vector3(577.191f, 45.499f, 395.054f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 282,
            Name = "282: Metabaseos Thalassai",
            TerritoryType = 961,
            Aetheryte = "Elpis",
            Position = new Vector3(-564.773f, 365.565f, -396.983f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 283,
            Name = "283: Lethe",
            TerritoryType = 961,
            Aetheryte = "Elpis",
            Position = new Vector3(511.612f, 171.196f, -284.598f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 284,
            Name = "284: Ktisis Hyperboreia",
            TerritoryType = 961,
            Aetheryte = "Elpis",
            Position = new Vector3(-328.388f, 329.377f, -709.847f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 285,
            Name = "285: Anagnorisis",
            TerritoryType = 961,
            Aetheryte = "Elpis",
            Position = new Vector3(154.735f, 32.4557f, 67.4875f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 286,
            Name = "286: Kydonia Knolls",
            TerritoryType = 959,
            Aetheryte = "Mare Lamentorum",
            Position = new Vector3(-506.592f, 160.132f, 27.7916f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 287,
            Name = "287: The Carrotorium",
            TerritoryType = 959,
            Aetheryte = "Mare Lamentorum",
            Position = new Vector3(-705.319f, -108.382f, -765.582f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 288,
            Name = "288: Greatest Endsvale",
            TerritoryType = 959,
            Aetheryte = "Mare Lamentorum",
            Position = new Vector3(725.246f, -167.536f, -607.866f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 289,
            Name = "289: Heimdall's Last Sight",
            TerritoryType = 959,
            Aetheryte = "Mare Lamentorum",
            Position = new Vector3(643.915f, 152.587f, 399.98f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 290,
            Name = "290: The Watcher's Palace",
            TerritoryType = 959,
            Aetheryte = "Mare Lamentorum",
            Position = new Vector3(-398.694f, 212.77f, 556.404f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 291,
            Name = "291: Stigma-1",
            TerritoryType = 960,
            Aetheryte = "Ultima Thule",
            Position = new Vector3(560.204f, 441.653f, 421.724f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 292,
            Name = "292: Ostrakon Deka-hexi",
            TerritoryType = 960,
            Aetheryte = "Ultima Thule",
            Position = new Vector3(608.249f, 473.114f, 269.086f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 293,
            Name = "293: Ostrakon Tria",
            TerritoryType = 960,
            Aetheryte = "Ultima Thule",
            Position = new Vector3(-138.452f, 308.149f, -384.383f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 294,
            Name = "294: Ostrakon Deka-okto",
            TerritoryType = 960,
            Aetheryte = "Ultima Thule",
            Position = new Vector3(-627.449f, 89.449f, -166.28f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 295,
            Name = "295: Ostrakon Hena",
            TerritoryType = 960,
            Aetheryte = "Ultima Thule",
            Position = new Vector3(80.1805f, 615.7f, 290.524f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 296,
            Name = "296: Bayside Bevy",
            TerritoryType = 1185,
            Aetheryte = "Tuliyollal",
            Position = new Vector3(-61.3676f, -6.47014f, 153.035f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 297,
            Name = "297: The Resplendent Quarter",
            TerritoryType = 1185,
            Aetheryte = "Tuliyollal",
            Position = new Vector3(-270.373f, 41.0371f, -9.0471f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 298,
            Name = "298: High Tide Harbor",
            TerritoryType = 1185,
            Aetheryte = "Tuliyollal",
            Position = new Vector3(67.6737f, -16.9816f, 208.576f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 299,
            Name = "299: The For'ard Cabins",
            TerritoryType = 1185,
            Aetheryte = "Tuliyollal",
            Position = new Vector3(-154.397f, -14.015f, 552.339f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 300,
            Name = "300: Hunu'iliy",
            TerritoryType = 1185,
            Aetheryte = "Tuliyollal",
            Position = new Vector3(253.428f, 144.793f, -389.17f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 301,
            Name = "301: Wachunpelo",
            TerritoryType = 1187,
            Aetheryte = "Urqopacha",
            Position = new Vector3(378.503f, -145.922f, -442.534f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 302,
            Name = "302: Miplu's Mate Garden",
            TerritoryType = 1187,
            Aetheryte = "Urqopacha",
            Position = new Vector3(-349.199f, -22.2085f, -629.692f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 303,
            Name = "303: Shades of Grief",
            TerritoryType = 1187,
            Aetheryte = "Urqopacha",
            Position = new Vector3(262.195f, 55.4074f, 137.092f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 304,
            Name = "304: Naryor Gorna",
            TerritoryType = 1187,
            Aetheryte = "Urqopacha",
            Position = new Vector3(657.3f, 120.993f, 187.35f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 305,
            Name = "305: Chirwagur Saltern",
            TerritoryType = 1187,
            Aetheryte = "Urqopacha",
            Position = new Vector3(24.4835f, 45.4964f, 689f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 306,
            Name = "306: Worqor Lar Dor",
            TerritoryType = 1187,
            Aetheryte = "Urqopacha",
            Position = new Vector3(-622.958f, 74.6766f, 36.2281f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 307,
            Name = "307: House of Winds High",
            TerritoryType = 1188,
            Aetheryte = "Kozama'uka",
            Position = new Vector3(-615.963f, 94.4373f, -476.024f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 308,
            Name = "308: Cave Kikitola ",
            TerritoryType = 1188,
            Aetheryte = "Kozama'uka",
            Position = new Vector3(-163.795f, 1.32122f, -38.408f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 309,
            Name = "309: Breath Between",
            TerritoryType = 1188,
            Aetheryte = "Kozama'uka",
            Position = new Vector3(163.18f, 17.2793f, -117.762f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 310,
            Name = "310: Kozanuakiy",
            TerritoryType = 1188,
            Aetheryte = "Kozama'uka",
            Position = new Vector3(658.983f, 42.5622f, -538.478f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 311,
            Name = "311: Earthenshire",
            TerritoryType = 1188,
            Aetheryte = "Kozama'uka",
            Position = new Vector3(-537.908f, 142.911f, 327.299f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 312,
            Name = "312: Marsh Ligaka",
            TerritoryType = 1188,
            Aetheryte = "Kozama'uka",
            Position = new Vector3(367.013f, 110.851f, 362.146f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 313,
            Name = "313: Iq Br'aax",
            TerritoryType = 1189,
            Aetheryte = "Yak T'el",
            Position = new Vector3(-497.41f, 37.3686f, -368.907f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 314,
            Name = "314: Iq Rrax Tsoly",
            TerritoryType = 1189,
            Aetheryte = "Yak T'el",
            Position = new Vector3(574.77f, -47.241f, -748.063f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 315,
            Name = "315: The Xobr'it Cinderfield",
            TerritoryType = 1189,
            Aetheryte = "Yak T'el",
            Position = new Vector3(404.889f, 18.7486f, -309.258f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 316,
            Name = "316: Choliselvaas",
            TerritoryType = 1189,
            Aetheryte = "Yak T'el",
            Position = new Vector3(149.431f, 22.8287f, 104.712f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 317,
            Name = "317: The Ja Tiika Heartland",
            TerritoryType = 1189,
            Aetheryte = "Yak T'el",
            Position = new Vector3(-98.39f, -186.513f, 547.518f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 318,
            Name = "318: Tree of Living Light",
            TerritoryType = 1189,
            Aetheryte = "Yak T'el",
            Position = new Vector3(190.036f, -191.295f, 286.596f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 319,
            Name = "319: Hhusatahwi",
            TerritoryType = 1190,
            Aetheryte = "Shaaloani",
            Position = new Vector3(355.989f, 5.9294f, 421.71f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 320,
            Name = "320: Mehwahhetsoan",
            TerritoryType = 1190,
            Aetheryte = "Shaaloani",
            Position = new Vector3(310.046f, -15.0308f, -475.667f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 321,
            Name = "321: Lake Toari",
            TerritoryType = 1190,
            Aetheryte = "Shaaloani",
            Position = new Vector3(547.532f, -70.0376f, -398.562f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 322,
            Name = "322: Pyaayehe'pya",
            TerritoryType = 1190,
            Aetheryte = "Shaaloani",
            Position = new Vector3(-386.462f, 2.5292f, 642.427f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 323,
            Name = "323: Mount Loazensasaya",
            TerritoryType = 1190,
            Aetheryte = "Shaaloani",
            Position = new Vector3(-559.891f, 64.9228f, -455.619f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 324,
            Name = "324: Resolution",
            TerritoryType = 1186,
            Aetheryte = "Solution Nine",
            Position = new Vector3(-76.6026f, 39.7605f, -429.125f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 325,
            Name = "325: Residential Sector",
            TerritoryType = 1186,
            Aetheryte = "Solution Nine",
            Position = new Vector3(-418.323f, 14.8085f, 140.494f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 326,
            Name = "326: Mosaic",
            TerritoryType = 1186,
            Aetheryte = "Solution Nine",
            Position = new Vector3(-342.288f, 10.2147f, 27.7605f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 327,
            Name = "327: True Vue",
            TerritoryType = 1186,
            Aetheryte = "Solution Nine",
            Position = new Vector3(385.917f, 71.5109f, 44.5259f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 328,
            Name = "328: Nexus Arcade",
            TerritoryType = 1186,
            Aetheryte = "Solution Nine",
            Position = new Vector3(-236.991f, 1.43285f, -40.3866f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 329,
            Name = "329: The Thunderyards",
            TerritoryType = 1191,
            Aetheryte = "Heritage Found",
            Position = new Vector3(600.291f, 123.077f, -397.602f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 330,
            Name = "330: The Outskirts",
            TerritoryType = 1191,
            Aetheryte = "Heritage Found",
            Position = new Vector3(-68.746f, 104.956f, -674.874f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 331,
            Name = "331: Everkeep",
            TerritoryType = 1191,
            Aetheryte = "Heritage Found",
            Position = new Vector3(-191.083f, 23.3601f, -811.891f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 332,
            Name = "332: Crackling Chasm",
            TerritoryType = 1191,
            Aetheryte = "Heritage Found",
            Position = new Vector3(-38.3526f, 71.3283f, 180.536f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 333,
            Name = "333: The Nameslates",
            TerritoryType = 1191,
            Aetheryte = "Heritage Found",
            Position = new Vector3(197.802f, 89.0663f, 657.023f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 334,
            Name = "334: Archeo Alexandria",
            TerritoryType = 1191,
            Aetheryte = "Heritage Found",
            Position = new Vector3(-602.242f, -13.1964f, 789.846f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 335,
            Name = "335: Meso Terminal",
            TerritoryType = 1192,
            Aetheryte = "Living Memory",
            Position = new Vector3(21.9195f, 51.4f, 740.609f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 336,
            Name = "336: Canal Town",
            TerritoryType = 1192,
            Aetheryte = "Living Memory",
            Position = new Vector3(-113.821f, 0.523526f, 597.26f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 337,
            Name = "337: Yesterland",
            TerritoryType = 1192,
            Aetheryte = "Living Memory",
            Position = new Vector3(367.626f, 58.9396f, 388.662f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 338,
            Name = "338: Windspath Gardens",
            TerritoryType = 1192,
            Aetheryte = "Living Memory",
            Position = new Vector3(-750.983f, 33.4168f, -393.384f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 339,
            Name = "339: Asyle Volcane",
            TerritoryType = 1192,
            Aetheryte = "Living Memory",
            Position = new Vector3(437.48f, 22.6485f, -137.93f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
        new SightInfo
        {
            Id = 340,
            Name = "340: Steps of the Speaker",
            TerritoryType = 1192,
            Aetheryte = "Living Memory",
            Position = new Vector3(447.501f, 84.4827f, -693.345f),
            Emote = "lookout",
            TimeWindow = "",
            Weathers = new List<string> { }
        },
    };
}
