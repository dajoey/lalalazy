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
Console.WriteLine("== Group 7: 1.0.4.291 - external control lifts the restriction ==");
Console.WriteLine("   (while another plugin holds a lease and the user allows it, hostile-only presses");
Console.WriteLine("    and the DPS hard-target write fire out of combat; the saved settings are untouched)");
bool UnderControl(bool settingEnabled, int leases, TimeSpan silence) =>
    OutOfCombatGate.OutOfCombatAttacksAllowed(
        inCombatOnly: false, preferNonCombat: false,
        externalControlActive: OutOfCombatGate.ExternalControlAllowsOutOfCombatAttacks(
            settingEnabled,
            OutOfCombatGate.ExternalControlFresh(
                leases, silence, OutOfCombatGate.ExternalControlStalenessWindow)));
Case("engaged: default settings + fresh control => allowed", true, UnderControl(true, 1, TimeSpan.Zero));
Fire("engaged: hostile-only attack fires out of combat (the Henchman pull)", true, false, false, true,
    userAllowsOutOfCombatAttacks: UnderControl(true, 1, TimeSpan.Zero));
Fire("engaged: hostile-only gap-close fires out of combat", true, false, false, true,
    userAllowsOutOfCombatAttacks: UnderControl(true, 1, TimeSpan.Zero));
Case("engaged: DPS hard-target write allowed out of combat", true,
    OutOfCombatGate.MayWriteTarget(false, UnderControl(true, 1, TimeSpan.Zero)));

Console.WriteLine();
Console.WriteLine("== Group 8: the lease must be fresh, and the setting gates everything ==");
Case("fresh lease (0s since a control call) is control", true,
    OutOfCombatGate.ExternalControlFresh(1, TimeSpan.Zero, OutOfCombatGate.ExternalControlStalenessWindow));
Case("stale lease (no control call beyond the window) is not control", false,
    OutOfCombatGate.ExternalControlFresh(1, OutOfCombatGate.ExternalControlStalenessWindow + TimeSpan.FromMinutes(1),
        OutOfCombatGate.ExternalControlStalenessWindow));
Case("no lease is not control", false,
    OutOfCombatGate.ExternalControlFresh(0, TimeSpan.Zero, OutOfCombatGate.ExternalControlStalenessWindow));
Case("stale control: the restriction returns at the timeout", false,
    UnderControl(true, 1, OutOfCombatGate.ExternalControlStalenessWindow + TimeSpan.FromMinutes(1)));
Case("setting OFF: control active, restriction holds", false, UnderControl(false, 1, TimeSpan.Zero));
Case("released: no lease, restriction returns immediately", false, UnderControl(true, 0, TimeSpan.Zero));
Case("no control: saved opt-in alone still allows (281 semantics)", true,
    OutOfCombatGate.OutOfCombatAttacksAllowed(inCombatOnly: false, preferNonCombat: true, externalControlActive: false));
Case("no control: saved restrictive settings still passenger", false,
    OutOfCombatGate.OutOfCombatAttacksAllowed(inCombatOnly: false, preferNonCombat: false, externalControlActive: false));

Console.WriteLine();
Console.WriteLine("== Group 9: 1.0.4.291 - the earlier protections hold under external control ==");
bool engaged = UnderControl(true, 1, TimeSpan.Zero);
Fire("under control: damage-preset heal dump STILL waits (Kerachole/Druochole)", false, true, false, false,
    userAllowsOutOfCombatAttacks: engaged);
Fire("under control: unsanctioned self-only action STILL waits (Physis II)", false, true, false, false,
    userAllowsOutOfCombatAttacks: engaged);
Fire("under control: heal-preset friendly action unchanged (Medica)", false, true, false, true,
    fromHealPreset: true, userAllowsOutOfCombatAttacks: engaged);
Fire("under control: sanctioned prepull self-buff unchanged (tank stance)", false, true, false, true,
    sanctionedSelfBuff: true, userAllowsOutOfCombatAttacks: engaged);
Case("under control: incoming-damage detection STILL combat-only", false, OutOfCombatGate.MayDetectIncomingDamage(false));
Case("under control: reflect-penalty self-select STILL combat-only", false, OutOfCombatGate.MayWriteTarget(false));

