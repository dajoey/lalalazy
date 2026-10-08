namespace RotationSolver.Decisions;

/// <summary>What the settings window's top strip shows and allows for the on/off state.</summary>
/// <param name="StatusText">"Now: ON" or "Now: OFF".</param>
/// <param name="Reason">One plain sentence for the state, or "" when there is nothing to explain.</param>
/// <param name="ButtonLabel">"Turn on" or "Turn off".</param>
/// <param name="ButtonEnabled">Whether the button can be used now.</param>
/// <param name="ButtonWhy">Why the button is disabled ("" when it is enabled).</param>
public sealed record SwitchView(string StatusText, string Reason, string ButtonLabel, bool ButtonEnabled, string ButtonWhy);

/// <summary>
///     Fork-only decision core (no Dalamud or game types, compiles standalone): the one zone-dependent control of the
///     settings window. Turning PvP Solver on is allowed only in a PvP zone with a character in the world; turning it
///     off is allowed anywhere (the same rule as the Lazy Hub master toggle).
/// </summary>
public static class SwitchStatus
{
    /// <summary>The wording for a state.</summary>
    /// <param name="isOn">Whether PvP Solver is on.</param>
    /// <param name="inPvpZone">Whether the character is in a PvP zone.</param>
    /// <param name="playerAvailable">Whether a character is in the world.</param>
    public static SwitchView Describe(bool isOn, bool inPvpZone, bool playerAvailable)
    {
        if (isOn)
        {
            return new SwitchView("Now: ON", "PvP Solver is acting for the character.", "Turn off", true, string.Empty);
        }

        if (!playerAvailable)
        {
            return new SwitchView("Now: OFF", "No character is in the world yet. Settings can still be changed.", "Turn on", false, "Only available with a character in the world.");
        }

        if (!inPvpZone)
        {
            return new SwitchView("Now: OFF", "Not in a PvP zone. PvP Solver turns on in PvP zones; settings can be changed anywhere.", "Turn on", false, "Only available in a PvP zone.");
        }

        return new SwitchView("Now: OFF", "In a PvP zone. PvP Solver is ready to be turned on.", "Turn on", true, string.Empty);
    }
}
