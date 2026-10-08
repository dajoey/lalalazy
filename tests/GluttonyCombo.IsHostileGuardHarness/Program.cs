// IsHostileGuardHarness (2026-10-08): regression proof for the null-address guard in
// ECommons.GameFunctions.ObjectFunctions.IsHostile (crash dalamud_appcrash_20261008_124827:
// C0000005 read of address 0, native nameplate call reached with Address == IntPtr.Zero out of
// DRK_RetargetShadowstride's MouseOver/CurrentTarget IfHostile() chain).
//
// THE GUARD CASES fail against the unguarded source: a zero Address made IsHostile invoke the
// native delegate (the crash), and a null wrapper threw NullReferenceException at a.Address.
// They assert not just the false verdict but that the "native" function was NEVER invoked -
// the recorder in Fakes.cs counts invocations, so a future edit that calls first and checks
// later fails here even when the plate answer happens to come back harmless.
//
// THE CHARACTERIZATION CASES pin today's verdicts for a live address (yellow plate 7 -> hostile,
// unknown plate 0 -> not hostile, one native call each) so the guard cannot silently change a
// valid target's answer.
//
//   dotnet build tests/GluttonyCombo.IsHostileGuardHarness -c Release
//   dotnet tests/GluttonyCombo.IsHostileGuardHarness/bin/Release/net10.0/GluttonyCombo.IsHostileGuardHarness.dll

using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameFunctions;

namespace GluttonyCombo.IsHostileGuardHarness;

internal static class Program
{
    private static int _pass;
    private static int _fail;

    private sealed class FakeObj : IGameObject
    {
        public required nint Address { get; init; }

        public uint GameObjectId { get; init; }

        public System.Numerics.Vector3 Position { get; init; }

        public float HitboxRadius { get; init; }

        public bool IsTargetable { get; init; } = true;
    }

    private static int Main()
    {
        ZeroAddressNotHostileAndNativeNotCalled();
        NullWrapperNotHostileAndNativeNotCalled();
        ValidAddressYellowPlateIsHostile();
        ValidAddressUnknownPlateIsNotHostile();

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    private static void ZeroAddressNotHostileAndNativeNotCalled()
    {
        GuardHarness.NativeCalls = 0;
        Case("zero Address returns false without the native nameplate call", () =>
        {
            var obj = new FakeObj { Address = nint.Zero };
            var hostile = ObjectFunctions.IsHostile(obj);
            Check(!hostile, "a zero-address target must not be hostile");
            Check(GuardHarness.NativeCalls == 0,
                $"the native nameplate function must not run for a zero address (ran {GuardHarness.NativeCalls}x)");
        });
    }

    private static void NullWrapperNotHostileAndNativeNotCalled()
    {
        GuardHarness.NativeCalls = 0;
        Case("null wrapper returns false without the native nameplate call", () =>
        {
            var hostile = ObjectFunctions.IsHostile((IGameObject)null!);
            Check(!hostile, "a null target wrapper must not be hostile");
            Check(GuardHarness.NativeCalls == 0,
                $"the native nameplate function must not run for a null wrapper (ran {GuardHarness.NativeCalls}x)");
        });
    }

    private static void ValidAddressYellowPlateIsHostile()
    {
        GuardHarness.NativeCalls = 0;
        GuardHarness.NativeAnswer = 7; // yellow: attackable, not engaged
        Case("live address, yellow plate: still hostile, one native call", () =>
        {
            var obj = new FakeObj { Address = 0x1234 };
            var hostile = ObjectFunctions.IsHostile(obj);
            Check(hostile, "a yellow-plate target at a live address stays hostile");
            Check(GuardHarness.NativeCalls == 1,
                $"exactly one native call for a live address (got {GuardHarness.NativeCalls})");
        });
    }

    private static void ValidAddressUnknownPlateIsNotHostile()
    {
        GuardHarness.NativeCalls = 0;
        GuardHarness.NativeAnswer = 0; // not one of the hostile plate kinds
        Case("live address, non-hostile plate: still not hostile, one native call", () =>
        {
            var obj = new FakeObj { Address = 0x5678 };
            var hostile = ObjectFunctions.IsHostile(obj);
            Check(!hostile, "a non-hostile plate at a live address stays not hostile");
            Check(GuardHarness.NativeCalls == 1,
                $"exactly one native call for a live address (got {GuardHarness.NativeCalls})");
        });
    }

    private static void Case(string name, Action body)
    {
        try
        {
            body();
            Console.WriteLine($"PASS {name}");
            _pass++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL {name}: {ex.Message}");
            _fail++;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
