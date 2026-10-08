using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ECommons.ExcelServices;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Round 3 Part B, T1 by reflection and T4: the job-keyed option store of the real <c>Configs</c> class and a
///     configuration round trip built offline. <c>new Configs()</c> runs without the game, so the cases build one,
///     fill it the way an older build would have (a legacy per-job entry, other settings changed), serialise it with
///     the call the plugin's <c>Configs.Save</c> uses (<c>JsonConvert.SerializeObject(this, Formatting.Indented)</c>),
///     load it back, use the new accessors, and compare the files key by key. When env <c>PVPROT_OLD_BASIC_DLL</c>
///     names a build of the previous release, the same files also cross the version boundary (the old class reads the
///     new file; an untouched file is identical in both builds). Without it that part is reported as NOT RUN.
/// </summary>
internal static class ConfigRoundTripCases
{
    private const string ConfigsType = "RotationSolver.Basic.Configuration.Configs";
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static Type Json(string name) => Type.GetType(name + ", Newtonsoft.Json", throwOnError: true)!;

    private static string Serialize(object cfg)
    {
        Type convert = Json("Newtonsoft.Json.JsonConvert");
        Type formatting = Json("Newtonsoft.Json.Formatting");
        MethodInfo m = convert.GetMethods().Single(x => x.Name == "SerializeObject" && x.GetParameters().Length == 2
            && x.GetParameters()[0].ParameterType == typeof(object) && x.GetParameters()[1].ParameterType == formatting);
        return (string)m.Invoke(null, [cfg, Enum.ToObject(formatting, 1)])!; // Formatting.Indented, as Configs.Save writes
    }

    private static object Load(string json, Type configs)
    {
        MethodInfo m = Json("Newtonsoft.Json.JsonConvert").GetMethods().Single(x => x.Name == "DeserializeObject" && x.GetParameters().Length == 2
            && x.GetParameters()[0].ParameterType == typeof(string) && x.GetParameters()[1].ParameterType == typeof(Type));
        return m.Invoke(null, [json, configs])!;
    }

    private static object NewConfigs(Assembly asm) => Activator.CreateInstance(asm.GetType(ConfigsType, throwOnError: true)!, nonPublic: true)!;

    private static FieldInfo Field(object cfg, string name) =>
        cfg.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new MissingFieldException(cfg.GetType().Name, name);

    private static MethodInfo Method(object cfg, string name) =>
        cfg.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new MissingMethodException(cfg.GetType().Name, name);

    private static IDictionary<string, string>? SettingsFor(object cfg, Job job, bool create) =>
        (IDictionary<string, string>?)Method(cfg, "RotationSettingsFor").Invoke(cfg, [job, create]);

    // The path the previous build's generated RotationConfigurations property reads: _rotationConfigurationsDict[job][choice][name].
    private static string? OldPathRead(object cfg, Job job, string name)
    {
        object dict = Field(cfg, "_rotationConfigurationsDict").GetValue(cfg)!;
        object? byChoice = ((IDictionary)dict).Contains(job) ? ((IDictionary)dict)[job] : null;
        if (byChoice is not IDictionary choices || !choices.Contains(string.Empty))
        {
            return null;
        }

        return choices[string.Empty] is IDictionary<string, string> options && options.TryGetValue(name, out string? value) ? value : null;
    }

