using System.Text.Json;
using System.Text.RegularExpressions;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     "Let Jev call the shots" (notebook Projects/PvP Jev Co-pilot). Jev, reached through the PvP Jev relay on
///     the home network, may put one enemy first in the attack order, among the candidates the current targeting
///     mode allows; a stale or missing answer leaves PvP Solver on its own logic. PvP Solver never fires a PvP
///     Limit Break (no caller reads LimitBreakPvP), so the feature has no Limit Break part.
///     T1: the pure <see cref="JevCall"/> core (freshness, parse, snapshot shape). T2: the call sites (target
///     order, frame update, the HTTP call's thread and proxy, config and settings row) are shape-checked, not run.
/// </summary>
internal static class JevCopilot
{
    public static void Run()
    {
        Console.WriteLine("-- Jev co-pilot (T1 + T2) --");

        // ---- T1: freshness --------------------------------------------------------------------------------
        var a = new JevAnswer(4242UL, 100.0);
        Harness.Case("a fresh answer gives its focus", JevCall.FocusPick(a, 101.0) == 4242UL);
        Harness.Case("an answer exactly FreshSeconds old still counts", JevCall.FocusPick(a, 100.0 + JevCall.FreshSeconds) == 4242UL);
        Harness.Case("a stale answer gives no focus", JevCall.FocusPick(a, 100.0 + JevCall.FreshSeconds + 0.01) == null);
        Harness.Case("an answer stamped in the future counts as no answer", JevCall.FocusPick(a, 99.0) == null);
        Harness.Case("no answer gives no focus", JevCall.FocusPick(null, 1.0) == null);

        // ---- T1: parse --------------------------------------------------------------------------------------
        Harness.Case("parse reads the focus and stamps the time",
            JevCall.Parse("{\"focus\": 4242, \"focus_conf\": 0.98, \"lb\": null, \"ms\": 240}", 7.0) is { Focus: 4242UL, ReceivedAt: 7.0 });
        Harness.Case("a null focus parses as no focus", JevCall.Parse("{\"focus\": null}", 7.0) is { Focus: null });
        Harness.Case("garbage, a JSON array and a negative id parse as no answer or no focus",
            JevCall.Parse("not json", 1.0) == null && JevCall.Parse("[1,2]", 1.0) == null && JevCall.Parse("{\"focus\": -5}", 1.0) is { Focus: null });
        Harness.Case("an error body from the relay parses as no focus",
            JevCall.Parse("{\"error\": \"rate or daily cap\"}", 1.0) is { Focus: null });

        // ---- T1: snapshot -----------------------------------------------------------------------------------
        var enemies = Enumerable.Range(1, 12).Select(i => new JevEnemy((ulong)(1000 + i), "WHM", 0.3333f, 40f - i, i == 12, false, false, i == 11)).ToList();
        var allies = new[] { new JevAlly("PLD", 0.5f, 3f) };
        using var snap = JsonDocument.Parse(JevCall.SnapshotJson("MCH", 1.5f, enemies, allies));
        var root = snap.RootElement;
        var sent = root.GetProperty("enemies").EnumerateArray().ToList();
        Harness.Case("the snapshot keeps the nearest MaxEnemies enemies, nearest first",
            sent.Count == JevCall.MaxEnemies && sent[0].GetProperty("id").GetUInt64() == 1012UL && sent[0].GetProperty("guard").GetBoolean());
        Harness.Case("ratios are clamped and rounded", root.GetProperty("hp").GetSingle() == 1f
            && Math.Abs(sent[1].GetProperty("hp").GetSingle() - 0.33f) < 1e-6 && sent[1].GetProperty("on_me").GetBoolean());
        var keys = sent[0].EnumerateObject().Select(o => o.Name).ToArray();
        Harness.Case("each enemy carries exactly id, job, hp, dist, guard, invuln, cc, on_me (no names)",
            keys.SequenceEqual(["id", "job", "hp", "dist", "guard", "invuln", "cc", "on_me"]), string.Join(",", keys));
        Harness.Case("the snapshot fields are v, job, hp, enemies, allies (no Limit Break field, so the relay never asks one)",
            root.EnumerateObject().Select(o => o.Name).SequenceEqual(["v", "job", "hp", "enemies", "allies"]));
        Harness.Canary("canary: a stale answer still focusing would be caught", JevCall.FocusPick(a, 200.0) == 4242UL);

        // ---- T2: call sites ---------------------------------------------------------------------------------
        string Src(params string[] parts) => CsSource.Sanitize(File.ReadAllText(Path.Combine([Program.SrcRoot, .. parts])));

        var target = Src("PvPSolver.Basic", "Actions", "ActionTargetInfo.cs");
        int sticky = target.IndexOf("FindStickyTarget(filtered)", StringComparison.Ordinal);
        int jev = target.IndexOf("JevCall.FocusPick(JevCaller.Latest, JevCaller.Now)", StringComparison.Ordinal);
        int last = target.IndexOf("foreach (var obj in filtered)", sticky < 0 ? 0 : sticky, StringComparison.Ordinal);
        Harness.Case("target order: the Jev focus is read after the sticky-target hold and before the plain order",
            sticky > 0 && jev > sticky && last > 0 && jev < last && target.IndexOf("JevCall.FocusPick", jev + 1, StringComparison.Ordinal) < 0);
        Harness.Case("target order: the Jev focus is gated on the switch and PvP, and only returns a member of filtered",
            Regex.IsMatch(target, @"Service\.Config\.JevCallsShots\s*&&\s*DataCenter\.IsPvP\s*&&\s*JevCall\.FocusPick")
            && Regex.IsMatch(target, @"foreach\s*\(\s*var obj in filtered\s*\)\s*\{\s*if\s*\(\s*obj\.GameObjectId\s*==\s*jevFocus\s*\)"));

        var basic = Src("PvPSolver.Basic", "Actions", "ActionBasicInfo.cs");
        Harness.Case("no Jev code in BasicCheck: Jev never touches whether an action may be used", !basic.Contains("Jev"));

        var caller = Src("PvPSolver.Basic", "Helpers", "JevCaller.cs");
        Harness.Case("caller: off unless the switch, PvP and combat", Regex.IsMatch(caller, @"!Service\.Config\.JevCallsShots\s*\|\|\s*!DataCenter\.IsPvP\s*\|\|\s*!DataCenter\.InCombat"));
        Harness.Case("caller: the HTTP call starts on a pool thread and the client never looks up a proxy",
            Regex.IsMatch(caller, @"Task\.Run\(\s*\(\)\s*=>\s*SendAsync\(") && Regex.IsMatch(caller, @"UseProxy\s*=\s*false")
            && !Regex.IsMatch(caller, @"(?<!Task\.Run\(\(\) => )_\s*=\s*SendAsync\("));
        Harness.Case("caller: sends no player names", !Regex.IsMatch(caller, @"\.Name\b"));

        var updater = Src("PvPSolver", "Updaters", "MajorUpdater.cs");
        Harness.Case("frame update calls JevCaller.Update once", Regex.Matches(updater, @"JevCaller\.Update\(\)").Count == 1);

        var configs = Src("PvPSolver.Basic", "Configuration", "Configs.cs");
        Harness.Case("config: the switch defaults off", Regex.IsMatch(configs, @"private static readonly bool _jevCallsShots = false;"));
        Harness.Case("settings: the Targeting tab has the switch, default off, and its help makes no Limit Break promise",
            PvpSettingsCatalog.All.Any(s => s.Property == "JevCallsShots" && s.Tab == PvpSettingsCatalog.Targeting && !s.DefaultOn && !s.Help.Contains("Limit Break")));
    }
}