Console.WriteLine();
Console.WriteLine("== Group 10: external control never attacks a training dummy and never outlives its controller ==");
Console.WriteLine("   (AutoDuty left the duty for the housing district while its lease stayed registered: control lasted the whole");
Console.WriteLine("    10-minute staleness window, the Striking Dummy (NameId 541) was a valid enemy, and the first hit put the");
Console.WriteLine("    player in combat - after which every out-of-combat gate is moot and the rotation hits the dummy until it resets)");

// TerritoryIntendedUse values (ECommons TerritoryIntendedUseEnum).
const uint CityArea = 0, OpenWorld = 1, Inn = 2, Dungeon = 3, Trial = 10, ResidentialArea = 13, HousingInstances = 14, Eureka = 41;
const uint StrikingDummy = 541, SomeEnemy = 14605;

OutOfCombatGate.ExternalLease Lease(string name, double quietSeconds) => new(name, TimeSpan.FromSeconds(quietSeconds));
bool Active(uint territory, params OutOfCombatGate.ExternalLease[] leases) =>
    OutOfCombatGate.ExternalControlActive(true, territory, leases);

Case("Striking Dummy (NameId 541) is a training dummy", true, OutOfCombatGate.IsTrainingDummy(StrikingDummy));
Case("an ordinary enemy is not a training dummy", false, OutOfCombatGate.IsTrainingDummy(SomeEnemy));
Case("under external control a training dummy is never an attack target", false, OutOfCombatGate.ExternalControlMayTarget(true, StrikingDummy));
Case("under external control an ordinary enemy stays a target", true, OutOfCombatGate.ExternalControlMayTarget(true, SomeEnemy));
Case("without external control the dummy is unchanged (the player's own choice)", true, OutOfCombatGate.ExternalControlMayTarget(false, StrikingDummy));
Case("a registered lease counts as a controller even when control has lapsed", true, OutOfCombatGate.ExternalControllerPresent(false, 1));
Case("no lease and no control: no controller", false, OutOfCombatGate.ExternalControllerPresent(false, 0));
Case("lease registered, control lapsed: dummy is still not an attack target", false, OutOfCombatGate.ExternalControlMayTarget(OutOfCombatGate.ExternalControllerPresent(false, 1), StrikingDummy));

Case("housing district (13): external control does not apply", false, OutOfCombatGate.TerritoryAllowsExternalControl(ResidentialArea));
Case("housing interior (14): external control does not apply", false, OutOfCombatGate.TerritoryAllowsExternalControl(HousingInstances));
Case("city (0): external control does not apply", false, OutOfCombatGate.TerritoryAllowsExternalControl(CityArea));
Case("inn (2): external control does not apply", false, OutOfCombatGate.TerritoryAllowsExternalControl(Inn));
Case("open world (1): external control applies (FATE lessees)", true, OutOfCombatGate.TerritoryAllowsExternalControl(OpenWorld));
Case("dungeon (3): external control applies", true, OutOfCombatGate.TerritoryAllowsExternalControl(Dungeon));
Case("trial (10): external control applies", true, OutOfCombatGate.TerritoryAllowsExternalControl(Trial));
Case("Eureka (41): external control applies", true, OutOfCombatGate.TerritoryAllowsExternalControl(Eureka));

Case("AutoDuty re-asserts every 5 s: its quiet window is a minute, not ten",
    true, OutOfCombatGate.ExternalControlWindowFor("AutoDuty") <= TimeSpan.FromMinutes(1));
Case("another lessee keeps the 10-minute window (Lazy Fate Automation refreshes per FATE)",
    true, OutOfCombatGate.ExternalControlWindowFor("Lazy Fate Automation") == OutOfCombatGate.ExternalControlStalenessWindow);
Case("an unnamed lease keeps the 10-minute window",
    true, OutOfCombatGate.ExternalControlWindowFor(null) == OutOfCombatGate.ExternalControlStalenessWindow);

