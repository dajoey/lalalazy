using System.Reflection;
using System.Text;
using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.CrucibleReplay;

internal sealed record WindowReport(LogWindow Window, List<TickResult> Ticks)
{
    public int Count(TickClass c) => Ticks.Count(t => t.Class == c);
    public int Reproduced => Count(TickClass.Reproduced) + Count(TickClass.ReproducedGap);
    public int Strict => Ticks.Count(t => t.StrictlyRebuilt);
    /// <summary> Ticks whose recorded decision is a real rule, not the rotation floor (gcdchain / ooc:gcdchain). </summary>
    public IEnumerable<TickResult> Decisive => Ticks.Where(t => t.Recorded.Reason is not ("gcdchain" or "ooc:gcdchain"));
}

internal static class Program
{
    private static string Meta(string key) =>
        typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value ?? "";

    private static int Main(string[] args)
    {
        string? corpus = null, only = null, file = null;
        bool verbose = false, fields = false, markdown = false, canary = false, freeze = false, blank = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--corpus": corpus = args[++i]; break;
                case "--window": only = args[++i]; break;
                case "--file": file = args[++i]; break;
                case "-v": verbose = true; break;
                case "--fields": fields = true; break;
                case "--markdown": markdown = true; break;
                case "--canary": canary = true; break;
                case "--freeze": freeze = true; break;
                case "--blank-state": blank = true; break;
                default:
                    Console.Error.WriteLine("usage: --corpus <dir> | --file <window.tsv> [--window <name>] [-v] [--canary] [--freeze] [--blank-state] | --fields [--markdown]");
                    return 2;
            }
        }

        var problems = FieldTable.Check();
        if (problems.Count != 0)
        {
            foreach (var p in problems) Console.WriteLine("FIELD TABLE: " + p);
            Console.WriteLine("fail " + problems.Count);
            return 1;
        }
        if (fields)
        {
            PrintFields(markdown);
            return 0;
        }

        var label = Meta("SrcLabel");
        var paths = new List<string>();
        if (file is not null) paths.Add(file);
        else if (corpus is not null) paths.AddRange(Directory.GetFiles(corpus, "*.tsv").OrderBy(p => p, StringComparer.Ordinal));
        else { Console.Error.WriteLine("give --corpus or --file"); return 2; }
        if (only is not null)
            paths = paths.Where(p => Path.GetFileNameWithoutExtension(p) == only).ToList();
        if (paths.Count == 0) { Console.Error.WriteLine("no windows"); return 2; }

        Console.WriteLine($"Crucible replay: real BST_RotationLogic.Decide, source '{label}' ({Meta("SrcRoot")}), settings = BstSettings.Defaults()");
        var fail = 0;
        var reports = new List<WindowReport>();
        foreach (var path in paths)
        {
            var w = LogWindow.Load(path);
            var rb = new Rebuilder(w);
            var ticks = w.Cr.Select(c => Replayer.Run(blank
                ? new RebuiltTick { Cr = c, Cfg = Rebuilder.Settings(w), State = default, Gaps = new HashSet<string>(FieldTable.Rows.Select(r => r.Name)) }
                : rb.Rebuild(c))).ToList();
            var rep = new WindowReport(w, ticks);
            reports.Add(rep);

            var expectKey = "expect." + label;
            var expect = w.Header.TryGetValue(expectKey, out var e) && int.TryParse(e, out var n) ? n : (int?)null;
            var minPct = w.Header.TryGetValue("min_reproduced_pct", out var mp) && double.TryParse(mp, out var mpv) ? mpv : (double?)null;
            if (minPct is not null && 100.0 * rep.Reproduced / Math.Max(1, ticks.Count) < minPct) { fail++; Console.WriteLine($"  FAIL {w.Name}: reproduced below the window's min_reproduced_pct {minPct}"); }
            if (w.Skipped != 0) { fail++; Console.WriteLine($"  FAIL {w.Name}: {w.Skipped} line(s) did not parse as CR|/BT|/DS|"); }
            var verdict = expect is null ? "no expectation" : expect == rep.Reproduced ? "as expected" : "MISMATCH (expected " + expect + ")";
            if (expect is not null && expect != rep.Reproduced) fail++;
            var pct = ticks.Count == 0 ? 0 : 100.0 * rep.Reproduced / ticks.Count;
            var causes = ticks.Where(t => !t.Matches).GroupBy(t => t.Cause).OrderByDescending(g => g.Count()).Take(3)
                .Select(g => $"{g.Count()}x {g.Key}");
            Console.WriteLine($"window {w.Name,-34} build {w.Build,-9} ticks {ticks.Count,4} | reproduced {rep.Reproduced,4} ({pct,5:0.0}%) " +
                              $"[exact {rep.Count(TickClass.Reproduced)}, gap-dependent {rep.Count(TickClass.ReproducedGap)}] | differ {ticks.Count - rep.Reproduced,4} " +
                              $"[gap {rep.Count(TickClass.GapDiffers)}, unreached {rep.Count(TickClass.Unreached)}] | decisive {rep.Decisive.Count(t => t.Matches)}/{rep.Decisive.Count()} | all-fields-rebuilt {rep.Strict} | {verdict}");
            if (ticks.Count != rep.Reproduced)
                Console.WriteLine($"    why it differs: {string.Join("; ", causes)}");
            var ruleTicks = ticks.Where(t => t.RuleApplicable).ToList();
            if (ruleTicks.Count != 0)
            {
                var mism = ruleTicks.Count(t => t.RuleRecordedForbidden != t.RuleReplayedForbidden);
                Console.WriteLine($"    collector rule ConditionallyForbidden (Abaddon): {ruleTicks.Count} tick(s) replayed from mo=/ou=, {mism} differ from the recorded f=X");
                if (verbose)
                    foreach (var t in ruleTicks.Where(t => t.RuleRecordedForbidden != t.RuleReplayedForbidden))
                        Console.WriteLine($"      {t.Tick.Cr.Ms} mo={t.Tick.Cr.MorphoAlive} ou={t.Tick.Cr.OtherEnemiesUp} hp={t.Tick.Cr.TargetHp} recorded f={(t.RuleRecordedForbidden ? "X" : "-")} rule says {(t.RuleReplayedForbidden ? "forbidden" : "attackable")}");
            }
            if (freeze)
                Freeze(path, expectKey, rep.Reproduced);
            if (verbose || (expect is not null && expect != rep.Reproduced))
                foreach (var t in ticks.Where(t => verbose ? true : !t.Matches))
                    PrintTick(t);
        }

        fail += Canary(reports);

        var total = reports.Sum(r => r.Ticks.Count);
        var repro = reports.Sum(r => r.Reproduced);
        var exact = reports.Sum(r => r.Count(TickClass.Reproduced));
        var strict = reports.Sum(r => r.Strict);
        Console.WriteLine($"corpus: {reports.Count} windows, {total} ticks | reproduced {repro} ({100.0 * repro / Math.Max(1, total):0.0}%) | " +
                          $"reproduced AND insensitive to every gap field {exact} ({100.0 * exact / Math.Max(1, total):0.0}%) | " +
                          $"all decision-critical fields rebuilt {strict} ({100.0 * strict / Math.Max(1, total):0.0}%) | not replayable (some critical field missing) {total - strict} ({100.0 * (total - strict) / Math.Max(1, total):0.0}%)");
        var dec = reports.SelectMany(r => r.Decisive).ToList();
        Console.WriteLine($"decisive ticks (recorded decision is a rule, not the gcdchain floor): {dec.Count}, reproduced {dec.Count(t => t.Matches)} ({100.0 * dec.Count(t => t.Matches) / Math.Max(1, dec.Count):0.0}%), reproduced and insensitive to every gap field {dec.Count(t => t.Class == TickClass.Reproduced)}");
        var ruleAll = reports.SelectMany(r => r.Ticks).Where(t => t.RuleApplicable).ToList();
        if (ruleAll.Count != 0)
            Console.WriteLine($"collector rule ConditionallyForbidden (Abaddon): {ruleAll.Count} tick(s) replayed from mo=/ou=, {ruleAll.Count(t => t.RuleRecordedForbidden != t.RuleReplayedForbidden)} differ from the recorded f=X");
        Console.WriteLine("gap fields that change a decision on their own, by ticks (what to log first):");
        foreach (var g in reports.SelectMany(r => r.Ticks).SelectMany(t => t.Sensitive).GroupBy(x => x).OrderByDescending(g => g.Count()).Take(12))
            Console.WriteLine($"  {g.Key,-28} {g.Count(),4} ticks   {FieldTable.ByName[g.Key].Proposal}");
        Console.WriteLine("fail " + fail);
        return fail == 0 ? 0 : 1;
    }

    private static void PrintTick(TickResult t)
    {
        var tag = t.Class switch
        {
            TickClass.Reproduced => "=",
            TickClass.ReproducedGap => "= (gap-dependent)",
            TickClass.GapDiffers => "DIFF gap",
            _ => "DIFF unreached",
        };
        var sb = new StringBuilder($"  {t.Tick.Cr.Ms} {tag}: recorded {t.Recorded} | replay {t.Replayed}{(t.IdDiffers ? " (same rule, action id differs)" : "")}");
        if (t.Alternatives.Count != 0)
            sb.Append($" | other completions give {string.Join(", ", t.Alternatives.Take(3))} via {string.Join(",", t.Sensitive.Take(6))}");
        Console.WriteLine(sb.ToString());
    }

    /// <summary> Clear one state field of a replayed hold tick and show the decision flip. Returns the number of failed canaries. </summary>
    private static int Canary(List<WindowReport> reports)
    {
        var fail = 0;
        foreach (var rep in reports)
        {
            var hit = rep.Ticks.FirstOrDefault(t => t.Matches && t.Recorded.Reason == "crucible:hold-do-not-attack");
            if (hit is null) continue;
            var s = Replayer.Baseline(hit.Tick);
            var before = Replayer.Decide(s, hit.Tick.Cfg);
            s.TargetDoNotAttack = false;
            var after = Replayer.Decide(s, hit.Tick.Cfg);
            var flipped = before != after;
            Console.WriteLine($"canary {rep.Window.Name} tick {hit.Tick.Cr.Ms}: TargetDoNotAttack true -> false");
            Console.WriteLine($"  - decision {before}");
            Console.WriteLine($"  + decision {after}");
            Console.WriteLine(flipped ? "  canary: decision flipped, as designed" : "  canary FAILED: decision did not change");
            if (!flipped) fail++;
        }
        return fail;
    }

    private static void Freeze(string path, string key, int reproduced)
    {
        var lines = File.ReadAllLines(path).Where(l => !l.StartsWith("#" + key + "=", StringComparison.Ordinal)).ToList();
        var at = lines.FindLastIndex(l => l.StartsWith('#')) + 1;
        lines.Insert(at, $"#{key}={reproduced}");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
    }

    private static void PrintFields(bool markdown)
    {
        var rows = FieldTable.Rows;
        if (markdown)
        {
            Console.WriteLine("| BstState field | class | source / how |");
            Console.WriteLine("|---|---|---|");
            foreach (var r in rows)
                Console.WriteLine($"| `{r.Name}` | {r.Kind.ToString().ToLowerInvariant().Replace("rebuilt", "rebuildable")} | {(r.Kind == Kind.Missing ? "not logged" : r.Source)}{(r.Note.Length != 0 && r.Kind != Kind.Missing ? " - " + r.Note : "")} |");
        }
        else
        {
            foreach (var r in rows)
                Console.WriteLine($"{r.Kind,-10} {r.Name,-30} {r.Source}{(r.Note.Length != 0 ? " | " + r.Note : "")}{(r.Proposal.Length != 0 ? " | proposal: " + r.Proposal : "")}");
        }
        Console.WriteLine($"fields: {rows.Count} ({rows.Count(r => r.Kind == Kind.Rebuilt)} rebuildable, {rows.Count(r => r.Kind == Kind.Derivable)} derivable, {rows.Count(r => r.Kind == Kind.Missing)} missing); BstState declares {FieldTable.StateFields().Length}");
    }
}
