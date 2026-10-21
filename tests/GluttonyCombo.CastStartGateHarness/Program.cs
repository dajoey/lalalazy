// Offline proof for the shared cast-start movement gate (task
// tasks-20261021-ffxiv-forced-slidecast-roots-player-01).
//
// Compiles the REAL src/GluttonyCombo/GluttonyCombo/AutoRotation/CastStartGate.cs
// - the exact file that ships - with no Dalamud and no ECommons. The report:
// with the auto-rotation driving caster jobs (and the orbwalker integration
// configured but its plugin not installed), a spell with a cast bar began the
// instant movement stopped, so every reposition out of danger meant standing in
// the cast bar until its slide-cast window. The stillness window makes a spell
// wait that many seconds after movement stops; instants, orbwalker-driven
// movement and the shipped moving-blocks behavior are unchanged.
//
//   dotnet build tests/GluttonyCombo.CastStartGateHarness -c Release
//   dotnet tests/GluttonyCombo.CastStartGateHarness/bin/Release/net10.0/GluttonyCombo.CastStartGateHarness.dll
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

void Blocks(string name, float castTime, bool orbwalking, double timeMovingMs, double timeStillS, float stillnessS, bool expected) =>
    Case(name, expected, CastStartGate.Blocks(castTime, orbwalking, TimeSpan.FromMilliseconds(timeMovingMs), TimeSpan.FromSeconds(timeStillS), stillnessS));

Console.WriteLine("== Group 1: shipped defaults (stillness 0) keep the 1.0.4.315 behavior ==");
Blocks("stillness 0: moving blocks a spell (Glans, Bassima - 1.5s bars)", 1.5f, false, 200, 0, 0f, true);
Blocks("stillness 0: stationary allows a spell the instant movement stops", 1.5f, false, 0, 5, 0f, false);
Blocks("stillness 0: timeStill below zero-window is impossible; stationary allows", 2.0f, false, 0, 0.01, 0f, false);

Console.WriteLine();
Console.WriteLine("== Group 2: never blocks instants or orbwalker-driven movement ==");
Blocks("instant never blocks, even while moving (Aqua Sorcere-class fixes)", 0f, false, 300, 0, 0f, false);
Blocks("instant never blocks under a stillness window either", 0f, false, 0, 0.2, 1.5f, false);
Blocks("orbwalking never blocks while moving", 1.5f, true, 200, 0, 0f, false);
Blocks("orbwalking never blocks under a stillness window", 1.5f, true, 0, 0.1, 1.5f, false);

Console.WriteLine();
Console.WriteLine("== Group 3: the stillness window holds spells after a fresh stop (new behavior) ==");
Blocks("stillness 1.5: a spell 0.5s after the stop waits", 1.5f, false, 0, 0.5, 1.5f, true);
Blocks("stillness 1.5: at the window's edge the spell may begin", 1.5f, false, 0, 1.5, 1.5f, false);
Blocks("stillness 2.0: at the window's edge the spell may begin", 2.0f, false, 0, 2.0, 2.0f, false);
Blocks("stillness 1.5: a spell just before the edge still waits", 1.5f, false, 0, 1.4, 1.5f, true);

Console.WriteLine();
Console.WriteLine("== Group 4: movement always wins, whatever the window says ==");
Blocks("stillness 1.5: actually moving blocks even with long stillness", 1.5f, false, 100, 99, 1.5f, true);
Blocks("stillness 1.5: moving blocks right at the stop boundary (timeMoving just started)", 1.5f, false, 1, 0, 1.5f, true);

Console.WriteLine();
if (failures > 0)
{
    Console.WriteLine($"FAILED: {failures} of {total} cases");
    return 1;
}
Console.WriteLine($"OK: {total} cases");
return 0;
