using System.Text.Json;
using System.Text.RegularExpressions;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     "Let Jev call the shots" (notebook Projects/PvP Jev Co-pilot). Jev, reached through the PvP Jev relay on
///     the home network, may put one enemy first in the attack order and may veto a PvP Limit Break; it never
///     fires one, and a stale or missing answer leaves PvP Solver on its own logic.
///     T1: the pure <see cref="JevCall"/> core (freshness, parse, snapshot shape). T2: the four call sites
///     (target order, Limit Break check, frame update, config and settings row) are shape-checked, not run.
/// </summary>
internal static class JevCopilot
{
    public static void Run()
    {
        Console.WriteLine("-- Jev co-pilot (T1 + T2) --");

        // ---- T1: freshness --------------------------------------------------------------------------------
        var a = new JevAnswer(4242UL, true, 100.0);
        Harness.Case("a fresh answer gives its focus and its hold", JevCall.FocusPick(a, 101.0) == 4242UL && JevCall.HoldLimitBreak(a, 101.0));
        Harness.Case("an answer exactly FreshSeconds old still counts", JevCall.FocusPick(a, 100.0 + JevCall.FreshSeconds) == 4242UL);
        Harness.Case("a stale answer gives no focus and no hold",
            JevCall.FocusPick(a, 100.0 + JevCall.FreshSeconds + 0.01) == null && !JevCall.HoldLimitBreak(a, 103.5));
        Harness.Case("an answer stamped in the future counts as no answer", JevCall.FocusPick(a, 99.0) == null && !JevCall.HoldLimitBreak(a, 99.0));
        Harness.Case("no answer gives no focus and no hold", JevCall.FocusPick(null, 1.0) == null && !JevCall.HoldLimitBreak(null, 1.0));
        Harness.Case("a fresh answer without hold never holds", !JevCall.HoldLimitBreak(a with { HoldLimitBreak = false }, 101.0));

        // ---- T1: parse --------------------------------------------------------------------------------------
        var p = JevCall.Parse("{\"focus\": 4242, \"focus_conf\": 0.98, \"lb\": \"hold\", \"lb_go\": 0.22, \"ms\": 240}", 7.0);
        Harness.Case("parse reads focus, hold and stamps the time", p is { Focus: 4242UL, HoldLimitBreak: true, ReceivedAt: 7.0 });
        var none = JevCall.Parse("{\"focus\": null, \"lb\": null, \"lb_go\": 0.59}", 7.0);
        Harness.Case("null focus and null lb parse as no focus and no hold", none is { Focus: null, HoldLimitBreak: false });
        var go = JevCall.Parse("{\"focus\": 1, \"lb\": \"go\"}", 7.0);
        Harness.Case("an lb of \"go\" is never a hold (Jev cannot fire a Limit Break)", go is { HoldLimitBreak: false });
        Harness.Case("garbage, a JSON array and a negative id parse as no answer or no focus",
            JevCall.Parse("not json", 1.0) == null && JevCall.Parse("[1,2]", 1.0) == null && JevCall.Parse("{\"focus\": -5}", 1.0) is { Focus: null });
        Harness.Case("an error body from the relay parses as no focus and no hold",
            JevCall.Parse("{\"error\": \"rate or daily cap\"}", 1.0) is { Focus: null, HoldLimitBreak: false });

        // ---- T1: snapshot -----------------------------------------------------------------------------------
        var enemies = Enumerable.Range(1, 12).Select(i => new JevEnemy((ulong)(1000 + i), "WHM", 0.3333f, 40f - i, i == 12, false, false, i == 11)).ToList();
        var allies = new[] { new JevAlly("PLD", 0.5f, 3f) };
        using var snap = JsonDocument.Parse(JevCall.SnapshotJson("MCH", 1.5f, true, enemies, allies));
        var root = snap.RootElement;
        var sent = root.GetProperty("enemies").EnumerateArray().ToList();
        Harness.Case("the snapshot keeps the nearest MaxEnemies enemies, nearest first",
            sent.Count == JevCall.MaxEnemies && sent[0].GetProperty("id").GetUInt64() == 1012UL && sent[0].GetProperty("guard").GetBoolean());
        Harness.Case("ratios are clamped and rounded; LB ready is 1", root.GetProperty("hp").GetSingle() == 1f && root.GetProperty("lb").GetSingle() == 1f
            && Math.Abs(sent[1].GetProperty("hp").GetSingle() - 0.33f) < 1e-6 && sent[1].GetProperty("on_me").GetBoolean());
        var keys = sent[0].EnumerateObject().Select(o => o.Name).ToArray();
        Harness.Case("each enemy carries exactly id, job, hp, dist, guard, invuln, cc, on_me (no names)",
            keys.SequenceEqual(["id", "job", "hp", "dist", "guard", "invuln", "cc", "on_me"]), string.Join(",", keys));
        Harness.Case("the relay's field names match: v, job, hp, lb, enemies, allies",
            root.EnumerateObject().Select(o => o.Name).SequenceEqual(["v", "job", "hp", "lb", "enemies", "allies"]));
        Harness.Canary("canary: a stale answer still focusing would be caught", JevCall.FocusPick(a, 200.0) == 4242UL);

        // ---- T2: call sites ---------------------------------------------------------------------------------
        string Src(params string[] parts) => CsSource.Sanitize(File.ReadAllText(Path.Combine([Program.SrcRoot, .. parts])));

        var target = Src("PvPSolver.Basic", "Actions", "ActionTargetInfo.cs");
        int sticky = target.IndexOf("FindStickyTarget(filtered)", StringComparison.Ordinal);
        int jev = target.IndexOf("JevCall.FocusPick(JevCaller.Latest, JevCaller.Now)", StringComparison.Ordinal);
        int last = target.IndexOf("foreach (var obj in filtered)", sticky < 0 ? 0 : sticky, StringComparison.Ordinal);
        Harness.Case("target order: the Jev focus is read after the sticky-target hold and before the plain order",
            sticky > 0 && jev > sticky && last > 0 && jev < last && target.IndexOf("JevCall.FocusPick", jev + 1, StringComparison.Ordinal) < 0);
        Harness.Case("target order: the Jev focus is gated on the switch and PvP",
            Regex.IsMatch(target, @"Service\.Config\.JevCallsShots\s*&&\s*DataCenter\.IsPvP\s*&&\s*JevCall\.FocusPick"));

        var basic = Src("PvPSolver.Basic", "Actions", "ActionBasicInfo.cs");
        Harness.Case("Limit Break: the only Jev effect in BasicCheck is a veto on a PvP Limit Break",
            Regex.IsMatch(basic, @"if\s*\(\s*IsPvPLimitBreak\s*&&\s*DataCenter\.IsPvP\s*&&\s*Service\.Config\.JevCallsShots\s*&&\s*JevCall\.HoldLimitBreak\([^)]*\)\s*\)\s*\{\s*return\s+false;\s*\}")
            && Regex.Matches(basic, @"JevCall\.").Count == 1);

        var caller = Src("PvPSolver.Basic", "Helpers", "JevCaller.cs");
        Harness.Case("caller: readiness asks the action manager directly, never PvP Solver's own CanUse (no veto feedback loop)",
            caller.Contains("GetActionStatus(ActionType.Action, action.Info.AdjustedID)") && !Regex.IsMatch(caller, @"\.CanUse\s*\("));
        Harness.Case("caller: off unless the switch, PvP and combat", Regex.IsMatch(caller, @"!Service\.Config\.JevCallsShots\s*\|\|\s*!DataCenter\.IsPvP\s*\|\|\s*!DataCenter\.InCombat"));
        Harness.Case("caller: sends no player names", !Regex.IsMatch(caller, @"\.Name\b"));

        var updater = Src("PvPSolver", "Updaters", "MajorUpdater.cs");
        Harness.Case("frame update calls JevCaller.Update once", Regex.Matches(updater, @"JevCaller\.Update\(\)").Count == 1);

        var configs = Src("PvPSolver.Basic", "Configuration", "Configs.cs");
        Harness.Case("config: the switch defaults off", Regex.IsMatch(configs, @"private static readonly bool _jevCallsShots = false;"));
        Harness.Case("settings: the Targeting tab has the switch, default off",
            PvpSettingsCatalog.All.Any(s => s.Property == "JevCallsShots" && s.Tab == PvpSettingsCatalog.Targeting && !s.DefaultOn));
    }
}
