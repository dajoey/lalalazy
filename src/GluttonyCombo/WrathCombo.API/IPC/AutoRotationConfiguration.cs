#region

using WrathCombo.API.Enum;
using DPSMode = WrathCombo.API.Enum.DPSRotationMode;
using HealerMode = WrathCombo.API.Enum.HealerRotationMode;

#pragma warning disable CS0618 // Obsoletes here are likely ours, for consumers

#endregion

namespace WrathCombo.API;

public static partial class WrathIPCWrapper
{
    public static object? GetAutoRotationConfigState
        (AutoRotationConfigOption passedOption) =>
        SafeInvokeRawMethod(() =>
            RawMethod.GetAutoRotationConfigState.InvokeFunc(passedOption));

    private static SetResult SetAutoRotationConfigState
        (Guid lease, AutoRotationConfigOption passedOption, object value) =>
        SafeInvokeRawMethod(() =>
            RawMethod.SetAutoRotationConfigState
                .InvokeFunc(lease, passedOption, value));

    public static SetResult SetAutoRotationConfigState
        (Guid lease, AutoRotationConfigOption passedOption, bool value) =>
        SetAutoRotationConfigState(lease, passedOption, (object)value);

    public static SetResult SetAutoRotationConfigState
        (Guid lease, AutoRotationConfigOption passedOption, int value) =>
        SetAutoRotationConfigState(lease, passedOption, (object)value);

    public static SetResult SetAutoRotationConfigState
        (Guid lease, AutoRotationConfigOption passedOption, DPSMode value) =>
        SetAutoRotationConfigState(lease, passedOption, (object)value);

    public static SetResult SetAutoRotationConfigState
        (Guid lease, AutoRotationConfigOption passedOption, HealerMode value) =>
        SafeInvokeRawMethod(() =>
            RawMethod.SetAutoRotationConfigState
                .InvokeFunc(lease, passedOption, value));
}