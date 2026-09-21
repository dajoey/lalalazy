#region

#pragma warning disable CS0618

#endregion

namespace GluttonyCombo.API;

public static partial class WrathIPCWrapper
{
    /// <summary>
    ///     Gets the current upcoming positional hint snapshot, if any.
    /// </summary>
    public static PositionalHintSnapshot? GetUpcomingPositionalHint()
    {
        var wire = SafeInvokeRawMethod(() =>
            RawMethod.GetUpcomingPositionalHint.InvokeFunc());

        return PositionalHintSnapshot.TryFromWire(wire, out var snapshot)
            ? snapshot
            : null;
    }

    /// <summary>
    ///     Subscribes to positional-hint change notifications.
    /// </summary>
    public static void SubscribeUpcomingPositionalHint(Action callback) =>
        SafeInvokeRawMethod(() =>
            RawMethod.OnUpcomingPositionalHint.Subscribe(callback));

    /// <summary>
    ///     Unsubscribes from positional-hint change notifications.
    /// </summary>
    public static void UnsubscribeUpcomingPositionalHint(Action callback) =>
        SafeInvokeRawMethod(() =>
            RawMethod.OnUpcomingPositionalHint.Unsubscribe(callback));
}
