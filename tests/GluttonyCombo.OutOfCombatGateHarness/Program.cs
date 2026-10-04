// Offline proof for the shared out-of-combat gate (task
// tasks-20261004-gluttony-out-of-combat-self-target-overcap-01).
//
// Compiles the REAL src/GluttonyCombo/GluttonyCombo/AutoRotation/OutOfCombatGate.cs
// - the exact file that ships - with no Dalamud and no ECommons. The 2026-10-04
// report: standing in the field out of combat, the auto-rotation hard-targeted a
// nearby FATE mob, ran the pre-pull chain and dashed the capped Shield Charge in,
// and the reflect-penalty guard self-selected the player every tick - the whole
// pull ran with no player input. The 2026-10-04 amendment (15:58-15:59Z): the
// Sage case - out of combat the damage rotation pressed Kerachole/Druochole
// (Addersgall overcap protection), Eukrasia chains, Physis and the raidwide
// shield chain (per-cast counts in the 48h logs: the player's own raidwide-shield
// casts all sat inside combat windows; the observed ooc presses were Druochole
// x2, Eukrasia x4, Eukrasian Dosis III x2) at
// full-health parties - "healers reacting when there is nothing to heal".
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

void Fire(string name, bool canTargetHostile, bool canTargetSelf, bool targetArea, bool expected, bool fromHealPreset = false, bool sanctionedSelfBuff = false, bool userAllowsOutOfCombatAttacks = false) =>
    Case(name, expected, OutOfCombatGate.MayFire(canTargetHostile, canTargetSelf, targetArea, fromHealPreset, sanctionedSelfBuff, userAllowsOutOfCombatAttacks));

Console.WriteLine("== Group 1: hostile-only actions are combat-only - no press out of combat ==");
Fire("hostile-only action may not fire (Smash Axe, Shieldsplitter)", true, false, false, false);
Fire("hostile-only gap-close may not fire (Shield Charge overcap dash)", true, false, false, false);

Console.WriteLine();
Console.WriteLine("== Group 2: deliberately out-of-combat-legal actions keep firing ==");
Fire("self+hostile capable may fire (prepull buffs, BST horns, mudras)", true, true, false, true);
Fire("heal-preset friendly action may fire (cures, raises, Esuna from a heal preset)", false, true, false, true, fromHealPreset: true);
Fire("ground-targeted may fire (ground actions are not single-enemy)", false, false, true, true);
Fire("hostile ground may fire (Doton-class, aimed at ground not an enemy)", true, false, true, true);

Console.WriteLine();
Console.WriteLine("== Group 3: the hard-target write is combat-only ==");
Case("MayWriteTarget(out of combat) is false", false, OutOfCombatGate.MayWriteTarget(false));
Case("MayWriteTarget(in combat) is true", true, OutOfCombatGate.MayWriteTarget(true));

Console.WriteLine();
Console.WriteLine("== Group 4: 1.0.4.278 - out of combat, a damage preset does not react ==");
Console.WriteLine("   (friendly-only actions and dumps from a DAMAGE preset wait for combat; heal presets keep their need-gated out-of-combat behavior)");
Fire("damage-preset heal dump waits for combat (Kerachole 30s, Druochole)", false, true, false, false);
Fire("damage-preset regen waits for combat (Physis II 60s)", false, true, false, false);
Fire("damage-preset gauge dump waits for combat (Rhizomata 90s)", false, true, false, false);
Fire("damage-preset shield waits for combat (Eukrasian Diagnosis)", false, true, false, false);
Fire("damage-preset Swiftcast waits for combat (60s, unsanctioned)", false, true, false, false);
Fire("unsanctioned self-only action waits for combat (BypassBuffs off)", false, true, false, false);
Fire("heal-preset friendly action keeps firing (Medica at a hurt member)", false, true, false, true, fromHealPreset: true);
Fire("sanctioned prepull self-buff keeps firing (tank stance 2s, BypassBuffs)", false, true, false, true, sanctionedSelfBuff: true);
Fire("sanctioned GCD prepull buff keeps firing (Eukrasia 2.5s, BypassBuffs)", false, true, false, true, sanctionedSelfBuff: true);
Fire("prepull mudra with chosen target keeps firing (self+hostile)", true, true, false, true);
Console.WriteLine("   (all-zero bits: the controller never consults the gate for unknown sheet rows -");
Console.WriteLine("    the ActionSheet.TryGetValue guard in AutoRotationController pins that exemption;) ");
Console.WriteLine("    a pure call with zero bits is treated as an unsanctioned friendly-only press");
Fire("no target bits at all, unsanctioned and non-heal, waits for combat", false, false, false, false);

