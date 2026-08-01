using System.Collections.Generic;
using System.Numerics;
using LazyOccultCrescent.Enums;

namespace LazyOccultCrescent.Data;

public struct EventData
{
    public uint Id;

    public EventType Type;

    public string InternalName;

    public Demiatma? Demiatma;

    // North Horn drops Phantom Dispellers, not demiatma. The two are mutually
    // exclusive per zone - South Horn events carry Demiatma, North Horn events
    // carry Dispeller. Never both.
    public PhantomDispeller? Dispeller;

    public SoulShard? Soulshard;

    public MonsterNote? Note;

    public Aethernet? Aethernet;

    public Vector3? StartPosition;

    public float? Radius;

    private readonly static Dictionary<uint, EventData> SouthHornFates = new()
    {
        {
            1962,
            new EventData
            {
                Id = 1962,
                Type = EventType.Fate,
                InternalName = "Rough Waters",
                Demiatma = Enums.Demiatma.Azurite,
                StartPosition = new Vector3(162.00f, 56.00f, 676.00f),
            }
        },
        {
            1963,
            new EventData
            {
                Id = 1963,
                Type = EventType.Fate,
                InternalName = "The Golden Guardian",
                Demiatma = Enums.Demiatma.Azurite,
                StartPosition = new Vector3(373.20f, 70.00f, 486.00f),
            }
        },
        {
            1964,
            new EventData
            {
                Id = 1964,
                Type = EventType.Fate,
                InternalName = "King of the Crescent",
                Demiatma = Enums.Demiatma.Orpiment,
                StartPosition = new Vector3(-226.10f, 116.38f, 254.00f),
            }
        },
        {
            1965,
            new EventData
            {
                Id = 1965,
                Type = EventType.Fate,
                InternalName = "The Winged Terror",
                Demiatma = Enums.Demiatma.Realgar,
                Aethernet = Enums.Aethernet.TheWanderersHaven,
                StartPosition = new Vector3(-548.50f, 3.00f, -595.00f),
            }
        },
        {
            1966,
            new EventData
            {
                Id = 1966,
                Type = EventType.Fate,
                InternalName = "An Unending Duty",
                Demiatma = Enums.Demiatma.Malachite,
                StartPosition = new Vector3(-223.10f, 107.00f, 36.00f),
            }
        },
        {
            1967,
            new EventData
            {
                Id = 1967,
                Type = EventType.Fate,
                InternalName = "Brain Drain",
                Demiatma = Enums.Demiatma.Realgar,
                Aethernet = Enums.Aethernet.CrystallizedCaverns,
                StartPosition = new Vector3(-48.10f, 111.76f, -320.00f),
            }
        },
        {
            1968,
            new EventData
            {
                Id = 1968,
                Type = EventType.Fate,
                InternalName = "A Delicate Balance",
                Demiatma = Enums.Demiatma.Verdigris,
                StartPosition = new Vector3(-370.00f, 75.00f, 650.00f),
            }
        },
        {
            1969,
            new EventData
            {
                Id = 1969,
                Type = EventType.Fate,
                InternalName = "Sworn to Soil",
                Demiatma = Enums.Demiatma.Verdigris,
                StartPosition = new Vector3(-589.10f, 96.50f, 333.00f),
            }
        },
        {
            1970,
            new EventData
            {
                Id = 1970,
                Type = EventType.Fate,
                InternalName = "A Prying Eye",
                Demiatma = Enums.Demiatma.Azurite,
                StartPosition = new Vector3(-71.00f, 71.31f, 557.00f),
            }
        },
        {
            1971,
            new EventData
            {
                Id = 1971,
                Type = EventType.Fate,
                InternalName = "Fatal Allure",
                Demiatma = Enums.Demiatma.Orpiment,
                StartPosition = new Vector3(79.00f, 97.86f, 278.00f),
            }
        },
        {
            1972,
            new EventData
            {
                Id = 1972,
                Type = EventType.Fate,
                InternalName = "Serving Darkness",
                Demiatma = Enums.Demiatma.CaputMortuum,
                StartPosition = new Vector3(413.00f, 96.00f, -13.00f),
            }
        },
        {
            1976,
            new EventData
            {
                Id = 1976,
                Type = EventType.Fate,
                InternalName = "Persistent Pots",
                Demiatma = Enums.Demiatma.Orpiment,
                Note = MonsterNote.PersistentPots,
                StartPosition = new Vector3(200.00f, 111.73f, -215.00f),
            }
        },
        {
            1977,
            new EventData
            {
                Id = 1977,
                Type = EventType.Fate,
                InternalName = "Pleading Pots",
                Demiatma = Enums.Demiatma.Verdigris,
                Note = MonsterNote.PersistentPots,
                StartPosition = new Vector3(-481.00f, 75.00f, 528.00f),
            }
        },
    };

