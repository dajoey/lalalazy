// Offline proof for the tank buster scope decision table.
//
// Compiles the REAL src/GluttonyCombo/GluttonyCombo/Core/TankbusterScope.cs - the exact file that
// ships - with no Dalamud and no ECommons. If a game type ever leaks into that file this build
// breaks, which is the point: the scope decision is meant to stay pure so it can be asserted here
// instead of only in a live client.
//
//   dotnet build tests\GluttonyCombo.TankbusterScopeHarness -c Release
//   dotnet tests\GluttonyCombo.TankbusterScopeHarness\bin\Release\net10.0\GluttonyCombo.TankbusterScopeHarness.dll
//
// Prints PASS/FAIL per case, "OK" and exit 0 when every case passes.

using GluttonyCombo.Core;
using Role = GluttonyCombo.Core.TankbusterScope.TargetRole;

var failures = 0;
var total = 0;

void Case(string name, bool inParty, bool friendly, Role role, bool setting, bool expected)
{
    total++;
    var actual = TankbusterScope.Allows(inParty, friendly, role, setting);
    var ok = actual == expected;
    if (!ok) failures++;
    Console.WriteLine(
        $"{(ok ? "PASS" : "FAIL")}  {name,-72} " +
        $"(inParty={inParty,-5} friendly={friendly,-5} role={role,-15} setting={setting,-5} " +
        $"=> {actual}, expected {expected})");
}

Console.WriteLine("== Group 1: setting OFF must be byte-identical to the old party-only behaviour ==");
// The old code was: FilterToTargetRole(Tank) AND (IsInParty() OR (false && IsFriendly()))
// i.e. exactly "in party AND role is Tank". Every combination is enumerated below.
foreach (var friendly in new[] { true, false })
{
    Case("OFF: party tank is in scope", true, friendly, Role.Tank, false, true);
    Case("OFF: party healer/dps is NOT (role filter still does real work)", true, friendly, Role.OtherCombatRole, false, false);
    Case("OFF: party member with no resolvable role is NOT", true, friendly, Role.Unresolved, false, false);
    Case("OFF: out-of-party tank is NOT", false, friendly, Role.Tank, false, false);
    Case("OFF: out-of-party other role is NOT", false, friendly, Role.OtherCombatRole, false, false);
    Case("OFF: out-of-party unresolved is NOT", false, friendly, Role.Unresolved, false, false);
}

Console.WriteLine();
Console.WriteLine("== Group 2: setting ON must not change ANY in-party answer (the regression guard) ==");
// This is the case that matters most for existing users: turning the feature on must never alter
// how the plugin treats the player's own party.
foreach (var friendly in new[] { true, false })
{
    Case("ON: party tank still in scope", true, friendly, Role.Tank, true, true);
    Case("ON: party healer/dps still excluded", true, friendly, Role.OtherCombatRole, true, false);
    Case("ON: party unresolved still excluded", true, friendly, Role.Unresolved, true, false);
}

Console.WriteLine();
Console.WriteLine("== Group 3: setting ON, out of party - the actual new behaviour ==");
Case("ON: alliance TANK is in scope", false, true, Role.Tank, true, true);
Case("ON: trusted/Occult NPC with unresolvable role is in scope (the v1.0.4.171 gap)", false, true, Role.Unresolved, true, true);
Case("ON: alliance HEALER/DPS is still excluded - roles that DO resolve are still respected", false, true, Role.OtherCombatRole, true, false);

Console.WriteLine();
Console.WriteLine("== Group 4: friendliness is a hard gate on the out-of-party arm ==");
// isFriendly comes from TargetIsFriendly, which probes CanUseOn(Esuna) with a Cure fallback for
// event NPCs. If we cannot land a heal on it, shielding and announcing it are both pointless.
Case("ON: non-friendly out-of-party tank is excluded", false, false, Role.Tank, true, false);
Case("ON: non-friendly out-of-party unresolved is excluded (an enemy is not an Occult ally)", false, false, Role.Unresolved, true, false);
Case("ON: non-friendly out-of-party other role is excluded", false, false, Role.OtherCombatRole, true, false);

Console.WriteLine();
Console.WriteLine("== Group 5: negative controls - prove the fixture can actually distinguish ==");
// Without these, a decision table that returned a constant would sail through everything above.
var everTrue = false;
var everFalse = false;
foreach (var ip in new[] { true, false })
foreach (var fr in new[] { true, false })
foreach (var rl in new[] { Role.Tank, Role.OtherCombatRole, Role.Unresolved })
foreach (var st in new[] { true, false })
{
    if (TankbusterScope.Allows(ip, fr, rl, st)) everTrue = true;
    else everFalse = true;
}

total++;
if (everTrue && everFalse) Console.WriteLine("PASS  negative control: the table is not constant (yields both true and false)");
else { failures++; Console.WriteLine("FAIL  negative control: the table is CONSTANT - every assertion above is worthless"); }

// And prove the setting is load-bearing rather than decorative: there must exist at least one
// input where flipping only the setting flips the answer.
total++;
var settingMatters = false;
foreach (var ip in new[] { true, false })
foreach (var fr in new[] { true, false })
foreach (var rl in new[] { Role.Tank, Role.OtherCombatRole, Role.Unresolved })
{
    if (TankbusterScope.Allows(ip, fr, rl, true) != TankbusterScope.Allows(ip, fr, rl, false))
        settingMatters = true;
}
if (settingMatters) Console.WriteLine("PASS  negative control: TankbustersBeyondParty actually changes an outcome");
else { failures++; Console.WriteLine("FAIL  negative control: the setting is ignored - it is a dead toggle"); }

// NOTE on what this harness deliberately does NOT assert.
//
// "The shield and the alert agree" cannot be tested here, and writing a case that calls
// TankbusterScope.Allows twice and compares the two results would be a tautology dressed up as a
// test - it would pass even if the two call sites in VFX.cs had drifted apart again, which is the
// exact defect this card is about.
//
// That property is instead guaranteed structurally: VFX.cs has ONE predicate, InTankbusterScope,
// and both TryGetTankBusterTarget and PlayTankbusterAlert call it. The check that actually
// defends it is a source-level one - if a future change reintroduces a second scope test, this
// harness will not notice. Grep for IsInParty() in VFX.cs; it should appear only inside
// InTankbusterScope.

Console.WriteLine();
Console.WriteLine($"{total - failures}/{total} passed.");
if (failures > 0)
{
    Console.WriteLine($"{failures} FAILURES");
    return 1;
}

Console.WriteLine("OK");
return 0;
