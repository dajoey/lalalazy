using System.Globalization;
using Lalalazy.Hub;

namespace LazyHub.Core;

/// <summary>How a control's value reads in the hub window, and what a - or + click does to it. No Dalamud types.</summary>
public static class ControlFormat
{
    /// <summary>Text for the value cell of a row. A dash means "no value to show"; a button has none.</summary>
    public static string ValueText(ParsedControl control, ParsedState? state)
    {
        if (control.Kind == ControlKind.Button) return "";
        if (state?.Value == null) return "-";

        switch (control.Kind)
        {
            case ControlKind.Toggle:
                return state.Value is bool on ? (on ? "ON" : "OFF") : "-";

            case ControlKind.Stepper:
                return state.Value is double d
                    ? d.ToString("F" + Math.Max(0, Math.Min(6, control.Decimals)), CultureInfo.InvariantCulture) + control.Unit
                    : "-";

            case ControlKind.Choice:
                if (state.Value is not double idx) return "-";
                var i = (int)idx;
                return i >= 0 && i < control.Choices.Count ? control.Choices[i] : "-";

            default:
                return "-";
        }
    }

    /// <summary>The value after one - (direction -1) or + (direction +1) click, kept inside the range.</summary>
    public static double StepperNext(ParsedControl control, double current, int direction)
    {
        var next = current + Math.Sign(direction) * control.Step;
        if (next > control.Max) next = control.Max;
        if (next < control.Min) next = control.Min;
        return Math.Round(next, 4);   // 0.15 + 0.05 must read 0.2, not 0.20000000000000001
    }

    /// <summary>The choice index after one click on the previous or next button; wraps around.</summary>
    public static int ChoiceNext(ParsedControl control, double current, int direction)
    {
        var n = control.Choices.Count;
        if (n == 0) return 0;
        var idx = (int)current;
        return (((idx + Math.Sign(direction)) % n) + n) % n;
    }
}
