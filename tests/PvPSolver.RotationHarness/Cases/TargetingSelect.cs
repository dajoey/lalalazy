using System.Text;
using System.Text.RegularExpressions;
using RotationSolver.Basic.Data;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Round 2, change 1, TARGETING (SHARED-4). Entering the PvP state forced <c>TargetingType.LowHP</c>, so the
///     configured targeting list (and <c>PvPHighestPressure</c>) could never be selected.
///     T1: the pure <see cref="TargetingChoice.Resolve"/> over a dense domain against an independent reference
///     model, with the old PvP behaviour (forced LowHP) shown wrong for every configuration that selects another
///     entry. T2: the PvP case of both state methods no longer assigns <c>TargetingType.LowHP</c>, the sibling
///     cases are untouched, and nothing else in the source assigns a literal targeting type.
///     The call site <c>DataCenter.TargetingType</c> is shape-checked, not run (it needs the game's config).
/// </summary>
internal static class TargetingSelect
{
    private const int Big = (int)TargetingType.Big;
    private const int Small = (int)TargetingType.Small;
    private const int HighHP = (int)TargetingType.HighHP;
    private const int LowHP = (int)TargetingType.LowHP;
    private const int Pressure = (int)TargetingType.PvPHighestPressure;

    // Independent reference model: the getter before this change for the non-forced case (configured list,
    // empty list filled with the defaults, index wrapped), and the forced case as a plain precedence rule.
    private static int Reference(int? forced, int[] list, int index)
    {
        if (forced is { } f) return f;
        var l = list.Length == 0 ? new[] { LowHP, HighHP, Small, Big } : list;
        return l[((index % l.Length) + l.Length) % l.Length];
    }

    // The behaviour being fixed: the PvP state forces LowHP, whatever list and index are configured.
    private static int OldPvpBehaviour(int[] list, int index) => Resolve(LowHP, list, index);

    private static int Resolve(int? forced, int[] list, int index) => TargetingChoice.Resolve(forced, list, index);

    private static IEnumerable<(int? Forced, int[] List, int Index)> Domain()
    {
        int[][] lists =
        [
            [],
            [LowHP],
            [LowHP, HighHP, Small, Big],
            [Big, Small, HighHP, LowHP],
            [Pressure],
            [LowHP, Pressure],
            [LowHP, HighHP, Small, Big, Pressure],
            [(int)TargetingType.PvPHealers, (int)TargetingType.PvPTanks, (int)TargetingType.PvPDPS, LowHP, Pressure, Big],
        ];
        int?[] forced = [null, LowHP, Pressure, Big, 0, 42];
        foreach (var f in forced) foreach (var l in lists) for (var i = -9; i <= 14; i++) yield return (f, l, i);
    }

    private static string[] Mismatches(Func<int?, int[], int, int> impl) =>
        Domain().Where(r => impl(r.Forced, r.List, r.Index) != Reference(r.Forced, r.List, r.Index))
            .Select(r => $"forced={(r.Forced?.ToString() ?? "-")},list=[{string.Join(",", r.List)}],index={r.Index}").ToArray();

