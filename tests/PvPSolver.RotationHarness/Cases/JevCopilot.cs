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

        // ---- T1: snapshot (v3) ------------------------------------------------------------------------------
        var enemies = Enumerable.Range(1, 12).Select(i => new JevEnemy((ulong)(1000 + i), "WHM", 0.3333f, 18000u, 40f - i, 1f, 40f - i,
            i == 12, false, false, i == 11, 2, false, i == 12)).ToList();
        var allies = new[] { new JevAlly(77UL, "PLD", 26000u, 52000u, 3f, 1f, 2.8f, 1) };
        var self = new JevSelf("MCH", 52000u, 52000u, 1.23456f, false, false);
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
        Harness.Case("each enemy carries exactly the relay's fields (no names, unchanged in v3)",
            keys.SequenceEqual(["id", "job", "hp", "hp_now", "dist", "dx", "dz", "guard", "invuln", "cc", "on_me", "allies_on", "kuzushi", "lb_ok"]), string.Join(",", keys));
        var rootKeys = root.EnumerateObject().Select(o => o.Name).ToArray();
        Harness.Case("the snapshot is v3 with v, job, hp, hp_now, hp_max, facing, me_cc, my_target, used, lb, enemies, allies",
            root.GetProperty("v").GetInt32() == 3
            && rootKeys.SequenceEqual(["v", "job", "hp", "hp_now", "hp_max", "facing", "me_cc", "my_target", "used", "lb", "enemies", "allies"]), string.Join(",", rootKeys));
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
        var allyKeys = root.GetProperty("allies")[0].EnumerateObject().Select(o => o.Name).ToArray();
        Harness.Case("each ally carries id, job, hp, dist, dx, dz, enemies_on, alliance",
            allyKeys.SequenceEqual(["id", "job", "hp", "dist", "dx", "dz", "enemies_on", "alliance"]), string.Join(",", allyKeys));

        // Fix 1 (10-31: one second before a death Jev was told 84% while the player was at ~40%): the hp sent is the
        // real CurrentHp/MaxHp, never a predicted value with pending heals added.
        var hurt = new JevSelf("AST", 20000u, 50000u, 0f, false, false);
        using (var s3 = JsonDocument.Parse(JevCall.SnapshotJson(hurt, enemies, [new JevAlly(5UL, "WAR", 15000u, 60000u, 4f, 0f, 4f, 2)])))
        {
            var r3 = s3.RootElement;
            Harness.Case("real HP: the player's hp is hp_now / hp_max, and hp_max is sent",
                Math.Abs(r3.GetProperty("hp").GetSingle() - 0.4f) < 1e-6 && r3.GetProperty("hp_now").GetUInt32() == 20000u
                && r3.GetProperty("hp_max").GetUInt32() == 50000u);
            var ally = r3.GetProperty("allies")[0];
            Harness.Case("real HP: an ally's hp is its CurrentHp / MaxHp, and the ally carries its object id",
                Math.Abs(ally.GetProperty("hp").GetSingle() - 0.25f) < 1e-6 && ally.GetProperty("id").GetUInt64() == 5UL);
        }
        Harness.Case("real HP: a zero MaxHp gives 0, never a division by zero; a full bar gives 1",
            JevCall.HpRatio(100u, 0u) == 0f && JevCall.HpRatio(0u, 100u) == 0f && JevCall.HpRatio(100u, 100u) == 1f
            && Math.Abs(JevCall.HpRatio(1u, 3u) - 1f / 3f) < 1e-6);

        // Fix 4: party members first (nearest first), then the nearest alliance members marked alliance, at most 8.
        var mixed = new List<JevAlly>();
        for (int i = 0; i < 7; i++)
        {
            mixed.Add(new JevAlly((ulong)(200 + i), "SGE", 1u, 1u, 30f - i, 0f, 0f, 0, Alliance: false));
        }

        for (int i = 0; i < 10; i++)
        {
            mixed.Add(new JevAlly((ulong)(300 + i), "DRG", 1u, 1u, 2f + i, 0f, 0f, 0, Alliance: true));
        }

        using (var s4 = JsonDocument.Parse(JevCall.SnapshotJson(self, enemies, mixed)))
        {
            var al = s4.RootElement.GetProperty("allies").EnumerateArray().ToList();
            Harness.Case("allies: at most MaxAllies (8) are sent", JevCall.MaxAllies == 8 && al.Count == 8, al.Count.ToString());
            Harness.Case("allies: all party members come first, nearest first, even when alliance members are nearer",
                al.Take(7).Select(x => x.GetProperty("id").GetUInt64()).SequenceEqual(new ulong[] { 206, 205, 204, 203, 202, 201, 200 })
                && al.Take(7).All(x => !x.GetProperty("alliance").GetBoolean()));
            Harness.Case("allies: the rest is the nearest alliance member, marked alliance",
                al[7].GetProperty("id").GetUInt64() == 300UL && al[7].GetProperty("alliance").GetBoolean());
        }

        using (var s5 = JsonDocument.Parse(JevCall.SnapshotJson(self, enemies, mixed.Skip(5).Take(4))))
        {
            var al = s5.RootElement.GetProperty("allies").EnumerateArray().Select(x => x.GetProperty("id").GetUInt64()).ToArray();
            Harness.Case("allies: with 2 party members, alliance members fill in behind them, nearest first (old code: 4 nearest of any kind)",
                al.SequenceEqual(new ulong[] { 206, 205, 300, 301 }), string.Join(",", al));
        }

        // ---- T1: Fix 2, keep the last good pick when Jev's answer has no focus ------------------------------------
        var good = new JevAnswer(4242UL, false, null, 100.0);
        bool Hostile(ulong id) => id == 4242UL || id == 99UL;
        var unsure = new JevAnswer(null, false, null, 101.5);
        var kept = JevCall.KeepLastPick(good, unsure, Hostile);
        Harness.Case("keep pick: an answer with no focus keeps the previous focus while it is fresh and still hostile",
            JevCall.FocusPick(kept, 101.5) == 4242UL);
        Harness.Case("keep pick: the kept focus keeps its own age, so it goes stale FreshSeconds after Jev gave it",
            JevCall.FocusPick(kept, 100.0 + JevCall.FreshSeconds) == 4242UL && JevCall.FocusPick(kept, 103.01) == null);
        Harness.Case("keep pick: two unsure answers in a row do not refresh the kept focus",
            JevCall.FocusPick(JevCall.KeepLastPick(kept, new JevAnswer(null, false, null, 102.5), Hostile), 103.2) == null);
        Harness.Case("keep pick: an answer with a different focus replaces it",
            JevCall.FocusPick(JevCall.KeepLastPick(good, new JevAnswer(99UL, false, null, 101.0), Hostile), 101.0) == 99UL);
        Harness.Case("keep pick: not kept when the previous focus is stale",
            JevCall.FocusPick(JevCall.KeepLastPick(good, new JevAnswer(null, false, null, 103.5), Hostile), 103.5) == null);
        Harness.Case("keep pick: not kept when the previous focus is no longer a valid hostile target (dead, gone)",
            JevCall.FocusPick(JevCall.KeepLastPick(good, unsure, _ => false), 101.5) == null);
        Harness.Case("keep pick: the new answer's Limit Break call is never replaced by the old one",
            JevCall.KeepLastPick(good with { FireLimitBreak = true, LimitBreakTarget = 4242UL }, unsure, Hostile) is { FireLimitBreak: false, LimitBreakTarget: null, ReceivedAt: 101.5 }
            && JevCall.ShouldFireLimitBreak(JevCall.KeepLastPick(good, unsure with { FireLimitBreak = true }, Hostile), 101.5, double.MinValue, out _));
        Harness.Case("keep pick: no previous answer, nothing to keep",
            JevCall.FocusPick(JevCall.KeepLastPick(null, unsure, Hostile), 101.5) == null);
        // A relay that owns keeping (its reply carries "kept_why") already dropped the old pick on purpose (Guarded,
        // out of reach, too old): the plugin must not bring it back.
        Harness.Case("keep pick: a reply from a relay that keeps picks itself (kept_why present) is never overridden",
            JevCall.FocusPick(JevCall.KeepLastPick(good, unsure with { RelayKeeps = true }, Hostile), 101.5) == null);
        Harness.Case("keep pick: Parse marks a reply that carries kept_why (null or text) as relay-kept, and an old reply as not",
            JevCall.Parse("{\"focus\":null,\"kept_why\":null}", 1.0) is { RelayKeeps: true }
            && JevCall.Parse("{\"focus\":5,\"kept\":true,\"kept_why\":\"steady\"}", 1.0) is { RelayKeeps: true, Focus: 5UL }
            && JevCall.Parse("{\"focus\":null}", 1.0) is { RelayKeeps: false });

        // ---- T1: Fix 5, the game reports each action twice; the same action on the same target within 1.5 s counts once
        var uses = new JevRecentUses();
        Harness.Case("dedupe: the first use counts, its duplicate packet 0.2 s later does not",
            uses.IsNew(29400u, 1001UL, 10.0) && !uses.IsNew(29400u, 1001UL, 10.2));
        Harness.Case("dedupe: another action, or the same action on another target or none, still counts",
            uses.IsNew(29401u, 1001UL, 10.3) && uses.IsNew(29400u, 1002UL, 10.3) && uses.IsNew(29400u, null, 10.3) && !uses.IsNew(29400u, null, 10.4));
        Harness.Case("dedupe: the same action on the same target 1.5 s or more after the first counts again",
            !uses.IsNew(29400u, 1001UL, 11.49) && uses.IsNew(29400u, 1001UL, 11.51));
        Harness.Case("dedupe: UsedDedupeSeconds is 1.5", JevCall.UsedDedupeSeconds == 1.5);

        // ---- T1: Fix 3, an honest Limit Break log: fired when used, a skip only when a window closes without firing --
        var fl = new JevFireLog();
        Harness.Case("lb log: opening a window logs nothing", fl.Open(29255u, 10.0, 11.0) == null && fl.Tick(10.1) == null);
        fl.Refused(null); // CanUse refused this frame: PvP Solver's timing, not a reason
        Harness.Case("lb log: a refused frame inside the window logs nothing (10-31: all 7 canuse skips were in windows that fired)",
            fl.Tick(10.2) == null);
        string? fired = fl.Used(29255u, 10.4);
        Harness.Case("lb log: the Limit Break used inside the window logs JV|lb-fired once",
            fired != null && fired.StartsWith("JV|lb-fired action=29255", StringComparison.Ordinal) && fl.Used(29255u, 10.45) == null, fired ?? "null");
        Harness.Case("lb log: a window that fired never logs a skip",
            fl.Tick(11.0) == null && fl.Tick(13.0) == null);

        var fu = new JevFireLog();
        fu.Open(29255u, 20.0, 21.0);
        fu.Close(20.0, "unusable");
        Harness.Case("lb log: a window closed as unusable waits the grace for the use before calling it a skip",
            fu.Tick(20.5) == null);
        string? skipU = fu.Tick(20.0 + JevFireLog.GraceSeconds + 0.01);
        Harness.Case("lb log: a window closed as unusable with no use logs JV|lb-fire-skip reason=unusable once (10-31 14:51:47 logged nothing)",
            skipU == "JV|lb-fire-skip reason=unusable" && fu.Tick(30.0) == null, skipU ?? "null");

        var fg = new JevFireLog();
        fg.Open(29255u, 30.0, 31.0);
        fg.Close(30.3, "unusable");
        Harness.Case("lb log: the use packet arriving just after the close (the Limit Break emptied the gauge) logs fired, not a skip",
            fg.Used(29255u, 30.5) is { } f2 && f2.StartsWith("JV|lb-fired", StringComparison.Ordinal) && fg.Tick(40.0) == null);

        var fo = new JevFireLog();
        fo.Open(29255u, 40.0, 41.0);
        fo.Refused("out-of-range");
        Harness.Case("lb log: a window that runs out after out-of-range frames logs reason=out-of-range",
            fo.Tick(40.9) == null && fo.Tick(41.0 + JevFireLog.GraceSeconds + 0.01) == "JV|lb-fire-skip reason=out-of-range");
        var fn = new JevFireLog();
        fn.Open(29255u, 50.0, 51.0);
        fn.Refused("no-target");
        Harness.Case("lb log: reason=no-target when Jev's enemy was gone",
            fn.Tick(51.0 + JevFireLog.GraceSeconds + 0.01) == "JV|lb-fire-skip reason=no-target");
        var ft = new JevFireLog();
        ft.Open(29255u, 60.0, 61.0);
        ft.Refused("out-of-range");
        ft.Refused(null);
        Harness.Case("lb log: reason=timeout when the window ran out with the Limit Break offered or only refused by timing",
            ft.Tick(61.0 + JevFireLog.GraceSeconds + 0.01) == "JV|lb-fire-skip reason=timeout");
        var fx = new JevFireLog();
        fx.Open(29255u, 70.0, 71.0);
        Harness.Case("lb log: another action, or the Limit Break outside any window, logs nothing",
            fx.Used(29400u, 70.2) == null && new JevFireLog().Used(29255u, 1.0) == null);
        fx.Close(70.5, "unusable");
        Harness.Case("lb log: a use long after the grace is not credited to the window",
            fx.Used(29255u, 70.5 + JevFireLog.GraceSeconds + 0.5) == null);
        var fr = new JevFireLog();
        fr.Open(29255u, 80.0, 81.0);
        string? flushed = fr.Open(29255u, 80.5, 81.5);
        Harness.Case("lb log: a new window opening before the old verdict flushes the old one as a skip",
            flushed == "JV|lb-fire-skip reason=timeout", flushed ?? "null");
        Harness.Case("lb log: GraceSeconds is 2.0, still below the 2.5 s retry gap (the next window never opens inside a grace)",
            JevFireLog.GraceSeconds == 2.0 && JevFireLog.GraceSeconds < JevCall.FireRetrySeconds);
        var fl2 = new JevFireLog();
        fl2.Open(29255u, 90.0, 91.0);
        fl2.Close(90.2, "unusable");
        Harness.Case("lb log: a use report 1.5 s after the close (a lag spike) still logs fired, not a skip",
            fl2.Tick(91.5) == null && fl2.Used(29255u, 91.7) is { } f3 && f3.StartsWith("JV|lb-fired", StringComparison.Ordinal) && fl2.Tick(95.0) == null);

        // ---- T1: Fix 6, the optional heal answer (healer jobs): parsed, logged as a shadow, never acted on ---------
        Harness.Case("heal: an object id, \"self\", \"attack\" or \"guard\" parse; a missing or null heal is none",
            JevCall.Parse("{\"focus\": 1, \"heal\": 4242}", 1.0) is { Heal: "4242" }
            && JevCall.Parse("{\"heal\": \"self\"}", 1.0) is { Heal: "self" } && JevCall.Parse("{\"heal\": \"attack\"}", 1.0) is { Heal: "attack" }
            && JevCall.Parse("{\"heal\": \"guard\"}", 1.0) is { Heal: "guard" }
            && JevCall.Parse("{\"heal\": null}", 1.0) is { Heal: null } && JevCall.Parse("{\"focus\": 1}", 1.0) is { Heal: null, Focus: 1UL });
        Harness.Case("heal: a value of any other shape is ignored and the rest of the answer still parses",
            JevCall.Parse("{\"focus\": 3, \"heal\": \"everyone\"}", 1.0) is { Heal: null, Focus: 3UL }
            && JevCall.Parse("{\"focus\": 3, \"heal\": -1}", 1.0) is { Heal: null, Focus: 3UL }
            && JevCall.Parse("{\"focus\": 3, \"heal\": 1.5}", 1.0) is { Heal: null, Focus: 3UL }
            && JevCall.Parse("{\"focus\": 3, \"heal\": {\"id\": 1}}", 1.0) is { Heal: null, Focus: 3UL }
            && JevCall.Parse("{\"focus\": 3, \"heal\": true}", 1.0) is { Heal: null, Focus: 3UL });
        JevAnswer? SafeParse(string json)
        {
            try
            {
                return JevCall.Parse(json, 1.0);
            }
            catch (Exception)
            {
                return new JevAnswer(999UL, false, null, -1.0, "threw");
            }
        }

        Harness.Case("reply: a broken text escape in heal is none, never throws, and focus and Limit Break still parse",
            SafeParse("{\"focus\": 5, \"lb\": \"fire\", \"heal\": \"\\udc00x\"}") is { Focus: 5UL, FireLimitBreak: true, Heal: null });
        Harness.Case("reply: a broken text escape in lb is no fire, never throws, and the focus still parses",
            SafeParse("{\"focus\": 5, \"lb\": \"\\ud800\", \"heal\": \"self\"}") is { Focus: 5UL, FireLimitBreak: false, Heal: "self" });
        Harness.Case("unknown answer fields (numbers, strings, arrays, objects) never break parsing",
            JevCall.Parse("{\"focus\": 9, \"focus_conf\": 0.91, \"lb\": \"fire\", \"lb_target\": null, \"lb_hold_p\": 0.2, \"kept\": true, \"ms\": 184, \"probs\": {\"9\": 0.9}, \"why\": [\"low\", 1], \"new_thing\": \"x\"}", 2.0)
                is { Focus: 9UL, FireLimitBreak: true, LimitBreakTarget: null, Heal: null });
        var hs = new JevHealShadow();
        Harness.Case("heal shadow: one JV|heal-shadow line per change, none for the same call or for none",
            hs.Next("self") == "JV|heal-shadow heal=self" && hs.Next("self") == null
            && hs.Next("4242") == "JV|heal-shadow heal=4242" && hs.Next(null) == null && hs.Next(null) == null);
        var flap = new JevHealShadow();
        int flapLines = new[] { "4242", null, "4242", null, "4242", null, "4242" }.Count(h => flap.Next(h) != null);
        Harness.Case("heal shadow: an answer with no heal call is not a change (7 answers flapping on one ally log once)",
            flapLines == 1, flapLines + " lines");
        flap.Reset();
        Harness.Case("heal shadow: after a fight ends, the next fight's first call is logged even when it matches the last one",
            flap.Next("4242") == "JV|heal-shadow heal=4242");

        // ---- T1: Fix 7, relay failures are visible: one Information summary per fight or per 60 s of failures -----
        var st = new JevCallStats();
        Harness.Case("stats: nothing before or during a quiet fight", st.Tick(0.0, true) == null);
        st.Count(JevCallOutcome.Ok);
        st.Count(JevCallOutcome.Ok);
        st.Count(JevCallOutcome.Timeout);
        st.Count(JevCallOutcome.Non200);
        st.Count(JevCallOutcome.Error);
        Harness.Case("stats: no line per call while in combat before 60 s", st.Tick(30.0, true) == null);
        string? minute = st.Tick(60.0, true);
        Harness.Case("stats: while failures occur, one summary every 60 s with ok / timeout / non200 / error",
            minute != null && minute.StartsWith("JV|relay-calls ok=2 timeout=1 non200=1 error=1", StringComparison.Ordinal), minute ?? "null");
        Harness.Case("stats: the counts restart after each summary", st.Tick(61.0, true) == null);
        st.Count(JevCallOutcome.Ok);
        Harness.Case("stats: no 60 s line when the new period has no failure", st.Tick(125.0, true) == null);
        string? end = st.Tick(126.0, false);
        Harness.Case("stats: leaving combat writes the fight's summary even with no failure",
            end != null && end.StartsWith("JV|relay-calls ok=1 timeout=0 non200=0 error=0", StringComparison.Ordinal), end ?? "null");
        Harness.Case("stats: out of combat with nothing counted, nothing is written", st.Tick(127.0, false) == null && st.Tick(500.0, false) == null);
        var sl = new JevCallStats();
        _ = sl.Tick(200.0, true);
        sl.Count(JevCallOutcome.Ok);
        string? fightEnd = sl.Tick(210.0, false);
        sl.Count(JevCallOutcome.Timeout); // a call still in flight when combat ended lands after the fight's summary
        Harness.Case("stats: a call landing after the fight's summary writes nothing out of combat",
            fightEnd != null && fightEnd.StartsWith("JV|relay-calls ok=1 timeout=0", StringComparison.Ordinal) && sl.Tick(211.0, false) == null, fightEnd ?? "null");
        _ = sl.Tick(300.0, true);
        for (int i = 0; i < 59; i++)
        {
            sl.Count(JevCallOutcome.Ok);
        }

        string? quietMinute = sl.Tick(360.0, true);
        string? nextEnd = sl.Tick(361.0, false);
        Harness.Case("stats: counts never leak into the next fight (a late call from the last fight is dropped when the next starts)",
            quietMinute == null && nextEnd != null && nextEnd.StartsWith("JV|relay-calls ok=59 timeout=0 non200=0 error=0", StringComparison.Ordinal),
            (quietMinute ?? "null") + " / " + (nextEnd ?? "null"));
        Harness.Case("stats: an HTTP timeout or cancel counts as timeout, anything else as error",
            JevCall.Classify(new TaskCanceledException("t", new TimeoutException())) == JevCallOutcome.Timeout
            && JevCall.Classify(new OperationCanceledException()) == JevCallOutcome.Timeout
            && JevCall.Classify(new System.Net.Http.HttpRequestException("x", new TimeoutException())) == JevCallOutcome.Timeout
            && JevCall.Classify(new System.Net.Http.HttpRequestException("c", new OperationCanceledException("c", new TimeoutException()))) == JevCallOutcome.Timeout
            && JevCall.Classify(new System.Net.Http.HttpRequestException("refused", new System.Net.Sockets.SocketException(10061))) == JevCallOutcome.Error
            && JevCall.Classify(new System.Net.Http.HttpRequestException("refused")) == JevCallOutcome.Error
            && JevCall.Classify(new InvalidOperationException()) == JevCallOutcome.Error);

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
        Harness.Case("TryLimitBreak: no reason line on the first failed frame any more (SkipOnce is gone; the fire log decides at window close)",
            !caller.Contains("SkipOnce(") && !callerRaw.Contains("SkipOnce(\"canuse\")"));
        Harness.Case("TryLimitBreak: the window opens the fire log; unusable (or follow-up) closes it; out-of-range and a gone target are recorded as reasons",
            Regex.IsMatch(caller, @"FireLog\.Open\(limitBreak\.ID, now, _fireUntil\)")
            && Regex.IsMatch(callerRaw, @"_fireUntil\s*=\s*now;[^}]*FireLog\.Close\(now, [^;]*""unusable""\);", RegexOptions.Singleline)
            && callerRaw.Contains("FireLog.Refused(\"out-of-range\")") && callerRaw.Contains("FireLog.Refused(\"no-target\")")
            && !callerRaw.Contains("\"target-gone\""));
        Harness.Case("caller: the fire log and the relay summary are ticked every frame, before the switch/PvP/combat gate, and log at Information",
            caller.IndexOf("FireLog.Tick(now)", StringComparison.Ordinal) is int ft2 && ft2 > 0
            && caller.IndexOf("Stats.Tick(now,", StringComparison.Ordinal) is int st2 && st2 > 0
            && caller.IndexOf("public static void Update()", StringComparison.Ordinal) is int up && up > 0 && ft2 > up && st2 > up
            && caller.IndexOf("DropUsed();", up, StringComparison.Ordinal) is int gate && ft2 < gate && st2 < gate);
        var watcher2 = Src("PvPSolver", "Watcher.cs");
        Harness.Case("Watcher reports every own action effect to the fire log before the no-target-effect early return (a Limit Break that hits nobody still counts as fired)",
            Regex.IsMatch(watcher2, @"JevCaller\.NoteActionEffect\(set\.Action[^;]*\.RowId\);")
            && watcher2.IndexOf("JevCaller.NoteActionEffect(", StringComparison.Ordinal) < watcher2.IndexOf("if (set.TargetEffects.Length == 0)", StringComparison.Ordinal)
            && Regex.IsMatch(caller, @"public static void NoteActionEffect\(uint actionId\)\s*\{\s*if\s*\(!Service\.Config\.JevCallsShots\s*\|\|\s*!DataCenter\.IsPvP\)\s*\{\s*return;\s*\}\s*string\? line;\s*lock \(FireLogLock\)\s*\{\s*line = FireLog\.Used\(actionId, Now\);"));
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
        Harness.Case("the snapshot drains the used list under its lock, before any early return",
            Regex.IsMatch(caller, @"lock \(UsedLock\)\s*\{\s*used = \[\.\. UsedSinceSnapshot\];\s*UsedSinceSnapshot\.Clear\(\);")
            && caller.IndexOf("used = [.. UsedSinceSnapshot];", StringComparison.Ordinal) < caller.IndexOf("if (hostiles.Count == 0)", StringComparison.Ordinal));
        Harness.Case("no snapshot this frame (switch off, not PvP, not in combat): recorded casts are dropped",
            Regex.IsMatch(caller, @"!DataCenter\.InCombat\s*\|\|\s*Player\.Object == null\)\s*\{\s*DropUsed\(\);(\s*HealShadow\.Reset\(\);)?\s*return;"));
        Harness.Case("TryLimitBreak: a re-aimed Limit Break must be in range and sight of the chosen enemy (the usable check ignores range)",
            Regex.IsMatch(caller, @"if\s*\(!InRangeAndSight\(limitBreak\.AdjustedID, chosen\)\)")
            && caller.IndexOf("InRangeAndSight(limitBreak.AdjustedID, chosen)", StringComparison.Ordinal) < caller.IndexOf("limitBreak.Target = new TargetResult(", StringComparison.Ordinal)
            && callerRaw.Contains("FireLog.Refused(\"out-of-range\")") && caller.Contains("return err == 0 || err == 565;"));

        Harness.Case("snapshot: the player and allies are sent with real CurrentHp/MaxHp, never GetHealthRatio (which adds pending heals)",
            !Regex.IsMatch(caller, @"\bme\.GetHealthRatio\(\)") && !Regex.IsMatch(caller, @"\ba\.GetHealthRatio\(\)")
            && Regex.IsMatch(caller, @"new JevSelf\(me\.ClassJob\.Value\.Abbreviation\.ToString\(\), me\.CurrentHp, me\.MaxHp,")
            && Regex.IsMatch(caller, @"new JevAlly\(a\.GameObjectId, a\.ClassJob\.Value\.Abbreviation\.ToString\(\), a\.CurrentHp, a\.MaxHp,"));
        Harness.Case("snapshot: alliance members are the friendly player characters outside the party (PvP Solver's alliance list is empty in PvP)",
            Regex.IsMatch(caller, @"foreach \(IBattleChara a in DataCenter\.AllTargets\)")
            && Regex.IsMatch(caller, @"a is IPlayerCharacter")
            && Regex.IsMatch(caller, @"!partyIds\.Contains\(a\.GameObjectId\)")
            && Regex.IsMatch(caller, @"!a\.IsEnemy\(\)")
            && Regex.IsMatch(caller, @"foreach \(IBattleChara a in allianceMembers\)\s*\{\s*allies\.Add\(Ally\(a, alliance: true\)\);")
            && Regex.IsMatch(caller, @"foreach \(IBattleChara a in party\)\s*\{\s*allies\.Add\(Ally\(a, alliance: false\)\);"));
        Harness.Case("relay reply: an answer with no focus keeps the last good pick (KeepLastPick against the hostiles of that snapshot) before it is stored",
            Regex.IsMatch(caller, @"Volatile\.Write\(ref _latest, JevCall\.KeepLastPick\(Volatile\.Read\(ref _latest\), answer, hostileIds\.Contains\)\);")
            && Regex.Matches(caller, @"Volatile\.Write\(ref _latest,").Count == 1
            && Regex.IsMatch(caller, @"BuildSnapshot\(HashSet<ulong> hostileIds\)") && caller.Contains("_ = hostileIds.Add(b.GameObjectId);"));
        Harness.Case("NoteActionUsed: the same action on the same target within 1.5 s is recorded once (RecentUses under the lock)",
            Regex.IsMatch(caller, @"lock \(UsedLock\)\s*\{\s*if\s*\(UsedSinceSnapshot\.Count < 16 && RecentUses\.IsNew\(actionId, target, now\)\)"));
        Harness.Case("relay reply: the heal field is logged as JV|heal-shadow at Information, once per change, and nothing acts on it",
            Regex.IsMatch(caller, @"HealShadow\.Next\(answer\.Heal\) is \{\s*\} healLine")
            && callerRaw.Contains("PluginLog.Information($\"[JevCaller] {healLine}\")")
            && Regex.Matches(caller, @"\.Heal\b").Count == 1 && !caller.Contains("_lastHeal")
            && !Regex.IsMatch(target + invoke, @"Latest\??\.Heal\b"));
        Harness.Case("relay reply: the heal shadow restarts whenever no Jev fight is on (frame update, out-of-fight gate)",
            Regex.IsMatch(caller, @"!DataCenter\.InCombat\s*\|\|\s*Player\.Object == null\)\s*\{\s*DropUsed\(\);\s*HealShadow\.Reset\(\);\s*return;")
            && Regex.Matches(caller, @"HealShadow\.Reset\(\)").Count == 1);
        Harness.Case("TryLimitBreak: a window closed because the Limit Break turned into its follow-up says follow-up, not unusable",
            callerRaw.Contains("FireLog.Close(now, limitBreak.Info.AdjustedID != limitBreak.ID ? \"follow-up\" : \"unusable\");")
            && Regex.Matches(caller, @"FireLog\.Close\(").Count == 1);
        Harness.Case("relay reply: every call is counted (ok, timeout, non-200, error); per-call detail stays at Debug",
            Regex.IsMatch(caller, @"Stats\.Count\(JevCallOutcome\.Non200\)") && Regex.IsMatch(caller, @"Stats\.Count\(JevCallOutcome\.Ok\)")
            && Regex.IsMatch(caller, @"Stats\.Count\(JevCall\.Classify\(ex\)\)") && Regex.Matches(caller, @"Stats\.Count\(JevCallOutcome\.Error\)").Count >= 2
            && callerRaw.IndexOf("private static async Task SendAsync(", StringComparison.Ordinal) is int sa && sa > 0
            && callerRaw.IndexOf("private static string? BuildSnapshot(", sa, StringComparison.Ordinal) is int sb && sb > sa
            && Regex.Matches(callerRaw[sa..sb], @"PluginLog\.Information\(").Count == 1);
        var jevCore = Src("PvPSolver.Basic", "Decisions", "JevCall.cs");
        Harness.Case("no movement: PvP Solver's Jev code never moves the character (no navmesh IPC, no move or position calls)",
            !Regex.IsMatch(callerRaw + File.ReadAllText(Path.Combine(Program.SrcRoot, "PvPSolver.Basic", "Decisions", "JevCall.cs")), @"vnavmesh", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(caller + jevCore, @"MoveTo|PathfindAndMoveTo|SetPosition|Movement|Navmesh"));

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
