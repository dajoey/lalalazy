using System.Diagnostics;
using System.Reflection;

namespace GluttonyCombo.HookTeardownHarness;

/// <summary>
///     Offline proof that a Dalamud hot-reload of GluttonyCombo can no longer crash the game through
///     an ECommons hook detour that runs after (or while) its hook is being disposed
///     (game crash 2026-10-03 19:54 EDT: managed NullReferenceException escaping a detour called
///     from the game's per-frame VfxObject.Update, during the plugin's own Dispose()).
///     The detours are invoked with the hook statics null — exactly the state Dispose() leaves
///     behind while the game may still be inside a detour. No game, no Dalamud services: any
///     reliance on Svc inside the fail-soft path also fails here.
/// </summary>
internal static class Program
{
    private const int BlockMs = 400;

    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        Console.WriteLine("-- GluttonyCombo / ECommons hook teardown: detour after Dispose --");

        CaseA();
        CaseB();

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    // Case A: the state after Dispose() — hook statics null — must not throw out of any detour.
    private static void CaseA()
    {
        Console.WriteLine("-- case A: detours invoked with hook statics null (post-Dispose state) --");

        CheckNoThrow(typeof(ECommons.Hooks.StaticVfx), "StaticVfxRunDetour", [null!, 0f, 0]);
        CheckNoThrow(typeof(ECommons.Hooks.StaticVfx), "StaticVfxDtorDetour", [null!]);
        CheckNoThrow(typeof(ECommons.Hooks.StaticVfx), "StaticVfxCreateDetour", [null!, null!]);
        CheckNoThrow(typeof(ECommons.Hooks.ActorVfx), "ActorVfxDtorDetour", [(nint)0]);
        CheckNoThrow(typeof(ECommons.Hooks.ActorVfx), "ActorVfxCreateDetour", [(nint)0, (nint)0, (nint)0, 0f, (byte)0, (ushort)0, (byte)0]);

        // The ctor detour's ABI result is `this`; with the hook gone it must return its argument.
        var ctor = Detour(typeof(ECommons.Hooks.GameObjectCtor), "GameObjectConstructorDetour");
        Exception? ex = null;
        object? ret = null;
        try
        {
            ret = Invoke(ctor, [(nint)0x1234]);
        }
        catch (Exception e)
        {
            ex = e;
        }

        Check($"GameObjectConstructorDetour: no exception with hook null"
              + (ex == null ? "" : $" [{Describe(ex)}]"), ex is null);
        Check("GameObjectConstructorDetour: returns the object address when the hook is gone",
            ex is null && ret is nint v && v == 0x1234);
    }

    // Case B: Dispose() must wait (bounded) for a detour that is still running before
    // disposing and nulling the hooks, and the detour must return cleanly afterwards.
    private static void CaseB()
    {
        Console.WriteLine("-- case B: Dispose() waits for an in-flight detour --");
        RunDisposeWaits(typeof(ECommons.Hooks.StaticVfx), "StaticVfxRunDetour", "_staticVfxRunEvent",
            "Run", "Dispose", [null!, 0f, 0]);
        RunDisposeWaits(typeof(ECommons.Hooks.ActorVfx), "ActorVfxDtorDetour", "_actorVfxDtorEvent",
            "Run1", "Dispose", [(nint)0]);
    }

    private static void RunDisposeWaits(Type owner, string detourName, string eventField,
        string blockerMethod, string disposeName, object?[] detourArgs)
    {
        var entered = new ManualResetEvent(false);
        var release = new ManualResetEvent(false);
        var field = owner.GetField(eventField, BindingFlags.NonPublic | BindingFlags.Static)
            ?? ThrowMissing<FieldInfo>(owner, eventField);
        var delegateType = field.FieldType;

        var blocker = new Blocker(entered, release);
        var subscriber = Delegate.CreateDelegate(delegateType, blocker,
            blockerMethod);
        field.SetValue(null, subscriber);
        try
        {
            Exception? onThread = null;
            var t = new Thread(() =>
            {
                try
                {
                    Invoke(Detour(owner, detourName), detourArgs);
                }
                catch (Exception ex)
                {
                    onThread = ex;
                }
            });
            t.Start();
            if (!entered.WaitOne(5000))
            {
                Check($"{owner.Name}.{detourName}: the blocking subscriber was reached", false);
                release.Set();
                t.Join();
                return;
            }

            var sw = Stopwatch.StartNew();
            var dispose = owner.GetMethod(disposeName, BindingFlags.Public | BindingFlags.Static)
                ?? ThrowMissing<MethodInfo>(owner, disposeName);
            Exception? disposeEx = null;
            try
            {
                dispose.Invoke(null, null);
            }
            catch (Exception ex)
            {
                disposeEx = ex;
            }

            var waitedWhileBlocked = sw.ElapsedMilliseconds >= BlockMs / 2;
            Check($"{owner.Name}.{disposeName}: waits for the in-flight detour ({sw.ElapsedMilliseconds} ms while blocked)",
                waitedWhileBlocked);

            release.Set();
            t.Join(5000);
            Check($"{owner.Name}.{detourName}: returns cleanly once released", onThread is null);
            Check($"{owner.Name}.{disposeName}: does not throw", disposeEx is null);
        }
        finally
        {
            field.SetValue(null, null);
        }
    }

    /// <summary>A subscriber whose matching instance method blocks until released; bound to the
    /// module's private callback-delegate type by reflection so the real detour fires it.</summary>
    private sealed class Blocker(ManualResetEvent entered, ManualResetEvent release)
    {
        public void Run(nint a, float b, uint c)
        {
            entered.Set();
            release.WaitOne(5000);
        }

        public void Run1(nint a)
        {
            entered.Set();
            release.WaitOne(5000);
        }
    }

    private static void CheckNoThrow(Type owner, string detourName, object?[] args)
    {
        Exception? ex = null;
        try
        {
            Invoke(Detour(owner, detourName), args);
        }
        catch (Exception e)
        {
            ex = e;
        }

        Check($"{owner.Name}.{detourName}: no exception with hook null"
              + (ex == null ? "" : $" [{Describe(ex)}]"), ex is null);
    }

    private static string Describe(Exception ex)
        => ex is TargetInvocationException { InnerException: { } inner }
            ? inner.GetType().Name
            : ex.GetType().Name;

    private static MethodInfo Detour(Type owner, string name)
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

    private static T ThrowMissing<T>(Type owner, string name) where T : class
        => throw new MissingMemberException($"{owner.FullName}::{name} not found — module shape changed");

    private static void Check(string what, bool ok)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {what}"
                          + (ok ? "" : " — expected the teardown-safe behaviour described in the header"));
        if (ok) _pass++; else _fail++;
    }
}