    public static void Run()
    {
        Console.WriteLine("-- round 2, change 1 TARGETING (T1 + T2) --");

        // ---- T1: the pure core ----------------------------------------------------------------------------
        var rows = Domain().ToArray();
        var bad = Mismatches(Resolve);
        Harness.Case("Resolve equals the reference model over forced x list x index", bad.Length == 0,
            bad.Length == 0 ? $"{rows.Length} rows" : $"{bad.Length} differ, first: {bad[0]}");

        Harness.Case("default config (empty list, index 0) resolves to LowHP, as before",
            Resolve(null, [], 0) == LowHP);
        Harness.Case("the filled default list (LowHP, HighHP, Small, Big) with index 0 resolves to LowHP",
            Resolve(null, [LowHP, HighHP, Small, Big], 0) == LowHP);
        Harness.Case("an empty list resolves against the default list for any index (LowHP, HighHP, Small, Big, wrapping)",
            Enumerable.Range(0, 8).Select(i => Resolve(null, [], i)).SequenceEqual([LowHP, HighHP, Small, Big, LowHP, HighHP, Small, Big]));

        int[] configured = [LowHP, HighHP, Small, Big, Pressure];
        Harness.Case("a saved index selects the matching entry of the configured list",
            Enumerable.Range(0, configured.Length).All(i => Resolve(null, configured, i) == configured[i]));
        Harness.Case("PvPHighestPressure is selectable once the configured list and index choose it",
            Resolve(null, configured, 4) == Pressure);
        Harness.Case("an index past the end wraps like the getter's modulo",
            Resolve(null, configured, 5) == LowHP && Resolve(null, configured, 7) == Small);
        Harness.Case("a negative index wraps into the list and does not throw",
            Resolve(null, configured, -1) == Pressure && Resolve(null, [], -1) == Big);
        Harness.Case("a forced value wins over the list and the index",
            Resolve(Big, configured, 0) == Big && Resolve(Pressure, [], 3) == Pressure && Resolve(0, configured, 2) == 0);

        // The default list is the real enum's LowHP, HighHP, Small, Big, in that order.
        Harness.Case("TargetingChoice.DefaultList equals (int) of TargetingType LowHP, HighHP, Small, Big",
            TargetingChoice.DefaultList.SequenceEqual([(int)TargetingType.LowHP, (int)TargetingType.HighHP, (int)TargetingType.Small, (int)TargetingType.Big]),
            string.Join(",", TargetingChoice.DefaultList));

        // RED against the old behaviour: with the PvP state forcing LowHP, every configuration that selects another
        // entry resolves to LowHP anyway. The configured rows below are the ones the owner wants to reach.
        var unreachable = Enumerable.Range(0, configured.Length).Where(i => OldPvpBehaviour(configured, i) != configured[i]).ToArray();
        Harness.Case("RED on old behaviour: forcing LowHP makes the configured entries at indexes 1..4 unreachable (incl. PvPHighestPressure)",
            unreachable.SequenceEqual([1, 2, 3, 4]), "indexes " + string.Join(",", unreachable));
        Harness.Case("RED on old behaviour: with a default config the forced LowHP agrees with the new rule (default targeting unchanged)",
            OldPvpBehaviour([], 0) == Resolve(null, [], 0) && OldPvpBehaviour([LowHP, HighHP, Small, Big], 0) == Resolve(null, [LowHP, HighHP, Small, Big], 0));

        // Canaries: false by construction. A resolver that ignores the index, one that ignores the forced value, and
        // the old forced-LowHP behaviour must each be caught by the same table.
        Harness.Canary("a resolver that ignores the index satisfies the table",
            Mismatches((f, l, _) => Resolve(f, l, 0)).Length == 0);
        Harness.Canary("a resolver that ignores the forced value satisfies the table",
            Mismatches((_, l, i) => Resolve(null, l, i)).Length == 0);
        Harness.Canary("the old behaviour (PvP forces LowHP) satisfies the table",
            Mismatches((f, l, i) => f ?? OldPvpBehaviour(l, i)).Length == 0);

        // ---- T2: the source shape --------------------------------------------------------------------------
        var cmdPath = Path.Combine(Program.SrcRoot, "PvPSolver", "Commands", "RSCommands_StateSpecialCommand.cs");
        // The file holds a stray non-UTF-8 byte in a comment; read it as Latin-1 so every code character survives.
        var cmd = CsSource.Sanitize(File.ReadAllText(cmdPath, Encoding.Latin1));

        var methods = new[] { "UpdateState", "AutodutyUpdateState" };
        foreach (var name in methods)
        {
            var body = MethodBody(cmd, name);
            Harness.Case($"{name}: found with a switch over StateCommandType", body != null && body.Contains("switch"), body == null ? "method not found" : "");
            if (body == null) continue;
            var pvp = Cases(body).SingleOrDefault(c => c.Label == "PvP");
            Harness.Case($"{name}: has exactly one PvP case", pvp != default);
            if (pvp == default) continue;
            var assigns = Assignments(pvp.Body);
            Harness.Case($"{name}: the PvP case sets TargetingTypeOverride to null (the configured list applies)",
                assigns.SequenceEqual(["null"]), string.Join("|", assigns));
            Harness.Case($"{name}: the PvP case no longer assigns TargetingType.LowHP",
                !Regex.IsMatch(pvp.Body, @"TargetingType\s*\.\s*LowHP"));
        }

        // Sibling cases are untouched: UpdateState cases Off..Henched clear the override; AutodutyUpdateState keeps
        // its two cases that carry the requested targeting type and clears the rest.
        var upd = Cases(MethodBody(cmd, "UpdateState") ?? "").ToDictionary(c => c.Label, c => Assignments(c.Body));
        var siblingsUpdate = upd.Where(kv => kv.Key != "PvP").ToArray();
        Harness.Case("UpdateState: every other case still clears the override (null)",
            siblingsUpdate.Length >= 5 && siblingsUpdate.All(kv => kv.Value.SequenceEqual(["null"])),
            string.Join(", ", siblingsUpdate.Select(kv => kv.Key + "=" + string.Join("|", kv.Value))));
        var auto = Cases(MethodBody(cmd, "AutodutyUpdateState") ?? "").ToDictionary(c => c.Label, c => Assignments(c.Body));
        var autoCarry = auto.Where(kv => kv.Value.SequenceEqual(["targetingType"])).Select(kv => kv.Key).OrderBy(x => x).ToArray();
        var autoNull = auto.Where(kv => kv.Key != "PvP" && kv.Value.SequenceEqual(["null"])).Select(kv => kv.Key).OrderBy(x => x).ToArray();
        Harness.Case("AutodutyUpdateState: exactly the two requested-targeting cases (TargetOnly, AutoDuty) still carry targetingType; every other non-PvP case clears",
            autoCarry.SequenceEqual(["AutoDuty", "TargetOnly"]) && autoNull.Length == auto.Count - 3,
            $"carry=[{string.Join(",", autoCarry)}] null=[{string.Join(",", autoNull)}] cases=[{string.Join(",", auto.Keys)}]");

        // Nothing else forces a type: every TargetingTypeOverride assignment in the plugin source is null or the
        // requested targetingType, and TargetingType.LowHP is never assigned anywhere but the default-list fill.
        var forcing = new List<string>();
        var lowHpUses = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Program.SrcRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                                 && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
        {
            var san = CsSource.Sanitize(File.ReadAllText(file, Encoding.Latin1));
            foreach (Match m in Regex.Matches(san, @"TargetingTypeOverride\s*=(?!=)\s*(?<v>[^;]+);"))
            {
                var v = CsSource.Squash(m.Groups["v"].Value);
                if (v is not ("null" or "targetingType")) forcing.Add($"{Path.GetFileName(file)}:{CsSource.LineOf(san, m.Index)} = {v}");
            }

            foreach (Match m in Regex.Matches(san, @"TargetingType\s*\.\s*LowHP\b"))
            {
                var lineStart = san.LastIndexOf('\n', m.Index) + 1;
                var lineEnd = san.IndexOf('\n', m.Index);
                if (lineEnd < 0) lineEnd = san.Length;
                var line = CsSource.Squash(san.Substring(lineStart, lineEnd - lineStart));
                var allowed = line.StartsWith("case TargetingType.LowHP:") || line == "Service.Config.TargetingTypes.Add(TargetingType.LowHP);";
                if (!allowed) lowHpUses.Add($"{Path.GetFileName(file)}:{CsSource.LineOf(san, m.Index)} {line}");
            }
        }

        Harness.Case("no source line assigns a literal targeting type to TargetingTypeOverride", forcing.Count == 0, string.Join("; ", forcing));
        Harness.Case("TargetingType.LowHP appears only as the default-list entry and the sort case", lowHpUses.Count == 0, string.Join("; ", lowHpUses));

        // Call site: DataCenter.TargetingType goes through the pure resolver and no longer indexes the list itself.
        var dcPath = Path.Combine(Program.SrcRoot, "PvPSolver.Basic", "DataCenter.cs");
        var dc = CsSource.Sanitize(File.ReadAllText(dcPath));
        var getter = Regex.Match(dc, @"public\s+static\s+TargetingType\s+TargetingType\s*\{");
        var getterBody = getter.Success ? dc.Substring(getter.Index, CsSource.Match(dc, getter.Index + getter.Length - 1) - getter.Index) : "";
        Harness.Case("DataCenter.TargetingType calls TargetingChoice.Resolve with the override, the configured list and TargetingIndex",
            Regex.IsMatch(getterBody, @"TargetingChoice\s*\.\s*Resolve\(\s*\(int\?\)\s*TargetingTypeOverride\s*,[^;]*Service\.Config\.TargetingIndex\s*\)"),
            CsSource.Squash(getterBody));
        Harness.Case("DataCenter.TargetingType no longer indexes the list with a modulo itself",
            getterBody.Length > 0 && !Regex.IsMatch(getterBody, @"TargetingTypes\s*\[\s*Service\.Config\.TargetingIndex\s*%"));
        Harness.Case("DataCenter.TargetingType still fills an empty list with LowHP, HighHP, Small, Big",
            Regex.Matches(getterBody, @"TargetingTypes\s*\.\s*Add\(\s*TargetingType\.(LowHP|HighHP|Small|Big)\s*\)").Select(m => m.Groups[1].Value)
                .SequenceEqual(["LowHP", "HighHP", "Small", "Big"]));

        // Canary: the old PvP case text must not be accepted as clean.
        const string oldCase = "case StateCommandType.PvP: DataCenter.IsPvPStateEnabled = true; DataCenter.TargetingTypeOverride = TargetingType.LowHP; break;";
        var oldAssigns = Assignments(oldCase);
        Harness.Canary("the old PvP case text is accepted as clearing the override", oldAssigns.SequenceEqual(["null"]));
    }

