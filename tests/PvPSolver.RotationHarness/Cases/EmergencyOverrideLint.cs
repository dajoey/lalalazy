using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Round 3, both parts' concern: every <c>override bool EmergencyAbility</c> and <c>EmergencyGCD</c> in
///     <c>RebornRotations/PVPRotations/**</c> must end by returning the same-named base method. The generic
///     defensive stage (and the base Guard, Recuperate and Elixir) live in the base method, so an override that ends
///     any other way silently drops them (the bug class fixed for White Mage and Bard in 0.1.1.4). T2 over every
///     job file, with a coverage floor and canaries that reproduce the old White Mage ending.
/// </summary>
internal static class EmergencyOverrideLint
{
    private static readonly Regex Head = new(@"\boverride\s+bool\s+(?<name>Emergency(?:Ability|GCD))\s*\(", RegexOptions.Compiled);

    /// <summary>True when the method body's final statement is `return base.&lt;name&gt;(...);`.</summary>
    private static bool EndsWithSameNamedBase(string name, string body) =>
        Regex.IsMatch(body.TrimEnd(), @"(^|[;{}])\s*return\s+base\s*\.\s*" + Regex.Escape(name) + @"\s*\([^;{}]*\)\s*;\s*$");

    public static void Run()
    {
        Console.WriteLine("-- round 3: Emergency override lint (T2) --");

        var methods = new List<(string File, string Name, string Body)>();
        foreach (var path in SourceFiles.AllPvpRotationFiles())
        {
            var file = Path.GetFileName(path);
            foreach (var m in CsSource.Methods(CsSource.Sanitize(File.ReadAllText(path)), file, Head))
                methods.Add((file, m.Name, m.Body));
        }

        var bad = methods.Where(m => !EndsWithSameNamedBase(m.Name, m.Body)).Select(m => $"{m.File}:{m.Name}").ToArray();
        // Floor: 15 EmergencyAbility overrides and 2 EmergencyGCD overrides (White Mage, Bard) at origin/main 4d6d0e9f.
        Harness.Case("the scan covers every Emergency override (at least 15 EmergencyAbility and 2 EmergencyGCD)",
            methods.Count(m => m.Name == "EmergencyAbility") >= 15 && methods.Count(m => m.Name == "EmergencyGCD") >= 2,
            $"{methods.Count(m => m.Name == "EmergencyAbility")} EmergencyAbility, {methods.Count(m => m.Name == "EmergencyGCD")} EmergencyGCD");
        Harness.Case("every override bool EmergencyAbility / EmergencyGCD ends by returning the same-named base method", bad.Length == 0,
            bad.Length == 0 ? $"{methods.Count} overrides" : "do not: " + string.Join(", ", bad));

        // Canaries: the old White Mage ending, an ending that drops the base, and an ending in another base step.
        const string oldWhm = "EmergencyGCD", wrongBase = "if (x) { return true; } return base.GeneralGCD(out action);";
        Harness.Canary("the old White Mage ending (return base.GeneralGCD) is accepted for EmergencyGCD", EndsWithSameNamedBase(oldWhm, wrongBase));
        Harness.Canary("an ending that drops the base (action = null; return false;) is accepted",
            EndsWithSameNamedBase("EmergencyAbility", "if (x) { return true; } action = null; return false;"));
        Harness.Canary("a base call in the middle followed by `return false;` is accepted",
            EndsWithSameNamedBase("EmergencyAbility", "if (base.EmergencyAbility(nextGCD, out action)) { return true; } return false;"));
        Harness.Case("the matcher accepts the right ending", EndsWithSameNamedBase("EmergencyAbility", "if (x) { return true; } return base.EmergencyAbility(nextGCD, out action);"));
    }
}
