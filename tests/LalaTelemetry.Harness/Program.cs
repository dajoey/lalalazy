// LalaTelemetry harness. No Dalamud: compiles src/Shared/LalaTelemetry/Core/** only.
// Exit 0 = every check passed; one PASS/FAIL line per check.
//
//   dotnet build tests/LalaTelemetry.Harness -c Release
//   dotnet tests/LalaTelemetry.Harness/bin/Release/net10.0/LalaTelemetry.Harness.dll [repoRoot]
//        [--plugin-dll <path/to/Plugin.dll>]...   also assert a built plugin carries an embedded PDB + commit stamp
//        [--expect-commit <sha|unknown>]          override the expected build stamp (default: `git rev-parse --short=10 HEAD`)
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Lalalazy.Telemetry;

var positional = new List<string>();
var pluginDlls = new List<string>();
string? expectCommit = null;
var printSamples = false;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--plugin-dll" && i + 1 < args.Length) pluginDlls.Add(args[++i]);
    else if (args[i] == "--expect-commit" && i + 1 < args.Length) expectCommit = args[++i];
    else if (args[i] == "--print-samples") printSamples = true;
    else positional.Add(args[i]);
}

if (printSamples)
{
    // Synthetic example lines for docs/telemetry-lines.md (and for a parser author to test against).
    H.PrintSamples();
    return 0;
}

var repoRoot = positional.Count > 0 ? positional[0] : H.FindRepoRoot(Directory.GetCurrentDirectory());
if (repoRoot is null)
{
    Console.Error.WriteLine("FAIL cannot find repo root (pass it as arg 1)");
    return 1;
}

var id = new TelemetryIdentity
{
    Plugin = "HarnessPlugin",
    DisplayName = "Harness Plugin",
    Version = "1.2.3.4",
    Channel = "testing",
    Commit = "abc1234def",
    Command = "/harness",
};

// ======================================================================= 1. escaping
{
    string[] samples =
    [
        "", "plain", "a|b", "a\\b", "\\p literal", "\\\\p", "line1\nline2", "crlf\r\nnext", "tab\there",
        "ctrl\u0001\u001f\u007f", "equals=inside=value", "trailing\\", "emoji \U0001F600 ok", "|||", "\\u0041 not an escape",
        "λ unicode ✓", "x|y\\z\n\r\t\u0000",
    ];
    var allRound = true;
    var allClean = true;
    foreach (var s in samples)
    {
        var e = TelemetryEscape.Escape(s);
        if (TelemetryEscape.Unescape(e) != s) { allRound = false; Console.WriteLine($"   round-trip broke for {H.Show(s)} -> {H.Show(e)}"); }
        if (e.IndexOfAny(['|', '\n', '\r', '\t', '\u0000', '\u0001', '\u007f']) >= 0) { allClean = false; Console.WriteLine($"   escaped value still has a raw separator/control char: {H.Show(e)}"); }
    }
    H.Check("escape: Unescape(Escape(s)) == s for every sample", allRound);
    H.Check("escape: an escaped value never contains | CR LF TAB or a control char", allClean);
    H.Check("escape: unknown escape and dangling backslash are kept literally", TelemetryEscape.Unescape("a\\qb\\") == "a\\qb\\");

    var sb = new System.Text.StringBuilder();
    var cut = TelemetryEscape.AppendEscaped(sb, "ab\U0001F600cd", 3);
    H.Check("escape: truncation never splits a surrogate pair", cut && sb.ToString() == "ab", H.Show(sb.ToString()));

    var line = new TelemetryLineBuilder("ER|", 1, "error").Field("a", "short").Field("m", new string('x', 50), 10).Field("st", "y", 5).ToString();
    H.Check("line builder: truncated fields are listed in a trailing tr= field", line.EndsWith("|tr=m", StringComparison.Ordinal), line);
    H.Check("line parser: tr= parses back", ParsedTelemetryLine.TryParse(line, out var pl) && pl.Get("tr") == "m" && pl.Get("m") == new string('x', 10));
    H.Check("line parser: rejects a line without unixms", !ParsedTelemetryLine.TryParse("ER|notanumber|x", out _));
    H.Check("line parser: value containing '=' keeps everything after the first '='",
        ParsedTelemetryLine.TryParse(new TelemetryLineBuilder("ER|", 5, "k").Field("m", "a=b=c").ToString(), out var eqp) && eqp.Get("m") == "a=b=c");
}

