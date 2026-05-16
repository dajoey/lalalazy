#region

using System.ComponentModel;

#endregion

namespace WrathCombo.API.Enum;

public enum CancellationReason
{
    [Description("The Wrath user manually elected to revoke your lease.")]
    WrathUserManuallyCancelled = 0,

    [Description(
        "Your plugin was detected as having been disabled, not that you're likely to see this.")]
    LeaseePluginDisabled = 1,

    [Description("The Wrath plugin is being disabled.")]
    WrathPluginDisabled = 2,

    [Description(
        "Your lease was released by IPC call, theoretically this was done by you.")]
    LeaseeReleased = 3,

    [Description(
        "IPC Services have been disabled remotely. Please see the commit history for /res/ipc_status.txt. https://github.com/PunishXIV/WrathCombo/commits/main/res/ipc_status.txt")]
    AllServicesSuspended = 4,

    [Description(
        "Player job has been changed and leases will have to be reapplied.")]
    JobChanged = 5,
}