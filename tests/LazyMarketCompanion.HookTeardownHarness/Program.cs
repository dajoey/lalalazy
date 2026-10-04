using System.Reflection;

namespace LazyMarketCompanion.HookTeardownHarness;

/// <summary>
///     Offline proof that a Dalamud hot-reload of LazyMarketCompanion can no longer crash the game
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
        Console.WriteLine("-- LazyMarketCompanion hook teardown: detour after Dispose --");

        CaseA();
        CaseB();

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    // Case A: the state after teardown — hook null — must not throw out of the detour.
    private static void CaseA()
    {
        Console.WriteLine("-- case A: detour invoked with the hook null (post-teardown state) --");

        var plugin = Lmc();
        var detour = Private(plugin, "RetainerItemCommandDetour");
        var commandType = detour.GetParameters()[4].ParameterType;
        var inventoryType = detour.GetParameters()[2].ParameterType;

        Exception? ex = null;
        try
        {
            Invoke(detour, [(nint)0, 1u, Enum.GetValues(inventoryType)!.GetValue(0)!, 0u, Enum.GetValues(commandType)!.GetValue(0)!]);
        }
        catch (Exception e)
        {
            ex = e;
        }

        Check($"Plugin.RetainerItemCommandDetour: no exception with hook null"
              + (ex == null ? "" : $" [{Describe(ex)}]"), ex is null);
    }

    // Case B: the hook teardown must wait (bounded) for a detour that is still in flight before
    // disposing the hook. The counter is held the way a running detour holds it.
    private static void CaseB()
    {
        Console.WriteLine("-- case B: teardown waits for an in-flight detour --");
        var plugin = Lmc();
        RunStopsWaiting(plugin, "LazyMarketCompanion.RetainerItemCommand", "DisposeRetainerItemCommandHook");
    }

    private static void RunStopsWaiting(Type owner, string moduleName, string teardownName)
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

        var teardown = owner.GetMethod(teardownName, BindingFlags.NonPublic | BindingFlags.Static);
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
                teardown.Invoke(null, null);
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
    private static Type Lmc()
        => Type.GetType("LazyMarketCompanion.Plugin, LazyMarketCompanion")
           ?? throw new MissingMemberException("LazyMarketCompanion.Plugin not found");

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
