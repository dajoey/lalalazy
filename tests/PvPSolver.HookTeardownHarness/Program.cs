using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace PvPSolver.HookTeardownHarness;

/// <summary>
///     Offline proof that a Dalamud hot-reload of PvPSolver can no longer crash the game through a
///     hook detour that runs after (or while) its hook is being torn down (crash family of
///     2026-08-31 and 2026-10-03: a managed exception escaping a detour the game had already entered,
///     while the plugin pulled the hook out from under it).
///     The detours are invoked with the hooks null — exactly the state teardown leaves behind while
///     the game may still be inside a detour. No game, no Dalamud services.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    // The packaging keep-lists leave Dalamud's own assemblies out of the output folder; resolve them from
    // the Dalamud dev folder the way the plugin build does so the detours' types load without a game.
    private static void AddDalamudResolver()
    {
        var dir = Environment.GetEnvironmentVariable("DALAMUD_HOME")
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                      "XIVLauncher", "addon", "Hooks", "dev");
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var file = Path.Combine(dir, name.Name + ".dll");
            return File.Exists(file) ? ctx.LoadFromAssemblyPath(file) : null;
        };
    }

    private static int Main()
    {
        AddDalamudResolver();
        Console.WriteLine("-- PvPSolver hook teardown: detour after teardown --");

        CaseA();
        CaseB();

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    // Case A: the state after teardown — hook null — must not throw out of the detour.
    private static unsafe void CaseA()
    {
        Console.WriteLine("-- case A: detours invoked with the hook null (post-teardown state) --");

        // Service's ctor initializes hooks through Dalamud services; the uninitialized object is exactly
        // the torn-down state (hook field null) the game can still be inside a detour of.
        var service = Uninitialized(Type.GetType("RotationSolver.Basic.Service, PvPSolver.Basic")
                                   ?? throw new MissingMemberException("RotationSolver.Basic.Service not found"));
        var detour = Private(service.GetType(), "ActorVfxCreateDetour");
        var pathType = detour.GetParameters()[0].ParameterType;

        var path = Encoding.ASCII.GetBytes("harness\0");
        Exception? ex = null;
        object? ret = null;
        fixed (byte* p = path)
        {
            try
            {
                ret = Invoke(detour, service,
                    [Pointer.Box(p, pathType), (nint)0, (nint)0, 0f, '\0', (ushort)0, '\0']);
            }
            catch (Exception e)
            {
                ex = e;
            }

            Check($"Service.ActorVfxCreateDetour: no exception with hook null"
                  + (ex == null ? "" : $" [{Describe(ex)}]"), ex is null);
            Check("Service.ActorVfxCreateDetour: returns no VFX handle when the hook is gone",
                ex is null && ret is IntPtr r && r == IntPtr.Zero);
        }

        var timeline = Uninitialized(Type.GetType("RotationSolver.ActionTimeline.ActionTimelineManager, PvPSolver")
                                     ?? throw new MissingMemberException("ActionTimelineManager not found"));

        ex = null;
        try
        {
            Invoke(Private(timeline.GetType(), "OnActorControl"), timeline,
                [0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0ul, (byte)0]);
        }
        catch (Exception e)
        {
            ex = e;
        }

        Check($"ActionTimelineManager.OnActorControl: no exception with hook null"
              + (ex == null ? "" : $" [{Describe(ex)}]"), ex is null);

        ex = null;
        try
        {
            Invoke(Private(timeline.GetType(), "OnCast"), timeline, [0u, IntPtr.Zero]);
        }
        catch (Exception e)
        {
            ex = e;
        }

        Check($"ActionTimelineManager.OnCast: no exception with hook null"
              + (ex == null ? "" : $" [{Describe(ex)}]"), ex is null);
    }

    // Case B: the teardown must wait (bounded) for a detour that is still in flight before disposing
    // the hooks. The counter is held the way a running detour holds it.
    private static void CaseB()
    {
        Console.WriteLine("-- case B: teardown waits for an in-flight detour --");
        var service = Uninitialized(Type.GetType("RotationSolver.Basic.Service, PvPSolver.Basic")!);
        RunStopsWaiting(service.GetType(), service, "Service", "Dispose", instance: true);

        var timeline = Uninitialized(Type.GetType("RotationSolver.ActionTimeline.ActionTimelineManager, PvPSolver")!);
        RunStopsWaiting(timeline.GetType(), timeline, "ActionTimelineManager", "DisposeHooks", instance: true);
    }

    private static void RunStopsWaiting(Type owner, object target, string moduleName, string teardownName, bool instance)
    {
        var field = owner.GetField("InFlight", BindingFlags.NonPublic | BindingFlags.Static)
                  ?? owner.GetField("InFlight", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            Check($"{moduleName}: an in-flight counter exists for its detours", false);
            return;
        }

        Check($"{moduleName}: an in-flight counter exists for its detours", true);
        var counter = field.GetValue(field.IsStatic ? null : target)!;
        var enter = counter.GetType().GetMethod("Enter", Type.EmptyTypes)!;
        var token = enter.Invoke(counter, null)!;
        var release = token.GetType().GetMethod("Dispose", Type.EmptyTypes)!;

        // Resolve the parameterless teardown unambiguously: Service overloads Dispose() and
        // Dispose(bool), and the plain name-only lookup throws AmbiguousMatchException on overloads.
        var teardownFlags = BindingFlags.NonPublic | BindingFlags.Public | (instance ? BindingFlags.Instance : BindingFlags.Static);
        var teardown = owner.GetMethod(teardownName, teardownFlags, binder: null, Type.EmptyTypes, modifiers: null);
        if (teardown == null)
        {
            Check($"{moduleName}.{teardownName}: the draining teardown exists", false);
            return;
        }

        // Invoke with the METHOD's binding, not the counter field's: the counter is static (case A drives
        // uninitialized instances, where instance fields are null) while the teardown can be an instance
        // method (Service.Dispose, ActionTimelineManager.DisposeHooks).
        var invokeTarget = teardown.IsStatic ? null : target;
        Exception? onThread = null;
        var alive = false;
        var t = new Thread(() =>
        {
            try
            {
                teardown.Invoke(invokeTarget, null);
            }
            catch (Exception ex)
            {
                onThread = ex;
            }
        });
        t.Start();
        Thread.Sleep(150); // teardown reaches its bounded wait; with no wait it has long returned
        alive = t.IsAlive;
        Check($"{moduleName}.{teardownName}: waits while a detour is in flight", alive);

        release.Invoke(token, null);
        t.Join(5000);
        Check($"{moduleName}.{teardownName}: returns cleanly once the detour leaves", onThread is null && !t.IsAlive);
    }

    private static object Uninitialized(Type type)
        => RuntimeHelpers.GetUninitializedObject(type);

    private static MethodInfo Private(Type owner, string name)
        => owner.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
           ?? ThrowMissing<MethodInfo>(owner, name);

    private static object? Invoke(MethodInfo mi, object target, object?[] args)
    {
        try
        {
            return mi.Invoke(target, args);
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException ?? tie;
        }
    }

    private static string Describe(Exception ex)
        => ex.GetType().Name;

    private static T ThrowMissing<T>(Type owner, string name) where T : class
        => throw new MissingMemberException($"{owner.FullName}::{name} not found — module shape changed");

    private static void Check(string what, bool ok)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {what}"
                          + (ok ? "" : " — expected the teardown-safe behaviour described in the header"));
        if (ok) _pass++; else _fail++;
    }
}
