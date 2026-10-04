using System.Reflection;

namespace LazyCrucible.HookTeardownHarness;

/// <summary>
///     Offline proof that a Dalamud hot-reload of LazyCrucible can no longer crash the game through
///     a hook detour that runs after (or while) its hook is being disposed (crash family of
///     2026-08-31 and 2026-10-03: a managed exception escaping a detour the game had already
///     entered, while Stop()/Teardown() pulled the hook out from under it).
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
        Console.WriteLine("-- LazyCrucible hook teardown: detour after Stop/Teardown --");

        CaseA();
        CaseB();

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    // Case A: the state after teardown — hook null — must not throw out of the detour.
    private static void CaseA()
    {
        Console.WriteLine("-- case A: detours invoked with the hook null (post-teardown state) --");

        var recorder = typeof(LazyCrucible.ScreenRecorder);
        Exception? ex = null;
        object? ret = null;
        try
        {
            ret = Invoke(Private(recorder, "FireCallbackDetour"), [null!, 0u, null!, false]);
        }
        catch (Exception e)
        {
            ex = e;
        }

        Check($"ScreenRecorder.FireCallbackDetour: no exception with hook null"
              + (ex == null ? "" : $" [{Describe(ex)}]"), ex is null);
        Check("ScreenRecorder.FireCallbackDetour: returns false (not handled) when the hook is gone",
            ex is null && ret is false);

        var probe = typeof(LazyCrucible.AgentProbe);
        ex = null;
        ret = null;
        try
        {
            var probeType = probe.GetNestedType("ProbeHook", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? ThrowMissing<Type>(probe, "ProbeHook");
            var probeInstance = Activator.CreateInstance(probeType)!;
            SetVia(probeType, probeInstance, "re");
            ret = Invoke(Private(probe, "ReceiveEventDetour"),
                [probeInstance, null!, null!, null!, 0u, 0ul]);
        }
        catch (Exception e)
        {
            ex = e;
        }

        Check($"AgentProbe.ReceiveEventDetour: no exception with hook null"
              + (ex == null ? "" : $" [{Describe(ex)}]"), ex is null);
        Check("AgentProbe.ReceiveEventDetour: echoes the caller's return buffer when the hook is gone",
            ex is null && ret is null);
    }

    // Case B: Stop()/Teardown() must wait (bounded) for a detour that is still in flight
    // before disposing the hooks. The counter is held the way a running detour holds it.
    private static void CaseB()
    {
        Console.WriteLine("-- case B: Stop/Teardown wait for an in-flight detour --");
        RunStopsWaiting(typeof(LazyCrucible.ScreenRecorder), "InFlight", "Stop");
        RunStopsWaiting(typeof(LazyCrucible.AgentProbe), "InFlight", "Teardown");
    }

    private static void RunStopsWaiting(Type owner, string counterField, string teardownName)
    {
        var field = owner.GetField(counterField, BindingFlags.NonPublic | BindingFlags.Static);
        if (field == null)
        {
            Check($"{owner.Name}: an in-flight counter exists for its detours", false);
            return;
        }

        Check($"{owner.Name}: an in-flight counter exists for its detours", true);
        var counter = field.GetValue(null)!;
        var enter = counter.GetType().GetMethod("Enter", Type.EmptyTypes)!;
        var token = enter.Invoke(counter, null)!;
        var release = token.GetType().GetMethod("Dispose", Type.EmptyTypes)!;

        Exception? onThread = null;
        var alive = false;
        var t = new Thread(() =>
        {
            try
            {
                owner.GetMethod(teardownName, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
            }
            catch (Exception ex)
            {
                onThread = ex;
            }
        });
        t.Start();
        Thread.Sleep(150); // teardown reaches its bounded wait; with no wait it has long returned
        alive = t.IsAlive;
        Check($"{owner.Name}.{teardownName}: waits while a detour is in flight", alive);

        release.Invoke(token, null);
        t.Join(5000);
        Check($"{owner.Name}.{teardownName}: returns cleanly once the detour leaves", onThread is null && !t.IsAlive);
    }

    private static void SetVia(Type probeType, object instance, string via)
    {
        var prop = probeType.GetProperty("Via", BindingFlags.Public | BindingFlags.Instance)
            ?? ThrowMissing<PropertyInfo>(probeType, "Via");
        if (prop.CanWrite)
            prop.SetValue(instance, via);
        else
            probeType.GetField("<Via>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(instance, via);
    }

    private static MethodInfo Private(Type owner, string name)
        => owner.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
           ?? ThrowMissing<MethodInfo>(owner, name);

    private static object? Invoke(MethodInfo mi, object?[] args)
    {
        try
        {
            return mi.Invoke(null, args);
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException ?? tie;
        }
    }

    private static string Describe(Exception ex)
        => ex is TargetInvocationException { InnerException: { } inner }
            ? inner.GetType().Name
            : ex.GetType().Name;

    private static T ThrowMissing<T>(Type owner, string name) where T : class
        => throw new MissingMemberException($"{owner.FullName}::{name} not found — module shape changed");

    private static void Check(string what, bool ok)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {what}"
                          + (ok ? "" : " — expected the teardown-safe behaviour described in the header"));
        if (ok) _pass++; else _fail++;
    }
}
