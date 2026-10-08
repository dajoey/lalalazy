// Fakes for the real OccultInstantCast.cs compiled into this harness (see the csproj header).
//
// Namespaces match the real ones so the unchanged source compiles against these types instead of
// Dalamud's / FFXIVClientStructs'. FakeGame is the one switchboard a case drives: which statuses the
// player holds, which actions were "just used", whether a cast bar is running, and which Phantom job
// the Occult Crescent state says is equipped.

using System.Runtime.InteropServices;

namespace Dalamud.Plugin.Services
{
    public interface IFramework
    {
    }
}

namespace GluttonyCombo.PhantomDualcastHarness
{
    public static class FakeGame
    {
        public static readonly HashSet<uint> PlayerStatuses = new();
        public static readonly HashSet<uint> JustUsedActions = new();
        public static bool PlayerAvailable = true;
        public static float TotalCastTime;
        public static float CurrentCastTime;

        /// <summary>null = not in Occult Crescent (no content instance).</summary>
        public static byte? SupportJob;

        public static void Reset()
        {
            PlayerStatuses.Clear();
            JustUsedActions.Clear();
            PlayerAvailable = true;
            TotalCastTime = 0;
            CurrentCastTime = 0;
            SupportJob = null;
        }
    }

    public sealed class FakePlayerObject
    {
        public float TotalCastTime => FakeGame.TotalCastTime;

        public float CurrentCastTime => FakeGame.CurrentCastTime;
    }
}

namespace ECommons.GameHelpers
{
    public static class Player
    {
        private static readonly GluttonyCombo.PhantomDualcastHarness.FakePlayerObject Obj = new();

        public static bool Available => GluttonyCombo.PhantomDualcastHarness.FakeGame.PlayerAvailable;

        public static GluttonyCombo.PhantomDualcastHarness.FakePlayerObject Object => Obj;
    }
}

namespace FFXIVClientStructs.FFXIV.Client.Game.InstanceContent
{
    public struct OccultCrescentState
    {
        public byte CurrentSupportJob;
    }

    public unsafe struct PublicContentOccultCrescent
    {
        public OccultCrescentState State;

        private static PublicContentOccultCrescent* _instance =
            (PublicContentOccultCrescent*)NativeMemory.AllocZeroed((nuint)sizeof(PublicContentOccultCrescent));

        public static PublicContentOccultCrescent* GetInstance()
        {
            var job = GluttonyCombo.PhantomDualcastHarness.FakeGame.SupportJob;
            if (job is null)
                return null;
            _instance->State.CurrentSupportJob = job.Value;
            return _instance;
        }
    }
}

namespace GluttonyCombo.CustomComboNS.Functions
{
    /// <summary>The slice of the real CustomComboFunctions that OccultInstantCast.cs calls.</summary>
    internal abstract partial class CustomComboFunctions
    {
        public static bool HasStatusEffect(uint effect) =>
            PhantomDualcastHarness.FakeGame.PlayerStatuses.Contains(effect);

        public static bool JustUsed(uint action) =>
            PhantomDualcastHarness.FakeGame.JustUsedActions.Contains(action);
    }
}
