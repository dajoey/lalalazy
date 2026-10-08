using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 7, VPR-COIL (VPR-1). T2: the Rattling Coil gate must be <c>UncoiledFuryPvP.Cooldown.IsCoolingDown</c>
///     alone. The rotation never issues Snake Scales, so a <c>SnakeScalesPvP.Cooldown.IsCoolingDown</c> clause is
///     true only after a manual press and keeps Rattling Coil from ever being used.
/// </summary>
internal static class VprCoil
{
    public static void Run()
    {
        Console.WriteLine("-- change 7 VPR-COIL (T2) --");

        var (_, san) = SourceFiles.Rotation("VPR_Default.PVP.cs");
        var m = SourceFiles.OverrideMethod("VPR_Default.PVP.cs", "EmergencyAbility");

        // The inner gate: the `if (...)` directly inside `if (RattlingCoilPvP.CanUse(out action)) { ... }`.
        var gates = RattlingCoilGates(m.Body);
        Harness.Case("VPR EmergencyAbility has a Rattling Coil branch", gates != null);
        if (gates == null) return;
        Harness.Case("Rattling Coil branch gate is exactly UncoiledFuryPvP.Cooldown.IsCoolingDown",
            gates.Count == 1 && gates[0] == "UncoiledFuryPvP.Cooldown.IsCoolingDown", string.Join(" | ", gates));

        Harness.Case("VPR rotation never waits on Snake Scales", !Regex.IsMatch(san, @"\bSnakeScalesPvP\b"),
            "SnakeScalesPvP referenced in VPR_Default.PVP.cs");

        // Canary: the same extraction on the old branch text must not come back as the new gate.
        const string oldBranch = "if (RattlingCoilPvP.CanUse(out action)) { if (SnakeScalesPvP.Cooldown.IsCoolingDown && UncoiledFuryPvP.Cooldown.IsCoolingDown) { return true; } }";
        var oldGates = RattlingCoilGates(oldBranch);
        Harness.Canary("old Rattling Coil gate is accepted as exactly UncoiledFuryPvP.Cooldown.IsCoolingDown",
            oldGates is { Count: 1 } && oldGates[0] == "UncoiledFuryPvP.Cooldown.IsCoolingDown");
    }

    /// <summary>The inner `if` conditions of the `if (RattlingCoilPvP.CanUse(out action)) { ... }` branch, or null.</summary>
    private static List<string>? RattlingCoilGates(string body)
    {
        var at = Regex.Match(body, @"if\s*\(\s*RattlingCoilPvP\.CanUse\(out action\)\s*\)\s*\{");
        if (!at.Success) return null;
        var open = at.Index + at.Length - 1;
        return CsSource.IfConditions(body.Substring(open + 1, CsSource.Match(body, open) - open - 1));
    }
}
