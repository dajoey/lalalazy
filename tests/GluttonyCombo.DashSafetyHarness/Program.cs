// Shield Charge dash safety (bst1013), offline against the shipped BST_DashSafety.cs.
//
// What changed and why it is asserted here:
//  1. The landing point alone decides. BossMod Reborn's IsDashSafe rejects any point inside any forbidden zone
//     regardless of activation, and an enemy's centre usually sits inside its own zones, so requiring the centre
//     too held dashes whose landing was fine. Stock BMR checks its own gap-closers at the landing point.
//  2. Overcap-only mode (gap-close off, spend at full charges) never gap-closes; its range stalls read
//     "overcap-only", never a BMR danger label (telemetry 10-06/07: dh=danger was the largest SG label, ~10% of
//     fight time, all under this configuration).
//  3. dh=danger splits into danger-landing (the hold) and danger-enemy (diagnostic: only the centre is unsafe).

using GluttonyCombo.Combos.PvE;
using L = GluttonyCombo.Combos.PvE.BST_DashSafety.Landing;

var failures = 0;

void Check<T>(string name, T expected, T actual)
{
    var pass = EqualityComparer<T>.Default.Equals(expected, actual);
    if (!pass) failures++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {name}: expected {expected?.ToString() ?? "null"}, got {actual?.ToString() ?? "null"}");
}

// ------------------------------------------------------------------------------------------- 1. landing verdict
Check("enemy centre in a zone, landing clear -> Safe (the landing decides)", L.Safe, BST_DashSafety.Judge(false, true));
Check("enemy centre clear, landing in a zone -> Danger", L.Danger, BST_DashSafety.Judge(true, false));
Check("both clear -> Safe", L.Safe, BST_DashSafety.Judge(true, true));
Check("both in zones -> Danger", L.Danger, BST_DashSafety.Judge(false, false));
Check("no landing answer -> Unknown", L.Unknown, BST_DashSafety.Judge(true, null));
Check("no centre answer, landing clear -> Safe (the centre is diagnostic only)", L.Safe, BST_DashSafety.Judge(null, true));
Check("diagnostic: centre-only danger seen", true, BST_DashSafety.EnemyCentreOnlyDanger(false, true));
Check("diagnostic: landing danger is not centre-only", false, BST_DashSafety.EnemyCentreOnlyDanger(false, false));
Check("diagnostic: no answers -> not centre-only", false, BST_DashSafety.EnemyCentreOnlyDanger(null, null));

// ------------------------------------------------------------------------------------------- 2. hold label
// Helper: gap-close on, level ok, a charge ready, 10 y out, nothing protected.
string? Gap(L landing, bool centreOnly = false, bool ready = true, int charges = 1, float dist = 10f, bool prot = false)
    => BST_DashSafety.HoldLabel(true, true, false, ready, charges, dist, prot, landing, centreOnly);

Check("gap-close: landing in a zone -> danger-landing", "danger-landing", Gap(L.Danger));
Check("gap-close: only the centre in a zone -> danger-enemy (diagnostic, not a hold)", "danger-enemy", Gap(L.Safe, centreOnly: true));
Check("gap-close: all clear -> null (could fire)", (string?)null, Gap(L.Safe));
Check("gap-close: unreachable", "unreachable", Gap(L.Unreachable));
Check("gap-close: unknown", "unknown", Gap(L.Unknown));
Check("gap-close: recast", "recast", Gap(L.Danger, ready: false, charges: 1));
Check("gap-close: charges", "charges", Gap(L.Danger, ready: false, charges: 0));
Check("gap-close: far", "far", Gap(L.Danger, dist: 25f));
Check("gap-close: protected", "protected", Gap(L.Danger, prot: true));
// dash-hold lane 2026-10-14: a mechanic going off (or just gone) names the hold, ranked after the landing verdicts and the range gates.
string? Mech(string? hold, L landing = L.Safe, bool centreOnly = false, float dist = 10f)
    => BST_DashSafety.HoldLabel(true, true, false, true, 1, dist, false, landing, centreOnly, hold);
Check("gap-close: a cast about to go off names cast-ending", "cast-ending", Mech("cast-ending"));
Check("gap-close: settling after the mechanic named", "settling", Mech("settling"));
Check("gap-close: a danger landing still reads danger-landing, not the mechanic", "danger-landing", Mech("cast-ending", L.Danger));
Check("gap-close: far still reads far", "far", Mech("cast-ending", dist: 25f));
Check("gap-close: the mechanic outranks the centre-only diagnostic", "hit-imminent", Mech("hit-imminent", centreOnly: true));
Check("gap-close: no mechanic, all clear -> null (unchanged)", (string?)null, Mech(null));
Check("below the level -> lvl", "lvl", BST_DashSafety.HoldLabel(false, true, false, true, 1, 10f, false, L.Danger, false));
Check("dash off entirely -> off", "off", BST_DashSafety.HoldLabel(true, false, false, true, 1, 10f, false, L.Danger, false));

