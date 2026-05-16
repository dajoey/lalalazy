#region

using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;

// ReSharper disable MemberHidesStaticFromOuterClass
// ReSharper disable RedundantSuppressNullableWarningExpression
// ReSharper disable StaticMemberInitializerReferesToMemberBelow

#endregion

namespace WrathCombo.API;

public static partial class WrathIPCWrapper
{
    public partial class RawMethod
    {
        /// <remarks>
        ///     Use
        ///     <see cref="Dalamud.Plugin.Ipc.ICallGateSubscriber{T1,TRet}.InvokeAction(T1)">
        ///         .InvokeAction()
        ///     </see>
        ///     , <see langword="void" /> return
        /// </remarks>
        [Obsolete("Use WrathIPCWrapper.RequestBlacklist instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<ActionType, uint, int, object>
            RequestBlacklist =
                Interface!.GetIpcSubscriber<ActionType, uint, int, object>
                    ($"{WC}{AR}RequestBlacklist");

        /// <remarks>
        ///     Use
        ///     <see cref="Dalamud.Plugin.Ipc.ICallGateSubscriber{T1,TRet}.InvokeAction(T1)">
        ///         .InvokeAction()
        ///     </see>
        ///     , <see langword="void" /> return
        /// </remarks>
        [Obsolete("Use WrathIPCWrapper.ResetBlacklist instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<ActionType, uint, object>
            ResetBlacklist =
                Interface!.GetIpcSubscriber<ActionType, uint, object>
                    ($"{WC}{AR}ResetBlacklist");

        /// <remarks>
        ///     Use
        ///     <see cref="Dalamud.Plugin.Ipc.ICallGateSubscriber{T1,TRet}.InvokeAction(T1)">
        ///         .InvokeAction()
        ///     </see>
        ///     , <see langword="void" /> return
        /// </remarks>
        [Obsolete("Use WrathIPCWrapper.ResetAllBlacklists instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<object>
            ResetAllBlacklists =
                Interface!.GetIpcSubscriber<object>
                    ($"{WC}{AR}ResetAllBlacklist");

        [Obsolete("Use WrathIPCWrapper.GetArtificialCooldown instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<ActionType, uint, float>
            GetArtificialCooldown =
                Interface!.GetIpcSubscriber<ActionType, uint, float>
                    ($"{WC}{AR}GetArtificialCooldown");

        /// <remarks>
        ///     Use
        ///     <see cref="Dalamud.Plugin.Ipc.ICallGateSubscriber{T1,TRet}.InvokeAction(T1)">
        ///         .InvokeAction()
        ///     </see>
        ///     , <see langword="void" /> return
        /// </remarks>
        [Obsolete("Use WrathIPCWrapper.RequestActionUse instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<ActionType, uint, int, bool?, object>
            RequestActionUse =
                Interface!.GetIpcSubscriber<ActionType, uint, int, bool?, object>
                    ($"{WC}{AR}RequestActionUse");

        /// <remarks>
        ///     Use
        ///     <see cref="Dalamud.Plugin.Ipc.ICallGateSubscriber{T1,TRet}.InvokeAction(T1)">
        ///         .InvokeAction()
        ///     </see>
        ///     , <see langword="void" /> return
        /// </remarks>
        [Obsolete("Use WrathIPCWrapper.ResetRequest instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<ActionType, uint, object>
            ResetRequest =
                Interface!.GetIpcSubscriber<ActionType, uint, object>
                    ($"{WC}{AR}ResetRequest");

        /// <remarks>
        ///     Use
        ///     <see cref="Dalamud.Plugin.Ipc.ICallGateSubscriber{T1,TRet}.InvokeAction(T1)">
        ///         .InvokeAction()
        ///     </see>
        ///     , <see langword="void" /> return
        /// </remarks>
        [Obsolete("Use WrathIPCWrapper.ResetAllRequests instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<object>
            ResetAllRequests =
                Interface!.GetIpcSubscriber<object>
                    ($"{WC}{AR}ResetAllRequests");
    }
}