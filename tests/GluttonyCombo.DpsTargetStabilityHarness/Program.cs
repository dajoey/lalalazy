// Failing-first cases for the DPS target thrash (GluttonyCombo 1.0.4.283, task
// tasks-20261005-gluttony-dps-target-thrash-root-cause-01).
//
// Joey, 2026-10-05T22:24:24Z, verbatim: "I do not like how sometimes it will cycle wildly through DPS
// targets.  It should not pick more than one DPS target per second, minimum.  But rather than just
// implement that as a hard rule and move on, I'd like to have the reason it's wildly switchign to be
// diagnosed and fixed at the root."
//
// Measured root cause (ffxivdb plugin_log_lines, GluttonyCombo CR| and RF| lines, 2026-09-28..10-05):
// the plugin-wide current target (OverrideTarget, what every combo and the CR| telemetry read) is set to
// the mode's pick each tick and then OVERWRITTEN by the out-of-range fallback (1.0.4.258) with the enemy
// that one press was aimed at, and never put back. The fallback verdict flaps per action (melee GCD
// versus ranged oGCD, reach jitter), so the current target alternated pick <-> fallback <-> pick. 437 of
// the 530 A->B->A flips seen in CR| had an RF| line naming exactly that pair; the rate on one board went
// from 0.5 to 5.8 flips per 1000 samples the day the fallback shipped.
//
// Semantics under test:
//   1. the fallback redirects one press and never becomes the current target (the root fix);
//   2. the DPS pick floor (backstop): a new pick at most once per second, except the first pick, a
//      pick whose held target is gone (dead / untargetable), and a mechanic target (interrupt, dispel
//      carrier, boss-mod target), which switch at once.

using GluttonyCombo.AutoRotation;

var failures = 0;

void Check(string name, bool ok, string detail = "")
{
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok || detail.Length == 0 ? "" : "  [" + detail + "]")}");
}

// The most changes of a value inside any window of windowMs (changes are timestamps in ms).
int MaxInWindow(List<long> changes, long windowMs)
{
    var best = 0;
    for (var i = 0; i < changes.Count; i++)
    {
        var n = 0;
        for (var j = i; j < changes.Count && changes[j] - changes[i] < windowMs; j++) n++;
        best = Math.Max(best, n);
    }
    return best;
}

// ---- 1. the fallback replay: real RF| burst, 2026-10-04 22:39:32.540 .. 22:39:37.274 (ms from the first line).
// The mode's pick stays "lightning sprite" (14632, 22 y away) the whole time; each line is one press the
// out-of-range fallback aimed at "flauros piece" (14631) or found nothing to aim at (null).
var picked = "14632 lightning sprite (chosen, out of reach)";
var piece = "14631 flauros piece (fallback)";
(long At, string? Near)[] replay =
[
    (0, piece), (191, piece), (382, null), (1082, piece), (1215, piece), (1532, piece), (3583, piece), (4734, null),
];
const long TickMs = 50;
const long QueuedHoldMs = 300; // after a press the queued action makes the next ticks return early, current target untouched

var presses = replay.ToDictionary(r => (r.At + TickMs - 1) / TickMs * TickMs, r => r.Near);

List<long> ReplayCurrentTargetChanges(Func<string?, string?, string?> afterTick)
{
    var changes = new List<long>();
    string? current = picked;
    long holdUntil = -1;
    for (var t = 0L; t <= 6000; t += TickMs)
    {
        var prev = current;
        if (t >= holdUntil)
        {
            current = picked;                                   // ExecuteST: OverrideTarget = the mode's pick
            if (presses.TryGetValue(t, out var near) && near is not null)
            {
                current = afterTick(picked, near);              // the fallback aimed this press at the near enemy
                holdUntil = t + QueuedHoldMs;
            }
        }
        if (current != prev) changes.Add(t);
    }
    return changes;
}

