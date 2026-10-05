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
    var off = new DpsTargetStability(0);
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

    var later = s.Resolve(22, 6_000, _ => true);
    Check("once the interval has passed the preferred pick is taken", later.Id == 22 && later.Changed && later.Why == DpsTargetStability.Why.Repick,
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

// ---- 5. the interval is the one second Joey named
Check("the minimum interval between DPS target picks is one second", DpsTargetStability.DefaultMinPickIntervalMs == 1000);

// ---- 6. telemetry line
{
    var line = DpsTargetStability.BuildLine(1791238646006, DpsTargetStability.Why.Repick, "st", 14631, 14632, 1204, 3);
    Check("the TS| line carries why, path, both enemies, the hold and the suppressed count",
        line == "TS|1791238646006|why=Repick|path=st|from=14631|to=14632|held=1204|sup=3" && line.Length <= 200, line);
}

Console.WriteLine(failures == 0 ? "OK" : $"FAILED ({failures})");
return failures == 0 ? 0 : 1;
