#region

using Dalamud.Plugin.Ipc;

// ReSharper disable MemberHidesStaticFromOuterClass
// ReSharper disable RedundantSuppressNullableWarningExpression
// ReSharper disable StaticMemberInitializerReferesToMemberBelow

#endregion

namespace WrathCombo.API;

public static partial class WrathIPCWrapper
{
    public partial class RawMethod
    {
        [Obsolete("Use WrathIPCWrapper.CanWeave instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<float?, bool>
            CanWeave =
                Interface!.GetIpcSubscriber<float?, bool>
                    ($"{WC}CanWeave");

        [Obsolete("Use WrathIPCWrapper.CanDelayedWeave instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<float?, float?, bool>
            CanDelayedWeave =
                Interface!.GetIpcSubscriber<float?, float?, bool>
                    ($"{WC}CanDelayedWeave");

        [Obsolete("Use WrathIPCWrapper.ActionReady instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<uint, bool?, bool?, bool>
            ActionReady =
                Interface!.GetIpcSubscriber<uint, bool?, bool?, bool>
                    ($"{WC}ActionReady");
    }
}