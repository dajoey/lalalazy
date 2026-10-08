// Fakes for the real ObjectFunctions.cs compiled into this harness (see the csproj header).
//
// Surface inventory: everything ObjectFunctions.cs names outside its own two files. The namespaces
// must match the real ones exactly so the unchanged source compiles against these types instead of
// Dalamud's. EzDelegate.Get hands back a delegate whose invocation records into GuardHarness and
// returns GuardHarness.NativeAnswer - the "native" nameplate-colour function never runs, so a case
// can prove the production code did NOT call it (the crash 20261008_124827 was that call made with
// a zero address).

namespace Dalamud.Game.ClientState.Objects.SubKinds
{
    // Placeholder: ObjectFunctions.cs carries this using but uses no type from it.
}

namespace ECommons.Logging
{
    // Placeholder: ObjectFunctions.cs carries this using but uses no type from it.
}

namespace Dalamud.Game.ClientState.Objects.Types
{
    using System;
    using System.Numerics;

    public interface IGameObject
    {
        nint Address { get; }

        uint GameObjectId { get; }

        Vector3 Position { get; }

        float HitboxRadius { get; }

        // Real Dalamud member; ObjectFunctions.cs names it in nameof(IGameObject.IsTargetable).
        bool IsTargetable { get; }
    }

    public interface IBattleNpc
    {
    }

    public interface IEventObj
    {
        nint Address { get; }
    }
}

namespace ECommons.DalamudServices
{
    using System.Collections.Generic;
    using Dalamud.Game.ClientState.Objects.Types;

    public static class Svc
    {
        public static readonly HarnessLog Log = new();
        public static readonly HarnessObjectTable Objects = new();
        public static readonly HarnessPartyList Party = new();
    }

    public class HarnessLog
    {
        public void Debug(string message)
        {
        }

        public void Warning(string message)
        {
        }
    }

    public class HarnessObjectTable : IEnumerable<IGameObject>
    {
        public IGameObject? LocalPlayer { get; set; }

        private readonly List<IGameObject> _objects = new();

        public void Add(IGameObject obj) => _objects.Add(obj);

        public IEnumerator<IGameObject> GetEnumerator() => _objects.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _objects.GetEnumerator();
    }

    public class HarnessPartyMember
    {
        public IGameObject? GameObject { get; set; }
    }

    public class HarnessPartyList : IEnumerable<HarnessPartyMember>
    {
        private readonly List<HarnessPartyMember> _members = new();

        public void Add(HarnessPartyMember member) => _members.Add(member);

        public IEnumerator<HarnessPartyMember> GetEnumerator() => _members.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _members.GetEnumerator();
    }
}

namespace ECommons.EzHookManager
{
    using System;
    using System.Reflection;
    using GluttonyCombo.IsHostileGuardHarness;

    public static class EzDelegate
    {
        public static T Get<T>(string signature) where T : Delegate
        {
            GuardHarness.NativeResolved++;
            var invoke = typeof(T).GetMethod("Invoke")
                ?? throw new InvalidOperationException($"Not a delegate type: {typeof(T)}");
            var parms = invoke.GetParameters();
            if (parms.Length == 1
                && parms[0].ParameterType == typeof(nint)
                && invoke.ReturnType == typeof(byte))
            {
                var recorder = typeof(GuardHarness).GetMethod(
                    nameof(GuardHarness.RecordNameplate),
                    BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("Harness recorder method missing.");
                return (T)Delegate.CreateDelegate(typeof(T), recorder);
            }

            throw new NotSupportedException(
                $"IsHostileGuardHarness fakes only the byte(nint) nameplate delegate; got {typeof(T)}.");
        }
    }
}

namespace FFXIVClientStructs.FFXIV.Client.Game.Object
{
    // Compile-only stand-ins for the structs ObjectFunctions.cs casts raw addresses into. The
    // harness never walks one (the recorder answers the nameplate queries instead).
    public unsafe struct GameObject
    {
        public bool GetIsTargetable() => true;
    }

    public unsafe struct EventObject
    {
    }
}

namespace GluttonyCombo.IsHostileGuardHarness
{
    /// <summary>Shared recorder state for the fake native nameplate delegate.</summary>
    internal static class GuardHarness
    {
        public static int NativeCalls;

        public static int NativeResolved;

        public static byte NativeAnswer;

        internal static byte RecordNameplate(nint address)
        {
            NativeCalls++;
            return NativeAnswer;
        }
    }
}