Case("AutoDuty in a duty, refreshed 5 s ago: control active", true, Active(Dungeon, Lease("AutoDuty", 5)));
Case("AutoDuty in the open world, quiet 8 s: control still active (walking to the next duty)", true, Active(OpenWorld, Lease("AutoDuty", 8)));
Case("AutoDuty lease registered but quiet 5 minutes: control is over", false, Active(OpenWorld, Lease("AutoDuty", 300)));
Case("AutoDuty lease still fresh but the player is in a housing district: control is over", false, Active(ResidentialArea, Lease("AutoDuty", 16)));
Case("AutoDuty lease still fresh but the player is in a city: control is over", false, Active(CityArea, Lease("AutoDuty", 5)));
Case("Lazy Fate Automation quiet 5 minutes in the open world: control stays", true, Active(OpenWorld, Lease("Lazy Fate Automation", 300)));
Case("quiet AutoDuty lease beside a fresh Henchman lease: control stays", true, Active(Dungeon, Lease("AutoDuty", 300), Lease("Henchman", 10)));
Case("no leases: no control", false, Active(Dungeon));
Case("setting off: no control however fresh the lease",
    false, OutOfCombatGate.ExternalControlActive(false, Dungeon, new[] { Lease("AutoDuty", 1) }));

Console.WriteLine();
Console.WriteLine("== Group 11: end-to-end replay of the incident through the same gate calls the controller makes ==");
// One tick = (territory, leases, enemy NameId in reach). The controller asks the gate the same four
// questions in this order: is control active here, may this enemy be targeted, may the rotation
// pull (hostile-only press), may it write the hard target. The leases carry InCombatOnly=false
// (AutoDuty sets it), PreferNonCombat is off, and the player is out of combat.
(bool active, bool dummyTargetable, bool mayPress, bool mayWriteTarget) Tick(uint territory, double autoDutyQuietSeconds)
{
    var active = Active(territory, Lease("AutoDuty", autoDutyQuietSeconds));
    var dummyTargetable = OutOfCombatGate.ExternalControlMayTarget(OutOfCombatGate.ExternalControllerPresent(active, 1), StrikingDummy);
    var allowed = OutOfCombatGate.OutOfCombatAttacksAllowed(inCombatOnly: false, preferNonCombat: false, externalControlActive: active);
    var mayPress = OutOfCombatGate.MayFire(canTargetHostile: true, canTargetSelf: false, targetArea: false, userAllowsOutOfCombatAttacks: allowed);
    var mayWrite = OutOfCombatGate.MayWriteTarget(inCombat: false, userAllowsOutOfCombatAttacks: allowed);
    return (active, dummyTargetable, mayPress, mayWrite);
}

var inDuty = Tick(Dungeon, 4);
Case("e2e 15:49 in the duty: control active, rotation may pull", true, inDuty.active && inDuty.mayPress && inDuty.mayWriteTarget);
var afterDuty = Tick(OpenWorld, 8);
Case("e2e 16:05:37 duty over, still walking: control active, dummy still never targetable", false, afterDuty.dummyTargetable);
var inHousing = Tick(ResidentialArea, 16);
Case("e2e 16:05:50 in the housing district: control released", false, inHousing.active);
Case("e2e 16:05:50 in the housing district, lease still registered: dummy is not targetable", false, inHousing.dummyTargetable);
Case("e2e 16:05:50 in the housing district: no out-of-combat pull", false, inHousing.mayPress);
Case("e2e 16:05:50 in the housing district: no hard-target write", false, inHousing.mayWriteTarget);
var quietLater = Tick(OpenWorld, 600);
Case("e2e AutoDuty never released and went quiet 10 minutes: no pull, no hard-target write", false, quietLater.mayPress || quietLater.mayWriteTarget);

Console.WriteLine();
Console.WriteLine("== Group 12: 1.0.4.304 - the hard-target arms take the dummy filter, and the control check runs off the lease table ==");
Console.WriteLine("   (the Manual and Tank_Target modes and the manual AoE branch resolve the target straight off the player's hard");
Console.WriteLine("    target, outside the enemy query that 1.0.4.303 filtered, so a dummy that was already the hard target was still");
Console.WriteLine("    hit while a lease was registered. The cases below execute the controller-side seam the per-tick path now routes");
Console.WriteLine("    through: the lease table's values in - no per-call list - and the control decision out.)");

