namespace RotationSolver.Decisions;

/// <summary>
///     Fork-only decision core (no Dalamud or game types, compiles standalone): which tab of the legacy Rotation
///     Solver window <c>/pvpsolver advanced [tab]</c> and the Advanced tab's quick links open. The result is the
///     name of a member of the legacy window's tab enum, or null.
/// </summary>
public static class AdvancedTab
{
    /// <summary>The legacy tabs that can be opened, as enum member names, in the order the quick links show them.</summary>
    public static readonly IReadOnlyList<string> Names = ["Actions", "List", "Basic", "UI", "Auto", "Target", "Extra", "Debug", "Main"];

    /// <summary>Plain-English names accepted besides the member names.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lists"] = "List",
        ["timing"] = "Basic",
        ["interface"] = "UI",
        ["windows"] = "UI",
        ["macros"] = "Main",
        ["about"] = "Main",
    };

    /// <summary>The tab member name for text typed after <c>/pvpsolver advanced</c>, or null when it names no openable tab.</summary>
    public static string? Resolve(string? text)
    {
        string word = (text ?? string.Empty).Trim();
        if (word.Length == 0)
        {
            return null;
        }

        foreach (string name in Names)
        {
            if (string.Equals(name, word, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return Aliases.TryGetValue(word, out string? alias) ? alias : null;
    }
}
