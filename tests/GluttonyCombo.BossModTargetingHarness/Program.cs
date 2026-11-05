// Offline cases for the status-immunity seam and the IPC endpoint latch that the DPS
// targeting paths share (GluttonyCombo; originally the "Use boss-mod targeting when
// active" checkbox harness, task tasks-20260927-gluttony-bmr-targeting-checkbox-01).
//
// Fork (Fight Brain 1a): the checkbox itself is REMOVED — it needed BMR's
// `Hints.PriorityTarget`, which stock BossMod Reborn does not provide, so it could never
// act and only logged on every load. What still ships from that work, and what this
// harness still pins, is:
//   1. the status-immunity seam (BossModTargetingGate.IsTargetUsable /
//      ResolveStatusImmuneTarget) every dropdown, manual-AoE and action-retarget path
//      converges on; and
//   2. the endpoint latch (IpcEndpointLatch) behind the live SetTransient distance write:
//      call once, latch on an endpoint-missing error, say so exactly once, stop calling,
//      and re-arm when BossMod Reborn reloads (new generation string).

using GluttonyCombo.AutoRotation;
using GluttonyCombo.Services.IPC;

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

// The dropdown result itself is guarded: direct modes such as Manual and
// Tank Target bypass the candidate list, so an immune result must be rejected.
Check("direct dropdown target with status immunity is rejected",
    expected: false,
    actual: BossModTargetingGate.IsTargetUsable(
        baseUsable: true, statusDamageImmune: true));
Check("direct dropdown attackable target is retained",
    expected: true,
    actual: BossModTargetingGate.IsTargetUsable(
        baseUsable: true, statusDamageImmune: false));

// All eight DPS dropdown values converge on this resolution seam after their
// tank/non-tank switch has selected an actor.
var dropdownModes = new[]
{
    "Manual", "Highest_Max", "Lowest_Max", "Highest_Current",
    "Lowest_Current", "Tank_Target", "Nearest", "Furthest",
};
foreach (var mode in dropdownModes)
    CheckEqual($"direct {mode} immune target selects attackable fallback",
        expected: "sahagin",
        actual: BossModTargetingGate.ResolveStatusImmuneTarget(
            selectedTarget: "ymir",
            selectedDamageImmune: true,
            fallbackTarget: "sahagin"));

CheckEqual("direct immune target with no attackable fallback returns none",
    expected: null,
    actual: BossModTargetingGate.ResolveStatusImmuneTarget(
        selectedTarget: "ymir",
        selectedDamageImmune: true,
        fallbackTarget: null as string));
CheckEqual("outside-Crucible/direct attackable selection is unchanged",
    expected: "ymir",
    actual: BossModTargetingGate.ResolveStatusImmuneTarget(
        selectedTarget: "ymir",
        selectedDamageImmune: false,
        fallbackTarget: "sahagin"));
CheckEqual("manual AoE (AoEIgnoreManual off) immune center selects filtered auto-target",
    expected: "sahagin",
    actual: BossModTargetingGate.ResolveStatusImmuneTarget(
        selectedTarget: "ymir",
        selectedDamageImmune: true,
        fallbackTarget: "sahagin"));
CheckEqual("manual AoE (AoEIgnoreManual on) keeps filtered auto-target",
    expected: "sahagin",
    actual: BossModTargetingGate.ResolveStatusImmuneTarget(
        selectedTarget: "sahagin",
        selectedDamageImmune: false,
        fallbackTarget: "sahagin"));

// ---------------------------------------------------------------------------------------------
// Endpoint latch (bst1013). The latch must: call once, latch on the endpoint-missing error, say
// so exactly once, stop calling, and re-arm when BossMod Reborn reloads (new instance or version
// = new generation string). It backs the live SetTransient distance write.
// ---------------------------------------------------------------------------------------------
const string Gen7534 = "7.5.6.34#1111";
const string Gen7534Reloaded = "7.5.6.34#2222";
const string Gen7535 = "7.5.6.35#3333";
var notReady = new Dalamud.Plugin.Ipc.Exceptions.IpcNotReadyError();

var latch = new IpcEndpointLatch();
Check("latch: first call goes through", true, latch.ShouldCall(Gen7534));
CheckEqual("latch: unknown before any call", IpcEndpointLatch.State.Unknown, latch.StateFor(Gen7534));
Check("latch: first IpcNotReadyError latches and asks for the one log line", true,
    latch.RecordFailure(Gen7534, notReady));