// The fake lease table: plain (lessee name, last control call) values through the same seam
// the controller drives its real lease registrations through. The projection here mirrors the
// controller's: the lessee name and the last refresh come off the lease value itself.
Dictionary<Guid, FakeLease> FakeLeases(params (string Name, double QuietSeconds)[] leases)
{
    var table = new Dictionary<Guid, FakeLease>();
    foreach (var l in leases)
        table[Guid.NewGuid()] = new FakeLease(l.Name, DateTime.Now - TimeSpan.FromSeconds(l.QuietSeconds));
    return table;
}

OutOfCombatGate.ExternalControlDecision Decision(uint territory, params (string Name, double QuietSeconds)[] leases) =>
    OutOfCombatGate.ExternalControlDecisionFor(true, territory, FakeLeases(leases).Values, l => (l.Name, l.Refreshed));

bool ActiveViaLeaseTable(uint territory, params (string Name, double QuietSeconds)[] leases) =>
    OutOfCombatGate.ExternalControlActive(true, territory, FakeLeases(leases).Values, l => (l.Name, l.Refreshed));

bool ActiveViaSpecList(uint territory, params (string Name, double QuietSeconds)[] leases)
{
    var spec = new List<OutOfCombatGate.ExternalLease>();
    foreach (var l in leases)
        spec.Add(new OutOfCombatGate.ExternalLease(l.Name, TimeSpan.FromSeconds(l.QuietSeconds)));
    return OutOfCombatGate.ExternalControlActive(true, territory, spec);
}

// The lease-table answer equals the list-based specification over the same inputs.
Case("lease table == spec list: AutoDuty fresh 5 s in the duty", ActiveViaSpecList(Dungeon, ("AutoDuty", 5)), ActiveViaLeaseTable(Dungeon, ("AutoDuty", 5)));
Case("lease table == spec list: AutoDuty walking 8 s in the open world", ActiveViaSpecList(OpenWorld, ("AutoDuty", 8)), ActiveViaLeaseTable(OpenWorld, ("AutoDuty", 8)));
Case("lease table == spec list: AutoDuty quiet 5 minutes, control over", ActiveViaSpecList(OpenWorld, ("AutoDuty", 300)), ActiveViaLeaseTable(OpenWorld, ("AutoDuty", 300)));
Case("lease table == spec list: AutoDuty fresh but the housing district", ActiveViaSpecList(ResidentialArea, ("AutoDuty", 16)), ActiveViaLeaseTable(ResidentialArea, ("AutoDuty", 16)));
Case("lease table == spec list: quiet AutoDuty beside a fresh Henchman", ActiveViaSpecList(Dungeon, ("AutoDuty", 300), ("Henchman", 10)), ActiveViaLeaseTable(Dungeon, ("AutoDuty", 300), ("Henchman", 10)));
Case("lease table == spec list: a fresh Lazy Fate Automation lease", ActiveViaSpecList(OpenWorld, ("Lazy Fate Automation", 5)), ActiveViaLeaseTable(OpenWorld, ("Lazy Fate Automation", 5)));
Case("lease table == spec list: Lazy Fate Automation quiet 30 minutes", ActiveViaSpecList(OpenWorld, ("Lazy Fate Automation", 1800)), ActiveViaLeaseTable(OpenWorld, ("Lazy Fate Automation", 1800)));
Case("lease table == spec list: no lease at all", ActiveViaSpecList(Dungeon), ActiveViaLeaseTable(Dungeon));
Case("lease table == spec list: setting off, fresh lease",
    OutOfCombatGate.ExternalControlActive(false, Dungeon, new[] { new OutOfCombatGate.ExternalLease("AutoDuty", TimeSpan.FromSeconds(5)) }),
    OutOfCombatGate.ExternalControlActive(false, Dungeon, FakeLeases(("AutoDuty", 5)).Values, l => (l.Name, l.Refreshed)));

// The seam's control answer keeps the per-lessee windows: the lessee name drives them.
Case("seam: AutoDuty quiet 5 minutes: control is over (the lessee name drives the window)", false, Decision(OpenWorld, ("AutoDuty", 300)).Active);
Case("seam: the same quiet lease under the ten-minute window stays control (Lazy Fate Automation)", true, Decision(OpenWorld, ("Lazy Fate Automation", 300)).Active);
Case("seam: a registered lease counts as a controller through the seam", true, Decision(OpenWorld, ("AutoDuty", 300)).ControllerPresent);

