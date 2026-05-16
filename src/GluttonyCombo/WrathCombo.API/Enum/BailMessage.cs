#region

using System.ComponentModel;

#endregion

namespace WrathCombo.API.Enum;

public enum BailMessage
{
    [Description("IPC services are currently disabled.")]
    LiveDisabled = 0,

    [Description("Invalid lease.")]
    InvalidLease = 1,

    [Description("Blacklisted lease.")]
    BlacklistedLease = 2,

    [Description("Not enough configurations available.")]
    NotEnoughConfigurations = 3,
}