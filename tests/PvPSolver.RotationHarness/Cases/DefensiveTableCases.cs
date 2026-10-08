using System.Text.RegularExpressions;
using RotationSolver.Basic.Data;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Round 3, Part A, item 7: the generic defensive table (<see cref="DefensiveTable"/>). T1 on the pure data
///     and its resolve functions: row counts, unique ids, every (enum name, id) pair equal to the real
///     <see cref="ActionID"/>, every status id tied to the real <see cref="StatusID"/> enum, every Fire and Gate row
///     off by default, every Param row on at the number it replaced, percents inside their floor, no row for Guard,
///     Purify or Elixir, the Recuperate floor, the per-job row sets, and the clamp / default / exact-scale rules.
///     Canaries: a table with a duplicated id, a Fire row that is on by default, and a clamp that ignores the floor.
/// </summary>
internal static class DefensiveTableCases
{
    // The statuses the table names, by the fork's StatusID enum member (checked below against the real enum, and
    // against the game's Status sheet by hand: name and description of every id, 2026.09.15 data).
    private static readonly (uint Id, string Name)[] StatusNames =
    [
        (4168, "Rampart_4168"), (4481, "StoneskinIi"), (3086, "Aquaveil_3086"), (4316, "WreathOfIce"),
        (4114, "TemperaCoat_4114"), (4320, "Forte"), (3224, "RadiantAegis_3224"), (2861, "CrestOfTimeBorrowed_2861"),
        (4096, "HardenedScales"), (1308, "BlackestNight_1308"), (3026, "HolySheltron_3026"), (3031, "StemTheTide_3031"),
        (4295, "HeartOfCorundum_4295"), (3171, "EarthResonance"), (3162, "HoningDance"), (3039, "UndeadRedemption"),
        (1316, "Hidden_1316"),
    ];

    // The fixed numbers the six Param rows replaced, as written at origin/main 4d6d0e9f (0 to 100 scale for GNB and DRK).
    private static readonly (string Name, float Ratio)[] ReplacedNumbers =
    [
        ("HeartOfCorundumPvP", 30f / 100f), ("RiddleOfEarthPvP", 0.8f), ("LadyOfCrownsPvP", 0.6f),
        ("MicrocosmosPvP", 0.6f), ("MeisuiPvP", 0.5f), ("ImpalementPvP", 60f / 100f),
    ];

    // The rows each job sees (its own, its role's and the shared Recuperate gate), as sets of enum names.
    private static readonly Dictionary<string, string[]> JobRows = new()
    {
        ["WHM"] = ["AquaveilPvP", "StoneskinIiPvP", "RecuperatePvP"],
        ["SCH"] = ["StoneskinIiPvP", "RecuperatePvP"],
        ["SGE"] = ["StoneskinIiPvP", "RecuperatePvP"],
        ["AST"] = ["StoneskinIiPvP", "RecuperatePvP", "LadyOfCrownsPvP", "MicrocosmosPvP"],
        ["BLM"] = ["WreathOfIcePvP", "RecuperatePvP"],
        ["PCT"] = ["TemperaCoatPvP", "RecuperatePvP"],
        ["RDM"] = ["FortePvP", "RecuperatePvP"],
        ["SMN"] = ["RadiantAegisPvP", "RecuperatePvP"],
        ["RPR"] = ["ArcaneCrestPvP", "RecuperatePvP"],
        ["VPR"] = ["SnakeScalesPvP", "RecuperatePvP"],
        ["DRK"] = ["TheBlackestNightPvP", "RampartPvP", "RecuperatePvP", "ImpalementPvP"],
        ["PLD"] = ["HolySheltronPvP", "RampartPvP", "RecuperatePvP"],
        ["WAR"] = ["BloodwhettingPvP", "RampartPvP", "RecuperatePvP"],
        ["GNB"] = ["RampartPvP", "RecuperatePvP", "HeartOfCorundumPvP"],
        ["DNC"] = ["CuringWaltzPvP", "RecuperatePvP"],
        ["MNK"] = ["RecuperatePvP", "RiddleOfEarthPvP"],
        ["NIN"] = ["RecuperatePvP", "MeisuiPvP"],
        ["DRG"] = ["RecuperatePvP"],
        ["SAM"] = ["RecuperatePvP"],
        ["BRD"] = ["RecuperatePvP"],
        ["MCH"] = ["RecuperatePvP"],
    };

    private static string[] DuplicateIds(IEnumerable<DefensiveRow> rows) =>
        rows.GroupBy(r => r.ActionId).Where(g => g.Count() > 1).Select(g => g.Key.ToString()).ToArray();

