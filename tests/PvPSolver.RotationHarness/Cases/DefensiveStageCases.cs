using System.Text.RegularExpressions;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Round 3, Part A, item 7: the generic defensive stage. T1 on the pure rules (chaining suppression, the
///     silence list, the Recuperate gate) with independent reference models, old behaviour shown wrong and canaries;
///     T2 on the stage shape: the base-block call sits after the Guard branch and before Recuperate, the setting
///     test precedes <c>CanUse</c>, <c>CanUse</c> carries no <c>skip*</c> flag except the row's two, Recuperate is
///     gated in both stages, and the stage text refuses mutants that ignore Enabled, fire during Guard or add a
///     <c>skip*</c> flag. The stage itself (it needs game objects) is shape-checked, not run.
/// </summary>
internal static class DefensiveStageCases
{
    private static readonly DefensiveKind[] Kinds = [DefensiveKind.Shield, DefensiveKind.Mitigation, DefensiveKind.SelfHeal];

    // ---- chaining ---------------------------------------------------------------------------------------------
    private static IEnumerable<DefensiveSibling[]> SiblingLists()
    {
        var singles = (from k in Kinds from enabled in new[] { false, true } from active in new[] { false, true } select new DefensiveSibling(k, enabled, active)).ToArray();
        yield return [];
        foreach (var a in singles) yield return [a];
        foreach (var a in singles) foreach (var b in singles) yield return [a, b];
    }

    private static readonly (long Now, long Until)[] Clocks = [(0, 0), (2499, 2500), (2500, 2500), (2501, 2500), (0, 5000), (10000, 2500), (4999, 5000)];

    // Independent reference: locked out while now < until; otherwise blocked by any enabled, status-active row of the same kind.
    private static bool Reference(DefensiveKind kind, long now, long until, IReadOnlyList<DefensiveSibling> others) =>
        now < until || others.Any(o => o.Kind == kind && o.Enabled && o.StatusActive);

    private static string[] ChainMismatches(Func<DefensiveKind, long, long, IReadOnlyList<DefensiveSibling>, bool> impl)
    {
        var bad = new List<string>();
        foreach (var kind in Kinds)
        foreach (var (now, until) in Clocks)
        foreach (var others in SiblingLists())
            if (impl(kind, now, until, others) != Reference(kind, now, until, others))
                bad.Add($"{kind},now={now},until={until},others=[{string.Join(";", others.Select(o => $"{o.Kind}/{(o.Enabled ? "on" : "off")}/{(o.StatusActive ? "up" : "down")}"))}]");
        return [.. bad];
    }

    private static int ChainRowCount() => Kinds.Length * Clocks.Length * SiblingLists().Count();

    // ---- silence ----------------------------------------------------------------------------------------------
    private static readonly float[] Times = [float.NaN, -1f, 0f, 4.99f, 5f, 5.0001f, 30f, float.PositiveInfinity];

    // Independent reference: the stage may run only when none of the silencers holds and the character has been alive for more than 5 s.
    private static bool SilenceReference(bool guard, bool status, bool hostile, float alive) =>
        !(!guard && !status && hostile && alive > 5f);

    private static string[] SilenceMismatches(Func<bool, bool, bool, float, bool> impl)
    {
        var bad = new List<string>();
        foreach (var g in new[] { false, true })
        foreach (var s in new[] { false, true })
        foreach (var h in new[] { false, true })
        foreach (var t in Times)
            if (impl(g, s, h, t) != SilenceReference(g, s, h, t)) bad.Add($"guard={g},status={s},hostile={h},alive={t}");
        return [.. bad];
    }

    // ---- gate -------------------------------------------------------------------------------------------------
    private static bool GateReference(bool inForce, float ratio, float percent) =>
        !inForce || (!float.IsNaN(ratio) && !float.IsNaN(percent) && ratio < percent);

    private static readonly float[] Ratios = [0f, 0.4999f, 0.5f, 0.5001f, 0.6f, 0.75f, 1f, float.NaN];
    private static readonly float[] Percents = [0.5f, 0.6f, 1f, float.NaN];

    private static string[] GateMismatches(Func<bool, float, float, bool> impl)
    {
        var bad = new List<string>();
        foreach (var f in new[] { false, true })
        foreach (var r in Ratios)
        foreach (var p in Percents)
            if (impl(f, r, p) != GateReference(f, r, p)) bad.Add($"inForce={f},ratio={r},percent={p}");
        return [.. bad];
    }

    // ---- stage shape --------------------------------------------------------------------------------------------
    private static readonly Regex VirtualBool = new(@"\bvirtual\s+bool\s+(?<name>\w+)\s*\(", RegexOptions.Compiled);
    private static readonly Regex StaticBool = new(@"\bstatic\s+bool\s+(?<name>\w+)\s*\(", RegexOptions.Compiled);

    private static string BasicFile(params string[] parts) => Path.Combine([Program.SrcRoot, "PvPSolver.Basic", .. parts]);

    private static string MethodBody(string file, Regex head, string name) =>
        CsSource.Squash(CsSource.Methods(CsSource.Sanitize(File.ReadAllText(file)), Path.GetFileName(file), head).Single(m => m.Name == name).Body);

    private static string[] TopLevelArgs(string text, int openParen)
    {
        var close = CsSource.Match(text, openParen);
        var inner = text.Substring(openParen + 1, close - openParen - 1);
        var args = new List<string>();
        int depth = 0, start = 0;
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] is '(' or '[' or '{') depth++;
            else if (inner[i] is ')' or ']' or '}') depth--;
            else if (inner[i] == ',' && depth == 0) { args.Add(inner[start..i].Trim()); start = i + 1; }
        }

        args.Add(inner[start..].Trim());
        return [.. args];
    }

    private static List<string> StageProblems(string body)
    {
        var p = new List<string>();
        int Idx(string s) => body.IndexOf(s, StringComparison.Ordinal);
        var master = Idx("config.PvpDefensivesMaster");
        var silence = Idx("DefensiveSilence.Silenced(CustomRotation.HasPVPGuard, HasAny(DefensiveTable.SilenceStatuses), DataCenter.HasHostilesInMaxRange, player.TimeAlive())");
        var health = Idx("float health = player.GetHealthRatio();");
        var loop = Idx("foreach (DefensiveRow row in rows)");
        var modeSkip = Idx("row.Mode != DefensiveMode.Fire");
        var setting = Idx("if (!config.DefensiveActive(row))");
        var hp = Idx("DefensiveTrigger.ShouldUse(true, health, config.DefensivePercent(row))");
        var statuses = Idx("HasAny(row.ProvidesStatus) || HasAny(row.SuppressWhileStatus)");
        var chain = Idx("DefensiveChaining.Blocked(row.Kind, now, _lockoutUntilMs, SiblingsOf(rows, row, config))");
        var find = Idx("Find(r, row.ActionId)");
        var realGcd = Idx("action.Info.IsRealGCD");
        var canUse = Idx(".CanUse(");
        var lockout = Idx("_lockoutUntilMs = now + DefensiveChaining.LockoutMs;");

        void Need(bool ok, string what) { if (!ok) p.Add(what); }
        Need(master >= 0 && (loop < 0 || master < loop), "the master switch is tested before the loop");
        Need(Idx("!DataCenter.IsPvP") >= 0, "the stage returns outside PvP");
        Need(silence >= 0 && (loop < 0 || silence < loop), "the silence rule (PvP Guard up, silencing statuses, no hostile in range, time alive) runs before the loop");
        Need(health >= 0 && (loop < 0 || health < loop), "the HP is the character's own raw health ratio, read once before the loop");
        Need(modeSkip >= 0, "rows that are not Fire are skipped");
        Need(setting >= 0 && (canUse < 0 || setting < canUse), "the setting test (DefensiveActive) precedes CanUse");
        Need(modeSkip >= 0 && setting >= 0 && modeSkip < setting, "the mode test precedes the setting test");
        Need(hp > setting && setting >= 0, "the HP test (DefensiveTrigger.ShouldUse, strictly below) follows the setting test");
        Need(statuses > hp && hp >= 0, "the dedupe and suppress statuses are checked after the HP test");
        Need(chain > statuses && statuses >= 0, "the chaining rule is checked after the statuses");
        Need(find > chain && chain >= 0, "the action is looked up in the rotation's own action list after the chaining rule");
        Need(realGcd >= 0 && (canUse < 0 || realGcd < canUse), "a real GCD is never returned from the ability stage");
        Need(canUse > find && find >= 0, "CanUse follows the lookup");
        Need(lockout > canUse && canUse >= 0, "the 2.5 s lockout starts after a successful CanUse");
        Need(Idx("TargetType.Self") > canUse && canUse >= 0, "the action is aimed at the character itself");
        Need(Regex.Matches(body, @"\.CanUse\(").Count == 1, "exactly one CanUse call");

        if (canUse >= 0)
        {
            var open = body.IndexOf('(', canUse);
            var args = TopLevelArgs(body, open);
            var named = args.Where(a => Regex.IsMatch(a, @"^\w+\s*:")).Select(a => a.Replace(" ", "")).ToArray();
            string[] expected = ["usedUp:row.UsedUp", "skipTargetStatusNeedCheck:row.SkipTargetStatusNeed", "targetOverride:TargetType.Self"];
            Need(args.Length > 0 && args[0].StartsWith("out "), "the first argument is the out action");
            Need(named.OrderBy(a => a, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(a => a, StringComparer.Ordinal)),
                "CanUse carries exactly usedUp, skipTargetStatusNeedCheck and targetOverride: Self (no other skip flag): " + string.Join(" | ", args));
        }

        return p;
    }

    public static void Run()
    {
        Console.WriteLine("-- round 3 Part A, item 7: the generic defensive stage (T1 + T2) --");

        // ---- T1: chaining ----------------------------------------------------------------------------------------
        var bad = ChainMismatches((k, n, u, o) => DefensiveChaining.Blocked(k, n, u, o));
        Harness.Case("chaining: Blocked equals the reference over kind x clock x 0..2 sibling rows", bad.Length == 0,
            bad.Length == 0 ? $"{ChainRowCount()} rows" : $"{bad.Count()} differ, first: {bad[0]}");
        Harness.Case("chaining: the lockout is 2.5 s and ends exactly at its deadline (now == until is not locked)",
            DefensiveChaining.LockoutMs == 2500 && DefensiveChaining.Blocked(DefensiveKind.Shield, 2499, 2500, []) && !DefensiveChaining.Blocked(DefensiveKind.Shield, 2500, 2500, []));
        Harness.Case("chaining: no sibling means free, a disabled or status-less sibling of the same kind does not block, a different kind does not block",
            !DefensiveChaining.Blocked(DefensiveKind.Shield, 0, 0, [])
            && !DefensiveChaining.Blocked(DefensiveKind.Shield, 0, 0, [new(DefensiveKind.Shield, false, true)])
            && !DefensiveChaining.Blocked(DefensiveKind.Shield, 0, 0, [new(DefensiveKind.Shield, true, false)])
            && !DefensiveChaining.Blocked(DefensiveKind.Shield, 0, 0, [new(DefensiveKind.Mitigation, true, true)]));
        Harness.Case("chaining: an enabled sibling of the same kind with its status active blocks",
            DefensiveChaining.Blocked(DefensiveKind.Shield, 0, 0, [new(DefensiveKind.Shield, true, true)])
            && DefensiveChaining.Blocked(DefensiveKind.SelfHeal, 0, 0, [new(DefensiveKind.Shield, true, false), new(DefensiveKind.SelfHeal, true, true)]));
        // RED on the old behaviour: before this change the base Emergency block had no generic rows at all, i.e. nothing
        // chained or was blocked; the rule must disagree with "never blocked" right after a use and next to an active sibling.
        var noChaining = ChainMismatches((_, _, _, _) => false);
        Harness.Case("RED on old behaviour: a stage with no chaining rule (never blocked) disagrees with the table one second after a use and next to an active same-kind sibling",
            Reference(DefensiveKind.Shield, 1000, 3500, []) && Reference(DefensiveKind.Mitigation, 0, 0, [new(DefensiveKind.Mitigation, true, true)]) && noChaining.Length > 0,
            $"{noChaining.Length} of {ChainRowCount()} rows differ");
        Harness.Canary("a chaining rule that ignores the kind satisfies the table",
            ChainMismatches((k, n, u, o) => n < u || o.Any(s => s.Enabled && s.StatusActive)).Length == 0);
        Harness.Canary("a chaining rule that ignores whether the sibling is enabled satisfies the table",
            ChainMismatches((k, n, u, o) => n < u || o.Any(s => s.Kind == k && s.StatusActive)).Length == 0);
        Harness.Canary("a chaining rule that ignores whether the sibling's status is active satisfies the table",
            ChainMismatches((k, n, u, o) => n < u || o.Any(s => s.Kind == k && s.Enabled)).Length == 0);
        Harness.Canary("a chaining rule without the lockout satisfies the table",
            ChainMismatches((k, n, u, o) => o.Any(s => s.Kind == k && s.Enabled && s.StatusActive)).Length == 0);
        Harness.Canary("a chaining rule that keeps the lockout one tick too long (<=) satisfies the table",
            ChainMismatches((k, n, u, o) => n <= u || o.Any(s => s.Kind == k && s.Enabled && s.StatusActive)).Length == 0);

        // ---- T1: silence list ------------------------------------------------------------------------------------
        var silenceBad = SilenceMismatches(DefensiveSilence.Silenced);
        Harness.Case("silence: Silenced equals the reference over guard x status x hostile x 8 times alive", silenceBad.Length == 0,
            silenceBad.Length == 0 ? $"{2 * 2 * 2 * Times.Length} rows" : $"{silenceBad.Length} differ, first: {silenceBad[0]}");
        Harness.Case("silence: only an unguarded, status-free moment with a hostile in range and more than 5 s alive lets the stage run",
            !DefensiveSilence.Silenced(false, false, true, 5.0001f) && DefensiveSilence.Silenced(false, false, true, 5f) && DefensiveSilence.Silenced(false, false, true, float.NaN)
            && DefensiveSilence.Silenced(true, false, true, 30f) && DefensiveSilence.Silenced(false, true, true, 30f) && DefensiveSilence.Silenced(false, false, false, 30f));
        Harness.Case("silence: the time alive floor is 5 seconds", DefensiveSilence.MinTimeAliveSeconds == 5f);
        Harness.Canary("a silence rule that forgets PvP Guard satisfies the table", SilenceMismatches((g, s, h, t) => s || !h || !(t > 5f)).Length == 0);
        Harness.Canary("a silence rule that forgets the quiet moment (no hostile) satisfies the table", SilenceMismatches((g, s, h, t) => g || s || !(t > 5f)).Length == 0);
        Harness.Canary("a silence rule that lets 5 seconds exactly through satisfies the table", SilenceMismatches((g, s, h, t) => g || s || !h || t < 5f).Length == 0);
        Harness.Canary("a silence rule that lets an unknown time alive (NaN) through satisfies the table", SilenceMismatches((g, s, h, t) => g || s || !h || t <= 5f).Length == 0);

        // ---- T1: the Recuperate gate -----------------------------------------------------------------------------
        var gateBad = GateMismatches(DefensiveGate.RecuperateAllows);
        Harness.Case("Recuperate gate: equals the reference over in-force x 8 ratios x 4 percents", gateBad.Length == 0,
            gateBad.Length == 0 ? $"{2 * Ratios.Length * Percents.Length} rows" : $"{gateBad.Length} differ, first: {gateBad[0]}");
        Harness.Case("Recuperate gate: a no-op while the row is off, whatever the HP (default behaviour unchanged)",
            Ratios.All(r => Percents.All(p => DefensiveGate.RecuperateAllows(false, r, p))));
        Harness.Case("Recuperate gate: in force, strictly below the percentage only", DefensiveGate.RecuperateAllows(true, 0.4999f, 0.5f) && !DefensiveGate.RecuperateAllows(true, 0.5f, 0.5f) && !DefensiveGate.RecuperateAllows(true, float.NaN, 0.6f));
        Harness.Canary("a gate that is always closed when in force and ignores the row-off case satisfies the table", GateMismatches((f, r, p) => f && r < p).Length == 0);
        Harness.Canary("a gate that ignores the HP satisfies the table", GateMismatches((f, r, p) => true).Length == 0);

        // ---- T2: the call sites in the base stages -------------------------------------------------------------------
        var abilityFile = BasicFile("Rotations", "CustomRotation_Ability.cs");
        var gcdFile = BasicFile("Rotations", "CustomRotation_GCD.cs");
        var emergency = MethodBody(abilityFile, VirtualBool, "EmergencyAbility");
        var guard = emergency.IndexOf("GuardPvP.CanUse(out act)", StringComparison.Ordinal);
        var tryFire = emergency.IndexOf("GenericDefensives.TryFire(this, out act)", StringComparison.Ordinal);
        var recuperate = emergency.IndexOf("RecuperatePvP.CanUse(out act)", StringComparison.Ordinal);
        Harness.Case("EmergencyAbility (base PvP block): the generic call sits after the Guard branch and before Recuperate",
            guard >= 0 && tryFire > guard && recuperate > tryFire, $"guard@{guard}, TryFire@{tryFire}, Recuperate@{recuperate}");
        Harness.Case("EmergencyAbility: the generic call is `if (TryFire(...)) { return true; }` inside the PvP block",
            Regex.IsMatch(emergency, @"if \(DataCenter\.IsPvP\) \{.*if \(GenericDefensives\.TryFire\(this, out act\)\) \{ return true; \}.*RecuperatePvP\.CanUse"));
        var ability = File.ReadAllText(abilityFile);
        var gcd = File.ReadAllText(gcdFile);
        var allBasicRotations = Directory.GetFiles(BasicFile("Rotations"), "*.cs", SearchOption.AllDirectories).Sum(f => Regex.Matches(CsSource.Sanitize(File.ReadAllText(f)), @"GenericDefensives\.TryFire\(").Count);
        Harness.Case("exactly one TryFire call in the rotation base code, and none in EmergencyGCD", allBasicRotations == 1 && !gcd.Contains("TryFire"), $"{allBasicRotations} call(s)");
        var pvpJobFiles = SourceFiles.AllPvpRotationFiles().Count(f => File.ReadAllText(f).Contains("GenericDefensives"));
        Harness.Case("no job rotation file calls the generic stage (it runs once, from the base stage)", pvpJobFiles == 0);
        foreach (var (name, text) in new[] { ("CustomRotation_Ability.cs", ability), ("CustomRotation_GCD.cs", gcd) })
        {
            var san = CsSource.Sanitize(text);
            var gated = Regex.Matches(san, @"RecuperatePvP\.CanUse\(out act\) && GenericDefensives\.RecuperateGate\(Player\)").Count;
            var all = Regex.Matches(san, @"RecuperatePvP\.CanUse\(").Count;
            Harness.Case($"{name}: the Recuperate call carries the gate (`&& GenericDefensives.RecuperateGate(Player)`)", all == 1 && gated == 1, $"{gated} gated of {all}");
        }

        // ---- T2: the stage itself --------------------------------------------------------------------------------
        var stageFile = BasicFile("Actions", "GenericDefensives.cs");
        var stageText = "";
        var stage = "";
        if (!File.Exists(stageFile))
        {
            Harness.Case("Actions/GenericDefensives.cs exists", false, stageFile);
        }
        else
        {
            stageText = File.ReadAllText(stageFile);
            stage = MethodBody(stageFile, StaticBool, "TryFire");
            var problems = StageProblems(stage);
            Harness.Case("TryFire: master switch, silence rule, mode filter, setting test before CanUse, HP test, statuses, chaining, GCD filter, lockout and exactly the row's two skip flags",
                problems.Count == 0, string.Join("; ", problems));

            // The stage never reads a Param or Gate row's action: the mode filter is the only way past the loop head.
            Harness.Case("TryFire skips rows of mode Param and Gate (`row.Mode != DefensiveMode.Fire` continues)",
                Regex.IsMatch(stage, @"if \(row\.Mode != DefensiveMode\.Fire\) \{ continue; \}"));

            Harness.Case("the row's action is looked up in the rotation's own action list (r.AllBaseActions, matched by ID)",
                Regex.IsMatch(CsSource.Squash(CsSource.Sanitize(stageText)), @"foreach \(IBaseAction candidate in r\.AllBaseActions\) \{ if \(candidate\.ID == actionId\)"));

            var gateMethod = MethodBody(stageFile, StaticBool, "RecuperateGate");
            Harness.Case("RecuperateGate reads the Recuperate row through DefensiveGate.RecuperateAllows",
                gateMethod.Contains("DefensiveTable.Recuperate") && gateMethod.Contains("DefensiveGate.RecuperateAllows(config.DefensiveActive(row)"));
        }

        // Canaries: the same checker must refuse a stage that ignores Enabled, fires during Guard, or adds a skip flag.
        if (stage.Length == 0) stage = "foreach (DefensiveRow row in rows) { }";
        Harness.Canary("a stage that ignores the Enabled setting passes the shape check",
            StageProblems(stage.Replace("if (!config.DefensiveActive(row))", "if (false)")).Count == 0);
        Harness.Canary("a stage that fires during PvP Guard passes the shape check",
            StageProblems(stage.Replace("CustomRotation.HasPVPGuard", "false")).Count == 0);
        Harness.Canary("a stage that asks CanUse before the setting test passes the shape check",
            StageProblems(stage.Replace("if (!config.DefensiveActive(row))", "if (action != null && action.CanUse(out IAction early) && !config.DefensiveActive(row))")).Count == 0);
        Harness.Canary("a stage that adds skipStatusNeed to CanUse passes the shape check",
            StageProblems(stage.Replace("targetOverride: TargetType.Self", "skipStatusNeed: true, targetOverride: TargetType.Self")).Count == 0);
        Harness.Canary("a stage that uses Param rows (no mode filter) passes the shape check",
            StageProblems(stage.Replace("row.Mode != DefensiveMode.Fire", "row.Mode == DefensiveMode.Gate")).Count == 0);
    }
}
