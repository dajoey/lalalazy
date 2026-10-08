using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     T2 shape shared by the optional "cleanse other players" actions (Bard Warden's Paean, White Mage Aquaveil).
///     Their <c>TargetStatusNeed</c> is the Purify-able afflictions, and with <c>StatusFromSelf</c> at its default
///     (true) only afflictions the caster applied are counted, so an ally's debuff is never found. The fix is
///     <c>setting.StatusFromSelf = false;</c>. Its only other consumers are <c>StatusNeed</c> and
///     <c>StatusProvide</c>, which these actions do not set, and the option that uses it is off by default.
/// </summary>
internal static class AllyCleanse
{
    public static void Check(string label, string basicFile, string method, string rotationFile, string option)
    {
        var path = Path.Combine(Program.SrcRoot, "PvPSolver.Basic", "Rotations", "Basic", basicFile);
        var san = CsSource.Sanitize(File.ReadAllText(path));
        var head = new Regex(@"\bstatic\s+partial\s+void\s+(?<name>" + method + @")\s*\(");
        var m = CsSource.Methods(san, basicFile, head).SingleOrDefault();
        Harness.Case($"{basicFile} has {method}", m != null);
        if (m == null) return;

        var body = CsSource.Squash(m.Body);
        Harness.Case($"{label}: {method} sets StatusFromSelf = false", Regex.IsMatch(body, @"setting\.StatusFromSelf\s*=\s*false\s*;"), body);
        Harness.Case($"{label}: still needs the Purify-able target statuses, friendly, Dispel target type",
            body.Contains("setting.TargetStatusNeed = StatusHelper.PurifyPvPStatuses;")
            && body.Contains("setting.IsFriendly = true;")
            && body.Contains("setting.TargetType = TargetType.Dispel;"), body);
        Harness.Case($"{label}: sets no StatusNeed or StatusProvide, the only other StatusFromSelf consumers",
            !Regex.IsMatch(body, @"setting\.Status(Need|Provide)\s*="), body);

        // Default behaviour: the option that reaches this path is off by default.
        var rot = CsSource.Sanitize(File.ReadAllText(SourceFiles.Rotation(rotationFile).Path));
        Harness.Case($"{label}: option {option} defaults to false",
            Regex.IsMatch(rot, @"public\s+bool\s+" + option + @"\s*\{\s*get;\s*set;\s*\}\s*=\s*false\s*;"), rotationFile);

        // Canary: the old method text must not be accepted as setting the flag.
        const string old = "setting.TargetStatusNeed = StatusHelper.PurifyPvPStatuses; setting.IsFriendly = true; setting.TargetType = TargetType.Dispel;";
        Harness.Canary($"{label}: old {method} text is accepted as setting StatusFromSelf = false",
            Regex.IsMatch(old, @"setting\.StatusFromSelf\s*=\s*false\s*;"));
    }
}