Console.WriteLine();
Console.WriteLine("== Group 5: 1.0.4.278 - incoming-damage detection is combat-only ==");
Console.WriteLine("   (field FATE mobs casting wide spells are not raidwides: no shield chain out of combat)");
Case("MayDetectIncomingDamage(in combat) is true", true, OutOfCombatGate.MayDetectIncomingDamage(true));
Case("MayDetectIncomingDamage(out of combat) is false", false, OutOfCombatGate.MayDetectIncomingDamage(false));

Console.WriteLine();
Console.WriteLine("== Group 6: the gate defers to the user's own out-of-combat settings ==");
Console.WriteLine("   (\"Prioritise Targets Not in Combat\" on + \"Restrict to Combat Only\" off = attack out of combat;");
Console.WriteLine("    defaults and the restricted setting keep the passenger behavior; friendly dumps stay combat-only)");
Case("settings: prioritise-not-in-combat ON + restrict-to-combat OFF => allowed", true, OutOfCombatGate.UserAllowsOutOfCombatAttacks(inCombatOnly: false, preferNonCombat: true));
Case("settings: defaults (prioritise OFF, restrict OFF) => passenger", false, OutOfCombatGate.UserAllowsOutOfCombatAttacks(inCombatOnly: false, preferNonCombat: false));
Case("settings: prioritise ON but restrict-to-combat ON => passenger", false, OutOfCombatGate.UserAllowsOutOfCombatAttacks(inCombatOnly: true, preferNonCombat: true));
Case("settings: restrict-to-combat ON, prioritise OFF => passenger", false, OutOfCombatGate.UserAllowsOutOfCombatAttacks(inCombatOnly: true, preferNonCombat: false));
Fire("opted in: hostile-only attack fires out of combat (Smash Axe)", true, false, false, true, userAllowsOutOfCombatAttacks: true);
Fire("opted in: hostile-only gap-close fires out of combat (Shield Charge)", true, false, false, true, userAllowsOutOfCombatAttacks: true);
Fire("not opted in: hostile-only attack still waits for combat", true, false, false, false, userAllowsOutOfCombatAttacks: false);
Fire("opted in: damage-preset heal dump STILL waits (Kerachole/Druochole overcap)", false, true, false, false, userAllowsOutOfCombatAttacks: true);
Fire("opted in: unsanctioned self-only action STILL waits (Physis II, Rhizomata)", false, true, false, false, userAllowsOutOfCombatAttacks: true);
Fire("opted in: heal-preset friendly action unchanged (Medica at a hurt member)", false, true, false, true, fromHealPreset: true, userAllowsOutOfCombatAttacks: true);
Case("opted in: DPS hard-target write allowed out of combat", true, OutOfCombatGate.MayWriteTarget(false, userAllowsOutOfCombatAttacks: true));
Case("not opted in: hard-target write still combat-only", false, OutOfCombatGate.MayWriteTarget(false, userAllowsOutOfCombatAttacks: false));
Case("opted in: incoming-damage detection STILL combat-only (no raidwide shields)", false, OutOfCombatGate.MayDetectIncomingDamage(false));

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine($"OK ({total}/{total} cases pass)");
    return 0;
}

Console.WriteLine($"FAIL ({failures}/{total} cases failed)");
return 1;
