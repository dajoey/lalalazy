using System.Globalization;
using System.Reflection;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Round 3 Part B, T1: the pure cores of the new settings window (all compiled into this harness from
///     <c>PvPSolver.Basic/Decisions</c>): option text in several cultures (including comma-decimal), percent clamp and
///     step, job keys, the targeting list edits, the on/off strip, the log-once gate, the advanced tab names, the
///     settings catalog. Each group carries a canary that must fail.
/// </summary>
internal static class SettingsCoreCases
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static T WithCulture<T>(CultureInfo culture, Func<T> action)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    public static void Run()
    {
        Console.WriteLine("-- round 3 Part B, T1: pure cores of the settings window --");
        OptionTextCases();
        PercentCases();
        JobKeyCases();
        TargetingListCases();
        SwitchCases();
        SmallCores();
        CatalogCases();
    }

    // ---- option text ---------------------------------------------------------------------------------------------------

    private static void OptionTextCases()
    {
        Harness.Case("the comma-decimal test culture (de-DE) really uses a comma, so the culture cases mean something",
            De.NumberFormat.NumberDecimalSeparator == "," && Fr.NumberFormat.NumberDecimalSeparator == "," && En.NumberFormat.NumberDecimalSeparator == ".");

        float[] values = [0f, 0.05f, 0.15f, 0.25f, 0.3f, 0.5f, 0.75f, 0.755f, 0.99f, 1f, 12.5f, 0.005f, 600f];
        CultureInfo[] cultures = [En, De, Fr, CultureInfo.InvariantCulture];
        int rows = 0;
        var failed = new List<string>();
        foreach (CultureInfo c in cultures)
        {
            foreach (float v in values)
            {
                rows++;
                string text = OptionText.Write(v, c);
                if (!OptionText.TryRead(text, out float back, c) || back != v)
                {
                    failed.Add($"{c.Name}:{v}->{text}->{back}");
                }
            }
        }

        Harness.Case("a float option written and read in the same culture round-trips exactly (en-US, de-DE, fr-FR, invariant)", failed.Count == 0, $"{rows} rows" + (failed.Count > 0 ? " " + string.Join("; ", failed) : ""));
        Harness.Case("a comma-decimal culture stores 0,75 and an English one 0.75, exactly what float.ToString() gives",
            OptionText.Write(0.75f, De) == "0,75" && OptionText.Write(0.75f, En) == "0.75" && OptionText.Write(0.75f, De) == 0.75f.ToString(De));
        Harness.Case("with no culture passed the current culture is used (the legacy window's behaviour)",
            WithCulture(De, () => OptionText.Write(0.75f)) == "0,75" && WithCulture(En, () => OptionText.Write(0.75f)) == "0.75"
            && WithCulture(De, () => OptionText.TryRead("0,75", out float v) && v == 0.75f)
            && WithCulture(De, () => OptionText.Write(42)) == "42");
        Harness.Case("documented legacy behaviour: a comma-decimal text read in an English culture is a thousands-grouped number (75), which is why the window must write and read in the same culture as the legacy window",
            WithCulture(En, () => OptionText.TryRead("0,75", out float v) && v == 75f));
        Harness.Case("bool texts are True and False and parse case-insensitively",
            OptionText.Write(true) == "True" && OptionText.Write(false) == "False" && OptionText.TryRead("true", out bool t) && t && OptionText.TryRead("FALSE", out bool f) && !f && !OptionText.TryRead("yes", out _));
        Harness.Case("whole numbers round-trip in a comma-decimal culture and reject text",
            OptionText.TryRead(OptionText.Write(50, De), out int n, De) && n == 50 && !OptionText.TryRead("5x", out int _, De) && !OptionText.TryRead(null, out int _, De));

        // The real rotation applies the stored text with RotationConfigBase.ChangeType (Convert.ChangeType: current culture).
        MethodInfo change = BasicAssembly.Type("RotationSolver.Basic.Configuration.RotationConfig.RotationConfigBase")
            .GetMethod("ChangeType", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? Apply(string text, Type type, CultureInfo culture) => WithCulture(culture, () => change.Invoke(null, [text, type]));
        Harness.Case("the real RotationConfigBase.ChangeType reads what OptionText wrote, in de-DE, en-US and fr-FR (float, int, bool)",
            Apply(OptionText.Write(0.75f, De), typeof(float), De) is float a && a == 0.75f
            && Apply(OptionText.Write(0.75f, En), typeof(float), En) is float b && b == 0.75f
            && Apply(OptionText.Write(0.3f, Fr), typeof(float), Fr) is float c2 && c2 == 0.3f
            && Apply(OptionText.Write(50, De), typeof(int), De) is int d && d == 50
            && Apply(OptionText.Write(true), typeof(bool), De) is bool e && e);

        bool same(OptionKind k, string? x, string? y, CultureInfo? c = null) => OptionText.SameValue(k, x, y, c);
        Harness.Case("SameValue: the default test of the reset button (bool, int and float, a different spelling of one number, garbage)",
            same(OptionKind.Bool, "True", "true") && !same(OptionKind.Bool, "True", "False")
            && same(OptionKind.Percent, "0.75", "0.750", En) && same(OptionKind.Percent, "0,75", "0,750", De) && !same(OptionKind.Percent, "0.75", "0.74", En)
            && same(OptionKind.Int, "50", "50") && !same(OptionKind.Int, "50", "51")
            && same(OptionKind.Text, "abc", "abc") && !same(OptionKind.Text, "abc", "abd") && !same(OptionKind.Float, "x", "y", En) && same(OptionKind.Float, "x", "x", En));
        Harness.Case("DefaultLabel: \"(default 75%)\" from the stored default in either culture, on/off for bools",
            OptionText.DefaultLabel(OptionKind.Percent, "0.75", En) == "(default 75%)" && OptionText.DefaultLabel(OptionKind.Percent, "0,75", De) == "(default 75%)"
            && OptionText.DefaultLabel(OptionKind.Percent, "0,25", De) == "(default 25%)"
            && OptionText.DefaultLabel(OptionKind.Bool, "True") == "(default on)" && OptionText.DefaultLabel(OptionKind.Bool, "False") == "(default off)"
            && OptionText.DefaultLabel(OptionKind.Int, "50") == "(default 50)" && OptionText.DefaultLabel(OptionKind.Percent, "junk", En) == "(default unknown)");

        // child option visibility: the legacy window's rule over its whole small domain
        var rowsChecked = 0;
        var wrong = new List<string>();
        foreach (bool found in new[] { false, true })
        foreach (bool isBool in new[] { false, true })
        foreach (string? text in new string?[] { null, "True", "False", "junk", "Alpha", " alpha ", "Beta" })
        foreach (string? required in new string?[] { null, "Alpha", "alpha" })
        {
            rowsChecked++;
            bool expected = !found || (isBool ? bool.TryParse(text, out bool on) && on : required == null || (text != null && string.Equals(text.Trim(), required.Trim(), StringComparison.OrdinalIgnoreCase)));
            if (OptionText.ChildVisible(found, isBool, text, required) != expected)
            {
                wrong.Add($"{found}/{isBool}/{text}/{required}");
            }
        }

        Harness.Case("ChildVisible equals the legacy rule (no parent shows the child, a bool parent must be on, otherwise the parent's text must equal the required value)", wrong.Count == 0, $"{rowsChecked} rows" + (wrong.Count > 0 ? " " + string.Join("; ", wrong) : ""));

        string two = "May be used, not will be used: Recuperate restores 16,000 HP and costs 2,000 MP. It is already used when 15,000 HP are missing. Worst case: several can fire.";
        Harness.Case("Excerpt keeps whole sentences up to the limit, never past a stop prefix, and always keeps the first sentence",
            OptionText.Excerpt(two, 300, "Worst case:") == "May be used, not will be used: Recuperate restores 16,000 HP and costs 2,000 MP. It is already used when 15,000 HP are missing."
            && OptionText.Excerpt(two, 20) == "May be used, not will be used: Recuperate restores 16,000 HP and costs 2,000 MP."
            && OptionText.Excerpt("A. B.", 3) == "A." && OptionText.Excerpt("takes 2.5 seconds. Next.", 100) == "takes 2.5 seconds. Next." && OptionText.Excerpt(null, 10) == "" && OptionText.Excerpt("  ", 10) == "");

        // canary: a writer that always uses the invariant culture does not produce the comma text a comma-decimal legacy window stores
        Harness.Canary("an invariant-culture writer matches the comma-decimal text the legacy window stores", 0.75f.ToString(CultureInfo.InvariantCulture) == OptionText.Write(0.75f, De));
        Harness.Canary("a reader that ignores the culture reads 0,75 as 0.75 in an English culture", WithCulture(En, () => OptionText.TryRead("0,75", out float v) && v == 0.75f));
    }

    // ---- percent clamp and step ----------------------------------------------------------------------------------------

    private static void PercentCases()
    {
        var bad = new List<string>();
        for (int p = 0; p <= 100; p++)
        {
            float ratio = OptionText.FromPoints(p);
            if (ratio != p / 100f || OptionText.ToPoints(ratio) != p || OptionText.Write(ratio, De) != (p / 100f).ToString(De))
            {
                bad.Add(p.ToString());
            }
        }

        Harness.Case("every whole percent 0..100 maps to the ratio the legacy slider writes (displayValue / 100) and back, and its text is the legacy text", bad.Count == 0, string.Join(",", bad));
        Harness.Case("a slider position is clamped into the option's range (a floor of 50%, a ceiling of 90%, below and above both)",
            OptionText.FromPoints(-5) == 0f && OptionText.FromPoints(250) == 1f && OptionText.FromPoints(49, 0.5f, 1f) == 0.5f && OptionText.FromPoints(10, 0.5f, 1f) == 0.5f
            && OptionText.FromPoints(95, 0f, 0.9f) == 0.9f && OptionText.FromPoints(75, 0.5f, 1f) == 0.75f);
        Harness.Case("ToPoints rounds half away from zero and tolerates stored values with extra decimals",
            OptionText.ToPoints(0.755f) == 76 && OptionText.ToPoints(0.754f) == 75 && OptionText.ToPoints(0f) == 0 && OptionText.ToPoints(1f) == 100 && OptionText.ToPoints(0.125f) == 13);
        Harness.Case("integer and float options are clamped; NaN and infinity fall back",
            OptionText.ClampInt(500, 1, 100) == 100 && OptionText.ClampInt(-3, 1, 100) == 1 && OptionText.ClampInt(7, 1, 100) == 7 && OptionText.ClampInt(7, 9, 3) == 9
            && OptionText.ClampFloat(2f, 0f, 1f, 0.5f) == 1f && OptionText.ClampFloat(float.NaN, 0f, 1f, 0.5f) == 0.5f && OptionText.ClampFloat(float.PositiveInfinity, 0f, 1f, 0.5f) == 0.5f);
        Harness.Canary("a slider that does not clamp stays inside the Recuperate floor", OptionText.FromPoints(10, 0.5f, 1f) * 100f == 10f);
    }

    // ---- job keys --------------------------------------------------------------------------------------------------------

    private static void JobKeyCases()
    {
        Harness.Case("the picker holds the 21 PvP jobs once each, in the five role groups (4, 4, 6, 3, 4)",
            JobKey.All.Count == 21 && JobKey.All.Distinct().Count() == 21 && JobKey.Groups.Select(g => g.Jobs.Count).SequenceEqual([4, 4, 6, 3, 4]));
        Harness.Case("every picker job key is a member name of ECommons.ExcelServices.Job",
            JobKey.All.All(k => Enum.TryParse<ECommons.ExcelServices.Job>(k, out var j) && Enum.IsDefined(j) && j.ToString() == k));
        Harness.Case("base classes map to their job (GLA PLD, MRD WAR, PGL MNK, LNC DRG, ARC BRD, ROG NIN, CNJ WHM, THM BLM); ACN is ambiguous and maps to nothing",
            JobKey.JobOfBaseClass("GLA") == "PLD" && JobKey.JobOfBaseClass("MRD") == "WAR" && JobKey.JobOfBaseClass("PGL") == "MNK" && JobKey.JobOfBaseClass("LNC") == "DRG"
            && JobKey.JobOfBaseClass("ARC") == "BRD" && JobKey.JobOfBaseClass("ROG") == "NIN" && JobKey.JobOfBaseClass("CNJ") == "WHM" && JobKey.JobOfBaseClass("THM") == "BLM"
            && JobKey.JobOfBaseClass("ACN") == "" && JobKey.JobOfBaseClass("WAR") == "");
        Harness.Case("every base class key is a member name of the Job enum and its target is one of the 21",
            new[] { "GLA", "MRD", "PGL", "LNC", "ARC", "ROG", "CNJ", "THM" }.All(k => Enum.TryParse<ECommons.ExcelServices.Job>(k, out _) && JobKey.IsPvpJob(JobKey.JobOfBaseClass(k))));
        Harness.Case("PickFor: a PvP job stays, a base class becomes its job, anything else (none, crafter, ACN) uses the fallback",
            JobKey.PickFor("WAR", "PLD") == "WAR" && JobKey.PickFor("MRD", "PLD") == "WAR" && JobKey.PickFor(null, "PLD") == "PLD" && JobKey.PickFor("CRP", "PLD") == "PLD"
            && JobKey.PickFor("ACN", "WHM") == "WHM" && JobKey.PickFor("", "PLD") == "PLD");
        Harness.Case("GroupOf finds a job's role row", JobKey.GroupOf("DNC")?.Name == "Ranged" && JobKey.GroupOf("PLD")?.Name == "Tanks" && JobKey.GroupOf("XXX") == null && JobKey.GroupOf(null) == null);
        Harness.Canary("a base class key is accepted as a PvP job", JobKey.IsPvpJob("GLA"));
    }

    // ---- targeting list edits --------------------------------------------------------------------------------------------

    private static bool Invariant(IReadOnlyList<int> list) => list.Count > 0 && list.Distinct().Count() == list.Count;

    private static void TargetingListCases()
    {
        int[][] starts = [[], [3], [3, 2], [3, 2, 1, 0], [3, 3, 2], [0, 0], [13, 3, 13, 2, 3]];
        int[] values = [0, 3, 13, 99];
        int[] indexes = [-1, 0, 1, 2, 5];
        int rows = 0;
        var failures = new List<string>();

        void Check(string name, IReadOnlyList<int> start, List<int> result, Func<List<int>, bool>? extra = null)
        {
            rows++;
            if (!Invariant(result) || (extra != null && !extra(result)))
            {
                failures.Add($"{name}({string.Join(",", start)}) -> ({string.Join(",", result)})");
            }
        }

        foreach (int[] start in starts)
        {
            List<int> clean = TargetingList.Clean(start);
            Check("Clean", start, clean, r => start.Length == 0 ? r.SequenceEqual(TargetingList.Default) : r.SequenceEqual(start.Distinct()));
            foreach (int v in values)
            {
                Check($"Add{v}", start, TargetingList.Add(start, v),
                    r => r.Contains(v) && r.Take(clean.Count).SequenceEqual(clean) && r.Count == (clean.Contains(v) ? clean.Count : clean.Count + 1));
            }

            foreach (int i in indexes)
            {
                Check($"Remove{i}", start, TargetingList.Remove(start, i),
                    r => (i >= 0 && i < clean.Count && clean.Count > 1) ? r.Count == clean.Count - 1 : r.SequenceEqual(clean));
                Check($"Up{i}", start, TargetingList.MoveUp(start, i),
                    r => r.Count == clean.Count && r.OrderBy(x => x).SequenceEqual(clean.OrderBy(x => x)) && (i <= 0 || i >= clean.Count ? r.SequenceEqual(clean) : r[i - 1] == clean[i] && r[i] == clean[i - 1]));
                Check($"Down{i}", start, TargetingList.MoveDown(start, i),
                    r => r.Count == clean.Count && r.OrderBy(x => x).SequenceEqual(clean.OrderBy(x => x)) && (i < 0 || i >= clean.Count - 1 ? r.SequenceEqual(clean) : r[i + 1] == clean[i] && r[i] == clean[i + 1]));
                foreach (int v in values)
                {
                    Check($"Replace{i}={v}", start, TargetingList.Replace(start, i, v),
                        r => r.Count == clean.Count && (i < 0 || i >= clean.Count || (clean.Contains(v) && clean[i] != v) ? r.SequenceEqual(clean) : r[i] == v));
                }
            }
        }

        Harness.Case("targeting list edits: the result is never empty and holds no entry twice; removal never empties; moves stay inside the list; add and replace never create a duplicate", failures.Count == 0, $"{rows} edits" + (failures.Count > 0 ? " " + string.Join("; ", failures.Take(5)) : ""));
        List<int> reset = TargetingList.Reset();
        reset.Add(99);
        Harness.Case("Reset gives the default order (Low HP, High HP, Small, Big) as a fresh copy", TargetingList.Reset().SequenceEqual([3, 2, 1, 0]) && TargetingList.Default.SequenceEqual([3, 2, 1, 0]));
        Harness.Case("the last entry cannot be removed, and an empty list reads as the default order",
            TargetingList.Remove([5], 0).SequenceEqual([5]) && TargetingList.Clean([]).SequenceEqual(TargetingList.Default));
        Harness.Case("Addable lists only the choices not in the list; the 14 choices have distinct values, names and labels",
            TargetingList.Choices.Count == 14 && TargetingList.Choices.Select(c => c.Value).Distinct().Count() == 14 && TargetingList.Choices.Select(c => c.Name).Distinct().Count() == 14
            && TargetingList.Choices.Select(c => c.Label).Distinct().Count() == 14 && TargetingList.Choices.All(c => c.Label.Length > 0 && c.Help.Length > 0 && c.Help.EndsWith('.'))
            && TargetingList.Addable([3, 2, 1, 0]).Count == 10 && TargetingList.Addable([]).Count == 14);
        Harness.Case("LabelOf names a known value and falls back to the number", TargetingList.LabelOf(13) == "Your team's pressure target" && TargetingList.LabelOf(99) == "99");

        Harness.Canary("a remove that does not guard the last entry leaves a non-empty list", Invariant(new List<int> { 3 }.Where(_ => false).ToList()));
        Harness.Canary("an add that does not check for duplicates keeps the list free of duplicates", Invariant([3, 3]));
    }

    // ---- the on/off strip ------------------------------------------------------------------------------------------------

    private static void SwitchCases()
    {
        var wrong = new List<string>();
        foreach (bool on in new[] { false, true })
        foreach (bool zone in new[] { false, true })
        foreach (bool player in new[] { false, true })
        {
            SwitchView v = SwitchStatus.Describe(on, zone, player);
            // turning off is always possible; turning on needs a PvP zone and a character
            bool expectedEnabled = on || (zone && player);
            bool ok = v.IsOn == on && v.ButtonEnabled == expectedEnabled && v.ButtonLabel == (on ? "Turn off" : "Turn on") && v.StatusText == (on ? "Now: ON" : "Now: OFF")
                      && (v.ButtonEnabled == (v.ButtonWhy.Length == 0)) && v.Reason.Length > 0;
            if (!ok)
            {
                wrong.Add($"{on}/{zone}/{player}");
            }
        }

        Harness.Case("the strip: off is always allowed; on needs a PvP zone and a character; a disabled button always says why", wrong.Count == 0, string.Join(",", wrong));
        Harness.Case("the strip names the reason outside a PvP zone", SwitchStatus.Describe(false, false, true).ButtonWhy == "Only available in a PvP zone." && SwitchStatus.Describe(false, false, true).Reason.Contains("PvP zone"));
        Harness.Canary("a strip that lets the solver turn on outside a PvP zone", SwitchStatus.Describe(false, false, true).ButtonEnabled);
    }

    // ---- log once, advanced tab names --------------------------------------------------------------------------------------

    private static void SmallCores()
    {
        var gate = new OncePerDistinct();
        bool first = gate.First("a");
        bool again = gate.First("a");
        bool other = gate.First("b");
        gate.Reset();
        bool afterReset = gate.First("a");
        Harness.Case("a draw failure is logged once per distinct failure: the same signature is reported once, a new one is reported, a reset allows it again",
            first && !again && other && afterReset);
        var full = new OncePerDistinct();
        for (int i = 0; i < OncePerDistinct.Capacity; i++)
        {
            _ = full.First("s" + i);
        }

        Harness.Case("the remembered set is capped, so a failure that changes every frame cannot grow it without end", !full.First("one more") && !full.First("s0"));
        Exception e1 = new InvalidOperationException("boom");
        Exception e2 = new InvalidOperationException("boom");
        Harness.Case("Signature of two equal exceptions is equal and differs by type and message",
            OncePerDistinct.Signature(e1) == OncePerDistinct.Signature(e2) && OncePerDistinct.Signature(e1) != OncePerDistinct.Signature(new ArgumentException("boom"))
            && OncePerDistinct.Signature(e1) != OncePerDistinct.Signature(new InvalidOperationException("bang")));
        var twice = new OncePerDistinct();
        Harness.Canary("a gate that reports the same failure twice", twice.First("x") && twice.First("x"));

        Harness.Case("AdvancedTab.Resolve maps member names and plain words (any case, extra spaces) and rejects unknown, empty and hidden tabs",
            AdvancedTab.Resolve("actions") == "Actions" && AdvancedTab.Resolve(" Timing ") == "Basic" && AdvancedTab.Resolve("LISTS") == "List" && AdvancedTab.Resolve("macros") == "Main"
            && AdvancedTab.Resolve("UI") == "UI" && AdvancedTab.Resolve("interface") == "UI" && AdvancedTab.Resolve("Duty") == null && AdvancedTab.Resolve("") == null
            && AdvancedTab.Resolve(null) == null && AdvancedTab.Resolve("nonsense") == null);
        Harness.Case("every name AdvancedTab offers resolves to itself", AdvancedTab.Names.All(n => AdvancedTab.Resolve(n) == n));
        Harness.Canary("an unknown word opens a tab", AdvancedTab.Resolve("nonsense") != null);
    }

    // ---- the catalog -----------------------------------------------------------------------------------------------------

    private static void CatalogCases()
    {
        IReadOnlyList<SettingSpec> all = PvpSettingsCatalog.All;
        string[] tabs = [PvpSettingsCatalog.Match, PvpSettingsCatalog.Survival, PvpSettingsCatalog.Targeting, PvpSettingsCatalog.Display, PvpSettingsCatalog.Advanced];
        Harness.Case("the catalog holds 39 global settings with unique property names on the five tabs that use it",
            all.Count == 39 && all.Select(s => s.Property).Distinct().Count() == 39 && all.All(s => tabs.Contains(s.Tab)), all.Count.ToString());
        Harness.Case("every setting has a plain label and a help line that is a full sentence (the help is printed under the control, not only in a tooltip)",
            all.All(s => s.Label.Trim().Length >= 3 && s.Help.Trim().Length >= 15 && s.Help.TrimEnd().EndsWith('.') && s.Section.Length > 0));
        Harness.Case("labels are unique inside their section", all.GroupBy(s => (s.Tab, s.Section)).All(g => g.Select(s => s.Label).Distinct().Count() == g.Count()));
        bool parentsOk = true;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Parent.Length == 0)
            {
                continue;
            }

            int parent = all.ToList().FindIndex(x => x.Property == all[i].Parent);
            if (parent < 0 || parent >= i || all[parent].Control != SettingControl.Toggle || all[parent].Tab != all[i].Tab)
            {
                parentsOk = false;
            }
        }

        Harness.Case("a parent is a toggle of the same tab that comes before its child", parentsOk);
        Harness.Case("percent defaults are ratios inside their range; seconds defaults are inside theirs; choice defaults are one of the choices; toggles carry no range or choice",
            all.Where(s => s.Control == SettingControl.Percent).All(s => s.Min >= 0f && s.Max <= 1f && s.DefaultNumber >= s.Min && s.DefaultNumber <= s.Max)
            && all.Where(s => s.Control == SettingControl.Seconds).All(s => s.Min < s.Max && s.DefaultNumber >= s.Min && s.DefaultNumber <= s.Max)
            && all.Where(s => s.Control == SettingControl.Choice).All(s => s.Choices.Count >= 2 && s.Choices.Any(c => c.Name == s.DefaultChoice) && s.Choices.Select(c => c.Name).Distinct().Count() == s.Choices.Count)
            && all.Where(s => s.Control == SettingControl.Toggle).All(s => s.Choices.Count == 0));
        Harness.Case("exactly one setting is stored per job (IgnorePvPInvincibility) and the Targeting tab draws it",
            all.Count(s => s.PerJob) == 1 && all.Single(s => s.PerJob).Property == "IgnorePvPInvincibility" && all.Single(s => s.PerJob).Tab == PvpSettingsCatalog.Targeting);
        Harness.Case("Of finds a setting by property and null for an unknown one", PvpSettingsCatalog.Of("HealthForGuard")?.Control == SettingControl.Percent && PvpSettingsCatalog.Of("Nope") == null);
        Harness.Case("settings per tab: Match 6, Survival 11, Targeting 7, Display 14, Advanced 1",
            tabs.Select(t => PvpSettingsCatalog.ForTab(t).Count()).SequenceEqual([6, 11, 7, 14, 1]), string.Join(",", tabs.Select(t => PvpSettingsCatalog.ForTab(t).Count())));
        Harness.Canary("a duplicated property name passes the unique check", all.Concat([all[0]]).Select(s => s.Property).Distinct().Count() == all.Count + 1);
    }
}
