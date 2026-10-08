using System.Text.RegularExpressions;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Round 3 Part B, T2 (source shape): the files the new settings window and its routing are made of. The cases
///     read the real source and fail on the old text: the option store is job-keyed, the default way to open the
///     settings is the new window, <c>/pvpsolver advanced</c> and the Advanced tab open the legacy window directly,
///     the new window files never read the zone or the loaded rotation, every ImGui scope is held by a using
///     statement, option text goes through the culture-mirroring core, saving happens on edit completion, and a draw
///     failure is logged once per distinct failure. The generator-attribute and version-12 checks of
///     <c>Configs.PvpSettings.cs</c> are in <c>PvpSettingsCases</c>.
/// </summary>
internal static class SettingsShapeCases
{
    private static string Src(params string[] parts) => Path.Combine([Program.SrcRoot, .. parts]);

    // A missing file reads as empty, so on a tree that lacks the file every check about it fails by name (the red replay shows each one).
    private static string Read(params string[] parts) => File.Exists(Src(parts)) ? File.ReadAllText(Src(parts)) : string.Empty;

    private static string Sanitized(params string[] parts) => CsSource.Sanitize(Read(parts));

    private static string PvpUiDir => Src("PvPSolver", "UI", "Pvp");

    private static string[] PvpFiles() => Directory.Exists(PvpUiDir) ? Directory.GetFiles(PvpUiDir, "*.cs").OrderBy(f => f, StringComparer.Ordinal).ToArray() : [];

    /// <summary>The body of a method of a file, found by name in the sanitized text; the raw body keeps string contents.</summary>
    private static string RawBody(string[] parts, string name)
    {
        string raw = Read(parts);
        string san = CsSource.Sanitize(raw);
        Match m = Regex.Match(san, @"\b" + Regex.Escape(name) + @"\s*\([^)]*\)\s*\{");
        if (!m.Success)
        {
            return string.Empty;
        }

        int open = m.Index + m.Length - 1;
        int close = CsSource.Match(san, open);
        return close < 0 ? string.Empty : raw[open..close];
    }

    /// <summary>The body of a method, found by name inside a sanitized file.</summary>
    private static string BodyOf(string sanitized, string name)
    {
        Match m = Regex.Match(sanitized, @"\b" + Regex.Escape(name) + @"\s*\([^)]*\)\s*(?:=>|\{)");
        if (!m.Success)
        {
            return string.Empty;
        }

        int open = m.Value.EndsWith('{') ? m.Index + m.Length - 1 : -1;
        if (open < 0)
        {
            int end = sanitized.IndexOf(';', m.Index);
            return end < 0 ? string.Empty : sanitized[m.Index..end];
        }

        int close = CsSource.Match(sanitized, open);
        return close < 0 ? string.Empty : sanitized[open..close];
    }

    public static void Run()
    {
        Console.WriteLine("-- round 3 Part B, T2: the settings window, the routing and the store (source shape) --");
        StoreShape();
        RoutingShape();
        WindowFilesShape();
        BehaviourShape();
    }

    // ---- the job-keyed option store --------------------------------------------------------------------------------------

