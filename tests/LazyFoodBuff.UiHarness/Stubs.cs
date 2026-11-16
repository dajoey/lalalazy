using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures;

// Everything in this file replaces the game / Dalamud runtime boundary of LazyFoodBuff. The window and chrome code
// under test is the real source; it only ever sees these stand-ins through the same member names it uses in game.

namespace Lumina.Excel.Sheets
{
    /// <summary>Stand-in for a Lumina text cell (the harness has no game data).</summary>
    internal readonly struct HarnessText(string value)
    {
        public string ExtractText() => value;
    }

    /// <summary>Stand-in for the ClassJob sheet row: the three members the window reads.</summary>
    internal readonly struct ClassJob(uint rowId, byte role, string name)
    {
        public uint RowId { get; } = rowId;
        public byte Role { get; } = role;
        public HarnessText Name { get; } = new(name);
    }
}

namespace LazyFoodBuff
{
    using Lumina.Excel.Sheets;

    /// <summary>The fake game state every scenario sets up before it draws.</summary>
    internal static class World
    {
        public const long JobIconTextureId = 0x4000;

        /// <summary>False reproduces "no character": Plugin.Objects.LocalPlayer is null.</summary>
        public static bool LoggedIn = true;
        public static bool Dead;
        public static bool BetweenAreas;
        public static bool OccupiedInEvent;
        public static bool InCombatDuty;

        /// <summary>The Well Fed status: the food (ItemFood row) and the time left on it.</summary>
        public static bool WellFed;
        public static uint WellFedFoodRow;
        public static TimeSpan WellFedLeft;

        public static List<Food> Foods { get; } = [];

        public static void Reset()
        {
            LoggedIn = true;
            Dead = false;
            BetweenAreas = false;
            OccupiedInEvent = false;
            InCombatDuty = false;
            WellFed = false;
            WellFedFoodRow = 0;
            WellFedLeft = TimeSpan.Zero;
            Foods.Clear();
        }
    }

    /// <summary>A food item: real item names, approximate stat bonuses and a settable inventory.</summary>
    internal sealed class Food
    {
        public Food(uint id, string name, uint itemFoodRowId, int hq, int nq,
            params (uint Param, uint NqPercent, uint NqCap, uint HqPercentBonus, uint HqCapBonus)[] stats)
        {
            Id = id;
            Name = name;
            ItemFoodRowId = itemFoodRowId;
            Hq = hq;
            Nq = nq;
            foreach (var s in stats)
                Stats[s.Param] = (s.NqPercent, s.NqCap, s.HqPercentBonus, s.HqCapBonus);
        }

        public uint Id { get; }
        public string Name { get; }
        public uint ItemFoodRowId { get; }
        public int Hq { get; set; }
        public int Nq { get; set; }
        public Dictionary<uint, (uint NqPercent, uint NqCap, uint HqPercentBonus, uint HqCapBonus)> Stats { get; } = new();

        public int InventoryCount(bool hq) => hq ? Hq : Nq;
    }

    internal sealed class FoodService
    {
        public IReadOnlyList<Food> AllFoods => World.Foods;

        internal static bool TryGetWellFedStatus(out uint itemFoodRowId, out TimeSpan remainingTime)
        {
            itemFoodRowId = World.WellFed ? World.WellFedFoodRow : 0;
            remainingTime = World.WellFed ? World.WellFedLeft : TimeSpan.Zero;
            return World.WellFed;
        }

        internal static bool IsInCombatDuty() => World.InCombatDuty;
    }

    /// <summary>The window only reads the log prefix and resets the tap; the real line format is compiled alongside.</summary>
    internal static class FoodTelemetry
    {
        public const string Prefix = FoodTelemetryFormat.Prefix;

        public static void Reset() { }
    }

    internal sealed class HarnessPlayer
    {
        public bool IsDead => World.Dead;
    }

    internal sealed class HarnessObjects
    {
        public HarnessPlayer? LocalPlayer => World.LoggedIn ? new HarnessPlayer() : null;
    }

    internal sealed class HarnessCondition
    {
        public bool this[ConditionFlag flag] => flag switch
        {
            ConditionFlag.BetweenAreas => World.BetweenAreas,
            ConditionFlag.OccupiedInEvent => World.OccupiedInEvent,
            _ => false,
        };
    }

    /// <summary>The ClassJob sheet: the real job names as the game stores them (lower case), in sheet order.</summary>
    internal sealed class HarnessSheet<T> : IEnumerable<T> where T : struct
    {
        private readonly List<T> _rows;
        private readonly Func<T, uint> _rowId;

        public HarnessSheet(List<T> rows, Func<T, uint> rowId)
        {
            _rows = rows;
            _rowId = rowId;
        }

        public bool TryGetRow(uint rowId, out T row)
        {
            foreach (var candidate in _rows)
            {
                if (_rowId(candidate) == rowId)
                {
                    row = candidate;
                    return true;
                }
            }
            row = default;
            return false;
        }

        public IEnumerator<T> GetEnumerator() => _rows.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class HarnessData
    {
        private static readonly List<ClassJob> Jobs =
        [
            new(0, 0, "adventurer"),
            new(19, 1, "paladin"), new(20, 2, "monk"), new(21, 1, "warrior"), new(22, 2, "dragoon"),
            new(23, 3, "bard"), new(24, 4, "white mage"), new(25, 4, "black mage"), new(27, 4, "summoner"),
            new(28, 4, "scholar"), new(30, 2, "ninja"), new(31, 3, "machinist"), new(32, 1, "dark knight"),
            new(33, 4, "astrologian"), new(34, 2, "samurai"), new(35, 4, "red mage"), new(36, 4, "blue mage"),
            new(37, 1, "gunbreaker"), new(38, 3, "dancer"), new(39, 2, "reaper"), new(40, 4, "sage"),
            new(41, 2, "viper"), new(42, 4, "pictomancer"), new(43, 4, "beastmaster"),
        ];

        public HarnessSheet<T>? GetExcelSheet<T>() where T : struct
        {
            if (typeof(T) == typeof(ClassJob))
                return (HarnessSheet<T>)(object)new HarnessSheet<ClassJob>(Jobs, j => j.RowId);
            return null;
        }
    }

    internal sealed class HarnessTextureWrap
    {
        public ImTextureID Handle => new(new IntPtr(World.JobIconTextureId));
    }

    internal sealed class HarnessTexture
    {
        public HarnessTextureWrap GetWrapOrEmpty() => new();
    }

    /// <summary>Every job icon resolves to one flat placeholder texture registered by the harness (the game icons are not available here).</summary>
    internal sealed class HarnessTextures
    {
        public HarnessTexture GetFromGameIcon(GameIconLookup lookup) => new();
    }

    internal sealed class Plugin
    {
        internal static HarnessObjects Objects { get; } = new();
        internal static HarnessCondition Condition { get; } = new();
        internal static HarnessData Data { get; } = new();
        internal static HarnessTextures Textures { get; } = new();

        public Configuration Config { get; set; } = new();
        internal readonly FoodService _service = new();

        /// <summary>Counts saves so the harness can tell that a control reported a change.</summary>
        public static int SaveCount { get; private set; }

        public void SaveConfig() => SaveCount++;
    }
}
