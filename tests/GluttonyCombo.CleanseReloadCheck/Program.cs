// GluttonyCombo.CleanseReloadCheck: the hot-reload path of a plugin carrying the cleanse gates,
// exercised as a real assembly lifecycle. 2026-10-24's freeze followed a mid-session plugin
// hot-reload; Dalamud hot-reload loads a fresh copy of the plugin assembly into a new collectible
// load context while the game keeps running. This check does the same to the SHIPPED assembly:
// load -> the pure gate types answer -> unload the context -> reload -> the gate types answer
// again with fresh state. The full frozen-scenario decision runs in the rotation harnesses (the
// real AST source compiled with CleanseGate); this check proves the assembly swap itself.
//
// Only the dependency-free gate types (CleanseGate, CleanseFrameGuard) are reflected here on
// purpose: they are the state a reload must not carry across, and reflecting them never touches
// a game or Dalamud type, so no game install is needed.

using System.Reflection;
using System.Runtime.Loader;

namespace GluttonyCombo.CleanseReloadCheck;

internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static void Check(string desc, bool ok, string detail = "")
    {
        if (ok) _pass++;
        else _fail++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {desc}{(ok || detail.Length == 0 ? "" : $" -- {detail}")}");
    }

    private static int Main(string[] args)
    {
        var dll = args.Length > 0 ? args[0]
            : Path.Combine("src", "GluttonyCombo", "GluttonyCombo", "bin", "Release", "GluttonyCombo.dll");
        dll = Path.GetFullPath(dll);
        Console.WriteLine($"-- GluttonyCombo cleanse hot-reload check over {dll} --");
        var overall = System.Diagnostics.Stopwatch.StartNew();

        Check("shipped assembly exists on disk", File.Exists(dll), dll);

        // ---- life 1: load, verdicts, arm in-flight state ----
        PluginLoadContext? life1 = new();
        Assembly? asm1 = life1.LoadFromAssemblyPath(dll);        var (gate1, guard1) = (ResolveType(asm1, "CleanseGate"), ResolveType(asm1, "CleanseFrameGuard"));
        Check("life 1: CleanseGate type resolves from the shipped assembly", gate1 is not null);
        Check("life 1: CleanseFrameGuard type resolves from the shipped assembly", guard1 is not null);

        var t0 = DateTime.UtcNow;
        var sev = (int)gate1!.GetMethod("SeverityOf", [typeof(uint)]!)!.Invoke(null, [18u])!;
        Check("life 1: SeverityOf(Poison 18) = Moderate (the captured debuff's verdict works)", sev == (int)CleanseSeverityRef.Moderate, $"got {sev}");

        gate1.GetMethod("NoteSent", [typeof(ulong), typeof(DateTime)])!.Invoke(null, [7ul, t0]);
        var inflight1 = (bool)gate1.GetMethod("IsInFlight", [typeof(ulong), typeof(DateTime)])!.Invoke(null, [7ul, t0.AddSeconds(1)])!;
        Check("life 1: the in-flight window reads true right after NoteSent", inflight1);

        guard1!.GetMethod("NoteInit", [typeof(DateTime)])!.Invoke(null, [t0]);
        var entered1 = (bool)guard1.GetMethod("EnterTick", [typeof(DateTime)])!.Invoke(null, [t0.AddSeconds(1)])!;
        Check("life 1: the guard init fence refuses a scan right after load", !entered1);

        // ---- the swap: drop every reference, collect, unload ----
        var asm1Weak = new WeakReference(asm1);
        gate1 = null; guard1 = null; asm1 = null; // no managed handle roots the context
        var unloadSw = System.Diagnostics.Stopwatch.StartNew();
        life1!.Unload();
        life1 = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var collected = !asm1Weak.IsAlive;
        unloadSw.Stop();
        Check($"life 1: load context unloaded (collected: {collected}, {unloadSw.Elapsed.TotalMilliseconds:F0}ms)", unloadSw.Elapsed.TotalSeconds < 20, "unload took too long");

        // ---- life 2: reload (the hot-reload analogue), verdicts again, state must be fresh ----
        PluginLoadContext? life2 = new();
        Assembly? asm2 = life2.LoadFromAssemblyPath(dll);        var (gate2, guard2) = (ResolveType(asm2, "CleanseGate"), ResolveType(asm2, "CleanseFrameGuard"));
        Check("life 2: CleanseGate type resolves from the reloaded assembly", gate2 is not null);
        Check("life 2: CleanseFrameGuard type resolves from the reloaded assembly", guard2 is not null);

        var inflight2 = (bool)gate2!.GetMethod("IsInFlight", [typeof(ulong), typeof(DateTime)])!.Invoke(null, [7ul, t0.AddSeconds(1)])!;
        Check("life 2: the in-flight window is FRESH - life 1's NoteSent did not survive the swap", !inflight2);

        var sev2 = (int)gate2.GetMethod("SeverityOf", [typeof(uint)]!)!.Invoke(null, [14u])!;
        Check("life 2: SeverityOf(Heavy 14) = High after the swap", sev2 == (int)CleanseSeverityRef.High, $"got {sev2}");

        var tReload = t0.AddSeconds(InitOffsetSeconds + 1);
        var entered2 = (bool)guard2!.GetMethod("EnterTick", [typeof(DateTime)])!.Invoke(null, [tReload])!;
        Check("life 2: the reloaded guard's init fence refuses a scan right after the swap", !entered2);
        var entered2b = (bool)guard2.GetMethod("EnterTick", [typeof(DateTime)])!.Invoke(null, [tReload.AddSeconds(InitFenceBufferSeconds)])!;
        var overruns2 = (int)guard2.GetMethod("get_OverrunCount", Type.EmptyTypes)!.Invoke(null, [])!;
        Check("life 2: after the init fence the scan enters and overrun state is fresh", entered2b && overruns2 == 0, $"entered={entered2b} overruns={overruns2}");

        life2!.Unload();
        life2 = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();

        overall.Stop();
        Check($"whole load -> unload -> reload lifecycle completed within the timeout ({overall.Elapsed.TotalMilliseconds:F0}ms)",
            overall.Elapsed.TotalSeconds < 60, "lifecycle too slow");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    // Seconds added to t0 to place life-2 guard reads past the init fence.
    private const double InitOffsetSeconds = 11.0;
    private const double InitFenceBufferSeconds = 20.0;

    /// <summary>Enum-member names of the plugin's CleanseSeverity, for readable assertions only.</summary>
    private enum CleanseSeverityRef
    {
        Trivial = 0,
        Moderate = 1,
        High = 2,
    }

    private static Type? ResolveType(Assembly asm, string typeName) =>
        asm.GetType($"GluttonyCombo.AutoRotation.{typeName}", throwOnError: false);

    /// <summary>A collectible context for the plugin assembly; other assemblies resolve from the default context.</summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        public PluginLoadContext() : base("CleanseReloadCheck-" + Guid.NewGuid().ToString("N"), isCollectible: true)
        {
        }

        protected override Assembly? Load(AssemblyName name) => null; // framework + game deps fall through to default
    }
}
