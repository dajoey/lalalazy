// Failing-first cases for the BossMod Reborn IPC stall trace (GluttonyCombo 1.0.4.283).
//
// Contract under test: every BossMod endpoint call GluttonyCombo makes is timed; a call over
// 100 ms writes exactly one `BMT|<unix ms>|ipc=<name>|ms=<n>|thread=<id>|tick=<n>` line (a fast
// call writes nothing and changes nothing), the value and any exception surface exactly as an
// untimed call, and a framework-tick gap over 1 s whose window contains a finished BossMod call
// writes one hitch line - once per stall, never again until a normal tick re-arms it.

using System.Text.RegularExpressions;
using GluttonyCombo.Services.IPC;

var failures = 0;

void Check(string name, bool expected, bool actual, string detail = "")
{
    var pass = expected == actual;
    if (!pass) failures++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {name}" +
                      (pass ? "" : $": expected {expected}, got {actual}{(detail == "" ? "" : $"  [{detail}]")}"));
}

void CheckTrue(string name, bool actual, string detail = "") => Check(name, true, actual, detail);

void CheckEqual<T>(string name, T expected, T actual)
{
    var pass = EqualityComparer<T>.Default.Equals(expected, actual);
    if (!pass) failures++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {name}: expected {expected}, got {actual}");
}

// The fake clock drives only the tick-gap windows; BMT durations come from a real Stopwatch.
long t = 0;
var lines = new List<string>();

void Reset()
{
    t = 1000;
    lines.Clear();
    IpcStallTrace.ResetForTest();
    IpcStallTrace.Clock = () => t;
    IpcStallTrace.Emit = line => lines.Add(line);
}

// ---------------------------------------------------------------------------
// 1. A fake IPC that sleeps 150 ms produces exactly one BMT line and returns
//    the same value. This is the case the change exists for.
// ---------------------------------------------------------------------------
Reset();
var slowResult = IpcStallTrace.Time("Hints.PriorityTarget", () =>
{
    Thread.Sleep(150);
    return 42ul;
});
CheckEqual("slow call returns the endpoint's value unchanged", 42ul, slowResult);
CheckEqual("slow call writes exactly one BMT line", 1, lines.Count);
CheckTrue("BMT line names the endpoint", lines.Count > 0 && lines[0].Contains("ipc=Hints.PriorityTarget"),
    lines.Count > 0 ? lines[0] : "(no line)");
var msMatch = lines.Count > 0 ? Regex.Match(lines[0], @"(?:\||^)ms=(\d+)(?:\||$)") : Match.Empty;
CheckTrue("BMT line carries an ms field over the 100 ms threshold",
    msMatch.Success && long.Parse(msMatch.Groups[1].Value) >= 100,
    lines.Count > 0 ? lines[0] : "(no line)");
CheckTrue("BMT line carries thread and tick fields",
    lines.Count > 0 && lines[0].Contains("thread=") && lines[0].Contains("tick="),
    lines.Count > 0 ? lines[0] : "(no line)");
CheckTrue("BMT line starts with the BMT| prefix",
    lines.Count > 0 && lines[0].StartsWith("BMT|"),
    lines.Count > 0 ? lines[0] : "(no line)");

// ---------------------------------------------------------------------------
// 2. A fast fake produces no output at all - the no-behavior-change guarantee.
// ---------------------------------------------------------------------------
Reset();
var fastResult = IpcStallTrace.Time("Hints.PriorityTarget", () => 7ul);
CheckEqual("fast call returns the endpoint's value unchanged", 7ul, fastResult);
CheckEqual("fast call writes no BMT line", 0, lines.Count);

// ---------------------------------------------------------------------------
// 3. An IPC exception surfaces exactly as today: the same exception object
//    propagates, and a fast failure writes no line. GetPriorityTargetId's
//    catch then still turns it into 0 upstream - unchanged by this change.
// ---------------------------------------------------------------------------
Reset();
var boom = new InvalidOperationException("endpoint missing");
Exception? caught = null;
try
{
    IpcStallTrace.Time<ulong>("Hints.PriorityTarget", () => throw boom);
}
catch (Exception e)
{
    caught = e;
}
CheckTrue("the endpoint's exception propagates unchanged", ReferenceEquals(boom, caught),
    caught is null ? "(nothing caught)" : caught.GetType().Name);
