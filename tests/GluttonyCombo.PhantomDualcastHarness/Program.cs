// Offline proof for the Occult Crescent instant-cast gates.
//
// The report: GluttonyCombo cast Swiftcast while Dualcast was up. Every "do not buy an instant cast"
// decision (Swiftcast, Triplecast, Lightspeed, the movement fillers, the auto-rez Swiftcast press,
// the CustomCombo.TryInvoke refusal) reads HasOrExpectsOccultInstantCast / HasOccultDualcast. Those
// asked for status 5438. The telemetry the plugin writes for its own decisions shows the live proc:
// a Summoner holding "1249:15.0" (the ordinary Dualcast, 15s) after a hard cast, with "5438:-"
// absent in every line. The Phantom Red Mage trait grants status 1249 on every job; so on a job
// that is not Red Mage the gate read "no Dualcast" for the whole proc.
//
//   dotnet build tests/GluttonyCombo.PhantomDualcastHarness -c Release
//   dotnet tests/GluttonyCombo.PhantomDualcastHarness/bin/Release/net10.0/GluttonyCombo.PhantomDualcastHarness.dll

using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Data;
using GluttonyCombo.PhantomDualcastHarness;

const uint Dualcast1249 = 1249;     // the ordinary Dualcast status: Red Mage's own, and the Phantom Red Mage trait's proc
const uint OccultDualcast5438 = 5438; // pre-7.55 marker status
const uint OccultQuickStatus = 4260;
const uint OccultQuickAction = 41625;

var failures = 0;
var total = 0;

void Case(string name, bool expected, bool actual)
{
    total++;
    var ok = actual == expected;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-86} => {actual}, expected {expected}");
}

void Statuses(string name, uint[] held, bool expectedGate, bool expectedHas, bool expectedStrict)
{
    FakeGame.Reset();
    foreach (var s in held) FakeGame.PlayerStatuses.Add(s);
    Case($"{name}: HasOrExpectsOccultInstantCast (the Swiftcast/Triplecast press gate)", expectedGate,
        CustomComboFunctions.HasOrExpectsOccultInstantCast);
    Case($"{name}: HasOccultDualcast", expectedHas, CustomComboFunctions.HasOccultDualcast);
    Case($"{name}: HasOccultInstantCast (strict, affirmative-pick gate)", expectedStrict,
        CustomComboFunctions.HasOccultInstantCast);
}

Console.WriteLine("== Group 1: which statuses the gate recognises ==");
Console.WriteLine("   (1249 is what a Summoner holds in the log after a hard cast; the first three cases are the reported overlap)");
Statuses("non-Red-Mage job holding the Phantom Red Mage proc (status 1249)", [Dualcast1249], true, true, true);
Statuses("pre-7.55 Occult Dualcast marker (status 5438)", [OccultDualcast5438], true, true, true);
Statuses("both statuses at once", [Dualcast1249, OccultDualcast5438], true, true, true);
Statuses("nothing held: the gate stays open, Swiftcast may be pressed", [], false, false, false);
Statuses("Occult Quick window (status 4260): free casts, no Dualcast", [OccultQuickStatus], true, false, true);

FakeGame.Reset();
FakeGame.JustUsedActions.Add(OccultQuickAction);
Case("Occult Quick action just pressed, status not landed yet: gate closed", true,
    CustomComboFunctions.HasOrExpectsOccultInstantCast);

Console.WriteLine();
Console.WriteLine("== Group 2: a Dualcast still on its way (slide-cast) counts as held ==");
// Phantom Red Mage's support-job index in the Occult Crescent state is 22. The tracker only arms
// after it has seen the proc once under that job; the first cases walk that sequence.
void PhantomRedMageSession()
{
    FakeGame.Reset();
    FakeGame.SupportJob = 22;
    FakeGame.PlayerStatuses.Add(Dualcast1249);         // a proc is seen once
    CustomComboFunctions.TrackOccultDualcast(null!);
    FakeGame.PlayerStatuses.Remove(Dualcast1249);      // ... and spent by the next spell
}

