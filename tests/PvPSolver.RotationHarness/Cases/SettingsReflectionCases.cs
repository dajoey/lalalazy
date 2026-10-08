using System.Collections;
using System.Globalization;
using System.Reflection;
using ECommons.ExcelServices;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Round 3 Part B, T3 (reflection over the compiled assemblies, nothing is instantiated except <c>Configs</c>): the
///     rotation options every job declares, the 21 PvP rotation types, the catalog of the settings window against the
///     real <c>Configs</c> class (name, type, default) and the defensive rows against the actions each job's rotation
///     really has.
/// </summary>
internal static class SettingsReflectionCases
{
    private sealed record OptionInfo(string Job, string Property, string DisplayName, Type PropertyType, string Parent, string CombatType, Type Owner);

    private static Type[] PluginTypes()
    {
        Assembly asm = typeof(RotationSolver.PvPSolverPlugin).Assembly;
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t != null).ToArray()!;
        }
    }

    private static CustomAttributeData? Attr(IEnumerable<CustomAttributeData> data, string name) => data.FirstOrDefault(a => a.AttributeType.Name == name);

    private static string EnumName(CustomAttributeTypedArgument arg) => Enum.ToObject(arg.ArgumentType, arg.Value!).ToString()!;

    private static string? Named(CustomAttributeData a, string name) => a.NamedArguments.FirstOrDefault(n => n.MemberName == name).TypedValue.Value as string;

    /// <summary>The PvP rotation types by job key, from the [Rotation] and [Jobs] attributes (read as data; no attribute is instantiated).</summary>
    private static Dictionary<string, List<Type>> RotationTypes()
    {
        var result = new Dictionary<string, List<Type>>();
        foreach (Type t in PluginTypes())
        {
            if (!t.IsClass || t.IsAbstract)
            {
                continue;
            }

            CustomAttributeData? rotation = Attr(t.GetCustomAttributesData(), "RotationAttribute");

            // [Jobs] sits on the per-job base class (the attribute is inherited), so walk up the hierarchy
            CustomAttributeData? jobs = null;
            for (Type? b = t; b != null && jobs == null; b = b.BaseType)
            {
                jobs = Attr(b.GetCustomAttributesData(), "JobsAttribute");
            }

            if (rotation == null || jobs == null || EnumName(rotation.ConstructorArguments[1]) != "PvP")
            {
                continue;
            }

            var disabled = rotation.NamedArguments.FirstOrDefault(n => n.MemberName == "Disabled");
            if (disabled.TypedValue.Value is true)
            {
                continue;
            }

            var list = (IReadOnlyCollection<CustomAttributeTypedArgument>)jobs.ConstructorArguments[0].Value!;
            string key = EnumName(list.First());
            if (!result.TryGetValue(key, out List<Type>? types))
            {
                result[key] = types = [];
            }

            types.Add(t);
        }

        return result;
    }

    public static void Run()
    {
        Console.WriteLine("-- round 3 Part B, T3: reflection over the compiled assemblies --");
        Dictionary<string, List<Type>> rotations = RotationTypes();
        JobsAndOptions(rotations);
        CatalogAgainstConfigs();
        DefensiveRowsAgainstRotations(rotations);
        EnumsAndTabs();
    }

    private static void JobsAndOptions(Dictionary<string, List<Type>> rotations)
    {
        Harness.Case("the 21 jobs of the picker each have exactly one PvP rotation type, and no other job has one",
            JobKey.All.All(k => rotations.TryGetValue(k, out List<Type>? l) && l.Count == 1) && rotations.Count == 21 && rotations.Keys.All(JobKey.IsPvpJob),
            string.Join(",", rotations.Where(r => r.Value.Count != 1).Select(r => r.Key + "x" + r.Value.Count)));

        var options = new List<OptionInfo>();
        foreach ((string job, List<Type> types) in rotations)
        {
            foreach (PropertyInfo p in types[0].GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                CustomAttributeData? a = Attr(p.GetCustomAttributesData(), "RotationConfigAttribute");
                if (a == null)
                {
                    continue;
                }

                options.Add(new OptionInfo(job, p.Name, Named(a, "Name") ?? string.Empty, p.PropertyType, Named(a, "Parent") ?? string.Empty, EnumName(a.ConstructorArguments[0]), types[0]));
            }
        }

        Harness.Case("the job options are the 32 the review found, in 12 jobs, and every one is a PvP option (the new window shows them all)",
            options.Count == 32 && options.Select(o => o.Job).Distinct().Count() == 12 && options.All(o => o.CombatType == "PvP"), $"{options.Count} in {options.Select(o => o.Job).Distinct().Count()} jobs");
        Harness.Case("every option has a non-empty display name",
            options.All(o => o.DisplayName.Trim().Length > 0), string.Join(",", options.Where(o => o.DisplayName.Trim().Length == 0).Select(o => o.Job + "." + o.Property)));
        Type[] supported = [typeof(bool), typeof(float), typeof(int), typeof(string)];
        Harness.Case("every option has a type the window can draw (bool, float, int, string or an enum)",
            options.All(o => supported.Contains(o.PropertyType) || o.PropertyType.IsEnum), string.Join(",", options.Where(o => !supported.Contains(o.PropertyType) && !o.PropertyType.IsEnum).Select(o => o.Job + "." + o.Property)));
        Harness.Case("(job, property name) and (job, display name) are unique, so a job's options never collide in the store or on screen",
            options.GroupBy(o => (o.Job, o.Property)).All(g => g.Count() == 1) && options.GroupBy(o => (o.Job, o.DisplayName)).All(g => g.Count() == 1));
        var missingParents = options.Where(o => o.Parent.Length > 0 && o.Owner.GetProperty(o.Parent, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) == null).Select(o => o.Job + "." + o.Property).ToList();
        Harness.Case("every Parent names a property of the same rotation, and every parent is a bool (the window's child rule needs no required value)",
            missingParents.Count == 0 && options.Where(o => o.Parent.Length > 0).All(o => o.Owner.GetProperty(o.Parent, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.PropertyType == typeof(bool)),
            string.Join(",", missingParents));
        Harness.Case("the options a job hides until its parent is on are the PLD Guardian threshold and nothing else",
            options.Where(o => o.Parent.Length > 0).Select(o => o.Job + "." + o.Property).SequenceEqual(["PLD.GuardianThreshold"]), string.Join(",", options.Where(o => o.Parent.Length > 0).Select(o => o.Job + "." + o.Property)));

        // the same-named options that leaked between jobs: six melee jobs share two names
        var shared = options.GroupBy(o => o.Property).Where(g => g.Select(x => x.Job).Distinct().Count() > 1).ToDictionary(g => g.Key, g => g.Select(x => x.Job).OrderBy(j => j, StringComparer.Ordinal).ToArray());
        Harness.Case("two option names are shared by the six melee jobs (BloodBathPvPPercent, SmitePvPPercent): the cross-job leak the job-keyed store fixes",
            shared.Count == 2 && shared.ContainsKey("BloodBathPvPPercent") && shared.ContainsKey("SmitePvPPercent")
            && shared["BloodBathPvPPercent"].SequenceEqual(["DRG", "MNK", "NIN", "RPR", "SAM", "VPR"]) && shared["SmitePvPPercent"].SequenceEqual(["DRG", "MNK", "NIN", "RPR", "SAM", "VPR"]),
            string.Join(";", shared.Select(s => s.Key + "=" + string.Join(",", s.Value))));
        Harness.Canary("a job with two PvP rotation types passes the one-rotation-per-job check", rotations.Count > 0 && rotations.Values.All(l => l.Count == 2));
    }

    // ---- the catalog against the real Configs class ---------------------------------------------------------------------

    private static void CatalogAgainstConfigs()
    {
        Type configs = BasicAssembly.Type("RotationSolver.Basic.Configuration.Configs");
        object cfg = Activator.CreateInstance(configs, nonPublic: true)!;
        var problems = new List<string>();
        foreach (SettingSpec spec in PvpSettingsCatalog.All)
        {
            PropertyInfo? p = configs.GetProperty(spec.Property, BindingFlags.Public | BindingFlags.Instance);
            if (p == null)
            {
                problems.Add(spec.Property + ": no such property");
                continue;
            }

            object? value;
            try
            {
                value = spec.PerJob ? null : p.GetValue(cfg);
            }
            catch (Exception e)
            {
                problems.Add($"{spec.Property}: reading it throws {(e.InnerException ?? e).GetType().Name}");
                continue;
            }

            switch (spec.Control)
            {
                case SettingControl.Toggle when spec.PerJob:
                    // stored per job: the accessor with a job that never stored one reads the field default
                    object? job = configs.GetMethod("IgnorePvPInvincibilityFor", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(cfg, [Job.SAM]);
                    if (p.PropertyType != typeof(bool) || job is not bool jobDefault || jobDefault != spec.DefaultOn)
                    {
                        problems.Add($"{spec.Property}: per-job default {job} != {spec.DefaultOn}");
                    }

                    break;
                case SettingControl.Toggle:
                    bool? current = value is bool plain ? plain : value?.GetType().GetProperty("Value")?.GetValue(value) as bool?;
                    if (current == null || current != spec.DefaultOn)
                    {
                        problems.Add($"{spec.Property}: default {current} != {spec.DefaultOn}");
                    }

                    break;
                case SettingControl.Percent:
                case SettingControl.Seconds:
                    if (value is not float number || number != spec.DefaultNumber)
                    {
                        problems.Add($"{spec.Property}: default {value} != {spec.DefaultNumber}");
                    }

                    break;
                case SettingControl.Choice:
                    string[] members = p.PropertyType.IsEnum ? Enum.GetNames(p.PropertyType) : [];
                    if (!p.PropertyType.IsEnum || value?.ToString() != spec.DefaultChoice || !spec.Choices.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(members.OrderBy(n => n, StringComparer.Ordinal)))
                    {
                        problems.Add($"{spec.Property}: default {value} / choices {string.Join("|", spec.Choices.Select(c => c.Name))} vs members {string.Join("|", members)}");
                    }

                    break;
            }

            if (spec.Parent.Length > 0 && configs.GetProperty(spec.Parent, BindingFlags.Public | BindingFlags.Instance) == null)
            {
                problems.Add($"{spec.Property}: parent {spec.Parent} not found");
            }
        }

        Harness.Case("every catalog setting is a real Configs property of the right kind with the default the window shows (toggles, percents, seconds, choices with every enum member offered, the per-job flag, parents)",
            problems.Count == 0, $"{PvpSettingsCatalog.All.Count} settings" + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""));

        // percent ranges: the property is a ratio
        Harness.Case("the percent settings are floats held as ratios and the seconds settings as seconds (HealthForGuard 0.15, AutoOffAfterCombatTime 30, StickyTargetMaxRemaining 30)",
            (float)configs.GetProperty("HealthForGuard")!.GetValue(cfg)! == 0.15f && (float)configs.GetProperty("AutoOffAfterCombatTime")!.GetValue(cfg)! == 30f && (float)configs.GetProperty("StickyTargetMaxRemaining")!.GetValue(cfg)! == 30f);

        Harness.Canary("a catalog entry naming a property Configs does not have passes the check", configs.GetProperty("NoSuchSetting") != null);
    }

    // ---- the defensive rows against the rotations ----------------------------------------------------------------------------

    private static void DefensiveRowsAgainstRotations(Dictionary<string, List<Type>> rotations)
    {
        var missing = new List<string>();
        foreach (DefensiveRow row in DefensiveTable.Rows)
        {
            string[] jobs = row.Job switch
            {
                "*" => [.. JobKey.All],
                "TANK" or "HEALER" => [.. JobKey.All.Where(j => DefensiveTable.RoleKeyOf(j) == row.Job)],
                _ => [row.Job],
            };
            foreach (string job in jobs)
            {
                if (!rotations.TryGetValue(job, out List<Type>? types))
                {
                    missing.Add($"{row.ActionName}: no rotation for {job}");
                    continue;
                }

                PropertyInfo? property = types[0].GetProperty(row.ActionName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (property == null)
                {
                    missing.Add($"{row.ActionName}: not on {types[0].Name} or its base classes");
                }
            }
        }

        Harness.Case("every row of the defensive table names an action that the rotation of each job it applies to (its own job, its role, or every job) really has",
            missing.Count == 0, $"{DefensiveTable.Rows.Count} rows" + (missing.Count > 0 ? ": " + string.Join("; ", missing.Take(6)) : ""));

        // job-specific rows appear in the rotation file or its base class, as text too (the base class is generated, so the rotation files and the base files are searched)
        var textMissing = new List<string>();
        string rotationsDir = Path.Combine(Program.SrcRoot, "PvPSolver", "RebornRotations", "PVPRotations");
        string basicDir = Path.Combine(Program.SrcRoot, "PvPSolver.Basic", "Rotations");
        string text = string.Join("\n", Directory.GetFiles(rotationsDir, "*.cs", SearchOption.AllDirectories).Concat(Directory.GetFiles(basicDir, "*.cs", SearchOption.AllDirectories)).Select(File.ReadAllText));
        foreach (DefensiveRow row in DefensiveTable.Rows.Where(r => r.Job.Length == 3))
        {
            if (!text.Contains(row.ActionName, StringComparison.Ordinal))
            {
                textMissing.Add(row.ActionName);
            }
        }

        Harness.Case("every job-specific action name also appears in the PvP rotation files or the rotation base files", textMissing.Count == 0, string.Join(",", textMissing));
        Harness.Canary("a row for an action no rotation has passes the check", typeof(RotationSolver.PvPSolverPlugin).Assembly.GetTypes().Any(t => t.GetProperty("NoSuchActionPvP") != null));
    }

    // ---- enums and tabs ----------------------------------------------------------------------------------------------------

    private static void EnumsAndTabs()
    {
        Type targeting = BasicAssembly.Type("RotationSolver.Basic.Data.TargetingType");
        string[] names = Enum.GetNames(targeting);
        Harness.Case("the targeting choices are the real TargetingType enum: every member offered once, with its real value",
            TargetingList.Choices.Count == names.Length && TargetingList.Choices.All(c => names.Contains(c.Name) && (int)Enum.Parse(targeting, c.Name) == c.Value),
            string.Join(",", names.Except(TargetingList.Choices.Select(c => c.Name))));
        Harness.Case("the default targeting list is Low HP, High HP, Small, Big as real enum values",
            TargetingList.Default.Select(v => Enum.GetName(targeting, v)).SequenceEqual(["LowHP", "HighHP", "Small", "Big"]));

        Type? tab = typeof(RotationSolver.PvPSolverPlugin).Assembly.GetType("RotationSolver.UI.RotationConfigWindowTab");
        string[] tabNames = tab == null ? [] : Enum.GetNames(tab);
        var skipped = tab == null ? new List<string>() : tab.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "TabSkipAttribute")).Select(f => f.Name).ToList();
        Harness.Case("the legacy tabs the Advanced quick links and /pvpsolver advanced open exist and are not hidden tabs",
            tab != null && AdvancedTab.Names.All(n => tabNames.Contains(n) && !skipped.Contains(n)), string.Join(",", AdvancedTab.Names.Where(n => !tabNames.Contains(n) || skipped.Contains(n))));
    }
}