    private readonly static Dictionary<uint, EventData> SouthHornCriticalEncounters = new()
    {
        {
            48,
            new EventData
            {
                Id = 48,
                Type = EventType.CriticalEncounter,
                InternalName = "The Forked Tower: Blood",
            }
        },
        {
            33,
            new EventData
            {
                Id = 33,
                Type = EventType.CriticalEncounter,
                InternalName = "Scourge of the Mind",
                Demiatma = Enums.Demiatma.Azurite,
                Aethernet = Enums.Aethernet.Eldergrowth,
            }
        },
        {
            34,
            new EventData
            {
                Id = 34,
                Type = EventType.CriticalEncounter,
                InternalName = "The Black Regiment",
                Demiatma = Enums.Demiatma.Orpiment,
                Soulshard = SoulShard.Ranger,
                Note = MonsterNote.BlackChocobos,
                Aethernet = Enums.Aethernet.Eldergrowth,
            }
        },
        {
            35,
            new EventData
            {
                Id = 35,
                Type = EventType.CriticalEncounter,
                InternalName = "The Unbridled",
                Demiatma = Enums.Demiatma.Azurite,
                Soulshard = SoulShard.Berserker,
                Note = MonsterNote.CrescentBerserker,
                Aethernet = Enums.Aethernet.Eldergrowth,
            }
        },
        {
            36,
            new EventData
            {
                Id = 36,
                Type = EventType.CriticalEncounter,
                InternalName = "Crawling Death",
                Demiatma = Enums.Demiatma.Azurite,
                Aethernet = Enums.Aethernet.Eldergrowth,
            }
        },
        {
            37,
            new EventData
            {
                Id = 37,
                Type = EventType.CriticalEncounter,
                InternalName = "Calamity Bound",
                Demiatma = Enums.Demiatma.Verdigris,
                Note = MonsterNote.CloisterDemon,
                Aethernet = Enums.Aethernet.Stonemarsh,
            }
        },
        {
            38,
            new EventData
            {
                Id = 38,
                Type = EventType.CriticalEncounter,
                InternalName = "Trial by Claw",
                Demiatma = Enums.Demiatma.Malachite,
                Aethernet = Enums.Aethernet.CrystallizedCaverns,
            }
        },
        {
            39,
            new EventData
            {
                Id = 39,
                Type = EventType.CriticalEncounter,
                InternalName = "From Times Bygone",
                Demiatma = Enums.Demiatma.Malachite,
                Note = MonsterNote.MythicIdol,
                Aethernet = Enums.Aethernet.Stonemarsh,
            }
        },
        {
            40,
            new EventData
            {
                Id = 40,
                Type = EventType.CriticalEncounter,
                InternalName = "Company of Stone",
                Demiatma = Enums.Demiatma.CaputMortuum,
                Aethernet = Enums.Aethernet.BaseCamp,
            }
        },
        {
            41,
            new EventData
            {
                Id = 41,
                Type = EventType.CriticalEncounter,
                InternalName = "Shark Attack",
                Demiatma = Enums.Demiatma.Realgar,
                Note = MonsterNote.NymianPotaladus,
                Aethernet = Enums.Aethernet.TheWanderersHaven,
            }
        },
        {
            42,
            new EventData
            {
                Id = 42,
                Type = EventType.CriticalEncounter,
                InternalName = "On the Hunt",
                Demiatma = Enums.Demiatma.CaputMortuum,
                Soulshard = SoulShard.Oracle,
                Aethernet = Enums.Aethernet.Eldergrowth,
            }
        },
        {
            43,
            new EventData
            {
                Id = 43,
                Type = EventType.CriticalEncounter,
                InternalName = "With Extreme Prejudice",
                Demiatma = Enums.Demiatma.Realgar,
                Aethernet = Enums.Aethernet.TheWanderersHaven,
            }
        },
        {
            44,
            new EventData
            {
                Id = 44,
                Type = EventType.CriticalEncounter,
                InternalName = "Noise Complaint",
                Demiatma = Enums.Demiatma.Orpiment,
                Aethernet = Enums.Aethernet.BaseCamp,
            }
        },
        {
            45,
            new EventData
            {
                Id = 45,
                Type = EventType.CriticalEncounter,
                InternalName = "Cursed Concern",
                Demiatma = Enums.Demiatma.Realgar,
                Note = MonsterNote.TradeTortoise,
                Aethernet = Enums.Aethernet.TheWanderersHaven,
            }
        },
        {
            46,
            new EventData
            {
                Id = 46,
                Type = EventType.CriticalEncounter,
                InternalName = "Eternal Watch",
                Demiatma = Enums.Demiatma.CaputMortuum,
                Aethernet = Enums.Aethernet.Eldergrowth,
            }
        },
        {
            47,
            new EventData
            {
                Id = 47,
                Type = EventType.CriticalEncounter,
                InternalName = "Flame of Dusk",
                Demiatma = Enums.Demiatma.Malachite,
                Aethernet = Enums.Aethernet.CrystallizedCaverns,
            }
        },
    };

