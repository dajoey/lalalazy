using System;
using System.Linq;
using System.Reflection;
using Dalamud.Game.ClientState.Keys;
using ECommons.DalamudServices;

namespace LazyOccultCrescent.Data;

// Detects the player taking the wheel.
//
// vnavmesh drives by feeding movement input, and it follows a waypoint list it
// computed once. Nothing in the plugin ever asked whether the *player* was also
// steering, so walking away from an automated route just meant vnavmesh kept
// aiming at the next waypoint of a now-stale path - which reads in game as the
// character stubbornly turning round and marching back to where you left off.
//
// Detecting input rather than movement is the important part: the character is
// always "moving" while vnavmesh drives it, so a position delta cannot tell the
// two apart. Keyboard and gamepad are both checked because gamepad users would
// otherwise get no yield at all.
public static class ManualControl
{
    // Synced from PathfinderConfig by PathfinderModule.
    public static bool Enabled { get; set; } = true;

    private readonly static VirtualKey[] MoveKeys =
    [
        VirtualKey.W, VirtualKey.A, VirtualKey.S, VirtualKey.D,
        VirtualKey.UP, VirtualKey.DOWN, VirtualKey.LEFT, VirtualKey.RIGHT,
    ];

    // Stick drift is real; anything under this is not a deliberate input.
    private const float StickDeadzone = 0.25f;

    private static DateTime lastInput = DateTime.MinValue;

    public static bool IsInputHeld()
    {
        foreach (var key in MoveKeys)
        {
            try
            {
                if (Svc.KeyState[key])
                {
                    return true;
                }
            }
            catch
            {
                // KeyState throws for keys Dalamud is not tracking; not fatal.
            }
        }

        // Gamepad: bound by reflection rather than compiled against a fixed
        // property name. Dalamud has moved this API around between versions, and
        // a stick-axis rename should degrade to "keyboard still works" rather
        // than failing the build.
        try
        {
            if (GamepadAxisMagnitude() > StickDeadzone)
            {
                return true;
            }
        }
        catch
        {
            // No gamepad service, or an API shape we do not recognise.
        }

        return false;
    }

    // Call once per tick from anything that wants to yield.
    public static bool Poll()
    {
        if (!Enabled || !IsInputHeld())
        {
            return false;
        }

        lastInput = DateTime.UtcNow;
        return true;
    }

    // True once the player has stopped steering for long enough that resuming
    // will not immediately fight them again.
    public static bool HasSettled(TimeSpan grace)
    {
        return DateTime.UtcNow - lastInput >= grace;
    }

    public static TimeSpan SinceLastInput
    {
        get => DateTime.UtcNow - lastInput;
    }

    public static void Reset()
    {
        lastInput = DateTime.MinValue;
    }

    private static PropertyInfo[]? stickProps;

    private static float GamepadAxisMagnitude()
    {
        var pad = Svc.GamepadState;
        if (pad == null)
        {
            return 0f;
        }

        stickProps ??= pad.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(float) && p.Name.Contains("LeftStick", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var magnitude = 0f;
        foreach (var prop in stickProps)
        {
            var value = prop.GetValue(pad);
            if (value is float f)
            {
                magnitude = MathF.Max(magnitude, MathF.Abs(f));
            }
        }

        return magnitude;
    }
}
