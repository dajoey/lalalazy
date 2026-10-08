using System.Reflection;
using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Round 3, Part A, item 7: the fork-only settings file <c>Configs.PvpSettings.cs</c>. T2: plain properties only
///     (none of the three generator attributes), the master switch defaults to true, the stored record has nullable
///     Enabled and Percent, <c>Configs.CurrentVersion</c> is still 12 and the main file defines no member of the same
///     name. T1-by-reflection on the real <c>Configs</c> class from PvPSolver.Basic (it needs no game): the
///     accessors apply the row defaults, clamp to the floor, honour the master switch, store sparsely, and the JSON
///     round trip writes only what was set and reads files that lack the keys.
/// </summary>
internal static class PvpSettingsCases
{
    private const string ConfigsType = "RotationSolver.Basic.Configuration.Configs";
    private const string TableType = "RotationSolver.Decisions.DefensiveTable";

    private static object Row(string field) => BasicAssembly.Type(TableType).GetField(field, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private static object RowByName(string actionName) =>
        ((System.Collections.IEnumerable)BasicAssembly.Type(TableType).GetField("Rows", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!)
        .Cast<object>().Single(r => (string)r.GetType().GetProperty("ActionName")!.GetValue(r)! == actionName);

    private static object Call(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance)!.Invoke(target, args)!;

    private static object? Prop(object target, string name) => target.GetType().GetProperty(name)!.GetValue(target);

    private static Type Json => Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", throwOnError: true)!;

    private static string Serialize(object value) =>
        (string)Json.GetMethods().Single(m => m.Name == "SerializeObject" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(object)).Invoke(null, [value])!;

    private static object Deserialize(string text, Type type) =>
        Json.GetMethods().Single(m => m.Name == "DeserializeObject" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string) && m.GetParameters()[1].ParameterType == typeof(Type))
            .Invoke(null, [text, type])!;

    public static void Run()
    {
        Console.WriteLine("-- round 3 Part A, item 7: Configs.PvpSettings (T2 + T1 by reflection) --");

        // ---- T2 --------------------------------------------------------------------------------------------------
        var dir = Path.Combine(Program.SrcRoot, "PvPSolver.Basic", "Configuration");
        var settingsPath = Path.Combine(dir, "Configs.PvpSettings.cs");
        var settingsExists = File.Exists(settingsPath);
        Harness.Case("Configs.PvpSettings.cs exists", settingsExists);
        var raw = settingsExists ? File.ReadAllText(settingsPath) : "";
        var san = CsSource.Sanitize(raw);
        Harness.Case("the file declares `internal partial class Configs` and uses none of [ConditionBool], [JobConfig], [JobChoiceConfig]",
            Regex.IsMatch(san, @"\binternal\s+partial\s+class\s+Configs\b") && !Regex.IsMatch(san, @"\b(ConditionBool|JobConfig|JobChoiceConfig)\b"));
        Harness.Case("PvpDefensivesMaster is a plain bool property defaulting to true",
            Regex.IsMatch(san, @"public\s+bool\s+PvpDefensivesMaster\s*\{\s*get;\s*set;\s*\}\s*=\s*true\s*;"));
        Harness.Case("PvpDefensiveSettings maps the action id as text to a record with nullable Enabled and Percent",
            Regex.IsMatch(san, @"public\s+ConcurrentDictionary<string,\s*PvpDefensiveSetting>\s+PvpDefensiveSettings\s*\{\s*get;\s*set;\s*\}")
            && Regex.IsMatch(san, @"record\s+PvpDefensiveSetting\s*\(.*?bool\?\s+Enabled\s*=\s*null.*?float\?\s+Percent\s*=\s*null", RegexOptions.Singleline));
        Harness.Case("null members of a stored setting are not written (NullValueHandling.Ignore on both)",
            Regex.Matches(raw, @"\[property: JsonProperty\(NullValueHandling = NullValueHandling\.Ignore\)\]").Count == 2);
        var main = CsSource.Sanitize(File.ReadAllText(Path.Combine(dir, "Configs.cs")));
        Harness.Case("Configs.CurrentVersion is still 12 (a mismatch resets all settings)", Regex.IsMatch(main, @"public\s+const\s+int\s+CurrentVersion\s*=\s*12\s*;"));
        var names = new[] { "PvpDefensivesMaster", "PvpDefensiveSettings", "DefensiveEnabled", "DefensivePercent", "DefensivePercentPoints", "DefensiveActive", "SetDefensiveEnabled", "SetDefensivePercent" };
        var clashes = names.Where(n => Regex.IsMatch(main, @"\b" + n + @"\b")).ToArray();
        Harness.Case("the main Configs file defines none of the new member names", clashes.Length == 0, string.Join(",", clashes));

        try
        {
            RunReflection(raw);
        }
        catch (Exception e)
        {
            var inner = e is TargetInvocationException { InnerException: { } i } ? i : e;
            Harness.Case("the Configs accessors and JSON round trip run against the real class", false, inner.GetType().Name + ": " + inner.Message);
        }
    }

    private static void RunReflection(string raw)
    {
        // ---- T1 by reflection on the real class ----------------------------------------------------------------------
        object cfg;
        try
        {
            cfg = Activator.CreateInstance(BasicAssembly.Type(ConfigsType), nonPublic: true)!;
        }
        catch (Exception e)
        {
            Harness.Case("Configs can be constructed offline (reflection)", false, (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message);
            return;
        }

        Harness.Case("Configs can be constructed offline (reflection)", true);
        var rampart = RowByName("RampartPvP");
        var recuperate = Row("Recuperate");
        var heart = Row("HeartOfCorundum");
        var impalement = Row("Impalement");

        bool Active(object row) => (bool)Call(cfg, "DefensiveActive", row);
        bool Enabled(object row) => (bool)Call(cfg, "DefensiveEnabled", row);
        float Percent(object row) => (float)Call(cfg, "DefensivePercent", row);
        float Points(object row) => (float)Call(cfg, "DefensivePercentPoints", row);
        var dict = () => (System.Collections.IDictionary)Prop(cfg, "PvpDefensiveSettings")!;

        var allRows = ((System.Collections.IEnumerable)BasicAssembly.Type(TableType).GetField("Rows")!.GetValue(null)!).Cast<object>().ToArray();
        Harness.Case("defaults: the master switch is on, the store is empty, every Fire and Gate row is off, every Param row is on",
            (bool)Prop(cfg, "PvpDefensivesMaster")! && dict().Count == 0 && allRows.Length == 20
            && allRows.All(r => Active(r) == (Prop(r, "Mode")!.ToString() == "Param")));
        Harness.Case("defaults: percent is the row default; the 0-100 reads are exactly 30 and 60",
            Percent(rampart) == 0.60f && Percent(recuperate) == 0.60f && Points(heart) == 30f && Points(impalement) == 60f);

        Call(cfg, "SetDefensiveEnabled", rampart, true);
        Call(cfg, "SetDefensivePercent", rampart, 0.4f);
        Harness.Case("a stored enabled flag and percent are returned", Active(rampart) && Percent(rampart) == 0.4f && dict().Count == 1);
        Call(cfg, "SetDefensivePercent", recuperate, 0.2f);
        Harness.Case("a percent below the Recuperate floor is stored and read as 0.50", Percent(recuperate) == 0.50f && !Active(recuperate));
        Call(cfg, "SetDefensivePercent", recuperate, float.NaN);
        Harness.Case("a NaN percent clears the stored percent (back to the default)", Percent(recuperate) == 0.60f && dict().Count == 1);

        cfg.GetType().GetProperty("PvpDefensivesMaster")!.SetValue(cfg, false);
        Harness.Case("master off: no row is in force (Param rows too) while the row flags themselves are unchanged",
            !Active(rampart) && Enabled(rampart) && !Active(heart) && Enabled(heart));
        cfg.GetType().GetProperty("PvpDefensivesMaster")!.SetValue(cfg, true);

        // ---- JSON round trip, sparse ---------------------------------------------------------------------------------
        var json = Serialize(cfg);
        var entry = Regex.Match(json, "\"43244\"\\s*:\\s*\\{(?<body>[^}]*)\\}");
        var body = entry.Success ? entry.Groups["body"].Value : "";
        Harness.Case("JSON: only the changed row is written, with only the members that were set",
            entry.Success && Regex.IsMatch(body, "\"Enabled\"\\s*:\\s*true") && Regex.IsMatch(body, "\"Percent\"\\s*:\\s*0\\.4") && !body.Contains("null") && !json.Contains("\"29711\""),
            entry.Success ? body.Trim() : "no entry");
        Call(cfg, "SetDefensiveEnabled", rampart, null);
        var percentOnly = Serialize(cfg);
        var onlyPercent = Regex.Match(percentOnly, "\"43244\"\\s*:\\s*\\{(?<body>[^}]*)\\}");
        Harness.Case("JSON: a row with only a percent set writes no Enabled member (no null)", onlyPercent.Success && !onlyPercent.Groups["body"].Value.Contains("Enabled") && onlyPercent.Groups["body"].Value.Contains("Percent"),
            onlyPercent.Success ? onlyPercent.Groups["body"].Value.Trim() : "no entry");
        Call(cfg, "SetDefensivePercent", rampart, null);
        Harness.Case("clearing both members removes the row (the store is sparse again)", dict().Count == 0 && !Regex.IsMatch(Serialize(cfg), "\"43244\""));

        Call(cfg, "SetDefensiveEnabled", rampart, true);
        Call(cfg, "SetDefensivePercent", rampart, 0.45f);
        Call(cfg, "SetDefensiveEnabled", heart, false);
        var back = Deserialize(Serialize(cfg), BasicAssembly.Type(ConfigsType));
        bool BackActive(object row) => (bool)Call(back, "DefensiveActive", row);
        Harness.Case("JSON round trip: stored enabled flags and percents survive, the version is still 12",
            BackActive(rampart) && (float)Call(back, "DefensivePercent", rampart) == 0.45f && !BackActive(heart) && (bool)Call(back, "DefensiveEnabled", heart) == false && (int)Prop(back, "Version")! == 12);
        var fresh = Deserialize("{\"Version\": 12}", BasicAssembly.Type(ConfigsType));
        Harness.Case("a saved file without the new keys loads with the defaults (master on, Param rows on, Fire rows off)",
            (bool)Prop(fresh, "PvpDefensivesMaster")! && (bool)Call(fresh, "DefensiveActive", heart) && !(bool)Call(fresh, "DefensiveActive", rampart) && ((System.Collections.IDictionary)Prop(fresh, "PvpDefensiveSettings")!).Count == 0);
        var unknown = Deserialize("{\"Version\": 12, \"PvpDefensiveSettings\": {\"43244\": {\"Percent\": 0.3, \"SomethingNew\": 1}}, \"AKeyFromTheFuture\": true}", BasicAssembly.Type(ConfigsType));
        Harness.Case("unknown keys in the file are ignored, a stored percent below the floor is clamped on read",
            (float)Call(unknown, "DefensivePercent", rampart) == 0.3f && !(bool)Call(unknown, "DefensiveActive", rampart));

        // ---- canaries ----------------------------------------------------------------------------------------------
        Harness.Canary("a JSON writer that emits nulls passes the sparse check", !"\"Enabled\": null, \"Percent\": 0.4".Contains("null"));
        Harness.Canary("a settings file that uses [JobConfig] passes the plain-properties check",
            !Regex.IsMatch(CsSource.Sanitize(raw + "\n[JobConfig] private readonly bool _x = false;"), @"\b(ConditionBool|JobConfig|JobChoiceConfig)\b"));
    }
}
