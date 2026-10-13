// Failing-first cases for the "Use boss-mod targeting when active" checkbox
// (GluttonyCombo 1.0.4.241, task tasks-20260927-gluttony-bmr-targeting-checkbox-01).
//
// Semantics under test (Joey, 2026-09-27T03:55:25Z): "Can we set a checkbox to
// supercede the normal autotargeting in gluttony. it would have it use the BMR
// autotargeting instead and then fall back to the dropdown if there is no active
// boss mod."
//
// The four states, expressed at the decision seam GetSingleTarget consults:
//   1. ON  + active module + opinion          -> boss-mod target wins
//   2. ON  + active module + NO opinion       -> dropdown fallback
//   3. ON  + NO active module                 -> dropdown fallback
//   4. OFF (module + opinion live or not)     -> byte-identical to pre-checkbox
// plus the stall guard: ON + opinion that resolves to an unusable target (dead,
// untargetable, out of range) -> dropdown fallback, never a dead-air rotation.
//
// States 2 and 3 arrive at this seam as the same input (endpoint id 0) BY DESIGN:
// BossModReborn's Hints.PriorityTarget endpoint reports 0 both when no module is
// active and when the active module expresses no attackable opinion. Both cases
// are asserted separately to pin that collapse: any future change that makes one
// of them override would fail exactly one of these two cases.

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

// 1. ON + active module + opinion -> the boss-mod target must win.
Check("on+module+opinion overrides",
    expected: true,
    actual: BossModTargetingGate.ShouldUseBossModTarget(
        checkboxOn: true, bossModTargetId: 0x1001234, targetUsable: true));

// 2. ON + active module + NO opinion (endpoint id 0) -> dropdown fallback.
Check("on+module+no-opinion falls back",
    expected: false,
    actual: BossModTargetingGate.ShouldUseBossModTarget(
        checkboxOn: true, bossModTargetId: 0, targetUsable: false));

// 3. ON + NO active module (endpoint id 0) -> dropdown fallback. Distinct scenario
//    from case 2 upstream; must stay a fallback here too.
Check("on+no-module falls back",
    expected: false,
    actual: BossModTargetingGate.ShouldUseBossModTarget(
        checkboxOn: true, bossModTargetId: 0, targetUsable: false));

// 4. OFF -> never overrides, even with a live module opinion available. This is
//    the "checkbox OFF = byte-identical behavior" guarantee.
Check("off never overrides",
    expected: false,
    actual: BossModTargetingGate.ShouldUseBossModTarget(
        checkboxOn: false, bossModTargetId: 0x1001234, targetUsable: true));

// Stall guard: ON + opinion whose actor fails the standard enemy filter
// (dead / untargetable / out of range / LOS) -> dropdown fallback, not a
// rotation locked onto an unusable target.
Check("on+unusable-target falls back (no stall)",
    expected: false,
    actual: BossModTargetingGate.ShouldUseBossModTarget(
        checkboxOn: true, bossModTargetId: 0x1001234, targetUsable: false));

// Regression (.241 in-game verdict): a status-based damage immunity can coexist
// with IsTargetable and the standard enemy checks. It must still reject BMR's
// opinion so normal targeting can choose an attackable enemy.
Check("on+status-immune target falls back",
    expected: false,
    actual: BossModTargetingGate.ShouldUseBossModTarget(
        checkboxOn: true, bossModTargetId: 0x1001234, targetUsable: true,
        targetDamageImmune: true));

// The dropdown result itself is guarded too: direct modes such as Manual and
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
// Endpoint latch (bst1013). Stock BossMod Reborn registers no Hints.PriorityTarget, so every call
// threw IpcNotReadyError at the 50 ms targeting throttle, logged only at Verbose. The latch must:
// call once, latch on the endpoint-missing error, say so exactly once, stop calling, and re-arm
// when BossMod Reborn reloads (new instance or version = new generation string).
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

// A latched endpoint answers 0 = no opinion, so the gate keeps the dropdown path (state 3 above).
Check("latch: missing endpoint (id 0) falls back to the dropdown", false,
    BossModTargetingGate.ShouldUseBossModTarget(checkboxOn: true, bossModTargetId: 0, targetUsable: false));

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