// The setting check moved from the property's early return into the seam argument: with the
// setting off and a lease still registered, control is over yet the controller is present, so
// the dummy stays off the target list (the 1.0.4.303 held-lease guarantee).
var settingOff = OutOfCombatGate.ExternalControlDecisionFor(false, OpenWorld, FakeLeases(("AutoDuty", 5)).Values, l => (l.Name, l.Refreshed));
Case("seam, setting off, fresh AutoDuty lease: control is over", false, settingOff.Active);
Case("seam, setting off, fresh AutoDuty lease: the controller is still present", true, settingOff.ControllerPresent);
Case("seam, setting off, fresh AutoDuty lease: the dummy is still not an attack target", false, settingOff.MayAttackCurrentTarget(StrikingDummy));

// Item 1: the hard-target arms consult the dummy decision through the seam.
Case("hard-targeted dummy, fresh AutoDuty lease: the hard-target arm does not attack it", false, Decision(OpenWorld, ("AutoDuty", 5)).MayAttackCurrentTarget(StrikingDummy));
Case("hard-targeted ordinary enemy, fresh AutoDuty lease: the hard-target arm still attacks it", true, Decision(OpenWorld, ("AutoDuty", 5)).MayAttackCurrentTarget(SomeEnemy));
Case("hard-targeted dummy, AutoDuty quiet 5 minutes, lease still registered: still not attacked", false, Decision(OpenWorld, ("AutoDuty", 300)).MayAttackCurrentTarget(StrikingDummy));
Case("no lease: the hard-target arm keeps the player's own dummy choice", true, Decision(OpenWorld).MayAttackCurrentTarget(StrikingDummy));
Case("no lease: the enemy query keeps the dummy too", true, Decision(OpenWorld).MayTarget(StrikingDummy));
Case("the query's filter and the hard-target arm agree under a registered lease", true, Decision(OpenWorld, ("AutoDuty", 300)).MayTarget(StrikingDummy) == Decision(OpenWorld, ("AutoDuty", 300)).MayAttackCurrentTarget(StrikingDummy));

// End to end: housing district, a registered lease, the dummy as the hard target - no attack, no target write.
var housingHardDummy = Decision(ResidentialArea, ("AutoDuty", 5));
var housingAllowed = OutOfCombatGate.OutOfCombatAttacksAllowed(inCombatOnly: false, preferNonCombat: false, externalControlActive: housingHardDummy.Active);
var housingPressAtDummy = housingHardDummy.MayAttackCurrentTarget(StrikingDummy)
    && OutOfCombatGate.MayFire(canTargetHostile: true, canTargetSelf: false, targetArea: false, userAllowsOutOfCombatAttacks: housingAllowed);
Case("e2e housing, lease registered, dummy hard-targeted: no attack at the dummy", false, housingPressAtDummy);
Case("e2e housing, lease registered, dummy hard-targeted: no hard-target write", false, OutOfCombatGate.MayWriteTarget(inCombat: false, userAllowsOutOfCombatAttacks: housingAllowed));
var dutyHardDummy = Decision(Dungeon, ("AutoDuty", 5));
var dutyAllowed = OutOfCombatGate.OutOfCombatAttacksAllowed(inCombatOnly: false, preferNonCombat: false, externalControlActive: dutyHardDummy.Active);
var dutyPressAtDummy = dutyHardDummy.MayAttackCurrentTarget(StrikingDummy)
    && OutOfCombatGate.MayFire(canTargetHostile: true, canTargetSelf: false, targetArea: false, userAllowsOutOfCombatAttacks: dutyAllowed);
Case("e2e duty, control active, dummy hard-targeted: no attack even with the out-of-combat doors open", false, dutyPressAtDummy);
Case("e2e duty, control active, ordinary enemy hard-targeted: the rotation still attacks it", true, Decision(Dungeon, ("AutoDuty", 5)).MayAttackCurrentTarget(SomeEnemy));

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine($"OK ({total}/{total} cases pass)");
    return 0;
}

Console.WriteLine($"FAIL ({failures}/{total} cases failed)");
return 1;
// The fake lease row the seam cases feed in.
readonly record struct FakeLease(string Name, DateTime Refreshed);