    // ---- tiny helpers over the sanitized source ---------------------------------------------------------------

    private static string? MethodBody(string san, string name)
    {
        var m = Regex.Match(san, @"\bvoid\s+" + name + @"\s*\(");
        if (!m.Success) return null;
        var close = CsSource.Match(san, m.Index + m.Length - 1);
        var open = san.IndexOf('{', close);
        var end = CsSource.Match(san, open);
        return san.Substring(open + 1, end - open - 1);
    }

    /// <summary>The <c>case StateCommandType.X:</c> sections of a switch (label and text up to the next case label).</summary>
    private static List<(string Label, string Body)> Cases(string body)
    {
        var list = new List<(string, string)>();
        var ms = Regex.Matches(body, @"\bcase\s+StateCommandType\s*\.\s*(?<l>\w+)\s*:").ToArray();
        for (var i = 0; i < ms.Length; i++)
        {
            var start = ms[i].Index + ms[i].Length;
            var end = i + 1 < ms.Length ? ms[i + 1].Index : body.Length;
            list.Add((ms[i].Groups["l"].Value, body.Substring(start, end - start)));
        }

        return list;
    }

    private static string[] Assignments(string text) =>
        Regex.Matches(text, @"TargetingTypeOverride\s*=(?!=)\s*(?<v>[^;]+);").Select(m => CsSource.Squash(m.Groups["v"].Value)).ToArray();
}
