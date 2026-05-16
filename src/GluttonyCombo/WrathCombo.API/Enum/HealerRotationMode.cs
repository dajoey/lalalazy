namespace WrathCombo.API.Enum;

// ReSharper disable UnusedMember.Global
// ReSharper disable InconsistentNaming
/// <summary>
///     Rotation targeting mode for healers.
/// </summary>
public enum HealerRotationMode
{
    Manual          = 0,
    Highest_Current = 1,
    Lowest_Current  = 2,
    //Self_Priority,
    //Tank_Priority,
    //Healer_Priority,
    //DPS_Priority,
}