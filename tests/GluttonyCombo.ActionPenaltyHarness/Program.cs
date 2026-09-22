// Offline proof for the action-penalty decision table.
//
// Compiles the REAL src/GluttonyCombo/GluttonyCombo/Core/ActionPenalty.cs - the exact file that
// ships - with no Dalamud and no ECommons. The null-statuses case is the regression guard for
// fingerprint 3072e17f8875: Player.Status is null with no local player, and Enumerable.Any on
// that null source threw ArgumentNullException out of UseActionDetour (4 hits in 26 h on
// 1.0.4.230). HasPenalty(null, ...) must return false, never throw.
//
//   dotnet build tests\GluttonyCombo.ActionPenaltyHarness -c Release
//   dotnet tests\GluttonyCombo.ActionPenaltyHarness\bin\Release\net10.0\GluttonyCombo.ActionPenaltyHarness.dll
//
// Prints PASS/FAIL per case, "OK" and exit 0 when every case passes.

using System.Collections.Frozen;
using GluttonyCombo.Core;

var accelerationBombs = new uint[] { 1001, 1002 }.ToFrozenSet();
var pyretics = new uint[] { 2001 }.ToFrozenSet();
var misc = new uint[] { 3001 }.ToFrozenSet();

var failures = 0;
var total = 0;

void Case(string name, IEnumerable<(uint StatusId, float RemainingTime)>? statuses, float threshold, bool expected)
{
    total++;
    bool actual;
    try
    {
        actual = ActionPenalty.HasPenalty(statuses, threshold, accelerationBombs, pyretics, misc);
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL  {name,-64} threw {ex.GetType().Name}, expected {expected}");
        return;
    }

    var ok = actual == expected;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-64} => {actual}, expected {expected}");
}

Console.WriteLine("== Group 1: the 3072e17f8875 regression - no local player must not throw ==");
Case("null statuses return false (was ArgumentNullException)", null, 1.5f, false);
Case("empty statuses return false", Array.Empty<(uint, float)>(), 1.5f, false);

Console.WriteLine();
Console.WriteLine("== Group 2: Acceleration Bomb is gated on remaining time ==");
Case("bomb inside threshold blocks", new[] { (1001u, 1.0f) }, 1.5f, true);
Case("bomb exactly at threshold blocks (<=)", new[] { (1001u, 1.5f) }, 1.5f, true);
Case("bomb past threshold does not block", new[] { (1001u, 2.0f) }, 1.5f, false);

Console.WriteLine();
Console.WriteLine("== Group 3: Pyretic blocks regardless of remaining time ==");
Case("pyretic with huge remaining time blocks", new[] { (2001u, 999f) }, 1.5f, true);
Case("pyretic at zero remaining time blocks", new[] { (2001u, 0f) }, 1.5f, true);

Console.WriteLine();
Console.WriteLine("== Group 4: Misc pauses are gated on remaining time like bombs ==");
Case("misc inside threshold blocks", new[] { (3001u, 0.5f) }, 1.5f, true);
Case("misc past threshold does not block", new[] { (3001u, 5.0f) }, 1.5f, false);

Console.WriteLine();
Console.WriteLine("== Group 5: unknown statuses never block ==");
Case("unknown id does not block", new[] { (9999u, 0.1f) }, 1.5f, false);
Case("pyretic among unknowns still blocks", new[] { (9999u, 0.1f), (2001u, 999f) }, 1.5f, true);
Case("bomb among unknowns inside threshold still blocks", new[] { (9999u, 0.1f), (1002u, 0.2f) }, 1.5f, true);

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine($"OK ({total}/{total} cases pass)");
    return 0;
}

Console.WriteLine($"FAIL ({failures}/{total} cases failed)");
return 1;
