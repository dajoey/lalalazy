#nullable enable
using System;
using System.Text.Json;

namespace Lalalazy.Hub;

/// <summary>Turns the JSON value the hub sent into the exact value a setter may receive, or refuses it.</summary>
internal static class ValueCoercion
{
    public static bool TryCoerce(ControlDef def, JsonElement json, out object? value, out string why)
    {
        value = null;
        why = "";

        switch (def.Kind)
        {
            case ControlKind.Toggle:
                if (json.ValueKind == JsonValueKind.True) { value = true; return true; }
                if (json.ValueKind == JsonValueKind.False) { value = false; return true; }
                why = "a toggle needs true or false";
                return false;

            case ControlKind.Stepper:
            {
                if (json.ValueKind != JsonValueKind.Number || !json.TryGetDouble(out var x) || double.IsNaN(x) || double.IsInfinity(x))
                {
                    why = "a stepper needs a number";
                    return false;
                }

                if (x < def.Min) x = def.Min;
                if (x > def.Max) x = def.Max;

                // Snap to the nearest multiple of the step (so a stored 60 stays 60 with step 5), then keep
                // the result inside the range: the ends of the range always stay reachable.
                var snapped = Math.Round(x / def.Step) * def.Step;
                if (snapped < def.Min) snapped = def.Min;
                if (snapped > def.Max) snapped = def.Max;

                value = Math.Round(snapped, Math.Max(0, Math.Min(6, def.Decimals)));
                return true;
            }

            case ControlKind.Choice:
            {
                if (json.ValueKind != JsonValueKind.Number || !json.TryGetInt32(out var index))
                {
                    why = "a choice needs a whole number index";
                    return false;
                }

                // A fractional number such as 1.5 does not parse as Int32, so it is refused above.
                if (index < 0 || index >= def.Choices.Count)
                {
                    why = "that choice does not exist";
                    return false;
                }

                value = index;
                return true;
            }

            default:
                why = "this control takes no value";
                return false;
        }
    }
}