// ======================================================================= 2. ER| line shape + key order
{
    var sink = new CaptureSink();
    var clock = new FakeClock();
    var ctx = new GameContext { TerritoryId = 1252, ClassJobId = 39, Level = 100, InCombat = true, BoundByDuty = false, Source = "live" };
    var hub = new TelemetryHub(id, sink, () => ctx, clock.Mono, clock.Unix);

    Exception thrown;
    try { H.ThrowNested("bad|value\nsecond line"); throw new UnreachableException(); }
    catch (Exception ex) { thrown = ex; }

    H.Check("ER: Error() writes a full line for the first occurrence", hub.Error("tick", thrown));
    var w = sink.Writes.Single();
    H.Check("ER: written at ERR level", w.Level == TelemetryLevel.Error);
    H.Check("ER: exactly one physical line (no raw CR/LF)", !w.Text.Contains('\n') && !w.Text.Contains('\r'));
    H.Check("ER: starts with ER|<unixms>|error|", w.Text.StartsWith($"ER|{clock.UnixNow}|error|", StringComparison.Ordinal), w.Text[..Math.Min(60, w.Text.Length)]);
    ParsedTelemetryLine.TryParse(w.Text, out var p);
    var keys = string.Join(",", p.Fields.Select(f => f.Key));
    H.Check("ER: key order is p,v,ch,c,a,fp,n,tt,j,lv,cb,du,x,m,st (docs/telemetry-lines.md)", keys == "p,v,ch,c,a,fp,n,tt,j,lv,cb,du,x,m,st", keys);
    H.Check("ER: identity fields", p.Get("p") == "HarnessPlugin" && p.Get("v") == "1.2.3.4" && p.Get("ch") == "testing" && p.Get("c") == "abc1234def");
    H.Check("ER: context fields", p.Get("tt") == "1252" && p.Get("j") == "39" && p.Get("lv") == "100" && p.Get("cb") == "1" && p.Get("du") == "0");
    H.Check("ER: x is the outer exception type", p.Get("x") == typeof(InvalidOperationException).FullName, p.Get("x"));
    H.Check("ER: m round-trips a message with | and a newline", p.Get("m") == "outer: bad|value\nsecond line", H.Show(p.Get("m")));
    H.Check("ER: st round-trips ex.ToString() exactly (inner exception + frames)", p.Get("st") == thrown.ToString());
    H.Check("ER: st carries the inner exception and a frame from this harness", p.Get("st")!.Contains("ArgumentException") && p.Get("st")!.Contains("ThrowNested"));
    H.Check("ER: fp is 12 lowercase hex", p.Get("fp") is { Length: 12 } fp && fp.All(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c)));
    H.Check("ER: n counts occurrences (first = 1)", p.Get("n") == "1");
    H.Check("ER: the full line lands in the error ring, a <=300-char head in the line ring",
        hub.Errors.Snapshot().Single().Line == w.Text && hub.Lines.Snapshot().Single().Line.Length <= TelemetryHub.RingErrorChars);

    hub.Swallowed("probe", new TimeoutException("t"));
    H.Check("ER: Swallowed() is kind=swallowed at WRN", sink.Writes.Last().Level == TelemetryLevel.Warning && sink.Writes.Last().Text.Contains("|swallowed|"));

    Exception withDetail;
    try { H.ThrowWithMessage("detail"); throw new UnreachableException(); } catch (Exception ex) { withDetail = ex; }
    hub.Error("menu", withDetail, "item 4487 hq=1");
    ParsedTelemetryLine.TryParse(sink.Writes.Last().Text, out var dp);
    H.Check("ER: optional detail sits between m and st as d=", string.Join(",", dp.Fields.Select(f => f.Key)) == "p,v,ch,c,a,fp,n,tt,j,lv,cb,du,x,m,d,st" && dp.Get("d") == "item 4487 hq=1");
    H.Check("ER: detail is not part of the fingerprint", dp.Get("fp") == ExceptionFingerprint.Compute(id.Plugin, "menu", withDetail));

    var huge = new Exception(new string('m', 5000));
    hub.Error("huge", huge);
    ParsedTelemetryLine.TryParse(sink.Writes.Last().Text, out var hp);
    H.Check("ER: an oversized message is cut to 512 chars and flagged tr=m", hp.Get("m")!.Length == TelemetryHub.MaxMessageChars && hp.Get("tr") == "m", hp.Get("tr"));
}

// ======================================================================= 3. fingerprint stability
{
    string Fp(Func<Exception> make, string area = "tick") => ExceptionFingerprint.Compute("P", area, make());
    Exception Catch(Action a) { try { a(); } catch (Exception e) { return e; } return new UnreachableException(); }

    var a1 = Fp(() => Catch(() => H.ThrowWithMessage("id 1 failed")));
    var a2 = Fp(() => Catch(() => H.ThrowWithMessage("id 99 failed for 'Somebody'")));
    H.Check("fp: same throw site, different message -> same fp", a1 == a2, $"{a1} {a2}");
    H.Check("fp: different area -> different fp", a1 != Fp(() => Catch(() => H.ThrowWithMessage("x")), "other"));
    H.Check("fp: different throw site -> different fp", a1 != Fp(() => Catch(() => H.ThrowNested("x"))));

    var stackA = "   at Foo.Bar.<Tick>b__12_0() in C:\\build\\src\\Foo.cs:line 42\n   at Foo.Bar.Tick(Int32 frame) in C:\\build\\src\\Foo.cs:line 99";
    var stackB = "   at Foo.Bar.<Tick>b__7_3() in /other/host/Foo.cs:line 57\n   at Foo.Bar.Tick(Int32 frame) in /other/host/Foo.cs:line 123";
    var nA = string.Join(";", ExceptionFingerprint.NormalizedFrames(stackA, 5));
    var nB = string.Join(";", ExceptionFingerprint.NormalizedFrames(stackB, 5));
    H.Check("fp: frames drop file path + line, fold digits (lambda ordinals, line shifts, host paths)", nA == nB && !nA.Contains("line") && !nA.Contains(".cs"), nA);
    H.Check("fp: only the first 5 frames count", ExceptionFingerprint.NormalizedFrames(string.Join("\n", Enumerable.Range(0, 9).Select(i => $"   at X.M{i}()")), 5).Count == 5);

    var noStack1 = ExceptionFingerprint.Compute("P", "a", new InvalidOperationException("slot 3 of 20 failed for \"Name\""));
    var noStack2 = ExceptionFingerprint.Compute("P", "a", new InvalidOperationException("slot 17 of 20 failed for \"Other\""));
    H.Check("fp: never-thrown exception falls back to the digit/quote-normalised message", noStack1 == noStack2);
    H.Check("fp: stable across calls (pure function)", Fp(() => Catch(() => H.ThrowWithMessage("q"))) == a1);
    H.Check("fp: same throw site reached from two different lines of the caller -> same fp (no line numbers)",
        Fp(() => Catch(() => H.CallThrowFrom(first: true))) == Fp(() => Catch(() => H.CallThrowFrom(first: false))));
    var agg = new AggregateException(Catch(() => H.ThrowWithMessage("in task")));
    H.Check("fp: an AggregateException (no stack of its own) is fingerprinted by its first inner exception's frames",
        ExceptionFingerprint.MethodFrames(agg, 5).Count == 0 && ExceptionFingerprint.Compute("P", "a", agg) != ExceptionFingerprint.Compute("P", "a", new AggregateException(new InvalidOperationException("in task"))));
    var frames = ExceptionFingerprint.MethodFrames(Catch(() => H.ThrowNested("x")), 5);
    H.Check("fp: frames are Type.Method with digits folded, no path/line", frames.Count > 0 && frames[0] == "H.ThrowNested" && frames.All(f => !f.Any(char.IsDigit) && !f.Contains(".cs")), string.Join(";", frames));
    H.Check("fp: type chain lists inner types", ExceptionFingerprint.TypeChain(new InvalidOperationException("o", new ArgumentException("i")))
        == "System.InvalidOperationException>System.ArgumentException");
}

