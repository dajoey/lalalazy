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
        var at = Regex.Match(m.Body, @"if\s*\(\s*RattlingCoilPvP\.CanUse\(out action\)\s*\)\s*\{");
        Harness.Case("VPR EmergencyAbility has a Rattling Coil branch", at.Success);
        if (!at.Success) return;
        var open = at.Index + at.Length - 1;
        var inner = m.Body.Substring(open + 1, CsSource.Match(m.Body, open) - open - 1);
        var gates = CsSource.IfConditions(inner);
        Harness.Case("Rattling Coil branch gate is exactly UncoiledFuryPvP.Cooldown.IsCoolingDown",
            gates.Count == 1 && gates[0] == "UncoiledFuryPvP.Cooldown.IsCoolingDown", string.Join(" | ", gates));

        Harness.Case("VPR rotation never waits on Snake Scales", !Regex.IsMatch(san, @"\bSnakeScalesPvP\b"),
            "SnakeScalesPvP referenced in VPR_Default.PVP.cs");

        // Canary: the old gate does not satisfy the exact-gate test.
        const string oldGate = "SnakeScalesPvP.Cooldown.IsCoolingDown && UncoiledFuryPvP.Cooldown.IsCoolingDown";
        Harness.Canary("old Rattling Coil gate equals the new gate", oldGate == "UncoiledFuryPvP.Cooldown.IsCoolingDown");
    }
}
