using System.Reflection;
using System.Runtime.InteropServices;
using KamiToolKit.Classes.Controllers;

namespace LazyWTMath.HookTeardownHarness;

/// <summary>
///     Offline proof that a Dalamud hot-reload of LazyWTMath (KamiToolKit) can no longer crash the
///     game through a hook detour that runs after (or while) its hook is being torn down (crash
///     family of 2026-08-31 and 2026-10-03: a managed exception escaping a detour the game had
///     already entered, while the plugin pulled the hook out from under it).
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
        Console.WriteLine("-- LazyWTMath (KamiToolKit) hook teardown: detour after teardown --");

        CaseA();
        CaseB();

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    // Case A: the state after teardown — hook null — must not throw out of the detour, and the
    // populate payload must not run against a torn-down hook.
    private static unsafe void CaseA()
    {
        Console.WriteLine("-- case A: detours invoked with the hook null (post-teardown state) --");

        var addonType = Type.GetType("KamiToolKit.NativeAddon, KamiToolKit")
                        ?? throw new MissingMemberException("KamiToolKit.NativeAddon not found");
        var detour = addonType.GetMethod("OnFireCallback", BindingFlags.NonPublic | BindingFlags.Static)
                     ?? ThrowMissing<MethodInfo>(addonType, "OnFireCallback");
        var unitBaseType = detour.GetParameters()[0].ParameterType;

        // A readable, zeroed stand-in AtkUnitBase: the fixed char Name array reads as empty and the
        // detour must fail soft before touching it at all once the hook is gone.
        var unitBase = Marshal.AllocHGlobal(0x1000);
        NativeMemory.Clear((void*)unitBase, 0x1000);
        Exception? ex = null;
        object? ret = null;
        try
        {
            try
            {
                ret = Invoke(detour, null,
                    [Pointer.Box((void*)unitBase, unitBaseType), 0u, null!, false]);
            }
            catch (Exception e)
            {
                ex = e;
            }

            Check($"NativeAddon.OnFireCallback: no exception with hook null"
                  + (ex == null ? "" : $" [{Describe(ex)}]"), ex is null);
            Check("NativeAddon.OnFireCallback: reports the callback as not handled when the hook is gone",
                ex is null && ret is false);
        }
        finally
        {
            Marshal.FreeHGlobal(unitBase);
        }

        var controller = NewController();
        ex = null;
        try
        {
            Invoke(Private(controller.GetType(), "OnPopulateDetour"), controller, [null!, null!, null!]);
        }
        catch (Exception e)
        {
            ex = e;
        }

        Check($"NativeListController.OnPopulateDetour: no exception with hook null"
              + (ex == null ? "" : $" [{Describe(ex)}]"), ex is null);

        ex = null;
        try
        {
            Invoke(Private(controller.GetType(), "OnRendererPopulateDetour"), controller, [null!, 0, null!, null!]);
        }
        catch (Exception e)
        {
            ex = e;
        }

        Check($"NativeListController.OnRendererPopulateDetour: no exception with hook null"
              + (ex == null ? "" : $" [{Describe(ex)}]"), ex is null);
    }

    // Case B: the teardown must wait (bounded) for a detour that is still in flight before disposing
    // the hooks. The counter is held the way a running detour holds it.
    private static void CaseB()
    {
        Console.WriteLine("-- case B: teardown waits for an in-flight detour --");
        var addonType = Type.GetType("KamiToolKit.NativeAddon, KamiToolKit")!;
        RunStopsWaiting(addonType, null, "NativeAddon.CloseCallback", "DisposeCloseCallback", instance: false);

        var controller = NewController();
        RunStopsWaiting(controller.GetType(), controller, "NativeListController", "DisposeHooks", instance: true);
    }

    // The controller is constructible without the game; its handlers throw if the detour payload
    // ever runs with the hook gone (fail soft means: do nothing).
    private static unsafe object NewController()
        => new NativeListController("HarnessAddon")
        {
            ShouldModifyElement = static (unitBase, item, nodes) => throw new InvalidOperationException("payload must not run when the hook is gone"),
            UpdateElement = static (unitBase, item, nodes) => { },
            ResetElement = static (unitBase, item, nodes) => { },
            GetPopulatorNode = static addon => throw new InvalidOperationException("not reached"),
        };

    private static void RunStopsWaiting(Type owner, object? target, string moduleName, string teardownName, bool instance)
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

        var teardown = owner.GetMethod(teardownName,
            BindingFlags.NonPublic | BindingFlags.Public | (instance ? BindingFlags.Instance : BindingFlags.Static));
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
                teardown.Invoke(field.IsStatic ? null : target, null);
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

    private static MethodInfo Private(Type owner, string name)
        => owner.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
           ?? ThrowMissing<MethodInfo>(owner, name);

    private static object? Invoke(MethodInfo mi, object? target, object?[] args)
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
