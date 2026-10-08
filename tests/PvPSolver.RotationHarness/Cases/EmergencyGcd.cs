using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 5, EMERGENCYGCD (WHM-1 and the BRD sibling). T2: an <c>override bool X(</c> in a per-job PvP
///     rotation must only call <c>base.X(</c>. WHM and BRD ended <c>EmergencyGCD</c> with
///     <c>base.GeneralGCD</c>, the empty stub, so their base Guard / Recuperate / Elixir step never ran.
/// </summary>
internal static class EmergencyGcd
{
    public static void Run()
    {
        Console.WriteLine("-- change 5 EMERGENCYGCD (T2) --");

        var files = SourceFiles.AllPvpRotationFiles();
        var methods = 0;
        var violations = new List<string>();
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            foreach (var m in CsSource.Methods(CsSource.Sanitize(File.ReadAllText(f)), name, SourceFiles.OverrideBool))
            {
                methods++;
                foreach (Match c in Regex.Matches(m.Body, @"\bbase\s*\.\s*(?<callee>\w+)\s*\("))
                    if (c.Groups["callee"].Value != m.Name)
                        violations.Add($"{name}:{m.Name} calls base.{c.Groups["callee"].Value}");
            }
        }

        // Guard against a vacuous pass: the scan must really have covered the 21 rotations.
        Harness.Case("scan covers every per-job rotation file and its override bool methods", files.Length >= 21 && methods >= 100,
            $"{files.Length} files, {methods} override bool methods");
        Harness.Case("no override bool X calls a base method other than base.X", violations.Count == 0,
            string.Join("; ", violations));

        foreach (var file in new[] { "WHM_Default.PVP.cs", "BRD_Default.PVP.cs" })
        {
            var m = SourceFiles.OverrideMethod(file, "EmergencyGCD");
            var final = Regex.Matches(m.Body, @"return\s+base\s*\.\s*(?<callee>\w+)\s*\(\s*(?<args>[^;]*?)\s*\)\s*;").Cast<Match>().LastOrDefault();
            var ok = final != null && final.Groups["callee"].Value == "EmergencyGCD"
                     && Regex.IsMatch(final.Groups["args"].Value, @"^nextGCD\s*,\s*out\s+action$");
            Harness.Case($"{file} EmergencyGCD ends with return base.EmergencyGCD(nextGCD, out action)", ok,
                final == null ? "no final base return" : "final return is " + Regex.Replace(final.Value, @"\s+", " "));
        }

        // Canary: the checker on a method that calls the wrong base step must NOT come back clean.
        const string wrong = "class W { protected override bool EmergencyGCD(IAction? n, out IAction? a) { return base.GeneralGCD(out a); } }";
        var bad = CsSource.Methods(CsSource.Sanitize(wrong), "w.cs", SourceFiles.OverrideBool)
            .SelectMany(m => Regex.Matches(m.Body, @"\bbase\s*\.\s*(?<callee>\w+)\s*\(").Cast<Match>().Where(c => c.Groups["callee"].Value != m.Name))
            .Count();
        Harness.Canary("EmergencyGCD calling base.GeneralGCD is accepted as clean", bad == 0);
    }
}
