using System.Reflection;
using System.Text.Json;
using ArmoireAutoFill.Models;
using ECommons.DalamudServices;

namespace ArmoireAutoFill.Data;

public static class ArmoireGearDatabase
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static List<ArmoireItem> AllItems { get; private set; } = [];
    public static List<DungeonInfo> DungeonSets { get; private set; } = [];

    public static void LoadFromEmbeddedResource()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "ArmoireAutoFill.Data.armoire_gear.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Svc.Log.Error($"Failed to load embedded resource: {resourceName}");
            return;
        }

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var data = JsonSerializer.Deserialize<EmbeddedGearData>(json, _jsonOptions);

        if (data?.dungeons == null)
            return;

        var allItems = new List<ArmoireItem>();
        var dungeonSets = new List<DungeonInfo>();

        foreach (var dungeonEntry in data.dungeons)
        {
            var dungeon = new DungeonInfo
            {
                ContentFinderConditionId = dungeonEntry.cfcId,
                Name = dungeonEntry.name,
            };

            foreach (var itemEntry in dungeonEntry.items)
            {
                var slot = ParseSlot(itemEntry.slot);
                var item = new ArmoireItem
                {
                    ItemId = itemEntry.id,
                    Name = itemEntry.name,
                    Slot = slot,
                    DungeonName = dungeon.Name,
                    ContentFinderConditionId = dungeon.ContentFinderConditionId,
                };
                dungeon.Items.Add(item);
                allItems.Add(item);
            }

            dungeonSets.Add(dungeon);
        }

        AllItems = allItems;
        DungeonSets = dungeonSets;
    }

    private static GearSlot ParseSlot(string slot) => slot switch
    {
        "Head" => GearSlot.Head,
        "Body" => GearSlot.Body,
        "Hands" => GearSlot.Hands,
        "Legs" => GearSlot.Legs,
        "Feet" => GearSlot.Feet,
        _ => GearSlot.Body,
    };

    public static int TotalItems => AllItems.Count;
    public static int OwnedCount => AllItems.Count(i => i.Owned != OwnershipStatus.NotOwned);
    public static int MissingCount => AllItems.Count(i => i.Owned == OwnershipStatus.NotOwned);

    private sealed class EmbeddedGearData
    {
        public List<EmbeddedDungeon> dungeons { get; set; } = [];
    }

    private sealed class EmbeddedDungeon
    {
        public uint cfcId { get; set; }
        public string name { get; set; } = string.Empty;
        public uint level { get; set; }
        public List<EmbeddedItem> items { get; set; } = [];
    }

    private sealed class EmbeddedItem
    {
        public uint id { get; set; }
        public string name { get; set; } = string.Empty;
        public string slot { get; set; } = string.Empty;
    }
}
