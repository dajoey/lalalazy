using System.Text.Json;
using System.Text.Json.Serialization;
using Lalalazy.Crucible;

namespace LazyCrucible;

/// <summary> One line of guide text with the sources that back it. </summary>
public sealed class GuideNote
{
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("src")] public List<string> Src { get; set; } = [];
}

/// <summary> A mechanic or a dangerous hit: what it looks like and what to do. </summary>
public sealed class GuideMechanic
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("ids")] public List<uint> Ids { get; set; } = [];
    [JsonPropertyName("cast")] public double? Cast { get; set; }
    [JsonPropertyName("tell")] public string Tell { get; set; } = "";
    [JsonPropertyName("do")] public string Do { get; set; } = "";
    [JsonPropertyName("src")] public List<string> Src { get; set; } = [];
}

/// <summary> A kin utility the fight calls for: interrupt (Soul Crush), dispel (Quelling Wave) or cleanse (Scouring Ash). </summary>
public sealed class GuideCounter
{
    [JsonPropertyName("need")] public string Need { get; set; } = "";
    [JsonPropertyName("what")] public string What { get; set; } = "";
    [JsonPropertyName("src")] public List<string> Src { get; set; } = [];

    /// <summary> Backed by the game's own enemy panel (the horn ranking scores it). </summary>
    public bool FromPanel => Src.Contains("panel");

    public CrucibleNeeds AsNeed => Need switch
    {
        "interrupt" => CrucibleNeeds.Interrupt,
        "dispel" => CrucibleNeeds.Dispel,
        "cleanse" => CrucibleNeeds.Cleanse,
        _ => CrucibleNeeds.None,
    };
}

/// <summary> Everything the guide knows about one battle. </summary>
public sealed class GuideFight
{
    [JsonPropertyName("board")] public int Board { get; set; }
    [JsonPropertyName("battle")] public int Battle { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("where")] public string Where { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("killOrder")] public GuideNote? KillOrder { get; set; }
    [JsonPropertyName("mechanics")] public List<GuideMechanic> Mechanics { get; set; } = [];
    [JsonPropertyName("hits")] public List<GuideMechanic> Hits { get; set; } = [];
    [JsonPropertyName("counters")] public List<GuideCounter> Counters { get; set; } = [];
    [JsonPropertyName("threats")] public List<string> Threats { get; set; } = [];
    [JsonPropertyName("bring")] public List<GuideNote> Bring { get; set; } = [];
    [JsonPropertyName("unknown")] public List<string> Unknown { get; set; } = [];

    /// <summary> <see cref="Threats"/> as flags (unknown words are ignored and reported by <see cref="CrucibleGuide.Validate"/>). </summary>
    [JsonIgnore]
    public CrucibleThreat ThreatFlags
    {
        get
        {
            var t = CrucibleThreat.None;
            foreach (var s in Threats)
                if (CrucibleGuide.TryThreat(s, out var f))
                    t |= f;
            return t;
        }
    }
}

/// <summary> The whole guide file. </summary>
public sealed class GuideFile
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("sources")] public Dictionary<string, string> Sources { get; set; } = [];
    [JsonPropertyName("fights")] public List<GuideFight> Fights { get; set; } = [];
}

/// <summary>
///     The per-fight information repository (embedded <c>CrucibleGuide.json</c>). PURE: parsing, lookup and the
///     consistency checks that keep the guide and the horn picks from disagreeing (every panel need the ranking scores
///     is listed as a counter, and no counter claims the panel for a need the panel does not have).
/// </summary>
internal sealed class CrucibleGuide
{
    private readonly Dictionary<(int, int), GuideFight> _byBattle = [];

    public GuideFile File { get; }

    private CrucibleGuide(GuideFile file)
    {
        File = file;
        foreach (var f in file.Fights)
            _byBattle[(f.Board, f.Battle)] = f;
    }

