#region

using System.ComponentModel;

#endregion

namespace WrathCombo.API.Enum;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable InconsistentNaming
public enum SetResult
{
    [Description("A default value that shouldn't ever be seen.")]
    IGNORED = -1,

    [Description("The configuration was set successfully.")]
    Okay = 0,

    [Description("The configuration will be set, it is working asynchronously.")]
    OkayWorking = 1,

    [Description("IPC services are currently disabled.")]
    IPCDisabled = 10,

    [Description("Invalid lease.")]
    InvalidLease = 11,

    [Description("Blacklisted lease.")]
    BlacklistedLease = 12,

    [Description("Configuration you are trying to set is already set.")]
    Duplicate = 13,

    [Description("Player object is not available.")]
    PlayerNotAvailable = 14,

    [Description("The configuration you are trying to set is not available.")]
    InvalidConfiguration = 15,

    [Description("The value you are trying to set is invalid.")]
    InvalidValue = 16,
}