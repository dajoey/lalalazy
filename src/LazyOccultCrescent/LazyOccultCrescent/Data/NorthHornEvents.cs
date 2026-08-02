using System.Collections.Generic;
using LazyOccultCrescent.Enums;

namespace LazyOccultCrescent.Data;

// North Horn (territory 1346) event tables.
//
// Ids and names are datamined from the 7.55 sqpack (2026-08-01) and are exact:
//   FATEs           Fate sheet rows 2072-2084. 2072/2073 carry Rule 4 (the pot-fate
//                   rule, matching South Horn's 1976/1977); 2074-2084 carry Rule 1.
//   Crit encounters DynamicEvent rows 49-63, with the two towers at 64/65. This
//                   mirrors South Horn's 33-47 + 48 one-for-one: 15 CEs and a tower.
//
// Aethernet hints are added as they are OBSERVED IN GAME, not derived. Straight-line
// nearest is a decent default but terrain routinely beats it, so a confirmed hint
// always wins. Confirmed so far:
//   2075 Eye to Eye -> Sinking Sanctuary (2026-08-01)
//
// Demiatma / Soulshard / Note are deliberately NOT guessed. None of them
// are reachable from Excel - they come out of drop tables and LGB layout - and a wrong
// mapping is worse than a missing one because the Automator would path to the wrong
// side of the map. They are null until observed in game; ZoneDiscovery fills the
// position side automatically as events fire.
public static class NorthHornEvents
{
    public readonly static Dictionary<uint, EventData> Fates = new()
    {
        // -- Pot FATEs (Rule 4). Note is set so existing "skip pot fates" logic works.
        { 2072, new EventData { Id = 2072, Type = EventType.Fate, InternalName = "Daylight Pottery", Note = MonsterNote.PersistentPots } },
        { 2073, new EventData { Id = 2073, Type = EventType.Fate, InternalName = "In a Pot of Bother", Note = MonsterNote.PersistentPots } },

        // -- Standard FATEs (Rule 1)
        { 2074, new EventData { Id = 2074, Type = EventType.Fate, InternalName = "Raging Thrall" } },
        { 2075, new EventData { Id = 2075, Type = EventType.Fate, InternalName = "Eye to Eye", Aethernet = Aethernet.SinkingSanctuary } },
        { 2076, new EventData { Id = 2076, Type = EventType.Fate, InternalName = "Shoreline Showdown" } },
        { 2077, new EventData { Id = 2077, Type = EventType.Fate, InternalName = "Waved Away" } },
        { 2078, new EventData { Id = 2078, Type = EventType.Fate, InternalName = "Allure of the Occult" } },
        { 2079, new EventData { Id = 2079, Type = EventType.Fate, InternalName = "Inconstant Gardener" } },
        { 2080, new EventData { Id = 2080, Type = EventType.Fate, InternalName = "Territorial Dispute" } },
        { 2081, new EventData { Id = 2081, Type = EventType.Fate, InternalName = "A Rotten Affair" } },
        { 2082, new EventData { Id = 2082, Type = EventType.Fate, InternalName = "Gale-force Encounter" } },
        { 2083, new EventData { Id = 2083, Type = EventType.Fate, InternalName = "Scale Model" } },
        { 2084, new EventData { Id = 2084, Type = EventType.Fate, InternalName = "Thunderregnum" } },
    };

    public readonly static Dictionary<uint, EventData> CriticalEncounters = new()
    {
        { 49, new EventData { Id = 49, Type = EventType.CriticalEncounter, InternalName = "Many Mouths to Feed" } },
        { 50, new EventData { Id = 50, Type = EventType.CriticalEncounter, InternalName = "Doubled Trouble" } },
        { 51, new EventData { Id = 51, Type = EventType.CriticalEncounter, InternalName = "Quarried Away" } },
        { 52, new EventData { Id = 52, Type = EventType.CriticalEncounter, InternalName = "Forbidden Folios" } },
        { 53, new EventData { Id = 53, Type = EventType.CriticalEncounter, InternalName = "Cursed Resurgence" } },
        { 54, new EventData { Id = 54, Type = EventType.CriticalEncounter, InternalName = "Imbalanced Diet" } },
        { 55, new EventData { Id = 55, Type = EventType.CriticalEncounter, InternalName = "Web of Terror" } },
        { 56, new EventData { Id = 56, Type = EventType.CriticalEncounter, InternalName = "A Beast Unleashed" } },
        { 57, new EventData { Id = 57, Type = EventType.CriticalEncounter, InternalName = "Dark Artistry" } },
        { 58, new EventData { Id = 58, Type = EventType.CriticalEncounter, InternalName = "Familiar Tactics" } },
        { 59, new EventData { Id = 59, Type = EventType.CriticalEncounter, InternalName = "Appalling Behavior" } },
        { 60, new EventData { Id = 60, Type = EventType.CriticalEncounter, InternalName = "Tiny Terror" } },
        { 61, new EventData { Id = 61, Type = EventType.CriticalEncounter, InternalName = "Lost on the Wind" } },
        { 62, new EventData { Id = 62, Type = EventType.CriticalEncounter, InternalName = "Ahead of the Competition" } },
        { 63, new EventData { Id = 63, Type = EventType.CriticalEncounter, InternalName = "Accept No Imitators" } },

        // The towers are listed so the UI can name them; the ForkedTower module
        // gates on player status, not on this table.
        { 64, new EventData { Id = 64, Type = EventType.CriticalEncounter, InternalName = "The Forked Tower: Magic" } },
        { 65, new EventData { Id = 65, Type = EventType.CriticalEncounter, InternalName = "The Forked Tower: Magic (Extreme)" } },
    };
}