// ======================================================================= 4. rate limiter
{
    var rl = new RateLimiter(burst: 3, summaryIntervalMs: 60_000, quietResetMs: 300_000);
    long t = 1_000;
    int full = 0, summaries = 0;
    long supTotal = 0;
    for (var i = 0; i < 6000; i++) // 6000 frames at ~16 ms = 96 s of an every-frame failure
    {
        var v = rl.Check("fp", t, null, out _, out var s);
        if (v == RateLimiter.Verdict.Full) full++;
        if (v == RateLimiter.Verdict.Summary) { summaries++; supTotal += s.Suppressed; }
        t += 16;
    }
    H.Check("limiter: first 3 occurrences are written in full", full == 3, $"full={full}");
    H.Check("limiter: then at most one summary per 60 s (96 s -> 1)", summaries == 1, $"summaries={summaries}");
    var flushed = rl.Flush(t, force: true);
    supTotal += flushed.Sum(f => f.Suppressed);
    H.Check("limiter: full + every suppressed count == occurrences (nothing lost)", full + supTotal == 6000, $"{full}+{supTotal}");
    H.Check("limiter: a forced flush leaves nothing pending", !rl.HasPending && rl.Flush(t, true).Count == 0);

    var v2 = rl.Check("fp", t + 300_000, null, out var total2, out _);
    H.Check("limiter: quiet for 5 min -> re-armed, written in full again", v2 == RateLimiter.Verdict.Full && total2 == 6001, $"{v2} total={total2}");

    var rl2 = new RateLimiter(burst: 1, summaryIntervalMs: 60_000, quietResetMs: 300_000);
    rl2.Check("x", 0, null, out _, out _);
    rl2.Check("x", 10, null, out _, out _); // suppressed, never flushed
    var v3 = rl2.Check("x", 400_000, null, out _, out var pend);
    H.Check("limiter: re-arm with an unflushed count -> SummaryThenFull carrying the old count", v3 == RateLimiter.Verdict.SummaryThenFull && pend.Suppressed == 1, $"{v3} sup={pend.Suppressed}");

    var rl3 = new RateLimiter(maxFingerprints: 8);
    for (var i = 0; i < 50; i++) rl3.Check("fp" + i, i, null, out _, out _);
    H.Check("limiter: distinct fingerprints are capped (no unbounded growth)", rl3.Count <= 8, $"count={rl3.Count}");
}

// ======================================================================= 5. hub rate limiting end to end
{
    var sink = new CaptureSink();
    var clock = new FakeClock();
    var hub = new TelemetryHub(id, sink, null, clock.Mono, clock.Unix);
    Exception ex;
    try { H.ThrowWithMessage("every frame"); throw new UnreachableException(); } catch (Exception e) { ex = e; }
    for (var i = 0; i < 100; i++) { hub.Error("tick", ex); clock.Advance(16); }
    H.Check("hub: 100 identical errors -> 3 full ER lines", sink.Writes.Count == 3, $"writes={sink.Writes.Count}");
    clock.Advance(61_000);
    hub.Pump();
    var last = sink.Writes.Last();
    ParsedTelemetryLine.TryParse(last.Text, out var sp);
    H.Check("hub: Pump() after the errors stop writes one summary with sup=97", sink.Writes.Count == 4 && sp.Kind == "summary" && sp.Get("sup") == "97" && sp.Get("n") == "100", last.Text[..Math.Min(160, last.Text.Length)]);
    var skeys = string.Join(",", sp.Fields.Select(f => f.Key));
    H.Check("hub: summary key order p,v,ch,c,a,fp,n,tt,j,lv,cb,du,of,sup,span,x,m", skeys == "p,v,ch,c,a,fp,n,tt,j,lv,cb,du,of,sup,span,x,m", skeys);
    H.Check("hub: summary keeps the level of what it summarises (ERR) and carries no stack", last.Level == TelemetryLevel.Error && sp.Get("st") is null);
    hub.Pump();
    H.Check("hub: Pump() with nothing pending writes nothing", sink.Writes.Count == 4);
}

