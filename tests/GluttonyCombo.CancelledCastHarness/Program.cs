// CancelledCastHarness (2026-11-01): the completion gates of the healer raidwide locks.
//
// A lock counts a cast as done only when the action actually completed - registered as used
// (JustUsed). Watching the cast start and then seeing the cast bar stop is NOT completion: a
// cancelled cast stops the bar the same way a finished one does, and the 2026-10-31 live run
// showed exactly that (four cancelled Medica II casts each marked the raidwide owned for 15 s
// and were never re-cast).
//
// Why JustUsed is a completion signal (characterized here as the TRUTH cases, true before and
// after the fix): the cast-bar watcher (Timer.CheckInterruptedCasts) fires OnCastInterrupted when
// the bar stops before its finish time, and that event cancels ActionWatching's pending
// last-action timestamp write - so a cancelled cast never lands a JustUsed timestamp, while a
// finished cast keeps it (the write is scheduled at cast-start to land before the bar ends).

using System.Text.RegularExpressions;

namespace GluttonyCombo.CancelledCastHarness;

internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        Console.WriteLine("-- GluttonyCombo cancelled-cast harness --");

        var root = FindRepoRoot();
        var src = Path.Combine(root, "src", "GluttonyCombo", "GluttonyCombo");
        var controller = Read(Path.Combine(src, "AutoRotation", "AutoRotationController.cs"));
        var timer = Read(Path.Combine(src, "CustomCombo", "Functions", "Timer.cs"));
        var watching = Read(Path.Combine(src, "Data", "ActionWatching.cs"));
        var cooldown = Read(Path.Combine(src, "CustomCombo", "Functions", "Cooldown.cs"));

        // ---- RED: the completion gate of each lock must not release on the saw-cast latch ----
        // The locks are not compiled into any harness, so this reads the shipped source; the
        // extractor pulls the exact `if` whose branch marks the raidwide used.
        (string Job, string Method)[] locks =
        [
            ("WHM", "WhmRaidwideRegenLock"),
            ("AST", "AstRaidwideRegenLock"),
            ("SCH", "SchRaidwideShieldLock"),
        ];

        foreach (var (job, method) in locks)
        {
            var body = MethodBody(controller, method)
                       ?? Fail($"could not extract {method} from AutoRotationController.cs");
            var gate = CompletionGate(body)
                       ?? Fail($"could not find the completion branch (the if whose body calls MarkRaidwideShieldUsed) in {method}");

            Check($"RED {job}: {method} completion does not release on the saw-cast latch " +
                  "(a watched cast that stopped may have been cancelled)",
                !Regex.IsMatch(gate, @"Saw\w*Cast\b"), gate);
            Check($"{job}: {method} completion requires the action to have registered as used (JustUsed)",
                gate.Contains("JustUsed("), gate);
        }

        // ---- TRUTH: the chain that makes JustUsed a completion signal (true before and after) ----
        Check("TRUTH: the cast-bar watcher fires OnCastInterrupted when the bar stops before its finish time",
            timer.Contains("OnCastInterrupted?.Invoke(castId)") &&
            Regex.IsMatch(timer, @"if\s*\(DateTime\.Now\s*<\s*castFinishedAt\)"),
            "Timer.CheckInterruptedCasts must fire OnCastInterrupted only when the cast stopped early");
        Check("TRUTH: a cast interrupt cancels the pending last-action timestamp write",
            watching.Contains("OnCastInterrupted += CancelPendingLastActionUpdate") &&
            Regex.IsMatch(watching, @"private static void CancelPendingLastActionUpdate\(uint interruptedAction\)\s*\{[^}]*source\.Cancel\(\)"),
            "ActionWatching must cancel the scheduled UpdateLastUsedAction on interrupt, so a cancelled cast never lands a JustUsed timestamp");
        Check("TRUTH: JustUsed reads the used-action timestamps",
            Regex.IsMatch(cooldown, @"JustUsed\(uint actionID[^)]*\)[\s\S]*ActionTimestamps\.TryGetValue"),
            "Cooldown.JustUsed must read ActionWatching.ActionTimestamps");

        // ---- SGE: no saw-cast latch, so no change needed there (characterization) ----
        var sge = MethodBody(controller, "SgeRaidwideShieldLock")
                  ?? Fail("could not extract SgeRaidwideShieldLock from AutoRotationController.cs");
        Check("SGE: the 2-step Eukrasia lock has no saw-cast latch (it marks on the instant cast's own result)",
            !Regex.IsMatch(sge, @"Saw\w*Cast\b") && sge.Contains("MarkRaidwideShieldUsed()"),
            "the SGE lock marks inside the branch gated on the UseAction result, not on watching a hard cast");

        // ---- the CANARY: the extractor fed the OLD completion text must flag it ----
        const string oldWhm = """
            if (!castingRegen &&
                (_whmSawRegenCast || JustUsed(WHM.Medica2, 1.5f) || JustUsed(WHM.Medica3, 1.5f)))
            {
                _whmSawRegenCast = false;
                _whmRegenPending = false;
                TimedRegenGate.Disarm();
                TimedRegenGate.MarkCast(TimeSpan.FromSeconds(15));
                MarkRaidwideShieldUsed();
                return false;
            }
            """;
        var oldGate = CompletionGate(oldWhm);
        CheckCanary("CANARY (expected to FAIL): the old WHM completion text (saw-cast latch) is accepted as clean",
            oldGate is not null && !Regex.IsMatch(oldGate, @"Saw\w*Cast\b"));

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    private static string Read(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("source file not found: " + path);
        return StripLineComments(File.ReadAllText(path));
    }

    /// <summary>Line comments become spaces (offsets preserved enough for brace matching).</summary>
    private static string StripLineComments(string s)
    {
        var o = new char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') { o[i] = ' '; i++; }
                if (i < s.Length) o[i] = '\n';
                continue;
            }

            o[i] = s[i];
        }

        return new string(o);
    }

    /// <summary>The body of `bool Name(...)` in <paramref name="source"/>, or null.</summary>
    private static string? MethodBody(string source, string name)
    {
        var m = Regex.Match(source, $@"\b\w[\w<>,\s\[\]?]*\s{name}\s*\(");
        if (!m.Success)
            return null;

        var openParen = source.IndexOf('(', m.Index);
        var closeParen = Match(source, openParen, '(', ')');
        if (closeParen < 0)
            return null;

        var j = closeParen + 1;
        while (j < source.Length && char.IsWhiteSpace(source[j])) j++;
        if (j >= source.Length || source[j] != '{')
            return null;

        var end = Match(source, j, '{', '}');
        return end < 0 ? null : source[(j + 1)..end];
    }

    /// <summary>
    ///     The condition of the completion branch: the `if` whose body (the block right after the
    ///     condition) calls <c>MarkRaidwideShieldUsed()</c>. Exactly one per lock method.
    /// </summary>
    private static string? CompletionGate(string body)
    {
        foreach (Match m in Regex.Matches(body, @"\bif\s*\("))
        {
            var open = m.Index + m.Length - 1;
            var close = Match(body, open, '(', ')');
            if (close < 0)
                continue;

            var condition = Regex.Replace(body[(open + 1)..close], @"\s+", " ").Trim();

            var j = close + 1;
            while (j < body.Length && char.IsWhiteSpace(body[j])) j++;
            if (j >= body.Length || body[j] != '{')
                continue;

            var end = Match(body, j, '{', '}');
            if (end < 0)
                continue;

            if (body[(j + 1)..end].Contains("MarkRaidwideShieldUsed()"))
                return condition;
        }

        return null;
    }

    private static int Match(string s, int open, char o, char c)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == o) depth++;
            else if (s[i] == c && --depth == 0) return i;
        }

        return -1;
    }

    private static string FindRepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent!)
        {
            if (File.Exists(Path.Combine(d.FullName, "src", "GluttonyCombo", "GluttonyCombo.slnx")))
                return d.FullName;
        }

        throw new DirectoryNotFoundException("lalalazy repo root (src/GluttonyCombo/GluttonyCombo.slnx) not found above " + AppContext.BaseDirectory);
    }

    private static string Fail(string why) => throw new InvalidOperationException(why);

    private static void Check(string desc, bool ok, string detail = "")
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine($"PASS {desc}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"FAIL {desc} [{detail}]");
        }
    }

    /// <summary>A canary asserts the OPPOSITE of the real behaviour and is required to fail.</summary>
    private static void CheckCanary(string desc, bool oppositeAssertion, string detail = "")
    {
        if (oppositeAssertion)
        {
            _fail++;
            Console.WriteLine($"FAIL {desc} — canary PASSED UNEXPECTEDLY, the harness is not actually " +
                              $"exercising this decision [{detail}]");
        }
        else
        {
            _pass++;
            Console.WriteLine($"FAIL {desc} — canary failed as expected [{detail}]");
        }
    }
}