    private static void StoreShape()
    {
        string san = Sanitized("PvPSolver.Basic", "Configuration", "RotationConfig", "RotationConfigBase.cs");
        int legacyAccessor = Regex.Matches(san, @"Service\.Config\.RotationConfigurations\b").Count;
        string store = BodyOf(san, "Store");
        Harness.Case("RotationConfigBase.cs reads the player-keyed Service.Config.RotationConfigurations only on the duty-rotation path (once, inside Store, after the _rotation test)",
            legacyAccessor == 1 && Regex.IsMatch(store, @"_rotation\s*!=\s*null\s*\?\s*Service\.Config\.RotationSettingsFor\(_rotation\.Job,\s*create\)\s*:\s*Service\.Config\.RotationConfigurations\b"),
            $"{legacyAccessor} occurrence(s); Store: {CsSource.Squash(store)}");
        Harness.Case("RotationConfigBase.cs goes through Store at the four sites: the value getter, the setter and the two constructors",
            Regex.Matches(san, @"\bStore\((?:false|true)\)").Count == 4 && Regex.IsMatch(san, @"get\s*=>\s*Store\(false\)") && Regex.IsMatch(san, @"Store\(true\)")
            && Regex.Matches(san, @"Store\(false\) is \{ \} stored").Count == 2);

        string settings = Sanitized("PvPSolver.Basic", "Configuration", "Configs.PvpSettings.cs");
        string accessor = BodyOf(settings, "RotationSettingsFor");
        Harness.Case("Configs.RotationSettingsFor keys by the job asked for, never by DataCenter.Job, never stores the shared field and creates only when asked",
            accessor.Length > 0 && !accessor.Contains("DataCenter") && !Regex.IsMatch(accessor, @"\b_rotationConfigurations\b") && Regex.Matches(accessor, @"if\s*\(\s*!create\s*\)").Count == 2
            && Regex.IsMatch(accessor, @"=\s*new\(\)") && accessor.Contains("_rotationConfigurationsDict.TryGetValue(job"), CsSource.Squash(accessor));
        Harness.Case("the per-job invincibility accessors read and write the dictionary of the generated property, keyed by the job asked for",
            Regex.IsMatch(settings, @"IgnorePvPInvincibilityFor\(Job job\)\s*=>\s*_ignorePvPInvincibilityDict\.TryGetValue\(job") && Regex.IsMatch(settings, @"SetIgnorePvPInvincibilityFor\(Job job, bool value\)\s*=>\s*_ignorePvPInvincibilityDict\[job\]\s*=\s*value"));
        Harness.Case("the two new top-level keys are written only once they differ from the default (an untouched file stays byte for byte what an older build writes)",
            Regex.IsMatch(settings, @"ShouldSerializePvpDefensivesMaster\(\)\s*=>\s*!PvpDefensivesMaster") && Regex.IsMatch(settings, @"ShouldSerializePvpDefensiveSettings\(\)\s*=>\s*!PvpDefensiveSettings\.IsEmpty"));
    }

    // ---- routing ----------------------------------------------------------------------------------------------------------

    private static void RoutingShape()
    {
        string plugin = Sanitized("PvPSolver", "RotationSolverPlugin.cs");
        string open = BodyOf(plugin, "OpenConfigWindow");
        string advanced = BodyOf(plugin, "OpenAdvancedWindow");
        string show = BodyOf(plugin, "ShowConfigWindow");
        Harness.Case("OpenConfigWindow opens the new settings window and not the legacy one", Regex.IsMatch(open, @"_pvpSettingsWindow\?\.Toggle\(\)") && !open.Contains("_rotationConfigWindow"), CsSource.Squash(open));
        Harness.Case("OpenAdvancedWindow opens the legacy window directly and never goes through the new window",
            advanced.Contains("_rotationConfigWindow.IsOpen = true") && advanced.Contains("SetActiveTab") && !advanced.Contains("_pvpSettingsWindow") && advanced.Contains("AdvancedTab.Resolve("));
        Harness.Case("ShowConfigWindow (used by the first-start tutorial) keeps opening the legacy window on its tab", show.Contains("_rotationConfigWindow.IsOpen = true") && show.Contains("SetActiveTab") && !show.Contains("_pvpSettingsWindow"));
        Harness.Case("the new window is created and added to the window system next to the legacy one",
            Regex.IsMatch(plugin, @"_pvpSettingsWindow\s*=\s*new\(\)") && Regex.IsMatch(plugin, @"windowSystem\.AddWindow\(_pvpSettingsWindow\)"));

        string doOne = RawBody(["PvPSolver", "Commands", "RSCommands_BasicInfo.cs"], "DoOneCommand");
        int advancedBranch = doOne.IndexOf("\"advanced\"", StringComparison.Ordinal);
        int firstEnum = doOne.IndexOf("TryGetOneEnum<", StringComparison.Ordinal);
        Harness.Case("/pvpsolver advanced [tab] is handled before the command enums are tried and calls OpenAdvancedWindow with the text after it",
            advancedBranch >= 0 && advancedBranch < firstEnum && Regex.IsMatch(doOne, @"OpenAdvancedWindow\(\s*command\[""advanced"".Length\.\.\]\.Trim\(\)\s*\)"), $"advanced@{advancedBranch}, first enum@{firstEnum}");
        Harness.Case("a bare /pvpsolver and an unknown command still open the settings through OpenConfigWindow (now the new window)", Regex.Matches(doOne, @"OpenConfigWindow\(\)").Count == 2);

        string ui = Read("PvPSolver", "Data", "UiString.cs");
        Match noRotation = Regex.Match(ui, @"\[Description\(""(?<text>[^""]*)""\)\]\s*ConfigWindow_NoRotation\b");
        Harness.Case("the legacy \"No rotations loaded!\" text points to the new window instead of a rotations tab that does not exist",
            noRotation.Success && noRotation.Groups["text"].Value.Contains("PvP Solver settings window") && !noRotation.Groups["text"].Value.Contains("rotations tab"), noRotation.Groups["text"].Value);
    }

