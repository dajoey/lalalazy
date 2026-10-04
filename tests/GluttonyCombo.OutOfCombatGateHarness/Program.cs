// Offline proof for the shared out-of-combat gate (task
// tasks-20261004-gluttony-out-of-combat-self-target-overcap-01).
//
// Compiles the REAL src/GluttonyCombo/GluttonyCombo/AutoRotation/OutOfCombatGate.cs
// - the exact file that ships - with no Dalamud and no ECommons. The 2026-10-04
// report: standing in the field out of combat, the auto-rotation hard-targeted a
// nearby FATE mob, ran the pre-pull chain and dashed the capped Shield Charge in,
// and the reflect-penalty guard self-selected the player every tick - the whole
// pull ran with no player input. These cases pin the two rules that stop the
// whole class: no target writes out of combat, no hostile-only presses out of
// combat.
//
//   dotnet build tests\GluttonyCombo.OutOfCombatGateHarness -c Release
//   dotnet tests\GluttonyCombo.OutOfCombatGateHarness\bin\Release\net10.0\GluttonyCombo.OutOfCombatGateHarness.dll
//
// Prints PASS/FAIL per case, "OK" and exit 0 when every case passes.

using GluttonyCombo.AutoRotation;

var failures = 0;
var total = 0;

void Case(string name, bool expected, bool actual)
{
    total++;
    var ok = actual == expected;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-72} => {actual}, expected {expected}");
}

void Fire(string name, bool canTargetHostile, bool canTargetSelf, bool targetArea, bool expected) =>
    Case(name, expected, OutOfCombatGate.MayFire(canTargetHostile, canTargetSelf, targetArea));

Console.WriteLine("== Group 1: hostile-only actions are combat-only - no press out of combat ==");
Fire("hostile-only action may not fire (Smash Axe, Shieldsplitter)", true, false, false, false);
Fire("hostile-only gap-close may not fire (Shield Charge overcap dash)", true, false, false, false);

Console.WriteLine();
Console.WriteLine("== Group 2: deliberately out-of-combat-legal actions keep firing ==");
Fire("self+hostile capable may fire (prepull buffs, BST horns, mudras)", true, true, false, true);
Fire("friendly-only may fire (cures, raises, Swiftcast, Esuna)", false, true, false, true);
Fire("ground-targeted may fire (ground actions are not single-enemy)", false, false, true, true);
Fire("hostile ground may fire (Doton-class, aimed at ground not an enemy)", true, false, true, true);
Fire("no target bits at all may fire (unknown sheet row: do not block)", false, false, false, true);

Console.WriteLine();
Console.WriteLine("== Group 3: the hard-target write is combat-only ==");
Case("MayWriteTarget(out of combat) is false", false, OutOfCombatGate.MayWriteTarget(false));
Case("MayWriteTarget(in combat) is true", true, OutOfCombatGate.MayWriteTarget(true));

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine($"OK ({total}/{total} cases pass)");
    return 0;
}

Console.WriteLine($"FAIL ({failures}/{total} cases failed)");
return 1;