// ======================================================================= 6. circuit breaker
{
    var o = new BreakerOptions { Threshold = 10, WindowMs = 5_000, CooldownMs = 30_000, MaxCooldownMs = 300_000 };
    var b = new CircuitBreaker(o);
    long t = 0;
    for (var i = 0; i < 9; i++) { b.TryEnter(t, out _); b.RecordFailure(t); t += 16; }
    H.Check("breaker: 9 failures inside the window do not trip", b.State == BreakerState.Closed);
    var tr = b.RecordFailure(t);
    H.Check("breaker: the 10th failure inside 5 s trips it", tr == BreakerTransition.Tripped && b.State == BreakerState.Open && b.Trips == 1);
    H.Check("breaker: skipped during the cooldown", !b.TryEnter(t + 29_999, out _));
    H.Check("breaker: after 30 s a trial is admitted (half-open)", b.TryEnter(t + 30_000, out var t1) && t1 == BreakerTransition.None && b.State == BreakerState.HalfOpen);
    tr = b.RecordFailure(t + 30_001);
    H.Check("breaker: a failed trial re-opens with double the cooldown", tr == BreakerTransition.Reopened && b.CurrentCooldownMs == 60_000 && b.Trips == 2);
    H.Check("breaker: still closed to calls before the doubled cooldown", !b.TryEnter(t + 30_001 + 59_000, out _));
    H.Check("breaker: next trial admitted after 60 s", b.TryEnter(t + 30_001 + 60_000, out _));
    H.Check("breaker: the call after a clean trial closes it (Recovered)", b.TryEnter(t + 30_001 + 60_016, out var t2) && t2 == BreakerTransition.Recovered && b.State == BreakerState.Closed);
    H.Check("breaker: cooldown resets after recovery", b.CurrentCooldownMs == 30_000);

    var slow = new CircuitBreaker(o);
    long ts = 0;
    for (var i = 0; i < 40; i++) { slow.RecordFailure(ts); ts += 1_000; } // one failure a second
    H.Check("breaker: 1 failure/s never fills 10 inside 5 s (no trip)", slow.State == BreakerState.Closed && slow.Trips == 0);

    var cap = new CircuitBreaker(o);
    long tc = 0;
    for (var i = 0; i < 10; i++) cap.RecordFailure(tc);
    for (var k = 0; k < 8; k++) { tc += cap.CurrentCooldownMs; cap.TryEnter(tc, out _); cap.RecordFailure(tc); }
    H.Check("breaker: cooldown doubling is capped at 5 min", cap.CurrentCooldownMs == 300_000, $"{cap.CurrentCooldownMs}");
}

// ======================================================================= 7. guard: every-frame failure -> one notice
{
    var sink = new CaptureSink();
    var clock = new FakeClock();
    var hub = new TelemetryHub(id, sink, null, clock.Mono, clock.Unix);
    var guard = hub.CreateGuard("tick", "the per-frame update");

    var broken = true;
    var bodyRuns = 0;
    void Frame()
    {
        if (!guard.TryEnter()) return;
        try
        {
            bodyRuns++;
            if (broken) H.ThrowTypeInit();
        }
        catch (Exception ex)
        {
            guard.Failed(ex);
        }
    }

    for (var f = 0; f < 60 * 60 * 10; f++) { Frame(); clock.Advance(16); } // 10 minutes of frames, always failing
    hub.Pump();
    var trips = sink.Writes.Count(w => w.Text.Contains("|trip|"));
    var fulls = sink.Writes.Count(w => w.Text.Contains("|error|"));
    H.Check("guard: a handler that throws every frame is tripped (runs ~10 times, not 36000)", bodyRuns < 30, $"body ran {bodyRuns} times");
    H.Check("guard: exactly ONE player notice for the whole session despite re-trips", sink.Notices.Count == 1 && trips > 1, $"notices={sink.Notices.Count} trips={trips}");
    H.Check("guard: notice names the plugin, the area label and the report command, no second person",
        sink.Notices[0].StartsWith("Harness Plugin: the per-frame update stopped after repeated errors", StringComparison.Ordinal)
        && sink.Notices[0].Contains("/harness report") && !sink.Notices[0].Contains(" you", StringComparison.OrdinalIgnoreCase), sink.Notices[0]);
    H.Check("guard: at most 3 full ER|error lines for one fingerprint", fulls <= 3, $"full={fulls}");
    var trip = sink.Writes.First(w => w.Text.Contains("|trip|"));
    ParsedTelemetryLine.TryParse(trip.Text, out var tp);
    var tkeys = string.Join(",", tp.Fields.Select(f => f.Key));
    H.Check("guard: trip line key order p,v,ch,c,a,ev,trips,fails,cool,tt,j,lv,cb,du,fp,x,m", tkeys == "p,v,ch,c,a,ev,trips,fails,cool,tt,j,lv,cb,du,fp,x,m", tkeys);
    H.Check("guard: trip line is ERR and names the failing type", trip.Level == TelemetryLevel.Error && tp.Get("x") == typeof(TypeInitializationException).FullName && tp.Get("ev") == "tripped");

    broken = false;
    for (var f = 0; f < 60 * 60 * 6; f++) { Frame(); clock.Advance(16); } // condition cleared; wait out the (capped) cooldown
    var rec = sink.Writes.Where(w => w.Text.Contains("|recover|")).ToList();
    H.Check("guard: once the failure clears, one recover line + one 'running again' notice", rec.Count == 1 && sink.Notices.Count == 2 && sink.Notices[1] == "Harness Plugin: the per-frame update is running again.", $"rec={rec.Count} notices={sink.Notices.Count}");
    H.Check("guard: recover line is INF", rec.Count == 1 && rec[0].Level == TelemetryLevel.Info);
    H.Check("guard: state is closed and the body runs every frame again", guard.Breaker.State == BreakerState.Closed);

    broken = true;
    for (var f = 0; f < 600; f++) { Frame(); clock.Advance(16); }
    broken = false;
    for (var f = 0; f < 60 * 60 * 6; f++) { Frame(); clock.Advance(16); }
    H.Check("guard: flapping later adds log lines but no further notices", sink.Notices.Count == 2, $"notices={sink.Notices.Count}");

    var transient = hub.CreateGuard("burst", "a transient");
    for (var i = 0; i < 5; i++) { transient.TryEnter(); transient.Failed(new InvalidOperationException("zone load")); clock.Advance(16); }
    for (var i = 0; i < 100; i++) { transient.TryEnter(); clock.Advance(16); }
    H.Check("guard: a 5-failure transient burst never trips", transient.Breaker.Trips == 0 && transient.Breaker.State == BreakerState.Closed);

    var runGuard = hub.CreateGuard("run", "run helper");
    var ran = runGuard.Run(() => { });
    var threw = runGuard.Run(() => throw new InvalidOperationException("x"));
    H.Check("guard: Run() returns true on success and false when the body threw", ran && !threw);
    H.Check("guard: hub lists every guard's state for a report", hub.GuardStates().Contains("guard.tick=closed/") && hub.GuardStates().Contains("guard.burst=closed/0/5"), hub.GuardStates());

    var detached = new TelemetryGuard(null, "detached", "d");
    for (var i = 0; i < 20; i++) { detached.TryEnter(); detached.Failed(new Exception("no hub")); }
    H.Check("guard: works without any installed hub (breaker only, no crash)", detached.Breaker.Trips >= 1);
}

