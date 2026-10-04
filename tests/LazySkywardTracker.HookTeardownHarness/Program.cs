using System.Reflection;
using System.Runtime.CompilerServices;

namespace LazySkywardTracker.HookTeardownHarness;

/// <summary>
///     Offline proof that a Dalamud hot-reload of LazySkywardTracker can no longer crash the game
///     through a hook detour that runs after (or while) its hook is being torn down (crash family
///     of 2026-08-31 and 2026-10-03: a managed exception escaping a detour the game had already
///     entered, while the plugin pulled the hook out from under it).
///     The detour is invoked with the hook null — exactly the state teardown leaves behind while
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
        Console.WriteLine("-- LazySkywardTracker hook teardown: detour after Dispose --");

        CaseA();
        CaseB();

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    // Case A: the state after teardown — hook null — must not throw out of the detour, and the
    // progress payload must not run against a torn-down hook.
    private static unsafe void CaseA()
    {
        Console.WriteLine("-- case A: detour invoked with the hook null (post-teardown state) --");

        var plugin = Lst();
        var detour = Private(plugin, "ReceiveAchievementProgressDetour");

        // The plugin's constructor wires Dalamud services; the uninitialized object is exactly the
        // torn-down state (hook field null) the game can still be inside a detour of.
        var instance = RuntimeHelpers.GetUninitializedObject(plugin);
        var achievementType = detour.GetParameters()[0].ParameterType;

        // A tracked achievement id: with the hook gone nothing may run at all, and the progress
        // cache must stay untouched.
        var trackedId = TrackedAchievementId(plugin);
        var cacheBefore = CacheSize(plugin);

        Exception? ex = null;
        try
        {
            Invoke(detour, instance, [Pointer.Box((void*)0, achievementType), trackedId, 1u, 2u]);
        }
        catch (Exception e)
        {
            ex = e;
        }

        Check($"Plugin.ReceiveAchievementProgressDetour: no exception with hook null"
              + (ex == null ? "" : $" [{Describe(ex)}]"), ex is null);
        Check("Plugin.ReceiveAchievementProgressDetour: the progress payload does not run when the hook is gone",
            ex is null && CacheSize(plugin) == cacheBefore);
    }

    // Case B: the hook teardown must wait (bounded) for a detour that is still in flight before
    // disabling the hook. The counter is held the way a running detour holds it.
    private static void CaseB()
    {
        Console.WriteLine("-- case B: teardown waits for an in-flight detour --");
        var plugin = Lst();
        var instance = RuntimeHelpers.GetUninitializedObject(plugin);
        RunStopsWaiting(plugin, instance, "LazySkywardTracker.ReceiveAchievementProgress", "DisposeAchievementHook");
    }

    private static uint TrackedAchievementId(Type plugin)
    {
        var dict = plugin.GetField("SkywardAchievements", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var keys = dict.GetType().GetProperty("Keys")!.GetValue(dict)!;
        var enumerator = keys.GetType().GetMethod("GetEnumerator")!.Invoke(keys, null)!;
        var moveNext = enumerator.GetType().GetMethod("MoveNext")!;
        var current = enumerator.GetType().GetProperty("Current")!;
        if (moveNext.Invoke(enumerator, null) is true)
            return (uint)current.GetValue(enumerator)!;
        throw new InvalidOperationException("no tracked achievement id found");
    }

    private static int CacheSize(Type plugin)
    {
        var cache = plugin.GetField("ProgressCache", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        return (int)cache.GetType().GetProperty("Count")!.GetValue(cache)!;
    }

    private static void RunStopsWaiting(Type owner, object target, string moduleName, string teardownName)
    {
        var field = owner.GetField("InFlight", BindingFlags.NonPublic | BindingFlags.Static);
        if (field == null)
        {
            Check($"{moduleName}: an in-flight counter exists for its detour", false);
            return;
        }

        Check($"{moduleName}: an in-flight counter exists for its detour", true);
        var counter = field.GetValue(null)!;
        var enter = counter.GetType().GetMethod("Enter", Type.EmptyTypes)!;
        var token = enter.Invoke(counter, null)!;
        var release = token.GetType().GetMethod("Dispose", Type.EmptyTypes)!;

        var teardown = owner.GetMethod(teardownName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (teardown == null)
        {
            Check($"{moduleName}.{teardownName}: the draining teardown exists", false);
            return;
        }

        Exception? onThread = null;
        var alive = false;
        var t = new Thread(() =>
        {
            try
            {
                teardown.Invoke(target, null);
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

    // The hook modules are internal to the plugin assembly; reflection reaches them by name.
    private static Type Lst()
        => Type.GetType("LazySkywardTracker.Plugin, LazySkywardTracker")
           ?? throw new MissingMemberException("LazySkywardTracker.Plugin not found");

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
