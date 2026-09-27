namespace GluttonyCombo.Services.IPC;

/// <summary>
///     Pure, Dalamud-free decision gate for IPC lease suspension.
///     Eliminates spurious warning emissions on job changes and plugin teardown
///     when no leases are registered, and ensures that active lease suspension
///     is logged at Debug rather than Warning.
/// </summary>
public static class LeaseSuspensionGate
{
    public enum LogLevel
    {
        Debug,
        Warning,
        Error
    }

    /// <summary>
    ///     Determines whether lease suspension should proceed.
    ///     When no leases are registered, suspension is a silent no-op.
    /// </summary>
    public static bool ShouldSuspend(int activeLeaseCount) => activeLeaseCount > 0;

    /// <summary>
    ///     The appropriate log level for lease suspension.
    ///     Suspending leases on job change or teardown is normal lifecycle cleanup,
    ///     not an unexpected failure or warning condition.
    /// </summary>
    public static LogLevel SuspensionLogLevel => LogLevel.Debug;
}
