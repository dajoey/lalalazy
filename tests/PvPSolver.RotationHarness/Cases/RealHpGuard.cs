using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Self-defence reads REAL HP. The Guard decision (both emergency stages), the Recuperate gate and the
///     generic defensive table read <c>Player.GetHealthRatio()</c>, which prefers <c>DataCenter.RefinedHP</c> -
///     predicted HP with pending heals added. Live (2026-10-31 Frontline): the predicted value read 84% one
///     second before a death at about 40% real HP, so Guard never fired. These decisions protect the body that
///     is taking damage NOW, so they read the real HP numbers (<c>JevCall.HpRatio</c>, added for the v3 snapshot
///     in 0.1.2.8 for exactly this reason).
///     T1: the real-HP helper's truth table. T2: each self-defence site reads it; RED on the old
///     <c>GetHealthRatio</c> text. Purify's block reads statuses only (no HP read), so it needs no change.
/// </summary>
internal static class RealHpGuard
{
    private static readonly Regex VirtualBool = new(@"\b(?:protected|public)\s+virtual\s+bool\s+(?<name>\w+)\s*\(", RegexOptions.Compiled);
    private static readonly Regex StaticBool = new(@"\bstatic\s+bool\s+(?<name>\w+)\s*\(", RegexOptions.Compiled);

    private static (string Path, string Sanitized) BasicFile(params string[] below)
    {
        var parts = new List<string> { Program.SrcRoot, "PvPSolver.Basic" };
        parts.AddRange(below);
        var path = Path.Combine(parts.ToArray());
        if (!File.Exists(path)) throw new FileNotFoundException(path);
        return (path, CsSource.Sanitize(File.ReadAllText(path)));
    }

    /// <summary>The condition of every `if` in the method whose body mentions <paramref name="needle"/>.</summary>
    private static List<string> GuardConditions((string Path, string Sanitized) file, string method, Regex head, string needle)
    {
        var m = CsSource.Methods(file.Sanitized, Path.GetFileName(file.Path), head).SingleOrDefault(x => x.Name == method)
                ?? throw new MissingMethodException(Path.GetFileName(file.Path), method);
        return CsSource.IfConditions(m.Body).Where(c => c.Contains(needle)).ToList();
    }

    private static List<string> LinesWith((string Path, string Sanitized) file, string needle) =>
        file.Sanitized.Split('\n').Where(l => l.Contains(needle)).Select(CsSource.Squash).ToList();

    public static void Run()
    {
        Console.WriteLine("-- self-defence reads real HP (T1 + T2, RED on the predicted-HP text) --");

        // ---- T1: JevCall.HpRatio is the real-HP ratio ------------------------------------------------------------
        Harness.Case("HpRatio(40, 100) is 0.4 (real current over max, no pending heals added)",
            RotationSolver.Decisions.JevCall.HpRatio(40, 100) == 0.4f);
        Harness.Case("HpRatio(0, 0) is 0 and HpRatio clamps above 1 (a wrong max can never read as invulnerable)",
            RotationSolver.Decisions.JevCall.HpRatio(0, 0) == 0f && RotationSolver.Decisions.JevCall.HpRatio(120, 100) == 1f
            && RotationSolver.Decisions.JevCall.HpRatio(0, 100) == 0f);

        var ability = BasicFile("Rotations", "CustomRotation_Ability.cs");
        var gcd = BasicFile("Rotations", "CustomRotation_GCD.cs");
        var generic = BasicFile("Actions", "GenericDefensives.cs");
        var helper = BasicFile("Helpers", "ObjectHelper.cs");

        // ---- T2 RED: the Guard conditions in both emergency stages read real HP ---------------------------------
        foreach (var (file, method) in new[] { (ability, "EmergencyAbility"), (gcd, "EmergencyGCD") })
        {
            var guard = GuardConditions(file, method, VirtualBool, "HealthForGuard");
            var bad = guard.Count(c => c.Contains("GetHealthRatio"));
            Harness.Case($"{Path.GetFileName(file.Path)} {method}: the Guard condition reads real HP (JevCall.HpRatio), not the predicted GetHealthRatio",
                guard.Count == 1 && bad == 0 && guard[0].Contains("JevCall.HpRatio"),
                guard.Count == 0 ? "no Guard condition found" : string.Join(" | ", guard));
        }

        // The Recuperate gate and the generic table's row test: same switch.
        var gate = CsSource.Methods(generic.Sanitized, "GenericDefensives.cs", StaticBool).SingleOrDefault(m => m.Name == "RecuperateGate")
                   ?? throw new MissingMethodException("GenericDefensives.cs", "RecuperateGate");
        Harness.Case("GenericDefensives.RecuperateGate passes real HP (JevCall.HpRatio) to the row test",
            gate.Body.Contains("JevCall.HpRatio") && !gate.Body.Contains("GetHealthRatio"),
            CsSource.Squash(gate.Body));
        var health = LinesWith(generic, "float health = ");
        Harness.Case("GenericDefensives.TryFire reads the row-test health from real HP (JevCall.HpRatio)",
            health.Count == 1 && health[0].Contains("JevCall.HpRatio") && !health[0].Contains("GetHealthRatio"),
            health.Count == 0 ? "health read not found" : health[0]);

        // Purify's block is status-only: nothing in it may grow an HP read (characterization of scope).
        var purify = GuardConditions(ability, "EmergencyAbility", VirtualBool, "PurifyPvP.CanUse");
        Harness.Case("the Purify block stays status-only (no HP read in its conditions)",
            purify.All(c => !c.Contains("HealthRatio")),
            purify.Count + " purify conditions");

        // ---- characterization: GetHealthRatio IS the predicted read (why these sites must not use it) ------------
        var ratio = CsSource.Methods(helper.Sanitized, "ObjectHelper.cs", new(@"\bpublic\s+static\s+float\s+GetHealthRatio\s*\(", RegexOptions.Compiled))
            .Where(m => m.Body.Contains("RefinedHP"));
        Harness.Case("ObjectHelper.GetHealthRatio still prefers DataCenter.RefinedHP (predicted HP): the self-defence sites above must not read it",
            ratio.Count() == 1,
            "the GetHealthRatio extension reads RefinedHP first");

        // ---- canaries ---------------------------------------------------------------------------------------------
        const string oldGuard = "GuardPvP.CanUse(out act) && Player?.GetHealthRatio() <= Service.Config.HealthForGuard";
        Harness.Canary("the OLD Guard text (predicted GetHealthRatio) is accepted as real-HP",
            oldGuard.Contains("JevCall.HpRatio") && !oldGuard.Contains("GetHealthRatio"));
        Harness.Canary("a HpRatio that returns the predicted-style value (no clamp, max 0 reads 1) passes the T1 table",
            RotationSolver.Decisions.JevCall.HpRatio(120, 100) > 1f || RotationSolver.Decisions.JevCall.HpRatio(0, 0) != 0f);
    }
}
