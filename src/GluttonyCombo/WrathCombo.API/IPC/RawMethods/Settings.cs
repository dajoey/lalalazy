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
        /// <summary>
        ///     Meant to throw on failure to test that IPC calls to Wrath Combo
        ///     are truly going through.
        /// </summary>
        /// <remarks>
        ///     Use
        ///     <see cref="Dalamud.Plugin.Ipc.ICallGateSubscriber{T1,TRet}.InvokeAction(T1)">
        ///         .InvokeAction()
        ///     </see>
        ///     , <see langword="void" /> return
        /// </remarks>
        [Obsolete("Use WrathIPCWrapper.Test instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<object>
            Test =
                Interface!.GetIpcSubscriber<object>
                    ($"{WC}Test");

        [Obsolete("Use WrathIPCWrapper.IPCReady instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<bool>
            IPCReady =
                Interface!.GetIpcSubscriber<bool>
                    ($"{WC}IPCReady");

        #region Normal Flow

        [Obsolete("Use WrathIPCWrapper.RegisterForLease instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<string, string, Guid?>
            RegisterForLease =
                Interface!.GetIpcSubscriber<string, string, Guid?>
                    ($"{WC}RegisterForLease");

        [Obsolete("Use WrathIPCWrapper.RegisterForLeaseWithCallback instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<string, string, string?, Guid?>
            RegisterForLeaseWithCallback =
                Interface!.GetIpcSubscriber<string, string, string?, Guid?>
                    ($"{WC}RegisterForLeaseWithCallback");

        [Obsolete("Use WrathIPCWrapper.GetAutoRotationState instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<bool>
            GetAutoRotationState =
                Interface!.GetIpcSubscriber<bool>
                    ($"{WC}GetAutoRotationState");

        [Obsolete("Use WrathIPCWrapper.SetAutoRotationState instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<Guid, bool, SetResult>
            SetAutoRotationState =
                Interface!.GetIpcSubscriber<Guid, bool, SetResult>
                    ($"{WC}SetAutoRotationState");

        [Obsolete("Use WrathIPCWrapper.IsCurrentJobAutoRotationReady instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<bool>
            IsCurrentJobAutoRotationReady =
                Interface!.GetIpcSubscriber<bool>
                    ($"{WC}IsCurrentJobAutoRotationReady");

        [Obsolete("Use WrathIPCWrapper.SetCurrentJobAutoRotationReady instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<Guid, SetResult>
            SetCurrentJobAutoRotationReady =
                Interface!.GetIpcSubscriber<Guid, SetResult>
                    ($"{WC}SetCurrentJobAutoRotationReady");

        /// <remarks>
        ///     Use
        ///     <see cref="Dalamud.Plugin.Ipc.ICallGateSubscriber{T1,TRet}.InvokeAction(T1)">
        ///         .InvokeAction()
        ///     </see>
        ///     , <see langword="void" /> return
        /// </remarks>
        [Obsolete("Use WrathIPCWrapper.ReleaseControl instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<Guid, object>
            ReleaseControl =
                Interface!.GetIpcSubscriber<Guid, object>
                    ($"{WC}ReleaseControl");

        #endregion

        #region Extra Job State Checks

        [Obsolete("Use WrathIPCWrapper.IsCurrentJobConfiguredOn instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<
                Dictionary<ComboTargetTypeKeys, ComboSimplicityLevelKeys?>>
            IsCurrentJobConfiguredOn =
                Interface!
                    .GetIpcSubscriber<
                            Dictionary<ComboTargetTypeKeys, ComboSimplicityLevelKeys
                                ?>>
                        ($"{WC}IsCurrentJobConfiguredOn");

        [Obsolete("Use WrathIPCWrapper.IsCurrentJobAutoModeOn instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<
                Dictionary<ComboTargetTypeKeys, ComboSimplicityLevelKeys?>>
            IsCurrentJobAutoModeOn =
                Interface!
                    .GetIpcSubscriber<
                            Dictionary<ComboTargetTypeKeys, ComboSimplicityLevelKeys
                                ?>>
                        ($"{WC}IsCurrentJobAutoModeOn");

        #endregion

        #region Fine-Grained Combo Methods

        [Obsolete("Use WrathIPCWrapper.GetComboNamesForJob instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<uint, List<string>?>
            GetComboNamesForJob =
                Interface!.GetIpcSubscriber<uint, List<string>?>
                    ($"{WC}GetComboNamesForJob");

        [Obsolete("Use WrathIPCWrapper.GetComboOptionNamesForJob instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<uint, Dictionary<string, List<string>>?>
            GetComboOptionNamesForJob =
                Interface!.GetIpcSubscriber<uint, Dictionary<string, List<string>>?>
                    ($"{WC}GetComboOptionNamesForJob");

        [Obsolete("Use WrathIPCWrapper.GetComboState instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<string, Dictionary<ComboStateKeys, bool>?>
            GetComboState =
                Interface!
                    .GetIpcSubscriber<string, Dictionary<ComboStateKeys, bool>?>
                        ($"{WC}GetComboState");

        [Obsolete("Use WrathIPCWrapper.SetComboState instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<Guid, string, bool, bool, SetResult>
            SetComboState =
                Interface!.GetIpcSubscriber<Guid, string, bool, bool, SetResult>
                    ($"{WC}SetComboState");

        [Obsolete("Use WrathIPCWrapper.GetComboOptionState instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<string, bool>
            GetComboOptionState =
                Interface!.GetIpcSubscriber<string, bool>
                    ($"{WC}GetComboOptionState");

        [Obsolete("Use WrathIPCWrapper.SetComboOptionState instead. " +
                  "Will be made internal in 1.1.0.")]
        public static readonly
            ICallGateSubscriber<Guid, string, bool, SetResult>
            SetComboOptionState =
                Interface!.GetIpcSubscriber<Guid, string, bool, SetResult>
                    ($"{WC}SetComboOptionState");

        #endregion
    }
}