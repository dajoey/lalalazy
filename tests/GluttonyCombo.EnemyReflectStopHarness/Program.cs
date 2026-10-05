// Offline proof for the enemy-reflect stop decision table.
//
// Compiles the REAL src/GluttonyCombo/GluttonyCombo/AutoRotation/EnemyReflectStop.cs - the exact
// file that ships - with no Dalamud and no ECommons. The Crucible cases are the regression guard
// for the 2026-10-04 silent dispels: the by-name reflect set caught the Crucible's own
// counter-stance spikes (Blaze Spikes 5465, Ice Spikes 2528 - both dispel rows), and the stop
// froze the whole auto-rotation (target the player, skip every press) for each stance's full
// 12 s - so the dispel that removes the stance could never fire: six windows, zero presses,
// zero dispels (fight 2026-10-04 20:17-20:22, GluttonyCombo 1.0.4.281).
//
//   dotnet build tests\GluttonyCombo.EnemyReflectStopHarness -c Release
//   dotnet tests\GluttonyCombo.EnemyReflectStopHarness\bin\Release\net10.0\GluttonyCombo.EnemyReflectStopHarness.dll
//
// Prints PASS/FAIL per case, "OK" and exit 0 when every case passes.

using System.Collections.Frozen;
using GluttonyCombo.AutoRotation;

// Synthetic values that mirror the real data exactly:
//   reflects = the by-name EnemyReflects set, which contains the Crucible's Blaze Spikes (5465)
//              and Ice Spikes (2528) alongside Eureka's own spikes (here 1111).
//   stances  = BST_CrucibleData.StanceStatuses intersect DispellableBuffs (5465, 2528) - the
//              counter stances the Crucible dispel lane removes in one cast.
//   5434 Paralyzing Spikes and 5145 Needles Out are Crucible counter stances that are NOT
//   dispellable and NOT name-matched reflects - they never stopped anything.
var reflects = new uint[] { 5465, 2528, 1111 }.ToFrozenSet();
var stances = new HashSet<uint> { 5465, 2528 };

var failures = 0;
var total = 0;

void Case(string name, IEnumerable<uint>? statusIds, bool crucibleTargeting, bool expected)
{
    total++;
    var actual = EnemyReflectStop.StopsRotation(statusIds, reflects, stances, crucibleTargeting);
    var ok = actual == expected;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-76} => {actual}, expected {expected}");
}

Console.WriteLine("== Group 1: the 2026-10-04 silent dispels - the Crucible's dispellable counter stances do not stop ==");
Case("Blaze Spikes on the Crucible board does not stop (was a 12 s full stop)", new uint[] { 5465 }, true, false);
Case("Ice Spikes on the Crucible board does not stop", new uint[] { 2528 }, true, false);
Case("Blaze Spikes beside other statuses does not stop", new uint[] { 4648, 5465, 4614 }, true, false);

Console.WriteLine();
Console.WriteLine("== Group 2: every other reflect keeps the full stop, Crucible board or not ==");
Case("a non-Crucible reflect still stops on a Crucible board", new uint[] { 1111 }, true, true);
Case("a second non-Crucible reflect stops beside the stance", new uint[] { 5465, 1111 }, true, true);
Case("a Crucible counter stance that is not a reflect never stops", new uint[] { 5434 }, true, false);
Case("Needles Out, not a reflect, never stops", new uint[] { 5145 }, true, false);

Console.WriteLine();
Console.WriteLine("== Group 3: outside the Crucible every reflect keeps the full stop (Eureka, the deep dungeons) ==");
Case("Blaze Spikes outside the Crucible still stops", new uint[] { 5465 }, false, true);
Case("Ice Spikes outside the Crucible still stops", new uint[] { 2528 }, false, true);
Case("two Crucible stances outside the Crucible still stop", new uint[] { 5465, 2528 }, false, true);

Console.WriteLine();
Console.WriteLine("== Group 4: nothing readable is not a stop ==");
Case("empty statuses do not stop", Array.Empty<uint>(), true, false);
Case("null statuses do not stop", null, true, false);
Case("null statuses do not stop outside the Crucible", null, false, false);

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine($"OK ({total}/{total} cases pass)");
    return 0;
}

Console.WriteLine($"FAIL ({failures}/{total} cases failed)");
return 1;
