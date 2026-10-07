// Offline proof for the AutoRotation AoE enemy-count gate (task
// tasks-20261007-ffxiv-blm1-aoe2-build-01, ranked row BLM-1).
//
// Compiles the REAL src/GluttonyCombo/GluttonyCombo/AutoRotation/AoETargetGate.cs
// - the exact file that ships - with no Dalamud and no ECommons.
//
// The 2026-10-04 Occult Crescent fight (Enc 2359 in the fight record): a 224 s
// window with exactly two engaged enemies where the character cast Fire IV x32,
// Paradox x8, High Thunder x9, Blizzard III x5, Blizzard IV x5, Xenoglossy x9
// and not one Fire II / High Fire II / Blizzard II / High Blizzard II - the AoE
// rotation never engaged although the BLM AoE preset (BLM_AoE_AdvancedMode) was
// enabled and the BLM AoE combo itself has a dedicated exactly-2 branch
// (BLM_Helper.cs:631-635: Blizzard4 at exactly 2, Freeze otherwise). The gate in
// ExecuteAoE returned false because the best AoE target's enemy count (2) sat
// below DPSAoETargets (default 3, AutoRotationConfig.cs:46). The rule this file
// pins: for BLM and only BLM, exactly 2 enemies in range lets the AoE rotation
// through; every other job and every other enemy count behave exactly as the
// configured threshold says, and a null threshold (AoE disabled) still means
// never.
//
//   dotnet run --project tests\GluttonyCombo.RotationHarness.BLM -c Release
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

bool Gate(int? aoeTargets, int maxHit, bool isBlackMage) =>
    AoETargetGate.MayRunAoe(aoeTargets, maxHit, isBlackMage);

Console.WriteLine("== Group 1: BLM-1 - the BLM AoE rotation engages at exactly 2 enemies ==");
Case("BLM, 2 enemies, default threshold 3: AoE runs (Blizzard4/Freeze branch reachable)", true, Gate(3, 2, isBlackMage: true));
Case("BLM, 2 enemies, raised threshold 4: AoE still runs (2 is BLM's own AoE count)", true, Gate(4, 2, isBlackMage: true));
Case("BLM, 2 enemies, threshold 2: AoE runs (threshold path, unchanged)", true, Gate(2, 2, isBlackMage: true));

Console.WriteLine();
Console.WriteLine("== Group 2: negatives - every other job and count keep the configured threshold ==");
Case("BLM, 1 enemy: no AoE (stays single-target)", false, Gate(3, 1, isBlackMage: true));
Case("non-BLM, 1 enemy: no AoE", false, Gate(3, 1, isBlackMage: false));
Case("non-BLM, 2 enemies, default threshold 3: threshold unchanged, no AoE", false, Gate(3, 2, isBlackMage: false));
Case("non-BLM, 2 enemies, threshold 4: no AoE", false, Gate(4, 2, isBlackMage: false));
Case("non-BLM, 2 enemies, threshold 2: AoE runs (user's own threshold, unchanged)", true, Gate(2, 2, isBlackMage: false));
Case("BLM, 3 enemies, threshold 3: AoE runs (threshold path, not the exception)", true, Gate(3, 3, isBlackMage: true));
Case("non-BLM, 3 enemies, threshold 3: AoE runs (unchanged)", true, Gate(3, 3, isBlackMage: false));
Case("non-BLM, 5 enemies, threshold 3: AoE runs (unchanged)", true, Gate(3, 5, isBlackMage: false));

Console.WriteLine();
Console.WriteLine("== Group 3: a null threshold (AoE disabled) means never, for every job ==");
Case("BLM, 2 enemies, AoE disabled: no AoE (the exception needs AoE enabled)", false, Gate(null, 2, isBlackMage: true));
Case("non-BLM, 5 enemies, AoE disabled: no AoE", false, Gate(null, 5, isBlackMage: false));

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine($"OK ({total}/{total} cases pass)");
    return 0;
}

Console.WriteLine($"FAIL ({failures}/{total} cases failed)");
return 1;
