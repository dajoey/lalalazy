#region

using Dalamud.Plugin.Ipc;

// ReSharper disable MemberHidesStaticFromOuterClass
// ReSharper disable RedundantSuppressNullableWarningExpression
// ReSharper disable StaticMemberInitializerReferesToMemberBelow

#endregion

namespace GluttonyCombo.API;

public static partial class WrathIPCWrapper
{
    public partial class RawMethod
    {
        [Obsolete("Use WrathIPCWrapper.GetUpcomingPositionalHint instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<uint[]?>
            GetUpcomingPositionalHint =
                Interface!.GetIpcSubscriber<uint[]?>
                    ($"{WC}GetUpcomingPositionalHint");

        /// <remarks>
        ///     Fires when the upcoming positional hint changes. Query
        ///     <see cref="GetUpcomingPositionalHint" /> for the latest snapshot.
        /// </remarks>
        [Obsolete("Use WrathIPCWrapper.SubscribeUpcomingPositionalHint instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<object>
            OnUpcomingPositionalHint =
                Interface!.GetIpcSubscriber<object>
                    ("OnUpcomingPositionalHint");
    }
}
