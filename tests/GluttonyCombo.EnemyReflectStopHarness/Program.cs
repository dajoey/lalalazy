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
// Group 7 models the controller's two-tick skip loop (detector input vs picker view) - see the
// Group 7 block below for the wiring it pins.
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

// The FATE-grind reflect skip (tasks-20261020-lazyfate-smn-rotation-stuck-01): while an
// external controller (Lazy Fate Automation) drives the rotation with FATE priority inside
// an overworld FATE, spikes on PART of the pack must not freeze the whole rotation - the
// picker takes the rest of the pack and the full stop never arms. 2026-10-20 18:35-18:38
// ET: seven 20-26 s frozen=1 windows back to back in one FATE, ~85% of 163 s with zero
// presses, while attackable non-carrier trash stood in the same pack.
var fate = new EnemyReflectStop.FateGrindSkipContext(
    ExternallyDriven: true, FatePriorityTargeting: true, PlayerInFate: true, ForayTerritory: false);

void FateCase(string name, int carrierCount, int candidateCount, EnemyReflectStop.FateGrindSkipContext context, bool expected)
{
    total++;
    var actual = EnemyReflectStop.FateGrindSkipsCarriers(carrierCount, candidateCount, context);
    var ok = actual == expected;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-76} => {actual}, expected {expected}");
}

Console.WriteLine();
Console.WriteLine("== Group 5: the FATE-grind reflect skip - part of the pack spikes, the grind keeps pressing ==");
FateCase("one spiked mob of five in a driven FATE skips instead of freezing", 1, 5, fate, true);
FateCase("four spiked mobs of five in a driven FATE still skip", 4, 5, fate, true);

Console.WriteLine();
Console.WriteLine("== Group 6: the skip never weakens the full stop ==");
FateCase("the whole pack spiked still takes the full stop", 5, 5, fate, false);
FateCase("no spiked mob arms no skip", 0, 5, fate, false);
FateCase("manual play in a FATE keeps the full stop", 1, 5, fate with { ExternallyDriven = false }, false);
FateCase("driven play without FATE priority keeps the full stop", 1, 5, fate with { FatePriorityTargeting = false }, false);
FateCase("driven play outside a FATE keeps the full stop", 1, 5, fate with { PlayerInFate = false }, false);
FateCase("a foray territory (Eureka) keeps the full stop", 1, 5, fate with { ForayTerritory = true }, false);

// Group 7: the wiring loop the controller runs around the core (review finding on 1.0.4.314).
// The skip verdict must be computed from the RAW priority pack on every tick; the picker's
// filtered view (this tick's armed carriers removed) sits strictly DOWNSTREAM of the decision.
// 1.0.4.314's detector enumerated the picker's filtered view instead, so an armed skip removed
// the carriers from the detector's own input: tick N armed, tick N+1 counted 0 of 4 and
// disarmed, and the same tick's picker - unfiltered again - saw the spiked carrier: press/
// stop oscillation with the carrier pickable on every other tick. AutoRotationController's
// EnemyHasReflectPenalty() is the detector and DPSTargeting.BaseSelection is the picker view;
// which of the two feeds the detector is the wiring under test here.
//
// TwoTickLoop walks two ticks over a fixed world (spiked flags, pack of `total`) and returns
// the two verdicts plus how many spiked mobs the tick-2 picker could actually pick. The core
// function called is the one that ships.
static (bool armed1, bool armed2, int pickerCarriers2) TwoTickLoop(
    int spiked, int total, bool detectorSeesPickerView, EnemyReflectStop.FateGrindSkipContext ctx)
{
    var world = Enumerable.Range(0, total).Select(i => i < spiked).ToList();
    var armed1 = EnemyReflectStop.FateGrindSkipsCarriers(world.Count(s => s), world.Count, ctx);
    // the picker's view once tick 1's verdict applied: while armed the carriers are not pickable
    var pickerAfterTick1 = armed1 ? world.Where(s => !s).ToList() : world;
    // the detector's tick-2 input: the raw pack (fixed wiring) or the picker's view (1.0.4.314)
    var detectorView2 = detectorSeesPickerView ? pickerAfterTick1 : world;
    var armed2 = EnemyReflectStop.FateGrindSkipsCarriers(detectorView2.Count(s => s), detectorView2.Count, ctx);
    var picker2 = armed2 ? world.Where(s => !s).ToList() : world;
    return (armed1, armed2, picker2.Count(s => s));
}

void LoopCase(string name, int spiked, int packSize, bool detectorSeesPickerView,
    (bool armed1, bool armed2, int pickerCarriers2) expected)
{
    total++;
    var actual = TwoTickLoop(spiked, packSize, detectorSeesPickerView, fate);
    var ok = actual == expected;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-76} => {actual}, expected {expected}");
}

Console.WriteLine();
Console.WriteLine("== Group 7: the detector counts the raw pack, never the picker's filtered view ==");
// The contract the wiring must satisfy: armed stays armed while the carrier lives and the
// tick-2 picker never sees a carrier. This is the case that FAILED against the 1.0.4.314
// wiring (detector fed the filtered view: armed2 false, the carrier back in the picker).
LoopCase("fixed wiring: an armed skip stays armed and the picker never sees the carrier", 1, 5,
    detectorSeesPickerView: false, expected: (armed1: true, armed2: true, pickerCarriers2: 0));
// The reproduced defect, kept as documentation: feeding the detector the picker's filtered
// view oscillates - the core cannot save a caller that filters its own input.
LoopCase("1.0.4.314 wiring reproduced: the filtered input disarms tick 2 and the carrier returns", 1, 5,
    detectorSeesPickerView: true, expected: (armed1: true, armed2: false, pickerCarriers2: 1));
// Decay while armed, raw wiring: every non-carrier dies -> the raw count is 1 of 1 -> no skip.
// False here is the FULL STOP (the world still carries a spike; the controller's stop covers
// the carrier: self-target, no presses), never an unarmed picker staring at the carrier.
LoopCase("decay to a spiked-only pack takes the full stop, not an unarmed picker", 1, 1,
    detectorSeesPickerView: false, expected: (armed1: false, armed2: false, pickerCarriers2: 1));
// Carrier dies while armed, raw wiring: the raw count is 0 of 4 -> clean disarm, the rest of
// the pack resumes with nothing skipped.
LoopCase("a dead carrier disarms cleanly and the rest of the pack resumes", 0, 4,
    detectorSeesPickerView: false, expected: (armed1: false, armed2: false, pickerCarriers2: 0));

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine($"OK ({total}/{total} cases pass)");
    return 0;
}

Console.WriteLine($"FAIL ({failures}/{total} cases failed)");
return 1;
