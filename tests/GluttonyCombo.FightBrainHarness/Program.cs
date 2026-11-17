// Fight Brain failing-first cases: the pure
// forecast cores that ship, compiled from the real FightBrainHit.cs.
//
// Page rules under test:
//   - duplicate entries for the same hit within 3 s merge, and the most specific source
//     sets the time (a cast hint always re-times a timeline guess);
//   - anything 3 s overdue is dropped;
//   - the cast gate blocks (in shadow) only when the cast would still run, plus 0.5 s,
//     at the moment the player must move out of the zone under the player;
//   - the strip shows only entries still ahead of now, nearest first, capped to 6;
//   - kept entries age out on the same Unix clock the entries carry, so a fight's
//     leftovers vanish instead of counting up in the negative.

using GluttonyCombo.AutoRotation;

var failures = 0;

void Check(string name, bool expected, bool actual)
{
    var pass = expected == actual;
    if (!pass) failures++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {name}: expected {expected}, got {actual}");
}

void CheckEqual<T>(string name, T expected, T actual)
{
    var pass = EqualityComparer<T>.Default.Equals(expected, actual);
    if (!pass) failures++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {name}: expected {expected}, got {actual}");
}

long Now = 1_000_000;
FightBrainHit Rw(float secondsIn, string source = "timeline", int spec = 2) =>
    new(FightBrainHitKind.Raidwide, Now + (long)(secondsIn * 1000), source, spec);
FightBrainHit Cast(float secondsIn, string name) =>
    new(FightBrainHitKind.CastHit, Now + (long)(secondsIn * 1000), name, 0, 12345);

// ---- merge: same kind within 3 s is one hit ------------------------------------------
var merged = FightBrainForecastCore.Merge([], [Rw(6.0f), Rw(7.5f)], Now);
CheckEqual("two raidwides 1.5 s apart merge to one", 1, merged.Count);

// ---- the more specific source wins the merge (a cast hint re-times the guess) --------
var sources = FightBrainForecastCore.Merge([], [Rw(6.0f), Rw(7.5f, "hints", 1)], Now);
CheckEqual("hints (spec 1) beats timeline (spec 2)", 1, sources.Count);
CheckEqual("the merged entry keeps the specific source's time",
    Now + 7500, sources[0].AtMs);
var cast = FightBrainForecastCore.Merge([], [Rw(6.0f), Cast(6.5f, "Medica")], Now);
CheckEqual("a cast hint is its own entry (it is not yet classified raidwide/tankbuster)", 2, cast.Count);
CheckEqual("the cast entry keeps its exact landing time", Now + 6500, cast[1].AtMs);

// ---- different kinds never merge ------------------------------------------------------
var kinds = FightBrainForecastCore.Merge([], [Rw(6.0f), new(FightBrainHitKind.Tankbuster, Now + 6500, "timeline", 2)], Now);
CheckEqual("a raidwide and a tankbuster stay separate", 2, kinds.Count);

// ---- overdue entries drop; old kept entries age out the same way ----------------------
var overdue = FightBrainForecastCore.Merge([], [Rw(-4.0f), Rw(5.0f)], Now);
CheckEqual("an entry 4 s overdue is dropped", 1, overdue.Count);
CheckEqual("an entry exactly 2 s overdue is kept (the 3 s rule)", 1,
    FightBrainForecastCore.Merge([], [Rw(-2.0f)], Now).Count);

// ---- the list is capped to the nearest hits -------------------------------------------
// Entries 4 s apart never merge (outside the 3 s window), so 20 of them reach the cap.
var many = Enumerable.Range(1, 20).Select(i => Rw(3f + 4 * i)).ToList();
CheckEqual("more than 12 entries are capped to 12", 12, FightBrainForecastCore.Merge([], many, Now).Count);

// ---- previously kept entries are re-merged without duplicates -------------------------
var kept = FightBrainForecastCore.Merge([], [Rw(6.0f)], Now);
var again = FightBrainForecastCore.Merge(kept, [Rw(6.5f, "hints", 1)], Now);
CheckEqual("re-reading the same hit does not duplicate it", 1, again.Count);
CheckEqual("and the fresh specific source re-times it", Now + 6500, again[0].AtMs);