PhantomRedMageSession();
FakeGame.TotalCastTime = 2.5f;                          // the next hard cast is now running
FakeGame.CurrentCastTime = 0.5f;
CustomComboFunctions.TrackOccultDualcast(null!);
Case("Phantom Red Mage equipped, proc seen before, hard cast running, no proc yet: Dualcast incoming", true,
    CustomComboFunctions.OccultDualcastIncoming);
Case("... and the press gate treats the incoming proc as held", true,
    CustomComboFunctions.HasOrExpectsOccultInstantCast);

PhantomRedMageSession();
CustomComboFunctions.TrackOccultDualcast(null!);        // no cast running
Case("Phantom Red Mage equipped, no cast running: nothing incoming, gate open", false,
    CustomComboFunctions.HasOrExpectsOccultInstantCast);

FakeGame.Reset();
FakeGame.SupportJob = 21;                               // some other Phantom job
FakeGame.PlayerStatuses.Add(Dualcast1249);
CustomComboFunctions.TrackOccultDualcast(null!);
FakeGame.PlayerStatuses.Remove(Dualcast1249);
FakeGame.TotalCastTime = 2.5f;
FakeGame.CurrentCastTime = 0.5f;
CustomComboFunctions.TrackOccultDualcast(null!);
Case("another Phantom job equipped, hard cast running: no Dualcast predicted", false,
    CustomComboFunctions.OccultDualcastIncoming);

FakeGame.Reset();
FakeGame.SupportJob = 22;
FakeGame.TotalCastTime = 2.5f;                          // proc never seen this session: no prediction yet
FakeGame.CurrentCastTime = 0.5f;
CustomComboFunctions.TrackOccultDualcast(null!);
Case("Phantom Red Mage equipped but the proc was never seen: no prediction yet", false,
    CustomComboFunctions.OccultDualcastIncoming);

Console.WriteLine();
Console.WriteLine("== Group 3: the action-issued log line names the source and the Dualcast state ==");
void Text(string name, string expected, string actual)
{
    total++;
    var ok = actual == expected;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-86} => {actual}");
    if (!ok) Console.WriteLine($"      expected {expected}");
}

Case("Swiftcast, Triplecast, Acceleration, Lightspeed and Occult Quick are watched", true,
    InstantCastAuditFormat.IsWatched(7561) && InstantCastAuditFormat.IsWatched(7421) &&
    InstantCastAuditFormat.IsWatched(7518) && InstantCastAuditFormat.IsWatched(3606) &&
    InstantCastAuditFormat.IsWatched(41625));
Case("an ordinary spell (Ruin, 163) is not watched", false, InstantCastAuditFormat.IsWatched(163));

Text("auto-rez Swiftcast into a held Phantom Red Mage Dualcast: one line, source and state",
    "IC|1791503030119|SMN|7561|Swiftcast|RezParty:non-rdm|None|dualcast1249=14.3;dualcast5438=-;quick4260=-;incoming=-;moving=1.0",
    InstantCastAuditFormat.BuildLine(1791503030119, "SMN", 7561, "RezParty:non-rdm", "None",
    [
        new("dualcast1249", 14.3f), new("dualcast5438", null), new("quick4260", null),
        new("incoming", null), new("moving", 1f),
    ]));
Text("an unclaimed press says so (not Gluttony's rotation code)",
    "IC|1|WHM|7561|Swiftcast|" + InstantCastAuditFormat.Unattributed + "|Queue|dualcast1249=-",
    InstantCastAuditFormat.BuildLine(1, "WHM", 7561, InstantCastAuditFormat.Unattributed, "Queue",
        [new("dualcast1249", null)]));

var last = new Dictionary<uint, long>();
Case("first line for an action is written", true, InstantCastAuditFormat.ShouldEmit(last, 7561, 10_000));
Case("a repeat 100 ms later is throttled", false, InstantCastAuditFormat.ShouldEmit(last, 7561, 10_100));
Case("another action is independent of it", true, InstantCastAuditFormat.ShouldEmit(last, 7421, 10_100));
Case("the same action after the throttle window is written again", true, InstantCastAuditFormat.ShouldEmit(last, 7561, 10_450));

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine($"OK ({total} checks)");
    return 0;
}

Console.WriteLine($"FAILED ({failures} of {total})");
return 1;
