#region

#pragma warning disable CS0618 // Obsoletes here are likely ours, for consumers

#endregion

namespace WrathCombo.API;

public static partial class WrathIPCWrapper
{
    /// <summary>
    ///     Checks if an action can be used currently without clipping a GCD.
    /// </summary>
    /// <param name="estimatedWeaveTime">
    ///     The animation-lock time associated with the action to be used.<br />
    ///     If <see langword="null" />, then the default animation-lock time is used.
    /// </param>
    /// <returns>
    ///     Whether an action with the weave time specified can be used currently.
    /// </returns>
    /// <exception cref="Error.UninitializedException">
    ///     When <see cref="Init">InitWrapper()</see> has not been called.
    /// </exception>
    /// <exception cref="Error.APIBehindException">
    ///     When this package needs updating.
    /// </exception>
    /// <exception cref="Error.IPCException">
    ///     When there was a
    ///     <see cref="Dalamud.Plugin.Ipc.Exceptions.IpcError">Dalamud IPC Error</see>
    ///     thrown.
    /// </exception>
    /// <exception cref="Error.UnexpectedException">
    ///     When WrathCombo.IPC needs updating.
    /// </exception>
    /// <remarks>
    ///     Respects the user's Setting in Wrath Combo for the maximum number of
    ///     weaves allowed per window. Can return <see langword="false" /> even if
    ///     there is plenty of time to weave, if the user has set a lower number of
    ///     allowed weaves.
    /// </remarks>
    public static bool CanWeave(float? estimatedWeaveTime = null) =>
        SafeInvokeRawMethod(() => RawMethod.CanWeave.InvokeFunc(estimatedWeaveTime));

    public static bool CanDelayedWeave(float? weaveStart, float? weaveEnd) =>
        SafeInvokeRawMethod(() =>
            RawMethod.CanDelayedWeave.InvokeFunc(weaveStart, weaveEnd));

    public static bool
        ActionReady(uint actionId, bool? recastCheck, bool? castCheck) =>
        SafeInvokeRawMethod(() =>
            RawMethod.ActionReady.InvokeFunc(actionId, recastCheck, castCheck));
}