namespace WrathCombo.API.Enum;

/// <summary>
///     Rotation targeting mode for DPS.
/// </summary>
public enum DPSRotationMode
{
    Manual          = 0,
    Highest_Max     = 1,
    Lowest_Max      = 2,
    Highest_Current = 3,
    Lowest_Current  = 4,
    Tank_Target     = 5,
    Nearest         = 6,
    Furthest        = 7,
}