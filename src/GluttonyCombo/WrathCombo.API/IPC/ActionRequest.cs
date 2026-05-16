#region

using FFXIVClientStructs.FFXIV.Client.Game;

#pragma warning disable CS0618 // Obsoletes here are likely ours, for consumers

#endregion

namespace WrathCombo.API;

public static partial class WrathIPCWrapper
{
    public static void RequestBlacklist
        (ActionType actionType, uint actionID, int timeMs) =>
        SafeInvokeRawMethod(() =>
            RawMethod.RequestBlacklist.InvokeAction(actionType, actionID, timeMs));

    public static void RequestBlacklist
        (ActionType actionType, uint actionID, TimeSpan timeMs) =>
        SafeInvokeRawMethod(() =>
            RawMethod.RequestBlacklist
                .InvokeAction(actionType, actionID,
                    (int)timeMs.TotalMilliseconds));

    public static void RequestActionUse
        (ActionType actionType, uint actionID, int timeMs, bool? isGcd) =>
        SafeInvokeRawMethod(() =>
            RawMethod.RequestActionUse
                .InvokeAction(actionType, actionID, timeMs, isGcd));

    public static void RequestActionUse
        (ActionType actionType, uint actionID, TimeSpan timeMs, bool? isGcd) =>
        SafeInvokeRawMethod(() =>
            RawMethod.RequestActionUse
                .InvokeAction(actionType, actionID,
                    (int)timeMs.TotalMilliseconds, isGcd));

    public static float GetArtificialCooldown
        (ActionType actionType, uint actionID) =>
        SafeInvokeRawMethod(() =>
            RawMethod.GetArtificialCooldown.InvokeFunc(actionType, actionID));

    public static void ResetBlacklist
        (ActionType actionType, uint actionID) =>
        SafeInvokeRawMethod(() =>
            RawMethod.ResetBlacklist.InvokeAction(actionType, actionID));

    public static void ResetRequest
        (ActionType actionType, uint actionID) =>
        SafeInvokeRawMethod(() =>
            RawMethod.ResetRequest.InvokeAction(actionType, actionID));

    public static void ResetAllBlacklists() =>
        SafeInvokeRawMethod(() =>
            RawMethod.ResetAllBlacklists.InvokeAction());

    public static void ResetAllRequests() =>
        SafeInvokeRawMethod(() =>
            RawMethod.ResetAllRequests.InvokeAction());
}