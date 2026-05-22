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
    public string TimeWindow { get; set; } = string.Empty; // e.g. "08:00-12:00" or null
    public List<string> Weathers { get; set; } = []; // e.g. ["Fair Skies", "Clear Skies"] or empty for any
}

public static class SightseeingDatabase
{
    public static readonly List<SightInfo> Sights = new()
    {
        new SightInfo
        {
            Id = 1,
            Name = "001: Barracuda Piers",
            TerritoryType = 129, // Limsa Upper
            Aetheryte = "Limsa Lominsa",
            Position = new Vector3(-58.5f, 40.0f, -143.0f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 2,
            Name = "002: The Astalicia",
            TerritoryType = 128, // Limsa Lower
            Aetheryte = "Limsa Lominsa",
            Position = new Vector3(-202.0f, 39.0f, 155.0f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 3,
            Name = "003: Seasong Grotto",
            TerritoryType = 134, // Middle La Noscea
            Aetheryte = "Limsa Lominsa", // Summerford Farms is closer, but tp to Limsa is default
            Position = new Vector3(-62.0f, 15.0f, -92.0f),
            Emote = "pray",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Rain" }
        },
        new SightInfo
        {
            Id = 4,
            Name = "004: The Skylift",
            TerritoryType = 134,
            Aetheryte = "Limsa Lominsa",
            Position = new Vector3(-234.0f, 18.0f, -164.0f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 5,
            Name = "005: La Noscea River",
            TerritoryType = 134,
            Aetheryte = "Limsa Lominsa",
            Position = new Vector3(216.0f, 16.0f, 250.0f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Clouds" }
        },
        new SightInfo
        {
            Id = 6,
            Name = "006: Oschon's Torch",
            TerritoryType = 135, // Lower La Noscea
            Aetheryte = "Limsa Lominsa",
            Position = new Vector3(100.0f, 19.0f, 750.0f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 7,
            Name = "007: Red Rooster Mead",
            TerritoryType = 135,
            Aetheryte = "Limsa Lominsa",
            Position = new Vector3(500.0f, 17.0f, -100.0f),
            Emote = "lookout",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Fog" }
        },
        new SightInfo
        {
            Id = 8,
            Name = "008: Brewer's Beacon",
            TerritoryType = 137, // Western La Noscea
            Aetheryte = "Aleport",
            Position = new Vector3(320.0f, 20.0f, 320.0f),
            Emote = "lookout",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 9,
            Name = "009: The Leatherworkers' Guild",
            TerritoryType = 132, // New Gridania
            Aetheryte = "Gridania",
            Position = new Vector3(88.0f, -7.0f, 40.0f),
            Emote = "salute",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Clouds" }
        },
        new SightInfo
        {
            Id = 10,
            Name = "010: Apcalis Falls",
            TerritoryType = 133, // Old Gridania
            Aetheryte = "Gridania",
            Position = new Vector3(-80.0f, 5.0f, -120.0f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 11,
            Name = "011: Bentbranch Meadows",
            TerritoryType = 136, // Central Shroud
            Aetheryte = "Bentbranch Meadows",
            Position = new Vector3(-200.0f, 12.0f, 240.0f),
            Emote = "sit",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 12,
            Name = "012: The Sanctum of the Twelve",
            TerritoryType = 138, // East Shroud
            Aetheryte = "The Hawthorne Hut",
            Position = new Vector3(350.0f, 25.0f, -320.0f),
            Emote = "pray",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 13,
            Name = "013: Little Solace",
            TerritoryType = 138,
            Aetheryte = "The Hawthorne Hut",
            Position = new Vector3(450.0f, 10.0f, -100.0f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Clear Skies" }
        },
        new SightInfo
        {
            Id = 14,
            Name = "014: The Royal Promenade",
            TerritoryType = 131, // Ul'dah Steps of Thal
            Aetheryte = "Ul'dah",
            Position = new Vector3(10.0f, 15.0f, -10.0f),
            Emote = "salute",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 15,
            Name = "015: The Gold Playbox",
            TerritoryType = 130, // Ul'dah Steps of Nald
            Aetheryte = "Ul'dah",
            Position = new Vector3(30.0f, 5.0f, 50.0f),
            Emote = "lookout",
            TimeWindow = "12:00-17:00",
            Weathers = new List<string> { "Clouds" }
        },
        new SightInfo
        {
            Id = 16,
            Name = "016: The Scorpions' Den",
            TerritoryType = 140, // Western Thanalan
            Aetheryte = "Horizon",
            Position = new Vector3(-250.0f, 10.0f, 180.0f),
            Emote = "lookout",
            TimeWindow = "18:00-05:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 17,
            Name = "017: The Silversand",
            TerritoryType = 140,
            Aetheryte = "Horizon",
            Position = new Vector3(-600.0f, -5.0f, 400.0f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Fog" }
        },
        new SightInfo
        {
            Id = 18,
            Name = "018: The Goon",
            TerritoryType = 145, // Eastern Thanalan
            Aetheryte = "Camp Drybone",
            Position = new Vector3(-120.0f, 25.0f, -50.0f),
            Emote = "comfort",
            TimeWindow = "17:00-18:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        },
        new SightInfo
        {
            Id = 19,
            Name = "019: The Invisible City",
            TerritoryType = 145,
            Aetheryte = "Camp Drybone",
            Position = new Vector3(-400.0f, 15.0f, 250.0f),
            Emote = "lookout",
            TimeWindow = "08:00-12:00",
            Weathers = new List<string> { "Clouds" }
        },
        new SightInfo
        {
            Id = 20,
            Name = "020: Highbridge",
            TerritoryType = 145,
            Aetheryte = "Camp Drybone",
            Position = new Vector3(300.0f, 40.0f, -200.0f),
            Emote = "pray",
            TimeWindow = "05:00-08:00",
            Weathers = new List<string> { "Fair Skies", "Clear Skies" }
        }
    };
}