var changes = ReplayCurrentTargetChanges(DpsTargetExposure.AfterTick);
Check("fallback replay: the current target never leaves the chosen pick (a fallback press is not a new pick)",
    changes.Count == 0, $"{changes.Count} changes: {string.Join(",", changes)}");
Check("fallback replay: no second where the current target changes more than once",
    MaxInWindow(changes, 1000) <= 1, $"max {MaxInWindow(changes, 1000)} in 1 s");

// ---- 2. the floor alone is not what stopped it: with the floor switched OFF the root fix still holds.
{
    var off = new DpsTargetStability(0, 0);
    ulong cur = 0;
    var flips = 0;
    for (long t = 0; t < 1000; t += 100)
    {
        var r = off.Resolve((t / 100) % 2 == 0 ? 11ul : 22ul, t, _ => true);
        if (r.Id != cur) { cur = r.Id; flips++; }
    }
    Check("floor off (interval 0): picks follow every tick, and the replay is still calm: the root fix is what stops the thrash",
        flips == 10 && changes.Count == 0, $"flips={flips} replay changes={changes.Count}");
}

// ---- 3. the floor (backstop): picks flapping every 100 ms for 10 s
{
    var s = new DpsTargetStability();
    var changed = new List<long>();
    ulong cur = 0;
    for (long t = 0; t < 10_000; t += 100)
    {
        ulong fresh = (t / 100) % 2 == 0 ? 11ul : 22ul;
        var r = s.Resolve(fresh, t, _ => true);
        if (r.Id != cur) { cur = r.Id; changed.Add(t); }
    }
    Check("flapping picks every 100 ms for 10 s: at most one change per second",
        MaxInWindow(changed, 1000) <= 1 && changed.Count <= 11,
        $"{changed.Count} changes, max {MaxInWindow(changed, 1000)} in 1 s");
}

// ---- 3b. the one-second floor on its own (stickiness off): the 1.0.4.283 guarantee is kept as a separate backstop
{
    var s = new DpsTargetStability(DpsTargetStability.DefaultMinPickIntervalMs, 0);
    var changed = new List<long>();
    ulong cur = 0;
    for (long t = 0; t < 10_000; t += 100)
    {
        var r = s.Resolve((t / 100) % 2 == 0 ? 11ul : 22ul, t, _ => true);
        if (r.Id != cur) { cur = r.Id; changed.Add(t); }
    }
    Check("floor alone, flapping picks every 100 ms for 10 s: at most one change per second",
        MaxInWindow(changed, 1000) <= 1 && changed.Count is > 1 and <= 11, $"{changed.Count} changes, max {MaxInWindow(changed, 1000)} in 1 s");
}