Check("latch: no further calls in the same generation", false, latch.ShouldCall(Gen7534));
CheckEqual("latch: state reads Missing", IpcEndpointLatch.State.Missing, latch.StateFor(Gen7534));
Check("latch: a second failure does not log again", false, latch.RecordFailure(Gen7534, notReady));

// The 50 ms throttle over a 20-minute session: today 24,000 calls (each a caught exception);
// with the latch exactly one call and one log line.
var sessionLatch = new IpcEndpointLatch();
int calls = 0, logLines = 0;
for (var tick = 0; tick < 24_000; tick++)
{
    if (!sessionLatch.ShouldCall(Gen7534))
        continue;
    calls++;
    if (sessionLatch.RecordFailure(Gen7534, notReady))
        logLines++;
}
CheckEqual("latch: 24,000 targeting ticks on stock BMR make 1 call", 1, calls);
CheckEqual("latch: and write 1 Information line", 1, logLines);

// Re-arm: a reload (new plugin instance) or an update (new version) is a new generation.
Check("latch: BMR reload re-arms (call again)", true, latch.ShouldCall(Gen7534Reloaded));
Check("latch: BMR update re-arms (call again)", true, latch.ShouldCall(Gen7535));
latch.RecordSuccess(Gen7535);
CheckEqual("latch: a build that answers reads Present", IpcEndpointLatch.State.Present, latch.StateFor(Gen7535));
Check("latch: Present keeps calling", true, latch.ShouldCall(Gen7535));

// Only endpoint-missing errors latch. An exception thrown inside the provider is transient.
var transient = new IpcEndpointLatch();
Check("latch: an exception inside the provider does not latch", false,
    transient.RecordFailure(Gen7535, new NullReferenceException()));
Check("latch: after a transient failure the next call goes through", true, transient.ShouldCall(Gen7535));
Check("latch: IpcTypeMismatchError latches (registered with other types)", true,
    new IpcEndpointLatch().RecordFailure(Gen7535, new Dalamud.Plugin.Ipc.Exceptions.IpcTypeMismatchError()));
Check("latch: IpcLengthMismatchError latches (registered with other arity)", true,
    new IpcEndpointLatch().RecordFailure(Gen7535, new Dalamud.Plugin.Ipc.Exceptions.IpcLengthMismatchError()));
Check("latch: a wrapped IpcNotReadyError is recognised",
    true, IpcEndpointLatch.IsMissingEndpointError(
        new System.Reflection.TargetInvocationException(notReady)));
Check("latch: IpcValueNullError is not 'missing' (the endpoint answered)", false,
    IpcEndpointLatch.IsMissingEndpointError(new Dalamud.Plugin.Ipc.Exceptions.IpcValueNullError()));

// Re-check throttle (review finding 4). A caller that re-checks at most once a second: the first
// ask must go through. The shipped field started at long.MinValue, so now - last overflowed
// negative, the throttle never passed.
const long Uptime = 5_000_000L; // Environment.TickCount64 a few minutes into a session
Check("recheck throttle: never checked -> due at once", true, IpcEndpointLatch.RecheckDue(null, Uptime, 1000));
Check("recheck throttle: never checked at clock 0 -> due", true, IpcEndpointLatch.RecheckDue(null, 0, 1000));
Check("recheck throttle: checked 200 ms ago -> not due", false, IpcEndpointLatch.RecheckDue(Uptime - 200, Uptime, 1000));
Check("recheck throttle: checked 1 s ago -> due", true, IpcEndpointLatch.RecheckDue(Uptime - 1000, Uptime, 1000));

if (failures > 0)
{
    Console.WriteLine($"{failures} case(s) FAILED");
    return 1;
}

Console.WriteLine("OK");
return 0;

// Stand-ins named like Dalamud's IPC errors (Dalamud.Plugin.Ipc.Exceptions in Dalamud.dll). The latch
// matches by type name, so these exercise exactly the path the live errors take.
namespace Dalamud.Plugin.Ipc.Exceptions
{
    public class IpcError : Exception { }
    public sealed class IpcNotReadyError : IpcError { }
    public sealed class IpcTypeMismatchError : IpcError { }
    public sealed class IpcLengthMismatchError : IpcError { }
    public sealed class IpcValueNullError : IpcError { }
}