// Overcap-only (the live config on 10-06/07): never a BMR danger label, whatever BMR answered.
var overcapLabels = new HashSet<string?>();
foreach (var ready in new[] { true, false })
foreach (var charges in new[] { 0, 1, 2 })
foreach (var dist in new[] { 2f, 10f, 25f })
foreach (var prot in new[] { true, false })
foreach (var landing in Enum.GetValues<L>())
foreach (var centreOnly in new[] { true, false })
    overcapLabels.Add(BST_DashSafety.HoldLabel(true, false, true, ready, charges, dist, prot, landing, centreOnly));
Check("overcap-only: every state reads overcap-only (288 combinations)", "overcap-only", string.Join(",", overcapLabels));
Check("overcap-only below the level still reads lvl", "lvl", BST_DashSafety.HoldLabel(false, false, true, true, 2, 10f, false, L.Danger, false));

// ------------------------------------------------------------------------------------------- 3. landing read gate
Check("read: gap-close on asks BMR", true, BST_DashSafety.LandingReadNeeded(true, false, 1, 2));
Check("read: overcap-only below full charges does not ask (its dash cannot fire)", false, BST_DashSafety.LandingReadNeeded(false, true, 1, 2));
Check("read: overcap-only at full charges asks", true, BST_DashSafety.LandingReadNeeded(false, true, 2, 2));
Check("read: dash off does not ask", false, BST_DashSafety.LandingReadNeeded(false, false, 2, 2));

// ------------------------------------------------------------------------------------------- 4. DS| diagnostic line
Check("DS line format", "DS|1791000000000|t=14531|enemy=0|landing=1|v=safe",
    BST_DashSafety.DiagnosticLine(1_791_000_000_000L, 14531, false, true));
Check("DS line with no answers", "DS|1|t=1|enemy=x|landing=x|v=unknown", BST_DashSafety.DiagnosticLine(1, 1, null, null));
Check("DS key changes with the centre answer", false,
    BST_DashSafety.DiagnosticKey(14531, true, true) == BST_DashSafety.DiagnosticKey(14531, false, true));
Check("DS key changes with the target", false,
    BST_DashSafety.DiagnosticKey(14531, true, true) == BST_DashSafety.DiagnosticKey(14532, true, true));

// ------------------------------------------------------------------------------------------- 5. live wiring (source text)
var bst = RepoFile("BST.cs");
var live = RepoFile("BST_CrucibleLive.cs");
Check("wiring: BST.cs fills DashHold from the shared ladder", true,
    bst.Contains("s.DashHold = BST_DashSafety.HoldLabel(", StringComparison.Ordinal));
Check("wiring: BST.cs gates the landing read", true,
    bst.Contains("BST_DashSafety.LandingReadNeeded(", StringComparison.Ordinal));
Check("wiring: ReadDashLanding takes its verdict from Judge", true,
    live.Contains("BST_DashSafety.Judge(atEnemy, atLanding)", StringComparison.Ordinal));
Check("wiring: BST.cs feeds the hazard clock every tick", true,
    bst.Contains("BST_RotationLogic.FeedDashClock(ref _dashHazardClock, ref s, Environment.TickCount64 / 1000.0)", StringComparison.Ordinal));
Check("wiring: BST.cs passes the mechanic hold into the SG| label", true,
    bst.Contains("mechanicHold: BST_RotationLogic.DashMechanicHold(s, BST_Crucible && s.CrucibleBoard != 0)", StringComparison.Ordinal));
Check("wiring: no inline both-required verdict left", false,
    live.Contains("atEnemy.Value && atLanding.Value", StringComparison.Ordinal));

if (failures > 0)
{
    Console.WriteLine($"{failures} case(s) FAILED");
    return 1;
}

Console.WriteLine("OK");
return 0;

static string RepoFile(string name)
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tools", "check-preset-ids.py")))
        dir = dir.Parent;
    return dir is null
        ? ""
        : File.ReadAllText(Path.Combine(dir.FullName, "src", "GluttonyCombo", "GluttonyCombo", "Combos", "PvE", "BST", name));
}