// ---- 4. the floor must not delay or break the legitimate cases
{
    var s = new DpsTargetStability();
    var first = s.Resolve(11, 5_000, _ => true);
    Check("the first pick is never delayed", first.Id == 11 && first.Changed && first.Why == DpsTargetStability.Why.First);

    var same = s.Resolve(11, 5_100, _ => true);
    Check("the same pick again is no change", same.Id == 11 && !same.Changed);

    var kept = s.Resolve(22, 5_500, _ => true);
    Check("a different pick inside the interval keeps the held one", kept.Id == 11 && !kept.Changed && kept.Why == DpsTargetStability.Why.Held && kept.Suppressed == 1,
        $"id={kept.Id} why={kept.Why} sup={kept.Suppressed}");

    // 11 was last the pick at 5_100; 1.0.4.301 keeps it until it has gone 1_500 ms without being preferred
    var inDwell = s.Resolve(22, 6_000, _ => true);
    Check("the interval has passed but the held enemy was the pick 900 ms ago: it stays", inDwell.Id == 11 && !inDwell.Changed && inDwell.Why == DpsTargetStability.Why.Held,
        $"id={inDwell.Id} why={inDwell.Why}");

    var later = s.Resolve(22, 6_600, _ => true);
    Check("once the held enemy has not been the pick for 1.5 s the preferred pick is taken", later.Id == 22 && later.Changed && later.Why == DpsTargetStability.Why.Repick,
        $"id={later.Id} why={later.Why}");
}
{
    var s = new DpsTargetStability();
    s.Resolve(11, 0, _ => true);
    var gone = s.Resolve(22, 100, id => id != 11);
    Check("the held target died / became untargetable: the new pick is taken at once", gone.Id == 22 && gone.Changed && gone.Why == DpsTargetStability.Why.HeldInvalid,
        $"id={gone.Id} why={gone.Why}");
}
{
    var s = new DpsTargetStability();
    s.Resolve(11, 0, _ => true);
    var forced = s.Resolve(22, 100, _ => true, forced: true);
    Check("a mechanic target (interrupt / dispel carrier / boss-mod target) is taken at once", forced.Id == 22 && forced.Changed && forced.Why == DpsTargetStability.Why.Forced,
        $"id={forced.Id} why={forced.Why}");
}
{
    var s = new DpsTargetStability();
    s.Resolve(11, 0, _ => true);
    var none = s.Resolve(0, 100, _ => true);
    var back = s.Resolve(22, 200, _ => true);
    Check("a tick with no pick returns no target and keeps the hold", none.Id == 0 && !none.Changed && back.Id == 11 && back.Why == DpsTargetStability.Why.Held,
        $"none={none.Id} back={back.Id} why={back.Why}");
}
{
    // the held pick stops being valid for good, then a pick appears: no wait
    var s = new DpsTargetStability();
    s.Resolve(11, 0, _ => true);
    s.Resolve(0, 100, _ => true);
    var after = s.Resolve(22, 150, id => id != 11);
    Check("the held target is gone while nothing else was picked: the next pick is not delayed", after.Id == 22 && after.Changed,
        $"id={after.Id} why={after.Why}");
}

// ---- 5. the real fast-switching burst (task tasks-20261011-gluttony-fast-target-switching-again-01).
// Joey, 2026-10-11 12:31 ET (build 1.0.4.299, AST, Nearest mode, FATE grind driven by Lazy Fate Automation): fast
// target switching is still happening. ffxivdb TS| rows 12:33:01.033 .. 12:33:13.696 ET: 13 pick changes, one
// about every 1.03 s, each after 12..18 different picks had been turned down. The floor works as written
// (never under 1000 ms) but only quantizes a pick that flips between near-equal mobs on every press, so a
// different enemy is taken at every expiry. The rows carry (path, ms held, picks turned down), not every
// press: the replay rebuilds the presses from them - between two changes, `sup` presses prefer the OTHER
// enemy inside the first second (the floor turns each down), each followed by one press that still prefers
// the held one (the flip), then the first press after the held time takes it if the rules allow.
{
    (string Path, long HeldMs, int Sup)[] rows =
    [
        ("st", 1044, 18), ("aoe", 1054, 17), ("st", 1038, 13), ("aoe", 1001, 16), ("st", 1055, 13), ("aoe", 1058, 17),
        ("st", 1013, 12), ("aoe", 1036, 16), ("st", 1310, 17), ("aoe", 1032, 16), ("aoe", 1031, 16), ("st", 1020, 1),
        ("aoe", 1015, 14),
    ];

    List<long> ReplayBurst(DpsTargetStability s, out int committedByRows)
    {
        var committed = new List<long>();
        ulong held = 101, other = 102;
        s.Resolve(held, 0, _ => true);                       // the enemy being fought when the burst starts
        long t = 0;
        committedByRows = 0;
        foreach (var row in rows)
        {
            var next = t + row.HeldMs;
            for (var k = 1; k <= row.Sup; k++)
            {
                var at = t + 990 * k / (row.Sup + 1);              // all turned down: inside the floor
                s.Resolve(other, at, _ => true);              // a press prefers the other mob
                s.Resolve(held, at + 1, _ => true);           // the next press prefers the held one again
            }
            // the first press after the floor expired: whichever mob it prefers is taken if the rules allow
            var r = s.Resolve(other, next, _ => true);
            if (r.Changed) { committed.Add(next); committedByRows++; (held, other) = (other, held); }
            t = next;
        }
        return committed;
    }

    var burst = ReplayBurst(new DpsTargetStability(), out var n);
    Check("real burst (10-11 12:33:01..13): a held enemy that is still preferred between presses is kept - no change in 13 s",
        n == 0, $"{n} changes at ms {string.Join(",", burst)}");

    // The replay is faithful: the floor alone (stickiness off, the 1.0.4.283..300 behavior) takes a different enemy at
    // every logged time - 13 changes, each after the logged hold.
    var legacy = ReplayBurst(new DpsTargetStability(DpsTargetStability.DefaultMinPickIntervalMs, 0), out var legacyN);
    var expected = new List<long>();
    long acc = 0;
    foreach (var row in rows) { acc += row.HeldMs; expected.Add(acc); }
    Check("the replay reproduces the logged burst on the floor alone (13 changes, at the logged holds)",
        legacyN == 13 && legacy.SequenceEqual(expected), $"{legacyN} changes at ms {string.Join(",", legacy)}");
}