// ======================================================================= 8. ring buffer
{
    var r = new RingBuffer(5);
    for (var i = 0; i < 12; i++) r.Add(i, "L" + i);
    var snap = r.Snapshot();
    H.Check("ring: keeps the newest N, oldest first", string.Join(",", snap.Select(e => e.Line)) == "L7,L8,L9,L10,L11");
    H.Check("ring: TotalAdded counts what fell off the front", r.TotalAdded == 12 && r.Count == 5);
    r.Add(99, null);
    H.Check("ring: null is ignored", r.TotalAdded == 12);

    var shared = new RingBuffer(200);
    Parallel.For(0, 4, _ => { for (var i = 0; i < 10_000; i++) shared.Add(i, "x"); });
    H.Check("ring: concurrent adds are safe (4x10k)", shared.TotalAdded == 40_000 && shared.Count == 200 && shared.Snapshot().Length == 200);

    var hub = new TelemetryHub(id, new CaptureSink(), ringCapacity: 3);
    foreach (var l in new[] { "CT|1|a", "CT|2|b", "CT|3|c", "CT|4|d" }) hub.Record(l);
    H.Check("ring: hub.Record keeps the last 3 lines verbatim", string.Join(",", hub.Lines.Snapshot().Select(e => e.Line)) == "CT|2|b,CT|3|c,CT|4|d");

    var tb = new TokenBucket(3, 10);
    var taken = Enumerable.Range(0, 10).Count(_ => tb.TryTake(0));
    H.Check("token bucket: burst capacity 3 at t=0", taken == 3);
    H.Check("token bucket: refills 10/s (1 token after 100 ms)", tb.TryTake(100) && !tb.TryTake(100));
}