    // ---------------------------------------------------------------------------
    // Zone-merged views.
    //
    // FATE ids (1962-1977 vs 2072-2084) and dynamic event ids (33-48 vs 49-65) do
    // not overlap between horns, so a single flat lookup is safe: only events that
    // are actually live in the current zone are ever looked up by id. The per-zone
    // dictionaries below exist for UI that wants to LIST events rather than resolve
    // one, which does need scoping or South Horn CEs bleed into a North Horn config.
    // ---------------------------------------------------------------------------

    public readonly static Dictionary<uint, EventData> Fates = Merge(SouthHornFates, NorthHornEvents.Fates);

    public readonly static Dictionary<uint, EventData> CriticalEncounters = Merge(SouthHornCriticalEncounters, NorthHornEvents.CriticalEncounters);

    private static Dictionary<uint, EventData> Merge(Dictionary<uint, EventData> a, Dictionary<uint, EventData> b)
    {
        var result = new Dictionary<uint, EventData>(a);
        foreach (var (key, value) in b)
        {
            result[key] = value;
        }

        return result;
    }

    public static Dictionary<uint, EventData> FatesFor(uint territory)
    {
        return territory == ZoneData.NORTHHORN ? NorthHornEvents.Fates : SouthHornFates;
    }

    public static Dictionary<uint, EventData> CriticalEncountersFor(uint territory)
    {
        return territory == ZoneData.NORTHHORN ? NorthHornEvents.CriticalEncounters : SouthHornCriticalEncounters;
    }

    public static Dictionary<uint, EventData> CurrentFates
    {
        get => FatesFor(ZoneData.CurrentTerritory);
    }

    public static Dictionary<uint, EventData> CurrentCriticalEncounters
    {
        get => CriticalEncountersFor(ZoneData.CurrentTerritory);
    }
}