    private static Dictionary<string, string> TopLevel(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText());
    }

    public static void Run()
    {
        Console.WriteLine("-- round 3 Part B, T1 by reflection and T4: the job-keyed option store and the configuration round trip --");
        Guarded("job-keyed store", AccessorCases);
        Guarded("configuration round trip", RoundTripCases);
        Guarded("cross-version", CrossVersionCases);
    }

    // One part that throws (for example because the tree under test has no job-keyed accessor) is one FAIL; the other parts still run.
    private static void Guarded(string part, Action run)
    {
        try
        {
            run();
        }
        catch (Exception e)
        {
            Exception inner = e is TargetInvocationException { InnerException: { } i } ? i : e;
            Harness.Case($"the {part} cases ran to the end", false, inner.GetType().Name + ": " + inner.Message);
        }
    }

    // ---- the job-keyed accessor ---------------------------------------------------------------------------------------

    private static void AccessorCases()
    {
        object cfg = NewConfigs(BasicAssembly.Get());
        IDictionary dictRoot = (IDictionary)Field(cfg, "_rotationConfigurationsDict").GetValue(cfg)!;
        object sharedField = Field(cfg, "_rotationConfigurations").GetValue(cfg)!;

        Harness.Case("a read of a job that never stored an option creates nothing and returns null", SettingsFor(cfg, Job.SAM, false) == null && dictRoot.Count == 0);
        IDictionary<string, string> sam = SettingsFor(cfg, Job.SAM, true)!;
        IDictionary<string, string> nin = SettingsFor(cfg, Job.NIN, true)!;
        sam["BloodBathPvPPercent"] = OptionText.Write(0.75f, De);
        nin["BloodBathPvPPercent"] = OptionText.Write(0.4f, De);
        Harness.Case("two jobs get two separate dictionaries: the same option name holds a different value in each, and neither is the shared field",
            !ReferenceEquals(sam, nin) && !ReferenceEquals(sam, sharedField) && !ReferenceEquals(nin, sharedField)
            && SettingsFor(cfg, Job.SAM, false)!["BloodBathPvPPercent"] == "0,75" && SettingsFor(cfg, Job.NIN, false)!["BloodBathPvPPercent"] == "0,4"
            && ((IDictionary<string, string>)sharedField).Count == 0);
        Harness.Case("a read after a create returns the same dictionary, and a third job still reads null", ReferenceEquals(SettingsFor(cfg, Job.SAM, false), sam) && SettingsFor(cfg, Job.VPR, false) == null && dictRoot.Count == 2);
        Harness.Case("the entry sits under the rotation choice \"\" of the job, the key the previous build's generated property uses", OldPathRead(cfg, Job.SAM, "BloodBathPvPPercent") == "0,75" && OldPathRead(cfg, Job.NIN, "BloodBathPvPPercent") == "0,4");

        // A model of the previous accessor: the first access of a job stores the shared field itself, so jobs first touched in a
        // session share one dictionary. The new accessor must differ from it exactly there.
        var field = new Dictionary<string, string>();
        var byJob = new Dictionary<Job, Dictionary<string, Dictionary<string, string>>>();
        Dictionary<string, string> OldGet(Job j)
        {
            if (!byJob.TryGetValue(j, out var d)) { d = byJob[j] = new(); }
            if (!d.TryGetValue(string.Empty, out var v)) { v = d[string.Empty] = field; }
            return v;
        }

        OldGet(Job.SAM)["BloodBathPvPPercent"] = "0.75";
        Harness.Case("RED on old behaviour (a model of the generated getter): jobs first touched in a session share one dictionary, so SAM's value shows up for NIN and RPR",
            OldGet(Job.NIN)["BloodBathPvPPercent"] == "0.75" && OldGet(Job.RPR)["BloodBathPvPPercent"] == "0.75" && ReferenceEquals(OldGet(Job.NIN), OldGet(Job.SAM)));

        Harness.Case("the per-job invincibility flag is stored per job, reads the field default for a job that never stored one and is written to the same dictionary the generated property uses",
            !(bool)Method(cfg, "IgnorePvPInvincibilityFor").Invoke(cfg, [Job.NIN])!
            && Method(cfg, "SetIgnorePvPInvincibilityFor") is { } set && set.Invoke(cfg, [Job.NIN, true]) == null
            && (bool)Method(cfg, "IgnorePvPInvincibilityFor").Invoke(cfg, [Job.NIN])! && !(bool)Method(cfg, "IgnorePvPInvincibilityFor").Invoke(cfg, [Job.SAM])!
            && ((IDictionary)Field(cfg, "_ignorePvPInvincibilityDict").GetValue(cfg)!).Contains(Job.NIN));
        Harness.Canary("a store that hands every job the shared dictionary keeps two jobs apart", !ReferenceEquals(OldGet(Job.WAR), OldGet(Job.DRK)));
    }

    // ---- the round trip -----------------------------------------------------------------------------------------------

    private static void RoundTripCases()
    {
        Assembly asm = BasicAssembly.Get();
        Type configs = asm.GetType(ConfigsType, throwOnError: true)!;
        object pristine = NewConfigs(asm);
        string pristineJson = Serialize(pristine);
        Dictionary<string, string> pristineKeys = TopLevel(pristineJson);
        Harness.Case("a configuration that never touched the new settings writes neither new top-level key (they are written only once set)",
            !pristineKeys.ContainsKey("PvpDefensivesMaster") && !pristineKeys.ContainsKey("PvpDefensiveSettings"), $"{pristineKeys.Count} keys in the untouched file");

        // 1. A configuration an older build wrote: a legacy per-job entry, a per-job flag and a few global settings.
        object legacy = NewConfigs(asm);
        IDictionary legacyRoot = (IDictionary)Field(legacy, "_rotationConfigurationsDict").GetValue(legacy)!;
        var warOptions = new System.Collections.Concurrent.ConcurrentDictionary<string, string> { ["Legacy option"] = "True", ["ShadowbringerThreshold"] = "50" };
        legacyRoot[Job.WAR] = NewChoiceDictionary(legacyRoot, warOptions);
        ((IDictionary)Field(legacy, "_ignorePvPInvincibilityDict").GetValue(legacy)!)[Job.NIN] = true;
        legacy.GetType().GetProperty("HealthForGuard")!.SetValue(legacy, 0.2f);
        string legacyJson = Serialize(legacy);

        // 2. The new build loads it, the player edits SAM's options in a comma-decimal culture and one defensive row, and it saves.
        object loaded = Load(legacyJson, configs);
        Harness.Case("the new build reads a legacy file: the WAR entry, the NIN flag and the Guard value are all found through the new accessors",
            SettingsFor(loaded, Job.WAR, false) is { } war && war["Legacy option"] == "True" && war["ShadowbringerThreshold"] == "50"
            && (bool)Method(loaded, "IgnorePvPInvincibilityFor").Invoke(loaded, [Job.NIN])! && (float)loaded.GetType().GetProperty("HealthForGuard")!.GetValue(loaded)! == 0.2f);
        SettingsFor(loaded, Job.SAM, true)!["BloodBathPvPPercent"] = OptionText.Write(0.75f, De);
        object rampart = DefensiveRow("RampartPvP");
        Method(loaded, "SetDefensiveEnabled").Invoke(loaded, [rampart, true]);
        string savedJson = Serialize(loaded);

        Dictionary<string, string> before = TopLevel(legacyJson);
        Dictionary<string, string> after = TopLevel(savedJson);
        string[] changed = after.Keys.Where(k => !before.TryGetValue(k, out string? old) || old != after[k]).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        string[] removed = before.Keys.Where(k => !after.ContainsKey(k)).ToArray();
        Harness.Case("saving after the edits changes only the two keys that hold them (the option store and the defensive settings); every other key, 'untouched', is byte for byte the same",
            removed.Length == 0 && changed.SequenceEqual(["PvpDefensiveSettings", "_rotationConfigurationsDict"]), $"{before.Count} keys; changed: {string.Join(",", changed)}");

        using JsonDocument beforeDoc = JsonDocument.Parse(before["_rotationConfigurationsDict"]);
        using JsonDocument afterDoc = JsonDocument.Parse(after["_rotationConfigurationsDict"]);
        Harness.Case("inside the option store the legacy WAR entry is byte for byte the same and only a SAM entry was added",
            beforeDoc.RootElement.GetProperty("WAR").GetRawText() == afterDoc.RootElement.GetProperty("WAR").GetRawText()
            && afterDoc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(["SAM", "WAR"]));

        object reloaded = Load(savedJson, configs);
        Harness.Case("the saved file loads back: SAM's comma-decimal text reads as 0.75 in that culture, the legacy entries survive, the defensive row is on and the version is still 12",
            SettingsFor(reloaded, Job.SAM, false) is { } sam && sam["BloodBathPvPPercent"] == "0,75" && OptionText.TryRead(sam["BloodBathPvPPercent"], out float ratio, De) && ratio == 0.75f
            && SettingsFor(reloaded, Job.WAR, false)!["Legacy option"] == "True" && (bool)Method(reloaded, "DefensiveActive").Invoke(reloaded, [rampart])!
            && (int)reloaded.GetType().GetProperty("Version")!.GetValue(reloaded)! == 12);
        Harness.Case("the file written by this build still holds the options where the previous build's accessor path looks (_rotationConfigurationsDict[job][\"\"][name])",
            OldPathRead(reloaded, Job.SAM, "BloodBathPvPPercent") == "0,75" && OldPathRead(reloaded, Job.WAR, "ShadowbringerThreshold") == "50");

        // 3. Clearing the new settings again puts the new top-level keys back to absent.
        Method(reloaded, "SetDefensiveEnabled").Invoke(reloaded, [rampart, null]);
        Dictionary<string, string> cleared = TopLevel(Serialize(reloaded));
        Harness.Case("a defensive setting changed back to its default leaves the file without the key again", !cleared.ContainsKey("PvpDefensiveSettings"));

        Harness.Canary("a file with the defensive key present counts as untouched", !TopLevel(savedJson).ContainsKey("PvpDefensiveSettings"));
    }

    private static object NewChoiceDictionary(IDictionary root, object options)
    {
        Type byChoice = root.GetType().GetGenericArguments()[1];
        IDictionary choices = (IDictionary)Activator.CreateInstance(byChoice)!;
        choices[string.Empty] = options;
        return choices;
    }

    private static object DefensiveRow(string actionName) =>
        ((IEnumerable)BasicAssembly.Type("RotationSolver.Decisions.DefensiveTable").GetField("Rows")!.GetValue(null)!).Cast<object>()
        .Single(r => (string)r.GetType().GetProperty("ActionName")!.GetValue(r)! == actionName);

    // ---- across the version boundary ----------------------------------------------------------------------------------

    private static void CrossVersionCases()
    {
        Assembly? old = BasicAssembly.Old();
        if (old == null)
        {
            Console.WriteLine("NOT RUN cross-version cases (env PVPROT_OLD_BASIC_DLL is not set): the previous build is not loaded, so 'an old build reads the new file' and 'an untouched file is identical in both builds' are covered only by the same-class cases above");
            return;
        }

        Assembly current = BasicAssembly.Get();
        Type oldConfigs = old.GetType(ConfigsType, throwOnError: true)!;
        Type newConfigs = current.GetType(ConfigsType, throwOnError: true)!;
        Harness.Case("cross-version setup: the previous build is a different build (it has no Configs.RotationSettingsFor)", oldConfigs.GetMethod("RotationSettingsFor", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) == null && newConfigs != oldConfigs);

        Harness.Case("an untouched configuration is byte for byte the same file in the previous build and in this one", Serialize(NewConfigs(old)) == Serialize(NewConfigs(current)));

        object touched = NewConfigs(current);
        SettingsFor(touched, Job.SAM, true)!["BloodBathPvPPercent"] = OptionText.Write(0.75f, De);
        SettingsFor(touched, Job.WAR, true)!["Legacy option"] = "True";
        Method(touched, "SetDefensiveEnabled").Invoke(touched, [DefensiveRow("RampartPvP"), true]);
        touched.GetType().GetProperty("PvpDefensivesMaster")!.SetValue(touched, false);
        string json = Serialize(touched);
        Harness.Case("the file this build writes (new keys included) loads in the previous build: the unknown keys are ignored, the version is 12 and the options are where its accessor looks",
            LoadOld(json, oldConfigs, out object? oldLoaded) && oldLoaded != null && (int)oldLoaded.GetType().GetProperty("Version")!.GetValue(oldLoaded)! == 12
            && OldPathRead(oldLoaded, Job.SAM, "BloodBathPvPPercent") == "0,75" && OldPathRead(oldLoaded, Job.WAR, "Legacy option") == "True");
        Harness.Case("a file the previous build wrote loads in this one and keeps every option",
            Load(Serialize(LegacyOld(oldConfigs)), newConfigs) is { } fromOld && SettingsFor(fromOld, Job.WAR, false)?["Legacy option"] == "True");
    }

    private static bool LoadOld(string json, Type oldConfigs, out object? loaded)
    {
        try
        {
            loaded = Load(json, oldConfigs);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine("old build could not load the new file: " + (e.InnerException ?? e).Message);
            loaded = null;
            return false;
        }
    }

    private static object LegacyOld(Type oldConfigs)
    {
        object cfg = Activator.CreateInstance(oldConfigs, nonPublic: true)!;
        IDictionary root = (IDictionary)cfg.GetType().GetField("_rotationConfigurationsDict", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cfg)!;
        root[Job.WAR] = NewChoiceDictionary(root, new System.Collections.Concurrent.ConcurrentDictionary<string, string> { ["Legacy option"] = "True" });
        return cfg;
    }
}
