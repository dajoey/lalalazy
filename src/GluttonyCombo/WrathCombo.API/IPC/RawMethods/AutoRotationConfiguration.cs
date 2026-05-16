#region

using Dalamud.Plugin.Ipc;
using WrathCombo.API.Enum;

// ReSharper disable MemberHidesStaticFromOuterClass
// ReSharper disable RedundantSuppressNullableWarningExpression
// ReSharper disable StaticMemberInitializerReferesToMemberBelow

#endregion

namespace WrathCombo.API;

public static partial class WrathIPCWrapper
{
    public partial class RawMethod
    {
        [Obsolete("Use WrathIPCWrapper.GetAutoRotationConfigState instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<object, object?>
            GetAutoRotationConfigState =
                Interface!.GetIpcSubscriber<object, object?>
                    ($"{WC}GetAutoRotationConfigState");

        [Obsolete("Use Typed WrathIPCWrapper.SetAutoRotationConfigState instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<Guid, object, object, SetResult>
            SetAutoRotationConfigState =
                Interface!.GetIpcSubscriber<Guid, object, object, SetResult>
                    ($"{WC}SetAutoRotationConfigState");
    }
}