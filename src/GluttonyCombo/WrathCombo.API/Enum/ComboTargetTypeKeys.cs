namespace WrathCombo.API.Enum;

/// <summary>
///     The keys for the types of target a combo is designed for, Single-Target or
///     Multi-Target.
/// </summary>
public enum ComboTargetTypeKeys
{
    /// <summary>
    ///     The key for conveying data about the Single-Target portion of a job
    ///     configuration.
    /// </summary>
    SingleTarget = 0,

    /// <summary>
    ///     The key for conveying data about the Multi-Target portion of a job
    ///     configuration.
    /// </summary>
    MultiTarget = 1,

    /// <summary>
    ///     The key for conveying data about the Single-Target combo for a healer.
    /// </summary>
    HealST = 2,

    /// <summary>
    ///     The key for conveying data about the Multi-Target combo for a healer.
    /// </summary>
    HealMT = 3,

    /// <summary>
    ///     The key for a combo that is not specified as single or multi-target.
    /// </summary>
    Other = 4,
}