    // ---- the window files ----------------------------------------------------------------------------------------------------

    private static void WindowFilesShape()
    {
        string[] files = PvpFiles();
        string[] expected = ["PvpAdvancedTab.cs", "PvpBinding.cs", "PvpDisplayTab.cs", "PvpJobOptions.cs", "PvpJobTab.cs", "PvpMatchTab.cs", "PvpSettingsWindow.cs", "PvpSurvivalTab.cs", "PvpTargetingTab.cs", "PvpUi.cs"];
        Harness.Case("the window is made of the expected files (window, six tabs, job options, drawing helpers, binding)",
            expected.All(e => files.Any(f => Path.GetFileName(f) == e)), string.Join(",", files.Select(Path.GetFileName)));

        string windowRaw = Read("PvPSolver", "UI", "Pvp", "PvpSettingsWindow.cs");
        Match tabList = Regex.Match(windowRaw, @"Tabs\s*=\s*\[(?<body>.*?)\];", RegexOptions.Singleline);
        string[] tabLabels = Regex.Matches(tabList.Groups["body"].Value, @"\(\s*(?:(?<lit>""[^""]+"")|PvpSettingsCatalog\.(?<cat>\w+))\s*,").Select(m => m.Groups["lit"].Success ? m.Groups["lit"].Value.Trim('"') : m.Groups["cat"].Value).ToArray();
        Harness.Case("the window has the six tabs of the design, in order: Match, Survival, Targeting, My Job, Display, Advanced",
            tabLabels.SequenceEqual(["Match", "Survival", "Targeting", "My Job", "Display", "Advanced"]), string.Join(",", tabLabels));

        var bad = new List<string>();
        foreach (string file in files)
        {
            string san = CsSource.Sanitize(File.ReadAllText(file));
            if (Regex.IsMatch(san, @"\bDataCenter\s*\.\s*(CurrentRotation|IsPvP)\b") || Regex.IsMatch(san, @"\bCurrentRotation\b") || Regex.IsMatch(san, @"\bIsPvP\b"))
            {
                bad.Add(Path.GetFileName(file));
            }
        }

        Harness.Case("no file of the new window reads DataCenter.CurrentRotation or DataCenter.IsPvP (every setting stays editable in every zone)", files.Length > 0 && bad.Count == 0, string.Join(",", bad));

        var scopes = new List<string>();
        foreach (string file in files)
        {
            string san = CsSource.Sanitize(File.ReadAllText(file));
            foreach (Match m in Regex.Matches(san, @"\bImGui\s*\.\s*(Begin|End|Push|Pop)\w*\s*\("))
            {
                scopes.Add($"{Path.GetFileName(file)}:{CsSource.LineOf(san, m.Index)} {m.Value.Trim()}");
            }

            string[] lines = san.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], @"\bImRaii\s*\.\s*\w+\s*\(") && !Regex.IsMatch(lines[i], @"^\s*using\s+(var\s+\w+\s*=\s*|\(\s*var\s+\w+\s*=\s*)ImRaii\s*\."))
                {
                    scopes.Add($"{Path.GetFileName(file)}:{i + 1} ImRaii scope not held by a using statement");
                }
            }
        }

        Harness.Case("every ImGui scope in the new window is an ImRaii object held by a using statement: no bare Begin/End or Push/Pop call, no ImRaii result that is not disposed",
            files.Length > 0 && scopes.Count == 0, string.Join("; ", scopes.Take(6)));

        var saves = new List<string>();
        foreach (string file in files)
        {
            string san = CsSource.Sanitize(File.ReadAllText(file));
            int n = Regex.Matches(san, @"\bConfig\s*\.\s*Save\s*\(\)").Count;
            if (n > 0)
            {
                saves.Add($"{Path.GetFileName(file)}={n}");
            }
        }

        // PvpUi.Save is the one saving helper; the reset button saves a fresh Configs object by hand
        Harness.Case("Config.Save() is called in exactly two places: the PvpUi.Save helper and the reset confirmation",
            saves.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(["PvpAdvancedTab.cs=1", "PvpUi.cs=1"]), string.Join(",", saves));

        string ui = Sanitized("PvPSolver", "UI", "Pvp", "PvpUi.cs");
        Harness.Case("sliders and drags save when released and flag a pending save in between; checkboxes save on change; the window flushes a pending save when it closes",
            BodyOf(ui, "PercentSlider").Contains("SaveOnRelease()") && BodyOf(ui, "NumberDrag").Contains("SaveOnRelease()") && BodyOf(ui, "SaveOnRelease").Contains("IsItemDeactivatedAfterEdit()")
            && Regex.IsMatch(BodyOf(ui, "DrawToggle"), @"TrySetBool\([^;]*;\s*Save\(\)") && BodyOf(ui, "Flush").Contains("_dirty")
            && Sanitized("PvPSolver", "UI", "Pvp", "PvpSettingsWindow.cs").Contains("PvpUi.Flush()"));

        string options = Sanitized("PvPSolver", "UI", "Pvp", "PvpJobOptions.cs");
        var assignments = Regex.Matches(options, @"\b(?:config|combo)\.Value\s*=\s*(?<rhs>[^;]+);").Select(m => CsSource.Squash(m.Groups["rhs"].Value)).ToList();
        bool allowed(string rhs) => rhs.StartsWith("OptionText.Write(", StringComparison.Ordinal) || rhs is "text" or "defaultText" or "config.DefaultValue" or "names[index]";
        Harness.Case("job option text is written only through OptionText (the legacy window's culture behaviour), a reset, or a text/combo choice; no number is formatted by hand",
            assignments.Count >= 8 && assignments.All(allowed) && !Regex.IsMatch(options, @"\b(float|int)\.(Try)?Parse\b|InvariantCulture"), string.Join(" | ", assignments.Where(a => !allowed(a))));

        Harness.Case("the job options are fetched from the cache on every frame and never kept: no field holds a rotation, a config or a config set; the call has no zone test and is guarded by a try/catch",
            Regex.IsMatch(options, @"RotationUpdater\.GetRotations\(job,\s*CombatType\.PvP\)") && !Regex.IsMatch(options, @"\bstatic\b[^;(]*\b(ICustomRotation|IRotationConfig|IRotationConfigSet|RotationConfigBase)\b[^;(]*(;|=)")
            && Regex.IsMatch(options, @"try\s*\{[^}]*GetRotations[^}]*\}\s*catch\s*\(Exception"));
        options = Read("PvPSolver", "UI", "Pvp", "PvpJobOptions.cs");
        Harness.Case("the job options show explicit states: no character, loading, failed, and \"no extra options\"",
            options.Contains("Log in with a character") && options.Contains("Loading the options") && options.Contains("could not be loaded") && options.Contains("This job has no extra options"));

        string window = Sanitized("PvPSolver", "UI", "Pvp", "PvpSettingsWindow.cs");
        string draw = BodyOf(window, "Draw");
        Harness.Case("the window draw is wrapped in a try/catch that logs once per distinct failure and renders an error line with a button to the legacy window",
            Regex.IsMatch(draw, @"try\s*\{\s*DrawShell\(\);\s*\}\s*catch\s*\(Exception ex\)") && draw.Contains("PvpUi.LogOnce(") && BodyOf(window, "DrawFailure").Contains("OpenAdvancedWindow()")
            && BodyOf(window, "DrawTabBody").Contains("catch (Exception") && Regex.IsMatch(Sanitized("PvPSolver", "UI", "Pvp", "PvpUi.cs"), @"Logged\.First\("));
        Harness.Case("the window id is distinct from the legacy window's id", Read("PvPSolver", "UI", "Pvp", "PvpSettingsWindow.cs").Contains("###pvpSettingsWindow") && !Read("PvPSolver", "UI", "Pvp", "PvpSettingsWindow.cs").Contains("###rsrConfigWindow"));
        Harness.Case("the only zone-dependent control, turning PvP Solver on, goes through the PvpSwitch helper outside the window files",
            window.Contains("PvpSwitch.View") && window.Contains("PvpSwitch.Set(") && File.Exists(Src("PvPSolver", "Helpers", "PvpSwitch.cs"))
            && Sanitized("PvPSolver", "Helpers", "PvpSwitch.cs").Contains("DataCenter.IsPvP"));
    }

    // ---- what the window says and does ---------------------------------------------------------------------------------------

    private static void BehaviourShape()
    {
        SettingSpec? master = PvpSettingsCatalog.Of("PvpDefensivesMaster");
        string[] six = ["Heart of Corundum", "Riddle of Earth", "Lady of Crowns", "Microcosmos", "Meisui", "Impalement"];
        Harness.Case("the help text under the master switch says in plain words that it also covers the six abilities that were automatic at a fixed HP before",
            master is { Tab: "Survival" } && six.All(master.Help.Contains) && master.Help.Contains("fixed HP") && master.Help.Contains("Recuperate") && master.Help.Contains("My Job tab"), master?.Help ?? "no master");

        string job = Read("PvPSolver", "UI", "Pvp", "PvpJobTab.cs");
        Harness.Case("the My Job tab draws Part A's rows of the picked job with an enable box, a percent slider, the default, a reset and the worst case once; Param rows are ordinary percent rows",
            job.Contains("DefensiveTable.ForJob(key)") && job.Contains("SetDefensiveEnabled") && job.Contains("SetDefensivePercent") && job.Contains("DefensiveTable.WorstCase") && job.Contains("master switch")
            && job.Contains("Use PvP Solver for this job") && job.Contains("PvpJobOptions.Draw(job)") && !job.Contains("Mode == DefensiveMode.Param"));
        Harness.Case("the My Job tab picks any of the 21 jobs and defaults to the played job; the targeting tab edits the invincibility flag of the picked job",
            job.Contains("JobKey.Groups") && job.Contains("JobKey.PickFor(") && Read("PvPSolver", "UI", "Pvp", "PvpTargetingTab.cs").Contains("IgnorePvPInvincibilityFor(job)"));
        Harness.Case("the Survival tab holds the master switch from the catalog and says what the switch does not cover",
            Read("PvPSolver", "UI", "Pvp", "PvpSurvivalTab.cs").Contains("Not covered by this switch") && PvpSettingsCatalog.ForTab("Survival").Any(s => s.Property == "PvpDefensivesMaster"));

        string advanced = Read("PvPSolver", "UI", "Pvp", "PvpAdvancedTab.cs");
        Harness.Case("the Advanced tab opens the legacy window, backs up and restores with a confirmation, resets with a confirmation and keeps the debug switch",
            advanced.Contains("OpenAdvancedWindow()") && advanced.Contains("OpenAdvancedWindow(tab)") && advanced.Contains("PopupModal(ResetPopup)") && advanced.Contains("PopupModal(RestorePopup)")
            && advanced.Contains("Config.Backup()") && advanced.Contains("Config.Restore()") && advanced.Contains("new Configs()") && PvpSettingsCatalog.ForTab("Advanced").Any(s => s.Property == "InDebug"));
        var linkTabs = Regex.Matches(Read("PvPSolver", "UI", "Pvp", "PvpAdvancedTab.cs"), @"\(""[^""]+"", ""(?<tab>\w+)"", ""[^""]+""\)").Select(m => m.Groups["tab"].Value).ToList();
        Harness.Case("every Advanced quick link names a tab AdvancedTab accepts", linkTabs.Count == 9 && linkTabs.All(t => AdvancedTab.Resolve(t) == t), string.Join(",", linkTabs));

        Harness.Canary("the old routing text (OpenConfigWindow toggling the legacy window) satisfies the new routing check", Regex.IsMatch("_rotationConfigWindow?.Toggle();", @"_pvpSettingsWindow\?\.Toggle\(\)"));
        Harness.Canary("a bare ImGui.Begin passes the scope lint", !Regex.IsMatch("ImGui.Begin(\"x\");", @"\bImGui\s*\.\s*(Begin|End|Push|Pop)\w*\s*\("));
        Harness.Canary("a window file that reads DataCenter.IsPvP passes the zone lint", !Regex.IsMatch("if (DataCenter.IsPvP) {}", @"\bIsPvP\b"));
        Harness.Canary("an ImRaii call that is not held by a using statement passes the dispose lint",
            Regex.IsMatch("var x = ImRaii.PushId(\"a\");", @"^\s*using\s+(var\s+\w+\s*=\s*|\(\s*var\s+\w+\s*=\s*)ImRaii\s*\."));
    }
}