// ---- the cast gate: cast end + 0.5 s must fit before (zone - walk-out) ----------------
// A 4 s hard cast started now ends at 4.5 (gate buffer included). A zone activating at
// 3 s with a 1 s walk-out must be moved for at 2 s: the cast would still run -> block.
Check("gate: a cast running past the must-move moment would block",
    true, FightBrainForecastCore.WouldBlockCast(castEndIn: 4.5, zoneIn: 3.0, standingInZone: true, walkOut: 1.0));
// The same cast with the zone 10 s out leaves plenty of time.
Check("gate: a far zone does not block",
    false, FightBrainForecastCore.WouldBlockCast(castEndIn: 4.5, zoneIn: 10.0, standingInZone: true, walkOut: 1.0));
// Not standing in the zone (the moment comes from the zone UNDER the player,
// not the earliest zone anywhere) -> never block.
Check("gate: a zone elsewhere never blocks",
    false, FightBrainForecastCore.WouldBlockCast(castEndIn: 4.5, zoneIn: 3.0, standingInZone: false, walkOut: 1.0));
// Exactly on the line (cast ends at must-move) is allowed: the comparison is strict.
Check("gate: a cast ending exactly at the must-move moment goes through",
    false, FightBrainForecastCore.WouldBlockCast(castEndIn: 2.0, zoneIn: 3.0, standingInZone: true, walkOut: 1.0));
// A zone already active (zoneIn <= 0) is not a forecast: never block on it.
Check("gate: an already-active zone is not a forecast",
    false, FightBrainForecastCore.WouldBlockCast(castEndIn: 4.5, zoneIn: 0.0, standingInZone: true, walkOut: 1.0));

// ---- the strip shows upcoming entries only ------------------------------------------
// The strip's view is a separate, stricter rule than the merge drop: an entry already
// past its moment (beyond a 1 s grace) is not a forecast and must not render.
var strip = FightBrainForecastCore.Strip([Rw(-5f), Rw(12f)], Now);
CheckEqual("a strip list with entries at -5 s and +12 s shows only the +12 s entry", 1, strip.Count);
CheckEqual("the kept entry is the +12 s one", Now + 12000, strip[0].Hit.AtMs);
CheckEqual("its countdown reads 12 s", 12.0, strip[0].Seconds);
// Control: future entries still list, nearest first.
var future = FightBrainForecastCore.Strip([Rw(20f), Rw(8f), Rw(14f)], Now);
CheckEqual("future entries still list on the strip", 3, future.Count);
CheckEqual("nearest first", Now + 8000, future[0].Hit.AtMs);
CheckEqual("then the middle one", Now + 14000, future[1].Hit.AtMs);
CheckEqual("then the farthest one", Now + 20000, future[2].Hit.AtMs);
// The 1 s grace: half a second past still shows, 2 s past does not.
CheckEqual("an entry half a second past its moment still shows (the 1 s strip grace)",
    1, FightBrainForecastCore.Strip([Rw(-0.5f)], Now).Count);
CheckEqual("an entry 2 s past its moment does not show", 0,
    FightBrainForecastCore.Strip([Rw(-2f)], Now).Count);
// The strip is capped to 6 rows.
var manyStrip = Enumerable.Range(1, 8).Select(i => Rw(3f + 4 * i)).ToList();
CheckEqual("the strip is capped to 6 entries", 6, FightBrainForecastCore.Strip(manyStrip, Now).Count);

// ---- kept entries age out on the SAME clock the entries carry ------------------------
// The tick prunes the kept list with Unix-ms now. A forecast read before a fight ended
// must vanish once 3 s past its time, not sit on the strip with a negative countdown.
var keptAtEnd = FightBrainForecastCore.Merge([], [Rw(6f)], Now);
var afterFight = FightBrainForecastCore.Merge(keptAtEnd, [], Now + 10_000);
CheckEqual("after the fight ends the kept forecast list is empty", 0, afterFight.Count);
CheckEqual("until then the kept entry stays",
    1, FightBrainForecastCore.Merge(keptAtEnd, [], Now + 8_000).Count);

if (failures > 0)
{
    Console.WriteLine($"{failures} case(s) FAILED");
    return 1;
}

Console.WriteLine("OK");
return 0;
