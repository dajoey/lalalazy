namespace RotationSolver.Decisions;

/// <summary>One row of the job picker: a role heading and its jobs, as job keys ("SAM").</summary>
/// <param name="Name">The role heading shown over the buttons.</param>
/// <param name="Jobs">The job keys of the role, in picker order.</param>
public sealed record JobGroup(string Name, IReadOnlyList<string> Jobs);

/// <summary>
///     Fork-only decision core (no Dalamud or game types, compiles standalone): the 21 jobs PvP Solver has a rotation
///     for, grouped for the settings window's job picker, and the mapping of a base class to its job. Job keys are the
///     member names of <c>ECommons.ExcelServices.Job</c> (what <c>Job.ToString()</c> returns); the tests tie them to
///     the enum and to the rotation types.
/// </summary>
public static class JobKey
{
    /// <summary>The picker rows, in display order.</summary>
    public static readonly IReadOnlyList<JobGroup> Groups =
    [
        new("Tanks", ["PLD", "WAR", "DRK", "GNB"]),
        new("Healers", ["WHM", "SCH", "AST", "SGE"]),
        new("Melee", ["MNK", "DRG", "NIN", "SAM", "RPR", "VPR"]),
        new("Ranged", ["BRD", "MCH", "DNC"]),
        new("Casters", ["BLM", "SMN", "RDM", "PCT"]),
    ];

    /// <summary>All 21 job keys, in picker order.</summary>
    public static readonly IReadOnlyList<string> All = [.. Groups.SelectMany(g => g.Jobs)];

    /// <summary>The job key a base class advances to (GLA to PLD); "" for a key that is not a base class or is ambiguous (ACN).</summary>
    public static string JobOfBaseClass(string key) => key switch
    {
        "GLA" => "PLD",
        "MRD" => "WAR",
        "PGL" => "MNK",
        "LNC" => "DRG",
        "ARC" => "BRD",
        "ROG" => "NIN",
        "CNJ" => "WHM",
        "THM" => "BLM",
        _ => string.Empty,
    };

    /// <summary>Whether the key is one of the 21 jobs.</summary>
    public static bool IsPvpJob(string? key) => key != null && All.Contains(key);

    /// <summary>
    ///     The picker's job for a played job key: the key itself when it is one of the 21, the job of a base class,
    ///     otherwise <paramref name="fallback"/> (not logged in, a crafter, an ambiguous class).
    /// </summary>
    public static string PickFor(string? playedKey, string fallback)
    {
        if (IsPvpJob(playedKey))
        {
            return playedKey!;
        }

        string job = JobOfBaseClass(playedKey ?? string.Empty);
        return job.Length > 0 ? job : fallback;
    }

    /// <summary>The group a job belongs to, or null.</summary>
    public static JobGroup? GroupOf(string? key) => Groups.FirstOrDefault(g => key != null && g.Jobs.Contains(key));
}