    public static CrucibleGuide Parse(string json) =>
        new(JsonSerializer.Deserialize<GuideFile>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip })
            ?? new GuideFile());

    public GuideFight? Fight(int board, int battle) => _byBattle.GetValueOrDefault((board, battle));

    /// <summary> The fight's threats, or none when the fight is missing (used by <see cref="RunContext.Build"/>). </summary>
    public CrucibleThreat ThreatsOf(int board, int battle) => Fight(board, battle)?.ThreatFlags ?? CrucibleThreat.None;

    private static readonly Dictionary<string, CrucibleThreat> ThreatWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["poison"] = CrucibleThreat.Poison,
        ["paralysis"] = CrucibleThreat.Paralysis,
        ["blind"] = CrucibleThreat.Blind,
        ["petrify"] = CrucibleThreat.Petrify,
        ["sleep"] = CrucibleThreat.Sleep,
        ["doom"] = CrucibleThreat.Doom,
        ["heavy"] = CrucibleThreat.Heavy,
        ["confusion"] = CrucibleThreat.Confusion,
        ["bind"] = CrucibleThreat.Bind,
        ["knockback"] = CrucibleThreat.Knockback,
        ["roomwide"] = CrucibleThreat.RoomWide,
        ["tankbuster"] = CrucibleThreat.Tankbuster,
        ["adds"] = CrucibleThreat.Adds,
        ["dontattack"] = CrucibleThreat.DontAttack,
        ["counterstance"] = CrucibleThreat.CounterStance,
        ["invulnerable"] = CrucibleThreat.Invulnerable,
    };

    public static bool TryThreat(string word, out CrucibleThreat threat) => ThreatWords.TryGetValue(word, out threat);

    /// <summary> Short label for a threat word. </summary>
    public static string ThreatLabel(string word) => word.ToLowerInvariant() switch
    {
        "roomwide" => "room-wide hits",
        "tankbuster" => "big single hits",
        "dontattack" => "do-not-attack targets",
        "counterstance" => "counter stances",
        "invulnerable" => "invulnerable phases",
        "petrify" => "petrification",
        _ => word.ToLowerInvariant(),
    };

    /// <summary>
    ///     Problems with the file: missing or duplicate battles, unknown threat words, a source key not in the
    ///     source list, and counters that disagree with the enemy panel the horn ranking scores. Empty = consistent.
    /// </summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        var seen = new HashSet<(int, int)>();
        foreach (var f in File.Fights)
        {
            var key = (f.Board, f.Battle);
            if (!seen.Add(key))
                problems.Add($"{f.Board}:{f.Battle} listed twice");

            foreach (var t in f.Threats)
                if (!TryThreat(t, out _))
                    problems.Add($"{f.Board}:{f.Battle} unknown threat '{t}'");

            foreach (var src in AllSources(f))
                if (!File.Sources.ContainsKey(src.Split(':')[0]))
                    problems.Add($"{f.Board}:{f.Battle} unknown source '{src}'");

            var panel = BST_CrucibleData.BattleNeeds(f.Board, f.Battle);
            var listed = CrucibleNeeds.None;
            foreach (var c in f.Counters)
            {
                if (c.AsNeed == CrucibleNeeds.None)
                {
                    problems.Add($"{f.Board}:{f.Battle} unknown counter need '{c.Need}'");
                    continue;
                }
                if (c.FromPanel)
                {
                    listed |= c.AsNeed;
                    if ((panel & c.AsNeed) == 0)
                        problems.Add($"{f.Board}:{f.Battle} counter '{c.Need}' claims the panel, which does not list it");
                }
            }
            if ((panel & ~listed) != CrucibleNeeds.None)
                problems.Add($"{f.Board}:{f.Battle} panel needs {panel & ~listed} missing from counters");
        }

        foreach (var b in BST_CrucibleData.Battles)
            if (!seen.Contains((b.Board, b.Battle)))
                problems.Add($"{b.Board}:{b.Battle} has no guide entry");
        return problems;
    }

    private static IEnumerable<string> AllSources(GuideFight f)
    {
        if (f.KillOrder is { } k)
            foreach (var s in k.Src)
                yield return s;
        foreach (var m in f.Mechanics.Concat(f.Hits))
            foreach (var s in m.Src)
                yield return s;
        foreach (var c in f.Counters)
            foreach (var s in c.Src)
                yield return s;
        foreach (var b in f.Bring)
            foreach (var s in b.Src)
                yield return s;
    }
}