// ======================================================================= 9. report bundle shape
{
    var ring = new[] { new RingBuffer.Entry(10, "CT|10|BST|x|1|2|0.00|0-|0.0|"), new RingBuffer.Entry(11, "ER|11|error|p=HarnessPlugin|m=a\\pb"), new RingBuffer.Entry(12, "BT|12|..") };
    var errors = Enumerable.Range(0, 20).Select(i => new RingBuffer.Entry(i, $"ER|{i}|error|p=P|st=" + new string('s', 3000))).ToArray();
    var input = new ReportInput
    {
        Id = "1NP3K7QA",
        UnixMs = 1_788_000_000_000,
        Identity = id,
        Text = "rotation stopped | after pull\nsecond line",
        Game = new GameContext { TerritoryId = 1252, ClassJobId = 39, JobAbbr = "BST", Level = 100, InCombat = true, X = 1.5f, Y = -2f, Z = 3.25f, Target = "BattleNpc:123:456:Striking Dummy", Conditions = ["NormalConditions", "InCombat"], Source = "live" },
        State = "auto=1;paused=0",
        Config = "A=1;B#=3",
        Ring = ring,
        RingTotal = 1234,
        Errors = errors,
        ErrorTotal = 20,
        VisibleAddons = ["_ActionBar", "RetainerSellList"],
        Addons = [new AddonCapture("RetainerSellList", 3, "0:i=5;1:s=a|b;2:b=1")],
    };
    var lines = ReportBuilder.BuildLines(input);
    var parsed = lines.Select(l => ParsedTelemetryLine.TryParse(l, out var pr) ? pr : null).ToList();
    var sections = string.Join(",", parsed.Select(x => x?.Kind ?? "?").Distinct());
    H.Check("report: every line is a parseable RP| record", parsed.All(x => x is not null && x.Prefix == "RP|"));
    H.Check("report: section order begin,text,game,state,config,errs,err,rings,ring,addons,addon,end", sections == "begin,text,game,state,config,errs,err,rings,ring,addons,addon,end", sections);
    H.Check("report: begin and end both carry lines=<total physical lines>", parsed[0]!.Get("lines") == lines.Count.ToString() && parsed[^1]!.Get("lines") == lines.Count.ToString());
    H.Check("report: every line carries the same id", parsed.All(x => x!.Get("id") == "1NP3K7QA"));
    H.Check("report: begin line identity + format version", parsed[0]!.Get("fmt") == "1" && parsed[0]!.Get("p") == "HarnessPlugin" && parsed[0]!.Get("c") == "abc1234def" && parsed[0]!.Get("ch") == "testing");
    H.Check("report: no line contains a raw newline (block splits on \\n safely)", lines.All(l => !l.Contains('\n') && !l.Contains('\r')));
    H.Check("report: free text round-trips (pipes, newline)", parsed.Single(x => x!.Kind == "text")!.Get("t") == input.Text);
    var game = parsed.Single(x => x!.Kind == "game")!;
    H.Check("report: game line", game.Get("tt") == "1252" && game.Get("j") == "39" && game.Get("ja") == "BST" && game.Get("pos") == "1.50,-2.00,3.25" && game.Get("cond") == "NormalConditions,InCombat" && game.Get("tgt")!.StartsWith("BattleNpc:123:456", StringComparison.Ordinal));
    var ringOut = parsed.Where(x => x!.Kind == "ring").Select(x => x!.Get("l")).ToList();
    H.Check("report: ring lines round-trip verbatim, oldest first", ringOut.SequenceEqual(ring.Select(e => e.Line)));
    H.Check("report: rings header says how many were seen in total", parsed.Single(x => x!.Kind == "rings")!.Get("seen") == "1234");
    var errOut = parsed.Where(x => x!.Kind == "err").Select(x => x!).ToList();
    H.Check("report: only the newest 16 errors, each cut to 2000 chars and flagged", errOut.Count == 16 && errOut[0].Get("at") == "4" && errOut.All(e => e.Get("l")!.Length == ReportBuilder.MaxErrorLineChars && e.Get("tr") == "l"));
    H.Check("report: addon values escaped and round-trip", parsed.Single(x => x!.Kind == "addon")!.Get("vals") == "0:i=5;1:s=a|b;2:b=1");
    var size = lines.Sum(l => l.Length + 1);
    H.Check("report: bounded size even with every section at its cap (< 200 KB worst case)", ReportBuilder.BuildLines(new ReportInput
    {
        Id = "X", UnixMs = 1, Identity = id,
        Ring = Enumerable.Range(0, 200).Select(i => new RingBuffer.Entry(i, new string('r', 5000))).ToArray(),
        Errors = errors,
        Addons = Enumerable.Range(0, 12).Select(i => new AddonCapture("A" + i, 24, new string('v', 9000))).ToArray(),
        State = new string('s', 9000), Config = new string('c', 9000),
    }).Sum(l => l.Length + 1) < 200_000, $"sample={size}");

    var ids = new HashSet<string> { ReportBuilder.NewId(1_788_000_000_000), ReportBuilder.NewId(1_788_000_000_000) };
    H.Check("report: two ids in the same second differ", ids.Count == 2);
    H.Check("report: id is 8 Crockford base32 chars", ids.All(x => x.Length == 8 && x.All(c => "0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(c))), string.Join(",", ids));

    var sink = new CaptureSink();
    var clock = new FakeClock();
    var hub = new TelemetryHub(id, sink, null, clock.Mono, clock.Unix);
    hub.Record("CT|1|x");
    var rid = hub.WriteReport(rid => hub.BaseInput(rid, "text", GameContext.Empty, "s=1", "c=2"));
    H.Check("report: WriteReport writes ONE WRN log entry holding the whole block", rid is not null && sink.Writes.Count == 1 && sink.Writes[0].Level == TelemetryLevel.Warning && sink.Writes[0].Text.Split('\n').Length > 5);
    H.Check("report: block begins with RP|<unixms>|begin| and ends with an end line", sink.Writes[0].Text.StartsWith($"RP|{clock.UnixNow}|begin|", StringComparison.Ordinal) && sink.Writes[0].Text.Split('\n')[^1].Contains("|end|"));
    H.Check("report: a second report inside 10 s is refused", hub.WriteReport(r => hub.BaseInput(r, "", GameContext.Empty, "", "")) is null);
    clock.Advance(10_001);
    H.Check("report: allowed again after 10 s", hub.WriteReport(r => hub.BaseInput(r, "", GameContext.Empty, "", "")) is not null);
    H.Check("report: the ring notes that a report was filed", hub.Lines.Snapshot().Any(e => e.Line.StartsWith("RP|", StringComparison.Ordinal) && e.Line.Contains("|filed|id=" + rid)));
}

// ======================================================================= 10. config summary
{
    var cfg = new SampleConfig();
    var s = ConfigSummary.Describe(cfg);
    H.Check("config: scalars, enums, collection counts, one nested level; strings left out",
        s == "Enabled=1;Count=7;Ratio=0.5;Mode=Fast;Items#=3;Set#=1;Nested.Inner=1;Nested.Depth=2;Field=9", s);
    H.Check("config: null -> empty", ConfigSummary.Describe(null) == "");
}

// ======================================================================= 11. embedded PDB -> file:line in stack traces
{
    var embedded = H.FindProbe("PdbProbeEmbedded.dll");
    var none = H.FindProbe("PdbProbeNone.dll");
    var throwerSrc = Path.Combine(repoRoot, "tests", "LalaTelemetry.Harness", "PdbProbe", "Thrower.cs");
    var expectedLine = File.Exists(throwerSrc)
        ? File.ReadAllLines(throwerSrc).Select((l, i) => (l, i)).First(x => x.l.Contains("// THROW-LINE")).i + 1
        : -1;

    H.Check("pdb: probe builds are present (embedded + none), no loose .pdb next to them",
        File.Exists(embedded) && File.Exists(none) && !File.Exists(Path.ChangeExtension(embedded, ".pdb")) && !File.Exists(Path.ChangeExtension(none, ".pdb")));
    H.Check("pdb: the embedded probe's PE debug directory has an EmbeddedPortablePdb entry", H.HasEmbeddedPdb(embedded));
    H.Check("pdb: the control probe has none", !H.HasEmbeddedPdb(none));

    var fromBytes = H.StackFromBytes(embedded);
    H.Check($"pdb: loaded FROM BYTES (no file, like a plugin loader), the stack names Thrower.cs:line {expectedLine}",
        expectedLine > 0 && fromBytes.Contains($"Thrower.cs:line {expectedLine}"), H.FirstFrame(fromBytes));
    var control = H.StackFromBytes(none);
    H.Check("pdb: control - the same code without a PDB reports no line number", control.Contains("Thrower.Throw") && !control.Contains(":line "), H.FirstFrame(control));

    string own;
    try { H.ThrowWithMessage("own"); own = ""; } catch (Exception e) { own = e.ToString(); }
    H.Check("pdb: the harness itself (DebugType embedded) reports file:line", own.Contains("Program.cs:line "), H.FirstFrame(own));
}

// ======================================================================= 12. unobserved task exceptions: ours only
{
    var sink = new CaptureSink();
    var hub = new TelemetryHub(id, sink);
    using var watcher = new UnobservedTaskWatcher(typeof(H).Assembly, ex => hub.Unobserved(ex));

    var probe = Assembly.LoadFrom(H.FindProbe("PdbProbeEmbedded.dll"));
    var foreignFactory = (Func<Task>)Delegate.CreateDelegate(typeof(Func<Task>), probe.GetType("PdbProbe.Thrower")!.GetMethod("Faulted")!);

    for (var attempt = 0; attempt < 10 && (watcher.OwnSeen == 0 || watcher.ForeignSeen == 0); attempt++)
    {
        H.DropFaultedTask(() => Task.Run(static () => H.ThrowWithMessage("own task")));
        H.DropFaultedTask(foreignFactory);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    H.Check("unobserved: an unobserved exception from THIS assembly is recorded as ER|unobserved", watcher.OwnSeen > 0 && sink.Writes.Any(w => w.Text.Contains("|unobserved|") && w.Text.Contains("own task")), $"own={watcher.OwnSeen}");
    H.Check("unobserved: one from another assembly (another plugin) is ignored", watcher.ForeignSeen > 0 && !sink.Writes.Any(w => w.Text.Contains("foreign task")), $"foreign={watcher.ForeignSeen}");
    H.Check("unobserved: ExceptionOrigin agrees on a direct exception", H.OwnOrigin() && !H.ForeignOrigin(probe));
    watcher.Dispose();
    var before = watcher.OwnSeen;
    H.DropFaultedTask(() => Task.Run(static () => H.ThrowWithMessage("after dispose")));
    GC.Collect(); GC.WaitForPendingFinalizers();
    H.Check("unobserved: Dispose unsubscribes", watcher.OwnSeen == before);
}

// ======================================================================= 13. build stamp
{
    var stamped = BuildStamp.CommitOf(typeof(H).Assembly);
    var expected = expectCommit ?? H.GitShortHead(repoRoot) ?? BuildStamp.Unknown;
    H.Check($"stamp: this harness was stamped with commit '{expected}'", stamped == expected, $"stamped={stamped}");
    H.Check("stamp: an assembly without the attribute reads 'unknown'", BuildStamp.CommitOf(typeof(object).Assembly) == BuildStamp.Unknown);
}

// ======================================================================= 14. built plugin DLLs (optional)
foreach (var dll in pluginDlls)
{
    var name = Path.GetFileName(dll);
    H.Check($"plugin {name}: file exists", File.Exists(dll), dll);
    if (!File.Exists(dll)) continue;
    H.Check($"plugin {name}: PDB embedded (stack traces will carry file:line)", H.HasEmbeddedPdb(dll));
    var commit = H.ReadCommitMetadata(dll);
    H.Check($"plugin {name}: commit stamp present ({commit ?? "none"})", commit is not null && (commit == BuildStamp.Unknown || System.Text.RegularExpressions.Regex.IsMatch(commit, "^[0-9a-f]{7,40}$")));
}

return H.Summarise();

// ======================================================================= helpers

internal sealed class CaptureSink : ITelemetrySink
{
    public readonly List<(TelemetryLevel Level, string Text)> Writes = new();
    public readonly List<string> Notices = new();
    public void Write(TelemetryLevel level, string text) { lock (Writes) Writes.Add((level, text)); }
    public void Notice(string text) { lock (Notices) Notices.Add(text); }
}

internal sealed class FakeClock
{
    private long _mono = 1_000_000;
    public long UnixNow { get; private set; } = 1_788_000_000_000;
    public long Mono() => _mono;
    public long Unix() => UnixNow;
    public void Advance(long ms) { _mono += ms; UnixNow += ms; }
}

internal enum SampleMode { Slow, Fast }

internal sealed class SampleInner
{
    public bool Inner { get; set; } = true;
    public int Depth { get; set; } = 2;
    public SampleInner? TooDeep => new() { Depth = 3 };
}

internal sealed class SampleConfig
{
    public bool Enabled { get; set; } = true;
    public int Count { get; set; } = 7;
    public double Ratio { get; set; } = 0.5;
    public SampleMode Mode { get; set; } = SampleMode.Fast;
    public string Secret { get; set; } = "a retainer name";
    public List<int> Items { get; set; } = [1, 2, 3];
    public HashSet<SampleMode> Set { get; set; } = [SampleMode.Fast];
    public SampleInner Nested { get; set; } = new();
    public int Throws => throw new InvalidOperationException("getter");
    public int Field = 9;
}

internal static class H
{
    private static int _pass;
    private static int _fail;

    public static void Check(string name, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"PASS {name}"); }
        else { _fail++; Console.WriteLine($"FAIL {name}{(string.IsNullOrEmpty(detail) ? "" : " :: " + detail)}"); }
    }

    public static int Summarise()
    {
        Console.WriteLine(_fail == 0 ? $"{_pass} passed, 0 failed OK" : $"{_pass} passed, {_fail} FAILED");
        return _fail == 0 ? 0 : 1;
    }

    public static void PrintSamples()
    {
        var sink = new CaptureSink();
        var clock = new FakeClock();
        var ident = new TelemetryIdentity { Plugin = "ExamplePlugin", DisplayName = "Example Plugin", Version = "1.0.4.230", Channel = "testing", Commit = "8dabb3bf20", Command = "/example" };
        var ctx = new GameContext { TerritoryId = 1252, ClassJobId = 39, Level = 100, InCombat = true };
        var hub = new TelemetryHub(ident, sink, () => ctx, clock.Mono, clock.Unix);
        var guard = hub.CreateGuard("tick", "the per-frame update");
        for (var i = 0; i < 12; i++)
        {
            guard.TryEnter();
            try { ThrowTypeInit(); } catch (Exception ex) { guard.Failed(ex); }
            clock.Advance(16);
        }
        clock.Advance(61_000);
        hub.Pump();
        clock.Advance(30_000);
        guard.TryEnter();
        clock.Advance(16);
        guard.TryEnter();
        hub.Record("CT|1788000091000|BST|BST_ST_Main|44880|44881|1.20|0-|87.5|");
        clock.Advance(15_000);
        hub.WriteReport(id => hub.BaseInput(id, "rotation stopped after the second pull | expected Brutal Rage", ctx with { JobAbbr = "BST", X = 12.5f, Y = 0f, Z = -40.25f, Target = "BattleNpc:1234:1073741901:Training Dummy", Conditions = ["InCombat", "NormalConditions"] },
            "auto=1;paused=0", "Enabled=1;Throttle=50;EnabledActions#=412",
            ["_ActionBar", "_TargetInfo", "SelectString"], [new AddonCapture("SelectString", 5, "0:s=Choose;1:u=3;2:s=Option A;")]));
        foreach (var w in sink.Writes)
            Console.WriteLine($"[{w.Level}] {w.Text}\n");
        foreach (var n in sink.Notices)
            Console.WriteLine($"[NOTICE] {n}");
    }

    public static string Show(string? s) => s is null ? "null" : "\"" + s.Replace("\n", "\\n").Replace("\r", "\\r") + "\"";

    public static string FirstFrame(string stack) => stack.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("at ", StringComparison.Ordinal))?.Trim() ?? stack;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowWithMessage(string message) => throw new InvalidOperationException(message);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CallThrowFrom(bool first)
    {
        if (first)
            ThrowWithMessage("a"); // one line...

        ThrowWithMessage("b"); // ...a different line, same method
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowNested(string message)
    {
        try { throw new ArgumentException("inner"); }
        catch (ArgumentException inner) { throw new InvalidOperationException("outer: " + message, inner); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowTypeInit() => throw new TypeInitializationException("Some.Static.Holder", new NullReferenceException("static ctor"));

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void DropFaultedTask(Func<Task> factory)
    {
        // Wait for completion WITHOUT observing the exception (no Wait/Result/await), then drop the
        // only reference so the next GC finalizes it and raises UnobservedTaskException.
        var fresh = factory();
        SpinWait.SpinUntil(() => fresh.IsCompleted, 2000);
    }

    public static bool OwnOrigin()
    {
        try { ThrowWithMessage("x"); } catch (Exception e) { return ExceptionOrigin.Involves(e, typeof(H).Assembly); }
        return false;
    }

    public static bool ForeignOrigin(Assembly probe)
    {
        try { probe.GetType("PdbProbe.Thrower")!.GetMethod("Throw")!.Invoke(null, null); }
        catch (TargetInvocationException e) { return ExceptionOrigin.Involves(e.InnerException, typeof(H).Assembly); }
        return true;
    }

    public static string StackFromBytes(string path)
    {
        var alc = new AssemblyLoadContext("pdb-probe-" + Path.GetFileNameWithoutExtension(path), isCollectible: true);
        try
        {
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            var asm = alc.LoadFromStream(ms);
            try { asm.GetType("PdbProbe.Thrower")!.GetMethod("Throw")!.Invoke(null, null); }
            catch (TargetInvocationException e) { return e.InnerException?.ToString() ?? ""; }
            return "";
        }
        finally
        {
            alc.Unload();
        }
    }

    /// <summary>The probe DLL copied into the harness output (the copy keeps the probe's relative folder).</summary>
    public static string FindProbe(string fileName)
        => Directory.EnumerateFiles(AppContext.BaseDirectory, fileName, SearchOption.AllDirectories).FirstOrDefault()
           ?? Path.Combine(AppContext.BaseDirectory, fileName);

    public static bool HasEmbeddedPdb(string path)
    {
        if (!File.Exists(path)) return false;
        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        return pe.ReadDebugDirectory().Any(d => d.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
    }

    /// <summary>Reads [AssemblyMetadata("LalaCommit", x)] from a DLL without loading it (it references Dalamud).</summary>
    public static string? ReadCommitMetadata(string path)
    {
        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        foreach (var h in md.GetAssemblyDefinition().GetCustomAttributes())
        {
            var ca = md.GetCustomAttribute(h);
            string? typeName = null;
            if (ca.Constructor.Kind == HandleKind.MemberReference)
            {
                var mr = md.GetMemberReference((MemberReferenceHandle)ca.Constructor);
                if (mr.Parent.Kind == HandleKind.TypeReference)
                    typeName = md.GetString(md.GetTypeReference((TypeReferenceHandle)mr.Parent).Name);
            }
            if (typeName != "AssemblyMetadataAttribute")
                continue;
            var blob = md.GetBlobReader(ca.Value);
            if (blob.ReadUInt16() != 1) continue;
            var key = blob.ReadSerializedString();
            var value = blob.ReadSerializedString();
            if (key == "LalaCommit") return value;
        }
        return null;
    }

    public static string? GitShortHead(string repo)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse --short=10 HEAD") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);
            return p.ExitCode == 0 && o.Length > 0 ? o : null;
        }
        catch
        {
            return null;
        }
    }

    public static string? FindRepoRoot(string start)
    {
        var d = new DirectoryInfo(start);
        while (d is not null)
        {
            if (File.Exists(Path.Combine(d.FullName, "pluginmaster.json"))) return d.FullName;
            d = d.Parent;
        }
        return null;
    }
}