// ---- 5b. stickiness must not freeze a real change of preference
{
    // fresh flips for 5 s (the held enemy keeps coming back), then a different enemy becomes the only pick for good
    var s = new DpsTargetStability();
    s.Resolve(101, 0, _ => true);
    for (long t = 50; t < 5_000; t += 50)
        s.Resolve(t % 100 == 0 ? 102ul : 101ul, t, _ => true);
    var stillHeld = s.Resolve(102, 5_000, _ => true);
    var changedAt = -1L;
    for (long t = 5_050; t <= 8_000 && changedAt < 0; t += 50)
        if (s.Resolve(102, t, _ => true).Changed) changedAt = t;
    Check("a flapping stream keeps the held enemy; a different enemy that is the only pick for good is taken within 1.5 s plus one tick",
        stillHeld.Id == 101 && changedAt is >= 0 and <= 4_950 + 1_500 + 50 + 50, $"held={stillHeld.Id} changedAt={changedAt}");
}
{
    // nothing flaps and the pick moves to another enemy for good: still no longer than the 1.5 s dwell
    var s = new DpsTargetStability();
    s.Resolve(101, 0, _ => true);
    s.Resolve(101, 10_000, _ => true);
    var early = s.Resolve(102, 10_100, _ => true);
    var late = s.Resolve(102, 11_600, _ => true);
    Check("a settled preference for another enemy is taken after 1.5 s (never stuck)", early.Id == 101 && !early.Changed && late.Id == 102 && late.Changed,
        $"early={early.Id}/{early.Why} late={late.Id}/{late.Why}");
}

// ---- 6. the interval is the one second Joey named
Check("the minimum interval between DPS target picks is one second", DpsTargetStability.DefaultMinPickIntervalMs == 1000);
Check("the held enemy is abandoned after 1.5 s without being the pick", DpsTargetStability.DefaultAbandonAfterMs == 1500);

// ---- 7. telemetry line
{
    var line = DpsTargetStability.BuildLine(1791238646006, DpsTargetStability.Why.Repick, "st", 14631, 14632, 1204, 3);
    Check("the TS| line carries why, path, both enemies, the hold and the suppressed count",
        line == "TS|1791238646006|why=Repick|path=st|from=14631|to=14632|held=1204|sup=3" && line.Length <= 200, line);
}

{
    var line = DpsTargetStability.BuildLine(1791736381033, DpsTargetStability.Why.Repick, "aoe", 12904, 12904, 1054, 17, 1073745374, 1073745401);
    Check("the TS| line ends with both object ids when given (same-species swaps show as a swap)",
        line == "TS|1791736381033|why=Repick|path=aoe|from=12904|to=12904|held=1054|sup=17|fid=1073745374|tid=1073745401" && line.Length <= 200, line);
}

Console.WriteLine(failures == 0 ? "OK" : $"FAILED ({failures})");
return failures == 0 ? 0 : 1;