CheckEqual("fast failing call writes no BMT line", 0, lines.Count);

// 3b. A SLOW failing call still writes its line - the duration is diagnostic
//     even when the call threw - and the exception still propagates.
Reset();
Exception? caught2 = null;
try
{
    IpcStallTrace.Time<ulong>("Hints.PriorityTarget", () =>
    {
        Thread.Sleep(150);
        throw boom;
    });
}
catch (Exception e)
{
    caught2 = e;
}
CheckTrue("slow failing call still propagates the exception", ReferenceEquals(boom, caught2));
CheckEqual("slow failing call writes exactly one BMT line", 1, lines.Count);

// ---------------------------------------------------------------------------
// 4. The tick-gap hitch line: a framework-tick gap over 1 s whose window
//    contains a finished BossMod call writes exactly one line naming it.
// ---------------------------------------------------------------------------
Reset();
IpcStallTrace.OnTickStart();                                   // baseline at t=1000
IpcStallTrace.Time("Hints.PriorityTarget", () => { t += 2; return 1ul; }); // finished at t=1002
CheckEqual("calls between normal ticks write nothing", 0, lines.Count);
t = 2600;                                                      // next entry: gap 1600 ms
IpcStallTrace.OnTickStart();
CheckEqual("a >1s tick gap containing a BossMod call writes exactly one hitch line", 1, lines.Count);
CheckTrue("hitch line names the endpoint and carries the hitch marker",
    lines.Count > 0 && lines[0].Contains("hitch|") && lines[0].Contains("ipc=Hints.PriorityTarget"),
    lines.Count > 0 ? lines[0] : "(no line)");
CheckTrue("hitch line reports the gap in ms", lines.Count > 0 && lines[0].Contains("ms=1600"),
    lines.Count > 0 ? lines[0] : "(no line)");

// 4b. Once per stall: the stall continuing across further big gaps writes
//     nothing more; a normal tick re-arms it.
t = 4200;
IpcStallTrace.OnTickStart();                                   // gap 1600 again, no new call
CheckEqual("a still-stalled system does not repeat the line", 1, lines.Count);
t = 4700;
IpcStallTrace.OnTickStart();                                   // gap 500 -> normal, re-arms
CheckEqual("a normal tick writes nothing", 1, lines.Count);
IpcStallTrace.Time("Hints.PriorityTarget", () => { t += 2; return 1ul; }); // finished at t=4702
t = 7000;
IpcStallTrace.OnTickStart();                                   // gap 2300 with a call in window
CheckEqual("a new stall after a normal tick writes one fresh hitch line", 2, lines.Count);
t = 9600;
IpcStallTrace.OnTickStart();                                   // gap 2600, no call since
CheckEqual("still exactly two lines after the second stall closes", 2, lines.Count);

// 4c. A big gap with NO BossMod call in the window writes nothing - the
//     hitch line only exists to tie a stall to BossMod IPC activity.
Reset();
IpcStallTrace.OnTickStart();                                   // baseline at t=1000
t = 5000;
IpcStallTrace.OnTickStart();                                   // gap 4000, no call ever made
CheckEqual("a >1s gap without BossMod IPC activity writes nothing", 0, lines.Count);

// 4d. The very first tick is only a baseline: no gap, no line.
Reset();
IpcStallTrace.OnTickStart();
CheckEqual("the first tick only sets the baseline", 0, lines.Count);

// ---------------------------------------------------------------------------
// 5. Unwired sink: nothing throws when no log is attached (the plugin's
//    state before initialize and after dispose).
// ---------------------------------------------------------------------------
IpcStallTrace.ResetForTest();
IpcStallTrace.Clock = () => t;
var unwired = IpcStallTrace.Time("Hints.IsDashSafe", () => true);
IpcStallTrace.OnTickStart();
CheckEqual("unwired sink returns values and does not throw", true, unwired);
CheckEqual("unwired sink collected nothing", 0, lines.Count);

if (failures > 0)
{
    Console.WriteLine($"{failures} case(s) FAILED");
    return 1;
}

Console.WriteLine("OK");
return 0;
