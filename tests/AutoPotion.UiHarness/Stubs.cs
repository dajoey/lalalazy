using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures;

// Game/runtime boundary of the AutoPotion window, nothing else. Every member mirrors the shape the real
// ConfigWindow reads from the plugin's services; the values come from the scenario being rendered.

namespace Lumina.Excel.Sheets
{
    /// <summary>Stand-in for the ClassJob sheet row: the window only reads <c>Name.ExtractText()</c>.</summary>
    internal readonly struct ClassJob
    {
        public ClassJob(string name) => Name = new HarnessText(name);
        public HarnessText Name { get; }
    }

    internal readonly struct HarnessText
    {
        private readonly string _text;
        public HarnessText(string text) => _text = text;
        public string ExtractText() => _text;
    }
}

namespace AutoPotion
{
    using Lumina.Excel.Sheets;

    internal sealed class Plugin
    {
        internal static HarnessObjects Objects { get; } = new();
        internal static HarnessCondition Condition { get; } = new();
        internal static HarnessData Data { get; } = new();
        internal static HarnessTextures Textures { get; } = new();
        internal static HarnessLog Log { get; } = new();

        public Configuration Config { get; set; } = new();
        public int SaveCount { get; private set; }
        public void SaveConfig() => SaveCount++;

        /// <summary>Clears every faked service back to "no character, no flags".</summary>
        internal static void ResetState()
        {
            Objects.LocalPlayer = null;
            Condition.Flags.Clear();
            Data.MissingJobs.Clear();
        }
    }

    internal sealed class HarnessObjects
    {
        public HarnessPlayer? LocalPlayer { get; set; }
    }

    internal sealed class HarnessPlayer
    {
        public HarnessClassJob ClassJob { get; init; } = new(0);
        public bool IsDead { get; init; }
        public uint MaxHp { get; init; } = 98_000;
    }

    internal readonly struct HarnessClassJob
    {
        public HarnessClassJob(uint rowId) => RowId = rowId;
        public uint RowId { get; }
    }

    internal sealed class HarnessCondition
    {
        public HashSet<ConditionFlag> Flags { get; } = [];
        public bool this[ConditionFlag flag] => Flags.Contains(flag);
    }

    /// <summary>ClassJob names as the game data stores them: lower case, the window upper-cases the first letter.</summary>
    internal sealed class HarnessData
    {
        private static readonly Dictionary<uint, string> Names = new()
        {
            [19] = "paladin",
            [21] = "warrior",
            [24] = "white mage",
            [25] = "black mage",
            [30] = "ninja",
            [32] = "dark knight",
            [33] = "astrologian",
            [37] = "gunbreaker",
            [38] = "dancer",
            [40] = "sage",
        };

        public HashSet<uint> MissingJobs { get; } = [];
        public HarnessSheet? GetExcelSheet<T>() where T : struct => new HarnessSheet(this);

        internal bool TryName(uint id, out string name)
        {
            name = "";
            return !MissingJobs.Contains(id) && Names.TryGetValue(id, out name!);
        }
    }

    internal sealed class HarnessSheet
    {
        private readonly HarnessData _data;
        public HarnessSheet(HarnessData data) => _data = data;

        public bool TryGetRow(uint id, out ClassJob row)
        {
            if (_data.TryName(id, out var name))
            {
                row = new ClassJob(name);
                return true;
            }
            row = default;
            return false;
        }
    }

    /// <summary>Game icons become small deterministic tiles registered with the rasterizer (see Program.IconTextures).</summary>
    internal sealed class HarnessTextures
    {
        public const long FirstIconTextureId = 1000;
        public HarnessTexture GetFromGameIcon(GameIconLookup lookup) => new((long)(FirstIconTextureId + lookup.IconId - 62000u));
    }

    internal sealed class HarnessTexture
    {
        private readonly long _id;
        public HarnessTexture(long id) => _id = id;
        public HarnessTexture GetWrapOrEmpty() => this;
        public ImTextureID Handle => new((IntPtr)_id);
    }

    internal sealed class HarnessLog
    {
        public void Information(string message) { }
        public void Debug(Exception exception, string message) { }
    }
}
