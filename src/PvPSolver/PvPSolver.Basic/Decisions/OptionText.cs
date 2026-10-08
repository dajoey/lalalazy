using System.Globalization;

namespace RotationSolver.Decisions;

/// <summary>What kind of control a rotation option is drawn with.</summary>
public enum OptionKind
{
    /// <summary>A true/false option.</summary>
    Bool,

    /// <summary>A float option with the percent unit: stored as a ratio (0.75), shown as 75%.</summary>
    Percent,

    /// <summary>A float option with any other unit.</summary>
    Float,

    /// <summary>A whole-number option.</summary>
    Int,

    /// <summary>A text option.</summary>
    Text,
}

/// <summary>
///     Fork-only decision core (no Dalamud or game types, compiles standalone): how the settings window reads and
///     writes the option strings of a job's rotation, so the new window and the legacy window agree on every file.
///     The option store is text. The legacy window writes numbers with the CURRENT culture's <c>ToString()</c>
///     (a comma-decimal locale stores "0,75") and reads them with the current culture's <c>TryParse</c>; the rotation
///     itself applies them with <c>bool.Parse</c> and <c>Convert.ChangeType</c> (also the current culture). Every
///     method here takes an optional culture (null = the current culture) so a test can run the same text through
///     two cultures, and the window passes none.
/// </summary>
public static class OptionText
{
    private const NumberStyles FloatStyles = NumberStyles.Float | NumberStyles.AllowThousands;
    private const NumberStyles IntStyles = NumberStyles.Integer;

    private static IFormatProvider Culture(IFormatProvider? culture) => culture ?? CultureInfo.CurrentCulture;

    /// <summary>The text of a bool option ("True" or "False"), as the legacy window writes it.</summary>
    public static string Write(bool value) => value.ToString();

    /// <summary>The text of a whole-number option, as the legacy window writes it.</summary>
    public static string Write(int value, IFormatProvider? culture = null) => value.ToString(Culture(culture));

    /// <summary>The text of a float option, as the legacy window writes it (shortest round-trip form, culture decimal separator).</summary>
    public static string Write(float value, IFormatProvider? culture = null) => value.ToString(Culture(culture));

    /// <summary>Reads a bool option the way the legacy window does (<c>bool.TryParse</c>).</summary>
    public static bool TryRead(string? text, out bool value) => bool.TryParse(text, out value);

    /// <summary>Reads a whole-number option the way the legacy window does (<c>int.TryParse</c>).</summary>
    public static bool TryRead(string? text, out int value, IFormatProvider? culture = null) =>
        int.TryParse(text, IntStyles, Culture(culture), out value);

    /// <summary>Reads a float option the way the legacy window does (<c>float.TryParse</c>: float style with thousands separators).</summary>
    public static bool TryRead(string? text, out float value, IFormatProvider? culture = null) =>
        float.TryParse(text, FloatStyles, Culture(culture), out value);

    /// <summary>The whole percent (75 for 0.75) shown on the slider; rounds half away from zero.</summary>
    public static int ToPoints(float ratio) => (int)MathF.Round(ratio * 100f, MidpointRounding.AwayFromZero);

    /// <summary>
    ///     The ratio for a slider position, clamped to [<paramref name="minRatio"/>, <paramref name="maxRatio"/>].
    ///     Divides the way the legacy slider does (<c>displayValue / 100</c> in float), so 75 becomes exactly 0.75f.
    /// </summary>
    public static float FromPoints(int points, float minRatio = 0f, float maxRatio = 1f)
    {
        int low = ToPoints(minRatio);
        int high = ToPoints(maxRatio);
        int clamped = Math.Clamp(points, low, Math.Max(low, high));
        return Math.Clamp(clamped / 100f, minRatio, maxRatio);
    }

    /// <summary>A whole-number option clamped into its range.</summary>
    public static int ClampInt(int value, int min, int max) => Math.Clamp(value, min, Math.Max(min, max));

    /// <summary>A float option clamped into its range; NaN and infinity fall back to <paramref name="fallback"/>.</summary>
    public static float ClampFloat(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, Math.Max(min, max)) : fallback;

    /// <summary>
    ///     Whether two stored texts mean the same value for a kind: parsed when both parse (floats within 1e-6),
    ///     otherwise the texts must be equal. Used for "is this the default" and the per-row reset button.
    /// </summary>
    public static bool SameValue(OptionKind kind, string? a, string? b, IFormatProvider? culture = null)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        switch (kind)
        {
            case OptionKind.Bool:
                return TryRead(a, out bool ba) && TryRead(b, out bool bb) && ba == bb;
            case OptionKind.Int:
                return TryRead(a, out int ia, culture) && TryRead(b, out int ib, culture) && ia == ib;
            case OptionKind.Percent:
            case OptionKind.Float:
                return TryRead(a, out float fa, culture) && TryRead(b, out float fb, culture) && MathF.Abs(fa - fb) <= 1e-6f;
            default:
                return false;
        }
    }

    /// <summary>The "(default 75%)" text shown next to an option, from the default's stored text.</summary>
    public static string DefaultLabel(OptionKind kind, string? defaultText, IFormatProvider? culture = null)
    {
        switch (kind)
        {
            case OptionKind.Bool:
                return TryRead(defaultText, out bool on) ? (on ? "(default on)" : "(default off)") : "(default unknown)";
            case OptionKind.Percent:
                return TryRead(defaultText, out float ratio, culture)
                    ? "(default " + ToPoints(ratio).ToString(CultureInfo.InvariantCulture) + "%)"
                    : "(default unknown)";
            case OptionKind.Float:
            case OptionKind.Int:
                return string.IsNullOrEmpty(defaultText) ? "(default unknown)" : "(default " + defaultText + ")";
            default:
                return string.IsNullOrEmpty(defaultText) ? "(default empty)" : "(default " + defaultText + ")";
        }
    }

    /// <summary>
    ///     Whether a child option is shown, the way the legacy window decides it: no parent found shows the child; a
    ///     bool parent shows it while the parent is on; any other parent shows it while the parent's text equals the
    ///     required value (ignoring case and surrounding spaces). No required value shows the child.
    /// </summary>
    public static bool ChildVisible(bool parentFound, bool parentIsBool, string? parentText, string? requiredValue)
    {
        if (!parentFound)
        {
            return true;
        }

        if (parentIsBool)
        {
            return bool.TryParse(parentText, out bool on) && on;
        }

        if (requiredValue == null)
        {
            return true;
        }

        return parentText != null && string.Equals(parentText.Trim(), requiredValue.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
