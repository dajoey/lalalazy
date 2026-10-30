using System.Text.Json;
using System.Text.RegularExpressions;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     "Let Jev call the shots" (notebook Projects/PvP Jev Co-pilot). Jev, reached through the PvP Jev relay on the
///     home network, puts one enemy first in the attack order (among the candidates the current targeting mode
///     allows) and fires the job's PvP Limit Break when its fresh answer says so and the game confirms the Limit
///     Break can be used on that target. Without a fresh answer PvP Solver is on its own logic, which never fires
///     a PvP Limit Break.
///     T1: the pure <see cref="JevCall"/> core (freshness, the retry guard, parse, snapshot shape). T2: the call sites
///     (target order, the Limit Break hook in Invoke, the game check before firing, the HTTP call's thread and proxy,
///     config and settings row) are shape-checked, not run.
/// </summary>
internal static class JevCopilot
{
    public static void Run()
    {
        Console.WriteLine("-- Jev co-pilot (T1 + T2) --");

        // ---- T1: freshness and the Limit Break retry guard -------------------------------------------------
        var a = new JevAnswer(4242UL, true, 4242UL, 100.0);
        Harness.Case("a fresh answer gives its focus", JevCall.FocusPick(a, 101.0) == 4242UL);
        Harness.Case("an answer exactly FreshSeconds old still counts", JevCall.FocusPick(a, 100.0 + JevCall.FreshSeconds) == 4242UL);
        Harness.Case("a stale answer gives no focus and no fire",
            JevCall.FocusPick(a, 103.01) == null && !JevCall.ShouldFireLimitBreak(a, 103.01, double.MinValue, out _));
        Harness.Case("an answer stamped in the future counts as no answer",
            JevCall.FocusPick(a, 99.0) == null && !JevCall.ShouldFireLimitBreak(a, 99.0, double.MinValue, out _));
        Harness.Case("a fresh fire answer fires at its target",
            JevCall.ShouldFireLimitBreak(a, 101.0, double.MinValue, out var t) && t == 4242UL);
        Harness.Case("a fire with no enemy target fires with a null target",
            JevCall.ShouldFireLimitBreak(a with { LimitBreakTarget = null }, 101.0, double.MinValue, out var t2) && t2 == null);
        Harness.Case("no second attempt within FireRetrySeconds of the last (a rejected use cannot stall the rotation)",
            !JevCall.ShouldFireLimitBreak(a, 101.0, 101.0 - JevCall.FireRetrySeconds + 0.01, out _)
            && JevCall.ShouldFireLimitBreak(a, 101.0, 101.0 - JevCall.FireRetrySeconds, out _));
        Harness.Case("an answer without fire never fires", !JevCall.ShouldFireLimitBreak(a with { FireLimitBreak = false }, 101.0, double.MinValue, out _));
        Harness.Case("no answer: no focus, no fire", JevCall.FocusPick(null, 1.0) == null && !JevCall.ShouldFireLimitBreak(null, 1.0, double.MinValue, out _));

        // ---- T1: parse --------------------------------------------------------------------------------------
        Harness.Case("parse reads focus, fire and the Limit Break target",
            JevCall.Parse("{\"focus\": 11, \"lb\": \"fire\", \"lb_target\": 11, \"lb_hold_p\": 0.05}", 7.0) is { Focus: 11UL, FireLimitBreak: true, LimitBreakTarget: 11UL, ReceivedAt: 7.0 });
        Harness.Case("a fire with a null target parses as fire with no target",
            JevCall.Parse("{\"focus\": null, \"lb\": \"fire\", \"lb_target\": null}", 7.0) is { FireLimitBreak: true, LimitBreakTarget: null });
        Harness.Case("a null lb, or any lb other than \"fire\", never fires, and its stray target is dropped",
            JevCall.Parse("{\"focus\": 1, \"lb\": null, \"lb_target\": 1}", 7.0) is { FireLimitBreak: false, LimitBreakTarget: null }
            && JevCall.Parse("{\"lb\": \"hold\"}", 7.0) is { FireLimitBreak: false } && JevCall.Parse("{\"lb\": \"go\"}", 7.0) is { FireLimitBreak: false });
        Harness.Case("garbage, a JSON array and a negative id parse as no answer or no focus",
            JevCall.Parse("not json", 1.0) == null && JevCall.Parse("[1,2]", 1.0) == null && JevCall.Parse("{\"focus\": -5}", 1.0) is { Focus: null });
        Harness.Case("an error body from the relay parses as no focus and no fire",
            JevCall.Parse("{\"error\": \"rate or daily cap\"}", 1.0) is { Focus: null, FireLimitBreak: false });

        // ---- T1: snapshot -----------------------------------------------------------------------------------
        var enemies = Enumerable.Range(1, 12).Select(i => new JevEnemy((ulong)(1000 + i), "WHM", 0.3333f, 18000u, 40f - i, 1f, 40f - i,
            i == 12, false, false, i == 11, 2, false, i == 12)).ToList();
        var allies = new[] { new JevAlly("PLD", 0.5f, 3f, 1f, 2.8f, 1) };
        var self = new JevSelf("MCH", 1.5f, 52000u, 1.23456f, false, false);
        using var snap = JsonDocument.Parse(JevCall.SnapshotJson(self, enemies, allies));
        var root = snap.RootElement;
        var sent = root.GetProperty("enemies").EnumerateArray().ToList();
        Harness.Case("the snapshot keeps the nearest MaxEnemies enemies, nearest first",
            sent.Count == JevCall.MaxEnemies && sent[0].GetProperty("id").GetUInt64() == 1012UL && sent[0].GetProperty("guard").GetBoolean());
        Harness.Case("ratios are clamped and rounded; facing rounded", root.GetProperty("hp").GetSingle() == 1f
            && Math.Abs(sent[1].GetProperty("hp").GetSingle() - 0.33f) < 1e-6 && Math.Abs(root.GetProperty("facing").GetSingle() - 1.235f) < 1e-4);
        Harness.Case("the Limit Break is ready when the game allows it on the player or on any enemy sent",
            root.GetProperty("lb").GetProperty("ready").GetBoolean() && !root.GetProperty("lb").GetProperty("self_ok").GetBoolean());
        var keys = sent[0].EnumerateObject().Select(o => o.Name).ToArray();
        Harness.Case("each enemy carries exactly the relay's fields (no names)",
            keys.SequenceEqual(["id", "job", "hp", "hp_now", "dist", "dx", "dz", "guard", "invuln", "cc", "on_me", "allies_on", "kuzushi", "lb_ok"]), string.Join(",", keys));
        Harness.Case("the snapshot is v2 with v, job, hp, hp_now, facing, me_cc, my_target, used, lb, enemies, allies",
            root.GetProperty("v").GetInt32() == 2
            && root.EnumerateObject().Select(o => o.Name).SequenceEqual(["v", "job", "hp", "hp_now", "facing", "me_cc", "my_target", "used", "lb", "enemies", "allies"]));
        Harness.Case("with nothing recorded, my_target is null and used is empty",
            root.GetProperty("my_target").ValueKind == JsonValueKind.Null && root.GetProperty("used").GetArrayLength() == 0);
        var withUse = self with { MyTarget = 1001UL, Used = [new JevUsed(29400u, 1001UL), new JevUsed(29401u, null)] };
        using (var s2 = JsonDocument.Parse(JevCall.SnapshotJson(withUse, enemies, allies)))
        {
            var used = s2.RootElement.GetProperty("used").EnumerateArray().ToList();
            Harness.Case("used actions carry the action id and its target (null when none), and my_target the player's target",
                s2.RootElement.GetProperty("my_target").GetUInt64() == 1001UL && used.Count == 2
                && used[0].GetProperty("a").GetUInt32() == 29400u && used[0].GetProperty("t").GetUInt64() == 1001UL
                && used[1].GetProperty("t").ValueKind == JsonValueKind.Null);
        }
        Harness.Case("each ally carries job, hp, dist, dx, dz, enemies_on",
            root.GetProperty("allies")[0].EnumerateObject().Select(o => o.Name).SequenceEqual(["job", "hp", "dist", "dx", "dz", "enemies_on"]));
        Harness.Canary("canary: a stale answer still firing would be caught", JevCall.ShouldFireLimitBreak(a, 200.0, double.MinValue, out _));

        // ---- T2: call sites ---------------------------------------------------------------------------------
        string Src(params string[] parts) => CsSource.Sanitize(File.ReadAllText(Path.Combine([Program.SrcRoot, .. parts])));

        var target = Src("PvPSolver.Basic", "Actions", "ActionTargetInfo.cs");
        int sticky = target.IndexOf("FindStickyTarget(filtered)", StringComparison.Ordinal);
        int jev = target.IndexOf("JevCall.FocusPick(JevCaller.Latest, JevCaller.Now)", StringComparison.Ordinal);
        int last = target.IndexOf("foreach (var obj in filtered)", sticky < 0 ? 0 : sticky, StringComparison.Ordinal);
        Harness.Case("target order: the Jev focus is read after the sticky-target hold and before the plain order",
            sticky > 0 && jev > sticky && last > 0 && jev < last && target.IndexOf("JevCall.FocusPick", jev + 1, StringComparison.Ordinal) < 0);
        Harness.Case("target order: gated on the switch and PvP, and only returns a member of filtered",
            Regex.IsMatch(target, @"Service\.Config\.JevCallsShots\s*&&\s*DataCenter\.IsPvP\s*&&\s*JevCall\.FocusPick")
            && Regex.IsMatch(target, @"foreach\s*\(\s*var obj in filtered\s*\)\s*\{\s*if\s*\(\s*obj\.GameObjectId\s*==\s*jevFocus\s*\)"));

        var invoke = Src("PvPSolver.Basic", "Rotations", "CustomRotation_Invoke.cs");
        int hook = invoke.IndexOf("JevCaller.TryLimitBreak(LimitBreakPvP, out IAction? jevLimitBreak)", StringComparison.Ordinal);
        int gcd = invoke.IndexOf("gcdAction = GCD();", StringComparison.Ordinal);
        Harness.Case("Invoke: the Limit Break hook runs before the GCD pick, gated on PvP and the switch, and returns the Limit Break",
            hook > 0 && gcd > hook && Regex.IsMatch(invoke, @"if\s*\(\s*DataCenter\.IsPvP\s*&&\s*Service\.Config\.JevCallsShots\s*&&\s*JevCaller\.TryLimitBreak\(LimitBreakPvP, out IAction\? jevLimitBreak\)\s*\)\s*\{\s*return jevLimitBreak;\s*\}"));

        var basic = Src("PvPSolver.Basic", "Actions", "ActionBasicInfo.cs");
        Harness.Case("no Jev code in BasicCheck: Jev never blocks an action", !basic.Contains("Jev"));

        var caller = Src("PvPSolver.Basic", "Helpers", "JevCaller.cs");
        // String literals are blanked by Sanitize; the log-line checks read the raw file.
        var callerRaw = File.ReadAllText(Path.Combine(Program.SrcRoot, "PvPSolver.Basic", "Helpers", "JevCaller.cs"));
        int should = caller.IndexOf("JevCall.ShouldFireLimitBreak(Latest, now, _lastFireAttempt, out ulong? target)", StringComparison.Ordinal);
        int window = caller.IndexOf("_fireUntil = now + FireWindowSeconds;", StringComparison.Ordinal);
        int gap = caller.IndexOf("_lastFireAttempt = _fireUntil;", StringComparison.Ordinal);
        int usable = caller.IndexOf("if (!LimitBreakUsable(limitBreak, aim ?? Player.Object.GameObjectId))", StringComparison.Ordinal);
        int canUse = caller.IndexOf("limitBreak.CanUse(", StringComparison.Ordinal);
        Harness.Case("TryLimitBreak: a fresh answer opens a window, the retry gap counts from its end, the game's check runs every frame before CanUse",
            should > 0 && window > should && gap > window && usable > gap && canUse > usable);
        Harness.Case("TryLimitBreak: the window closes as soon as the Limit Break is used or unusable",
            Regex.IsMatch(caller, @"if\s*\(!LimitBreakUsable\(limitBreak, aim \?\? Player\.Object\.GameObjectId\)\)\s*\{\s*_fireUntil\s*=\s*now;"));
        Harness.Case("TryLimitBreak: an enemy-targeted fire is aimed at Jev's enemy when the action picked another (Use fires at Target)",
            Regex.IsMatch(caller, @"limitBreak\.Target\.Target\?\.GameObjectId\s*!=\s*aim")
            && Regex.IsMatch(caller, @"limitBreak\.Target\s*=\s*new TargetResult\(chosen, \[chosen\], chosen\.Position\);")
            && Regex.IsMatch(caller, @"b\.GameObjectId == aim && !b\.IsDead"));
        Harness.Case("TryLimitBreak: a skipped fire logs one reason line per window (canuse or target-gone)",
            callerRaw.Contains("SkipOnce(\"canuse\")") && callerRaw.Contains("SkipOnce(\"target-gone\")") && callerRaw.Contains("JV|lb-fire-skip reason="));
        Harness.Case("LimitBreakUsable: not once the Limit Break has turned into its follow-up action (AdjustedID != ID)",
            Regex.IsMatch(caller, @"limitBreak\.Info\.AdjustedID\s*!=\s*limitBreak\.ID"));
        Harness.Case("LimitBreakUsable: the game check ignores casting and the GCD, and a placed Limit Break is checked with no target",
            Regex.IsMatch(caller, @"GetActionStatus\(ActionType\.Action, limitBreak\.ID, onWhom, checkRecastActive: false, checkCastingActive: false\)\s*==\s*0")
            && Regex.IsMatch(caller, @"limitBreak\.Info\.TargetAreaAction \? NoTarget : targetId") && caller.Contains("NoTarget = 0xE0000000"));
        Harness.Case("snapshot readiness uses the same check as firing", caller.Contains("bool LbOk(ulong id) => LimitBreakUsable(lb, id);"));
        int pending = target.IndexOf("JevCaller.PendingLimitBreakTarget is ulong jevLimitBreak", StringComparison.Ordinal);
        Harness.Case("target order: a pending Limit Break target goes first, even before the sticky-target hold",
            pending > 0 && sticky > pending);
        foreach (var (file, method) in new[] { ("MonkRotation.cs", "ModifyMeteodrivePvP"), ("NinjaRotation.cs", "ModifySeitonTenchuPvP"), ("SamuraiRotation.cs", "ModifyZantetsukenPvP") })
        {
            var rot = Src("PvPSolver.Basic", "Rotations", "Basic", file);
            Harness.Case($"{method}: the Guard-piercing Limit Break may target a Guarded enemy (IgnoreGuard)",
                Regex.IsMatch(rot, @"static partial void " + method + @"\(ref ActionSetting setting\)\s*\{\s*setting\.IgnoreGuard\s*=\s*true;\s*\}"));
        }
        Harness.Case("caller: off unless the switch, PvP and combat", Regex.IsMatch(caller, @"!Service\.Config\.JevCallsShots\s*\|\|\s*!DataCenter\.IsPvP\s*\|\|\s*!DataCenter\.InCombat"));
        Harness.Case("caller: the HTTP call starts on a pool thread and the client never looks up a proxy",
            Regex.IsMatch(caller, @"Task\.Run\(\s*\(\)\s*=>\s*SendAsync\(") && Regex.IsMatch(caller, @"UseProxy\s*=\s*false"));
        Harness.Case("caller: sends no names", !Regex.IsMatch(caller, @"\.Name\b"));

        var watcher = Src("PvPSolver", "Watcher.cs");
        Harness.Case("Watcher records each used action and its target for the snapshot, right after AddActionRec",
            Regex.IsMatch(watcher, @"DataCenter\.AddActionRec\(action!\.Value\);\s*JevCaller\.NoteActionUsed\(action\.Value\.RowId, tar\?\.GameObjectId\);")
            && Regex.Matches(watcher, @"JevCaller\.NoteActionUsed\(").Count == 1);
        Harness.Case("NoteActionUsed keeps nothing with the switch off or outside PvP, and at most 16 per snapshot",
            Regex.IsMatch(caller, @"public static void NoteActionUsed\(uint actionId, ulong\? targetId\)\s*\{\s*if\s*\(!Service\.Config\.JevCallsShots\s*\|\|\s*!DataCenter\.IsPvP\)")
            && caller.Contains("UsedSinceSnapshot.Count < 16"));
        Harness.Case("the snapshot drains the used list under its lock", Regex.IsMatch(caller, @"lock \(UsedLock\)\s*\{\s*used = \[\.\. UsedSinceSnapshot\];\s*UsedSinceSnapshot\.Clear\(\);"));

        var actions = Src("PvPSolver.Basic", "Rotations", "CustomRotation_Actions.cs");
        Harness.Case("the rotation exposes its PvP Limit Break to the caller (internal accessor)",
            Regex.IsMatch(actions, @"internal IBaseAction\? JevLimitBreak => LimitBreakPvP;"));

        var updater = Src("PvPSolver", "Updaters", "MajorUpdater.cs");
        Harness.Case("frame update calls JevCaller.Update once", Regex.Matches(updater, @"JevCaller\.Update\(\)").Count == 1);

        var configs = Src("PvPSolver.Basic", "Configuration", "Configs.cs");
        Harness.Case("config: the switch defaults off", Regex.IsMatch(configs, @"private static readonly bool _jevCallsShots = false;"));
        Harness.Case("settings: the Targeting tab has the switch, default off, and its help names the Limit Break",
            PvpSettingsCatalog.All.Any(s => s.Property == "JevCallsShots" && s.Tab == PvpSettingsCatalog.Targeting && !s.DefaultOn && s.Help.Contains("Limit Break")));
    }
}
