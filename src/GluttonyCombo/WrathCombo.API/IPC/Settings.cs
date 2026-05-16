#region

using WrathCombo.API.Enum;

#pragma warning disable CS0618 // Obsoletes here are likely ours, for consumers

#endregion

namespace WrathCombo.API;

public static partial class WrathIPCWrapper
{
    public static void Test() =>
        SafeInvokeRawMethod(() =>
            RawMethod.Test.InvokeAction());

    public static bool IPCReady() =>
        SafeInvokeRawMethod(() =>
            RawMethod.IPCReady.InvokeFunc());

    #region Normal Flow

    public static Guid? RegisterForLease
        (string internalPluginName, string pluginName) =>
        SafeInvokeRawMethod(() =>
            RawMethod.RegisterForLease.InvokeFunc(internalPluginName, pluginName));

    public static Guid? RegisterForLeaseWithCallback
    (string internalPluginName, string pluginName,
        string? ipcPrefixForCallback) =>
        SafeInvokeRawMethod(() =>
            RawMethod.RegisterForLeaseWithCallback
                .InvokeFunc(internalPluginName, pluginName, ipcPrefixForCallback));

    public static bool GetAutoRotationState() =>
        SafeInvokeRawMethod(() =>
            RawMethod.GetAutoRotationState.InvokeFunc());

    public static SetResult SetAutoRotationState(Guid lease, bool enabled = true) =>
        SafeInvokeRawMethod(() =>
            RawMethod.SetAutoRotationState.InvokeFunc(lease, enabled));

    public static bool IsCurrentJobAutoRotationReady() =>
        SafeInvokeRawMethod(() =>
            RawMethod.IsCurrentJobAutoRotationReady.InvokeFunc());

    public static SetResult SetCurrentJobAutoRotationReady(Guid lease) =>
        SafeInvokeRawMethod(() =>
            RawMethod.SetCurrentJobAutoRotationReady.InvokeFunc(lease));

    public static void ReleaseControl(Guid lease) =>
        SafeInvokeRawMethod(() =>
            RawMethod.ReleaseControl.InvokeAction(lease));

    #endregion

    #region Extra Job State Checks

    public static Dictionary<ComboTargetTypeKeys, ComboSimplicityLevelKeys?>
        IsCurrentJobConfiguredOn() =>
        SafeInvokeRawMethod(() =>
            RawMethod.IsCurrentJobConfiguredOn.InvokeFunc()) ??
        new Dictionary<ComboTargetTypeKeys, ComboSimplicityLevelKeys?>
        {
            { ComboTargetTypeKeys.SingleTarget, null },
            { ComboTargetTypeKeys.MultiTarget, null },
        };

    public static Dictionary<ComboTargetTypeKeys, ComboSimplicityLevelKeys?>
        IsCurrentJobAutoModeOn() =>
        SafeInvokeRawMethod(() =>
            RawMethod.IsCurrentJobAutoModeOn.InvokeFunc()) ??
        new Dictionary<ComboTargetTypeKeys, ComboSimplicityLevelKeys?>
        {
            { ComboTargetTypeKeys.SingleTarget, null },
            { ComboTargetTypeKeys.MultiTarget, null },
        };

    #endregion

    #region Fine-Grained Combo Methods

    public static List<string>? GetComboNamesForJob(uint jobID) =>
        SafeInvokeRawMethod(() =>
            RawMethod.GetComboNamesForJob.InvokeFunc(jobID));

    public static Dictionary<string, List<string>>?
        GetComboOptionNamesForJob(uint jobID) =>
        SafeInvokeRawMethod(() =>
            RawMethod.GetComboOptionNamesForJob.InvokeFunc(jobID));

    public static Dictionary<ComboStateKeys, bool>?
        GetComboState(string comboInternalName) =>
        SafeInvokeRawMethod(() =>
            RawMethod.GetComboState.InvokeFunc(comboInternalName));

    public static SetResult SetComboState
    (Guid lease, string comboInternalName,
        bool comboState = true, bool autoState = true) =>
        SafeInvokeRawMethod(() =>
            RawMethod.SetComboState
                .InvokeFunc(lease, comboInternalName, comboState, autoState));

    public static bool GetComboOptionState(string optionName) =>
        SafeInvokeRawMethod(() =>
            RawMethod.GetComboOptionState.InvokeFunc(optionName));

    public static SetResult SetComboOptionState
        (Guid lease, string optionName, bool comboState = true) =>
        SafeInvokeRawMethod(() =>
            RawMethod.SetComboOptionState
                .InvokeFunc(lease, optionName, comboState));

    #endregion
}