    private static string[] UnknownIds(IEnumerable<uint> ids) =>
        ids.Where(id => StatusNames.All(s => s.Id != id)).Select(id => id.ToString()).ToArray();

    private static string[] DefaultProblems(IEnumerable<DefensiveRow> rows) =>
        rows.Where(r => r.Mode == DefensiveMode.Param ? !r.DefaultEnabled : r.DefaultEnabled).Select(r => r.ActionName).ToArray();

    public static void Run()
    {
        Console.WriteLine("-- round 3 Part A, item 7: the defensive table (T1) --");
        var rows = DefensiveTable.Rows;

        // ---- shape and counts ----------------------------------------------------------------------------------
        Harness.Case("the table has 20 rows: 13 Fire and 1 Gate (phase 1) and 6 Param",
            rows.Count == 20 && rows.Count(r => r.Mode == DefensiveMode.Fire) == 13 && rows.Count(r => r.Mode == DefensiveMode.Gate) == 1
            && rows.Count(r => r.Mode == DefensiveMode.Param) == 6,
            $"{rows.Count} rows, Fire {rows.Count(r => r.Mode == DefensiveMode.Fire)}, Gate {rows.Count(r => r.Mode == DefensiveMode.Gate)}, Param {rows.Count(r => r.Mode == DefensiveMode.Param)}");

        var dup = DuplicateIds(rows);
        Harness.Case("action ids are unique", dup.Length == 0, dup.Length == 0 ? $"{rows.Count} ids" : "duplicated: " + string.Join(",", dup));
        Harness.Case("enum names are unique", rows.Select(r => r.ActionName).Distinct().Count() == rows.Count);

        // ---- each (enum name, id) equals the real enum ----------------------------------------------------------
        var wrongPairs = rows.Where(r => !Enum.TryParse<ActionID>(r.ActionName, out var real) || (uint)real != r.ActionId || !Enum.IsDefined(typeof(ActionID), r.ActionName))
            .Select(r => $"{r.ActionName}={r.ActionId}").ToArray();
        Harness.Case("every (enum name, id) pair equals the real (uint)ActionID.<Name>", wrongPairs.Length == 0,
            wrongPairs.Length == 0 ? $"{rows.Count} pairs" : "wrong: " + string.Join(",", wrongPairs));

        // ---- status ids tied to the real StatusID enum ----------------------------------------------------------
        var tableStatusIds = rows.SelectMany(r => r.ProvidesStatus.Concat(r.SuppressWhileStatus)).Concat(DefensiveTable.SilenceStatuses).Distinct().OrderBy(i => i).ToArray();
        var unknown = UnknownIds(tableStatusIds);
        Harness.Case("every status id the table names is in the verified list", unknown.Length == 0, unknown.Length == 0 ? $"{tableStatusIds.Length} ids" : "unverified: " + string.Join(",", unknown));
        var badStatus = StatusNames.Where(s => !Enum.TryParse<StatusID>(s.Name, out var real) || !Enum.IsDefined(typeof(StatusID), s.Name) || (uint)real != s.Id)
            .Select(s => $"{s.Name}={s.Id}").ToArray();
        Harness.Case("every verified status id equals (uint)StatusID.<Name> in the fork's enum", badStatus.Length == 0,
            badStatus.Length == 0 ? $"{StatusNames.Length} ids" : "wrong: " + string.Join(",", badStatus));
        Harness.Case("the silence statuses are Undead Redemption 3039 and Hidden 1316",
            DefensiveTable.SilenceStatuses.SequenceEqual([3039u, 1316u]) && (uint)StatusID.UndeadRedemption == 3039 && (uint)StatusID.Hidden_1316 == 1316);

        // ---- dedupe is present where the action has none of its own ----------------------------------------------
        var noDedupe = rows.Where(r => r.Mode == DefensiveMode.Fire && r.Kind != DefensiveKind.SelfHeal && r.ProvidesStatus.Count == 0).Select(r => r.ActionName).ToArray();
        Harness.Case("every Fire shield and mitigation row names the status it provides (dedupe)", noDedupe.Length == 0, string.Join(",", noDedupe));
        var bn = rows.Single(r => r.ActionName == "TheBlackestNightPvP");
        Harness.Case("The Blackest Night keeps its two-charge dedupe: status 1308 and usedUp", bn.ProvidesStatus.SequenceEqual([1308u]) && bn.UsedUp);
        Harness.Case("only Aquaveil skips the target-status check, only The Blackest Night uses usedUp",
            rows.Where(r => r.SkipTargetStatusNeed).Select(r => r.ActionName).SequenceEqual(["AquaveilPvP"]) && rows.Where(r => r.UsedUp).Select(r => r.ActionName).SequenceEqual(["TheBlackestNightPvP"]));
        Harness.Case("Curing Waltz stays silent during Honing Dance (3162)", rows.Single(r => r.ActionName == "CuringWaltzPvP").SuppressWhileStatus.SequenceEqual([3162u]));

        // ---- defaults ------------------------------------------------------------------------------------------
        var badDefault = DefaultProblems(rows);
        Harness.Case("every Fire and Gate row defaults OFF and every Param row defaults ON", badDefault.Length == 0, string.Join(",", badDefault));
        var paramMismatch = ReplacedNumbers.Where(n => rows.Single(r => r.ActionName == n.Name) is not { Mode: DefensiveMode.Param, DefaultEnabled: true } row || row.DefaultPercent != n.Ratio)
            .Select(n => n.Name).ToArray();
        Harness.Case("every Param row's default percent equals the number it replaced (30, 0.8, 0.6, 0.6, 0.5, 60)", paramMismatch.Length == 0 && ReplacedNumbers.Length == 6, string.Join(",", paramMismatch));
        var outOfRange = rows.Where(r => !(r.MinPercent >= 0f && r.MinPercent <= r.DefaultPercent && r.DefaultPercent <= 1f)).Select(r => r.ActionName).ToArray();
        Harness.Case("every default percent lies within [MinPercent, 1]", outOfRange.Length == 0, string.Join(",", outOfRange));
        var rec = rows.Single(r => r.ActionName == "RecuperatePvP");
        Harness.Case("Recuperate is the one Gate row: job *, minimum 0.50, default off at 0.60",
            rec.Mode == DefensiveMode.Gate && rec.Job == "*" && rec.MinPercent == 0.50f && !rec.DefaultEnabled && rec.DefaultPercent == 0.60f && ReferenceEquals(rec, DefensiveTable.Recuperate));
        Harness.Case("no other row has a floor above 0", rows.Where(r => r.MinPercent != 0f).Select(r => r.ActionName).SequenceEqual(["RecuperatePvP"]));

        // ---- rows that must not exist ----------------------------------------------------------------------------
        uint[] forbidden = [(uint)ActionID.GuardPvP, (uint)ActionID.PurifyPvP, (uint)ActionID.StandardissueElixirPvP, (uint)ActionID.GuardPvP_29735];
        Harness.Case("no row for Guard (either id), Purify or Standard-issue Elixir", rows.All(r => !forbidden.Contains(r.ActionId)));

        // ---- the named rows are the table's rows -------------------------------------------------------------------
        Harness.Case("the named Param rows are the table's own rows",
            new[] { DefensiveTable.HeartOfCorundum, DefensiveTable.RiddleOfEarth, DefensiveTable.LadyOfCrowns, DefensiveTable.Microcosmos, DefensiveTable.Meisui, DefensiveTable.Impalement }
                .Select(r => r.ActionName).SequenceEqual(ReplacedNumbers.Select(n => n.Name))
            && ReplacedNumbers.All(n => ReferenceEquals(DefensiveTable.ById((uint)Enum.Parse<ActionID>(n.Name)), rows.Single(r => r.ActionName == n.Name))));

        // ---- per-job sets and priority order ---------------------------------------------------------------------
        var jobProblems = new List<string>();
        foreach (var (job, expected) in JobRows)
        {
            var got = DefensiveTable.ForJob(job).Select(r => r.ActionName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var want = expected.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (!got.SequenceEqual(want)) jobProblems.Add($"{job}: got [{string.Join(",", got)}] want [{string.Join(",", want)}]");
        }

        var badJobKeys = JobRows.Keys.Concat(rows.Select(r => r.Job).Where(j => j is not ("*" or "TANK" or "HEALER"))).Distinct()
            .Where(j => !Enum.TryParse<ECommons.ExcelServices.Job>(j, out var parsed) || !Enum.IsDefined(parsed) || parsed.ToString() != j).ToArray();
        Harness.Case("every job key is the name of a member of ECommons.ExcelServices.Job (what DataCenter.Job.ToString() returns)", badJobKeys.Length == 0, string.Join(",", badJobKeys));
        Harness.Case("each of the 21 jobs sees exactly its own rows, its role's rows and Recuperate", JobRows.Count == 21 && jobProblems.Count == 0, string.Join("; ", jobProblems));
        var orderProblems = new List<string>();
        foreach (var job in JobRows.Keys)
        {
            var fire = DefensiveTable.ForJob(job).Where(r => r.Mode == DefensiveMode.Fire).ToList();
            var firstRole = fire.FindIndex(r => r.Job is "TANK" or "HEALER");
            var lastOwn = fire.FindLastIndex(r => r.Job == job);
            if (firstRole >= 0 && lastOwn > firstRole) orderProblems.Add(job);
        }

        Harness.Case("a job's own Fire rows come before the role rows (Rampart, Stoneskin II) in table order", orderProblems.Count == 0, string.Join(",", orderProblems));
        Harness.Case("the role keys map the four tanks and four healers", new[] { "PLD", "WAR", "DRK", "GNB" }.All(j => DefensiveTable.RoleKeyOf(j) == "TANK")
            && new[] { "WHM", "SCH", "AST", "SGE" }.All(j => DefensiveTable.RoleKeyOf(j) == "HEALER") && DefensiveTable.RoleKeyOf("MNK") == "" && DefensiveTable.RoleKeyOf("") == "");
        Harness.Case("an unknown job key sees only the shared Recuperate row", DefensiveTable.ForJob("XXX").Select(r => r.ActionName).SequenceEqual(["RecuperatePvP"]));

        // ---- plain-English text is present, the worst case is said on every Fire row ----------------------------------
        var emptyText = rows.Where(r => string.IsNullOrWhiteSpace(r.Label) || string.IsNullOrWhiteSpace(r.Tooltip)).Select(r => r.ActionName).ToArray();
        Harness.Case("every row has a label and a tooltip", emptyText.Length == 0, string.Join(",", emptyText));
        var noWorstCase = rows.Where(r => r.Mode != DefensiveMode.Param && !r.Tooltip.Contains(DefensiveTable.WorstCase)).Select(r => r.ActionName).ToArray();
        Harness.Case("the worst case (several enabled rows can still walk down the table) is in every Fire and Gate tooltip", noWorstCase.Length == 0 && DefensiveTable.WorstCase.Contains("more than one can still be used"),
            string.Join(",", noWorstCase));
        var roleWording = rows.Where(r => r.ActionName is "RampartPvP" or "StoneskinIiPvP" or "RecuperatePvP").Where(r => !r.Tooltip.StartsWith("May be used, not will be used")).Select(r => r.ActionName).ToArray();
        Harness.Case("the shared rows (Recuperate, Rampart, Stoneskin II) say 'may be used, not will be used'", roleWording.Length == 0, string.Join(",", roleWording));

        // DefersTo that names a rotation file names a file that exists.
        var missingFiles = new List<string>();
        foreach (var row in rows)
        foreach (Match m in Regex.Matches(row.DefersTo, @"\b[A-Z]{3}_Default\.Pv[Pp]\.cs"))
        {
            try { _ = SourceFiles.Rotation(m.Value); } catch (FileNotFoundException) { missingFiles.Add($"{row.ActionName}->{m.Value}"); }
        }

        Harness.Case("every rotation file named in a DefersTo note exists", missingFiles.Count == 0, string.Join(",", missingFiles));

        // ---- the action each row names is declared by the job's rotation (T2 over the Basic rotation sources) ---------
        var basicDir = Path.Combine(Program.SrcRoot, "PvPSolver.Basic", "Rotations", "Basic");
        var allBasic = string.Join("\n", Directory.GetFiles(basicDir, "*.cs").Select(File.ReadAllText));
        var shared = File.ReadAllText(Path.Combine(Program.SrcRoot, "PvPSolver.Basic", "Rotations", "CustomRotation_Actions.cs"));
        var generatedActions = Regex.Matches(File.ReadAllText(Path.Combine(Program.SrcRoot, "PvPSolver.SourceGenerators", "Properties", "Resources.resx")), @"static partial void Modify(\w+)\(").Select(m => m.Groups[1].Value).ToHashSet();
        var notModifiable = rows.Where(r => !generatedActions.Contains(r.ActionName)).Select(r => r.ActionName).ToArray();
        Harness.Case("every row's action is an action the rotation generator declares (a Modify<Name> hook exists)", notModifiable.Length == 0, string.Join(",", notModifiable));
        Harness.Case("the rotation sources are readable", allBasic.Length > 1000 && shared.Length > 100);

        // The statuses a row names as provided equal the action's own StatusProvide where the action declares one.
        var provideMismatch = new List<string>();
        var sanBasic = CsSource.Sanitize(allBasic);
        foreach (var (action, status) in new[] { ("FortePvP", "Forte"), ("RadiantAegisPvP", "RadiantAegis_3224"), ("TemperaCoatPvP", "TemperaCoat_4114"), ("RiddleOfEarthPvP", "EarthResonance") })
        {
            var m = Regex.Match(sanBasic, @"Modify" + action + @"\s*\(ref ActionSetting setting\)\s*\{[^}]*?StatusProvide\s*=\s*\[\s*StatusID\.(?<s>\w+)\s*\]");
            var row = rows.Single(r => r.ActionName == action);
            var id = (uint)Enum.Parse<StatusID>(status);
            if (!m.Success || m.Groups["s"].Value != status || !row.ProvidesStatus.SequenceEqual([id])) provideMismatch.Add(action);
        }

        Harness.Case("where the action declares StatusProvide, the row names the same status (Forte, Radiant Aegis, Tempera Coat, Riddle of Earth)", provideMismatch.Count == 0, string.Join(",", provideMismatch));

        // ---- resolve functions ----------------------------------------------------------------------------------
        var fireRow = rows.Single(r => r.ActionName == "RampartPvP");
        Harness.Case("ResolveEnabled: absent means the row default, a stored value wins",
            !DefensiveTable.ResolveEnabled(fireRow, null) && DefensiveTable.ResolveEnabled(fireRow, true) && DefensiveTable.ResolveEnabled(DefensiveTable.HeartOfCorundum, null) && !DefensiveTable.ResolveEnabled(DefensiveTable.HeartOfCorundum, false));
        Harness.Case("ResolvePercent: absent, NaN and infinity mean the row default",
            DefensiveTable.ResolvePercent(fireRow, null) == 0.60f && DefensiveTable.ResolvePercent(fireRow, float.NaN) == 0.60f
            && DefensiveTable.ResolvePercent(fireRow, float.PositiveInfinity) == 0.60f && DefensiveTable.ResolvePercent(fireRow, float.NegativeInfinity) == 0.60f);
        Harness.Case("ResolvePercent: a stored value is used and clamped into [MinPercent, 1]",
            DefensiveTable.ResolvePercent(fireRow, 0.4f) == 0.4f && DefensiveTable.ResolvePercent(fireRow, -3f) == 0f && DefensiveTable.ResolvePercent(fireRow, 7f) == 1f
            && DefensiveTable.ResolvePercent(rec, 0.2f) == 0.50f && DefensiveTable.ResolvePercent(rec, 0.5f) == 0.50f && DefensiveTable.ResolvePercent(rec, 0.51f) == 0.51f);
        var scaleBad = Enumerable.Range(0, 101).Where(p => DefensiveTable.ToPercentPoints(p / 100f) != p).ToArray();
        Harness.Case("ToPercentPoints is exact for every whole percent 0 to 100 (0.3 reads as 30, not 30.000002)", scaleBad.Length == 0 && 0.3f * 100f != 30f, scaleBad.Length == 0 ? "101 values" : string.Join(",", scaleBad));
        Harness.Case("the two 0-100 defaults read back exactly: Heart of Corundum 30, Impalement 60",
            DefensiveTable.ToPercentPoints(DefensiveTable.ResolvePercent(DefensiveTable.HeartOfCorundum, null)) == 30f && DefensiveTable.ToPercentPoints(DefensiveTable.ResolvePercent(DefensiveTable.Impalement, null)) == 60f);

        // ---- canaries (false by construction) --------------------------------------------------------------------
        Harness.Canary("a table with a duplicated id has no duplicates", DuplicateIds(rows.Append(rows[0])).Length == 0);
        Harness.Canary("a Fire row that is on by default passes the default check",
            DefaultProblems(rows.Select(r => r.ActionName == "RampartPvP" ? r with { DefaultEnabled = true } : r)).Length == 0);
        Harness.Canary("a Param row that is off by default passes the default check",
            DefaultProblems(rows.Select(r => r.ActionName == "MeisuiPvP" ? r with { DefaultEnabled = false } : r)).Length == 0);
        Harness.Canary("a Param row with a changed default number matches the replaced number",
            ReplacedNumbers.All(n => (n.Name == "MeisuiPvP" ? 0.55f : rows.Single(r => r.ActionName == n.Name).DefaultPercent) == n.Ratio));
        Harness.Canary("a clamp that ignores the floor lets Recuperate go to 0.2", Math.Clamp(0.2f, 0f, 1f) == DefensiveTable.ResolvePercent(rec, 0.2f));
        Harness.Canary("a status id outside the verified list is accepted", UnknownIds(tableStatusIds.Append(9999u)).Length == 0);
    }
}
