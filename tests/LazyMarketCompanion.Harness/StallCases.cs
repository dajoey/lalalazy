using LazyMarketCompanion.AutoMarket;

// Offline cases 163-169: the retainer-dialog stall guard (0.2.8.14).
//
// The simulation reproduces the TaskManager contract the plugin runs on - one step at a time,
// a hard 10 s limit per step, and a timeout that CLEARS the whole remaining chain - against a fake
// game that tracks which dialogs are open. Two policies drive it: Legacy is the 0.2.8.13 behaviour
// (a row step waits out the hard limit; an aborted chain releases AutoRetainer as it finds the
// game), Guarded is the shipped one (StepStall in front of the limit, DialogRecovery before the
// release). The Legacy runs are the bug, kept as controls so the Guarded assertions cannot pass
// vacuously. The source-scan cases pin that MarketAutomation really uses the guarded path.
internal static class StallCases
{
  private enum Policy { Legacy, Guarded }

  private sealed class World
  {
    public readonly HashSet<string> Open = new(StringComparer.Ordinal);
    public readonly HashSet<int> NoMarketReply = [];        // rows whose Compare Prices click never answers
    public readonly HashSet<int> HistoryNeverAnswers = [];  // rows on an empty board whose Universalis lookup is swallowed
    public readonly HashSet<int> EmptyBoard = [];
    public readonly Dictionary<int, int> Repriced = [];
    public readonly List<string> Log = [];
    public long Now;
    public bool Released;
    public string[] OpenAtRelease = [];
    public bool Skip;
    public int? Price;
    public long PriceAt = -1;
  }

  private sealed record SimStep(string Name, Func<bool?> Run, int DelayAfterMs = 0);

  // The legacy task manager: head step is retried every tick, a step older than the hard limit
  // clears the queue (AbortOnTimeout), a finished step is followed by its delay.
  private sealed class Chain(World w)
  {
    private readonly List<SimStep> _queue = [];
    public bool Aborted;
    public string? AbortedOn;
    public bool Finished;
    private long _startedAt = -1;
    private long _notBefore;

    public void Enqueue(SimStep s) => _queue.Add(s);
    public bool Busy => _queue.Count > 0;

    public void Tick()
    {
      if (_queue.Count == 0 || w.Now < _notBefore) return;
      var step = _queue[0];
      if (_startedAt < 0) _startedAt = w.Now;
      var r = step.Run();
      if (r == true)
      {
        _queue.RemoveAt(0);
        _startedAt = -1;
        _notBefore = w.Now + step.DelayAfterMs;
        if (_queue.Count == 0) Finished = true;
        return;
      }
      if (w.Now - _startedAt >= StallPolicy.HardStepLimitMs)
      {
        Aborted = true;
        AbortedOn = step.Name;
        _queue.Clear();
        _startedAt = -1;
      }
    }
  }

  private static void Pump(World w, Chain c, long maxMs, Action? frame = null)
  {
    var end = w.Now + maxMs;
    while (w.Now < end)
    {
      c.Tick();
      frame?.Invoke();
      if (!c.Busy && frame == null) return;
      w.Now += 16;
    }
  }

  // One pinch row, mirroring MarketAutomation.EnqueueSingleItem + InsertSingleItem: open the row's
  // menu, adjust price, click Compare Prices, wait, set the price. Guarded wraps each wait in StepStall.
  private static void EnqueueRow(World w, Chain c, StepStall stall, Policy p, int row)
  {
    bool? Bounded(string stage, int limit, string waitingFor, Func<bool?> step, bool lastOfRow = false)
    {
      var r = step();
      if (p == Policy.Legacy) return r;
      var v = stall.Evaluate(stage + row, w.Now, r, limit, out var waited);
      if (v == StallVerdict.Pending) return false;
      if (v == StallVerdict.Done) return true;
      w.Log.Add(StallLine.Skipped(stage, row, waited, waitingFor));
      foreach (var d in new[] { "ItemSearchResult", "ContextMenu", "RetainerSell" }) w.Open.Remove(d);
      w.Price = null;
      w.Skip = !lastOfRow;
      return true;
    }

    c.Enqueue(new SimStep($"OpenItemContextMenu{row}", () => Bounded("OpenItemContextMenu", StallPolicy.RowStepSoftMs, "the sell list",
      () => { if (w.Skip) return true; if (!w.Open.Contains("RetainerSellList")) return false; w.Open.Add("ContextMenu"); return true; }), 100));
    c.Enqueue(new SimStep($"ClickAdjustPrice{row}", () => Bounded("ClickAdjustPrice", StallPolicy.RowStepSoftMs, "the item menu",
      () => { if (w.Skip) return true; if (!w.Open.Contains("ContextMenu")) return false; w.Open.Remove("ContextMenu"); w.Open.Add("RetainerSell"); return true; }), 100));
    c.Enqueue(new SimStep($"ClickComparePrice{row}", () => Bounded("ClickComparePrice", StallPolicy.RowStepSoftMs, "the price dialog",
      () =>
      {
        if (w.Skip) return true;
        if (!w.Open.Contains("RetainerSell")) return false;
        if (!w.NoMarketReply.Contains(row))
        {
          w.Open.Add("ItemSearchResult");
          w.Price = w.EmptyBoard.Contains(row) ? -1 : 1000 + row;
          w.PriceAt = w.Now + 1200;
        }
        return true;
      }), 5000));
    var historyStartedAt = -1L;
    c.Enqueue(new SimStep($"SetNewPrice{row}", () => Bounded("SetNewPrice", StallPolicy.PriceStepSoftMs, "a market-board price", () =>
    {
      if (w.Skip) { w.Skip = false; return true; }
      if (w.Price == null || w.Now < w.PriceAt) return false;
      if (w.Price < 0)
      {
        // Empty board: the sale-history lookup. 0.2.8.13 lost an HTTP timeout here (swallowed as a
        // cancellation) and nothing ever answered; the Guarded step gives up at its soft budget.
        if (historyStartedAt < 0) historyStartedAt = w.Now;
        if (w.HistoryNeverAnswers.Contains(row)) return false;
        w.Price = 900;
      }
      w.Repriced[row] = w.Repriced.GetValueOrDefault(row) + 1;
      w.Open.Remove("RetainerSell");
      w.Open.Remove("ItemSearchResult");
      w.Price = null;
      return true;
    }, lastOfRow: true), 0));
  }

  private sealed record SessionResult(World W, Chain C, string? LastStage, string[] Closed);

  // An AutoRetainer postprocess session: the sell list is open, N rows are walked, the list closes.
  // When the chain ends without finishing, the watchdog hands AutoRetainer back - Guarded closes
  // the dialogs first.
  private static SessionResult RunArSession(Policy p, int rows, Action<World>? setup = null)
  {
    var w = new World();
    w.Open.Add("RetainerSellList");
    w.Open.Add("SelectString");
    setup?.Invoke(w);
    var c = new Chain(w);
    var stall = new StepStall();
    for (var i = 0; i < rows; i++) EnqueueRow(w, c, stall, p, i);
    c.Enqueue(new SimStep("AR.CloseSellList", () => { w.Open.Remove("RetainerSellList"); return true; }, 300));
    c.Enqueue(new SimStep("AR.Finish", () => { return true; }));

    var recovery = new DialogRecovery(0);
    var recovering = false;
    var closed = Array.Empty<string>();
    Pump(w, c, 600_000, () =>
    {
      if (w.Released) return;
      if (c.Busy) return;
      if (c.Aborted && p == Policy.Guarded)
      {
        if (!recovering) { recovery = new DialogRecovery(w.Now); recovering = true; }
        var step = recovery.Next(w.Now, w.Open.Contains);
        if (step.Kind == RecoveryKind.Close) { w.Open.Remove(step.Addon!); return; }
        if (step.Kind == RecoveryKind.Wait) return;
        closed = recovery.Closed.ToArray();
      }
      w.Released = true;
      w.OpenAtRelease = w.Open.Where(n => n != "SelectString").ToArray();
    });
    return new SessionResult(w, c, c.AbortedOn, closed);
  }

  public delegate void CheckFn(string name, bool ok, string detail = "");

  public static void Run(CheckFn check)
  {
    // 163. A row whose Compare Prices click never gets a market-board reply (the 15:22 shape).
    {
      var legacy = RunArSession(Policy.Legacy, 5, w => w.NoMarketReply.Add(2));
      check("163 legacy control: a silent market board aborts the chain on SetNewPrice2",
        legacy.C.Aborted && legacy.LastStage == "SetNewPrice2", $"aborted={legacy.C.Aborted} on={legacy.LastStage}");
      check("163 legacy control: the abort leaves the price dialog open when AutoRetainer is released",
        legacy.W.Released && legacy.W.OpenAtRelease.Contains("RetainerSell"), string.Join(",", legacy.W.OpenAtRelease));
      check("163 legacy control: rows after the stall were never priced",
        !legacy.W.Repriced.ContainsKey(3) && !legacy.W.Repriced.ContainsKey(4));

      var g = RunArSession(Policy.Guarded, 5, w => w.NoMarketReply.Add(2));
      check("163 a silent market board skips that row; the chain is NOT aborted", !g.C.Aborted, g.LastStage ?? "");
      check("163 every other row is still priced exactly once",
        g.W.Repriced.Count == 4 && g.W.Repriced.Values.All(v => v == 1) && !g.W.Repriced.ContainsKey(2), string.Join(",", g.W.Repriced));
      check("163 the session releases AutoRetainer with no dialog of ours open", g.W.Released && g.W.OpenAtRelease.Length == 0, string.Join(",", g.W.OpenAtRelease));
      check("163 the stall names its stage and row in the log",
        g.W.Log.Count == 1 && g.W.Log[0].Contains("stage=SetNewPrice row=2"), string.Join(" | ", g.W.Log));
    }

    // 164. An empty board whose sale-history lookup never answers (the 08:52 shape: 162 tasks cleared).
    {
      var legacy = RunArSession(Policy.Legacy, 8, w => { w.EmptyBoard.Add(5); w.HistoryNeverAnswers.Add(5); });
      check("164 legacy control: a lost history answer aborts the chain with 2+ rows still queued",
        legacy.C.Aborted && legacy.LastStage == "SetNewPrice5" && !legacy.W.Repriced.ContainsKey(7), $"on={legacy.LastStage}");
      var g = RunArSession(Policy.Guarded, 8, w => { w.EmptyBoard.Add(5); w.HistoryNeverAnswers.Add(5); });
      check("164 a lost history answer skips that row at the soft budget, before the hard limit",
        !g.C.Aborted && g.W.Log.Count == 1 && g.W.Log[0].Contains("stage=SetNewPrice row=5"), string.Join(" | ", g.W.Log));
      check("164 the rows after it are priced", g.W.Repriced.ContainsKey(6) && g.W.Repriced.ContainsKey(7));
      check("164 the soft budget sits under the hard limit",
        StallPolicy.PriceStepSoftMs < StallPolicy.HardStepLimitMs && StallPolicy.RowStepSoftMs < StallPolicy.PriceStepSoftMs);
    }

    // 165. A step that stalls BEFORE the price dialog (the context menu never opens).
    {
      var g = RunArSession(Policy.Guarded, 4, w => { });
      check("165 control: a clean 4-row session prices every row once and logs no stall",
        g.W.Repriced.Count == 4 && g.W.Repriced.Values.All(v => v == 1) && g.W.Log.Count == 0 && !g.C.Aborted);

      var w2 = new World();
      w2.Open.Add("RetainerSellList");
      var c2 = new Chain(w2);
      var stall = new StepStall();
      // Row 0's menu never opens because the sell list is not there; row 1 has a sell list again.
      w2.Open.Remove("RetainerSellList");
      EnqueueRow(w2, c2, stall, Policy.Guarded, 0);
      c2.Enqueue(new SimStep("Reopen", () => { w2.Open.Add("RetainerSellList"); return true; }));
      EnqueueRow(w2, c2, stall, Policy.Guarded, 1);
      Pump(w2, c2, 120_000);
      check("165 a row whose menu never opens is skipped inside the soft budget and the next row still runs",
        !c2.Aborted && w2.Repriced.ContainsKey(1) && !w2.Repriced.ContainsKey(0) && w2.Log.Any(l => l.Contains("stage=OpenItemContextMenu row=0")),
        string.Join(" | ", w2.Log));
    }

    // 166. A chain that DOES abort (a non-row wait) must still hand back a clean game.
    {
      var w = new World();
      w.Open.UnionWith(["RetainerSellList", "RetainerSell", "ItemSearchResult", "ContextMenu", "SelectString"]);
      var legacyRelease = w.Open.Where(n => n != "SelectString").ToArray();
      check("166 legacy control: releasing as-found leaves 4 dialogs of ours open", legacyRelease.Length == 4, string.Join(",", legacyRelease));

      var rec = new DialogRecovery(0);
      long now = 0;
      var order = new List<string>();
      for (var i = 0; i < 100 && !w.Released; i++, now += 100)
      {
        var s = rec.Next(now, w.Open.Contains);
        if (s.Kind == RecoveryKind.Close) { order.Add(s.Addon!); w.Open.Remove(s.Addon!); }
        if (s.Kind is RecoveryKind.Done or RecoveryKind.GaveUp) w.Released = true;
      }
      check("166 recovery closes innermost-first: result, menu, price dialog, sell list",
        order.SequenceEqual(["ItemSearchResult", "ContextMenu", "RetainerSell", "RetainerSellList"]), string.Join(",", order));
      check("166 recovery leaves the retainer menu alone for AutoRetainer", w.Open.SetEquals(["SelectString"]), string.Join(",", w.Open));
      check("166 recovery never lists the buyback-abandon confirm", !DialogRecovery.CloseOrder.Contains(DialogRecovery.ProtectedConfirm));

      var stuck = new DialogRecovery(0);
      var gaveUp = stuck.Next(StallPolicy.RecoveryGiveUpMs, n => n == "RetainerSell");
      check("166 recovery gives up at its time bound instead of waiting forever", gaveUp.Kind == RecoveryKind.GaveUp, gaveUp.Kind.ToString());

      var asked = new DialogRecovery(0);
      var first = asked.Next(0, n => n == "RetainerSell");
      var second = asked.Next(100, n => n == "RetainerSell");
      var third = asked.Next(StallPolicy.RecoveryReaskMs + 1, n => n == "RetainerSell");
      check("166 recovery re-asks a stuck dialog only after the re-ask interval",
        first.Kind == RecoveryKind.Close && second.Kind == RecoveryKind.Wait && third.Kind == RecoveryKind.Close,
        $"{first.Kind},{second.Kind},{third.Kind}");

      var line = StallLine.Recovered("chain ended without Finish", "SetNewPrice15", ["RetainerSell"], []);
      check("166 the recovery line names the last stage and what it closed", line.Contains("SetNewPrice15") && line.Contains("closed RetainerSell"), line);
    }

    // 167. A sweep interrupted partway: the skipped retainers are named, and a re-run does only those.
    {
      var progress = new SweepProgress();
      var names = new[] { "R1", "R2", "R3", "R4" };
      var repriced = new Dictionary<string, int>();
      progress.Begin(names, 0);
      foreach (var n in new[] { "R1", "R2" }) { repriced[n] = repriced.GetValueOrDefault(n) + 1; progress.MarkDone(n); }
      // R3's chain aborts midway: its rows were partly walked but it never reached its Done marker.
      check("167 an interrupted sweep names the retainers it did not run", progress.NotRun.SequenceEqual(["R3", "R4"]), string.Join(",", progress.NotRun));
      progress.Interrupt(1000);

      var skipped = progress.Begin(names, 2000);
      check("167 the re-run skips exactly the retainers that finished", skipped.SequenceEqual(["R1", "R2"]), string.Join(",", skipped));
      check("167 the re-run plans only the remaining retainers", progress.Planned.SequenceEqual(["R3", "R4"]), string.Join(",", progress.Planned));
      foreach (var n in progress.Planned) { repriced[n] = repriced.GetValueOrDefault(n) + 1; progress.MarkDone(n); }
      check("167 after the re-run every retainer was repriced exactly once", names.All(n => repriced.GetValueOrDefault(n) == 1), string.Join(",", repriced));
      check("167 a finished re-run leaves nothing to resume", progress.NotRun.Count == 0);
      progress.Complete();
      check("167 the sweep after a completed one starts from the full list", progress.Begin(names, 3000).Count == 0 && progress.Planned.Count == 4);

      var late = new SweepProgress();
      late.Begin(names, 0);
      late.MarkDone("R1");
      late.Interrupt(0);
      check("167 an interruption older than the resume window is not resumed",
        late.Begin(names, SweepProgress.ResumeWindowMs + 1).Count == 0 && late.Planned.Count == 4);

      var twice = new SweepProgress();
      twice.Begin(names, 0);
      twice.MarkDone("R1");
      twice.Interrupt(10);
      twice.Begin(names, 20);
      twice.MarkDone("R2");
      twice.Interrupt(30);
      var s2 = twice.Begin(names, 40);
      check("167 a second interruption keeps the first run's finished retainers out too", s2.SequenceEqual(["R1", "R2"]), string.Join(",", s2));
    }

    // 168. The session cap follows the rows walked.
    {
      var perRow = StallPolicy.PerRowBudgetMs(5069, 5041);
      var flat = 5L * 60 * 1000;
      var cap20 = StallPolicy.SessionCapMs(flat, 20, perRow);
      check("168 the per-row budget covers the configured delays plus slack", perRow >= 5069 + 5041 + 3000, perRow.ToString());
      check("168 a 20-row pinch at the configured delays fits inside its session cap",
        cap20 > flat + 20 * 10_980L, cap20.ToString());
      check("168 a short list keeps the base cap", StallPolicy.SessionCapMs(flat, 0, perRow) == flat);
    }

    // 169. The wiring: MarketAutomation really routes through the guard (source scan; control form of case 162).
    {
      var roots = new[]
      {
        Path.Combine("..", "..", "..", "..", "..", "src", "LazyMarketCompanion"),
        Path.Combine("src", "LazyMarketCompanion"),
      };
      var root = roots.Where(Directory.Exists).FirstOrDefault() ?? "";
      var path = Path.Combine(root, "MarketAutomation.cs");
      check("169 scan: MarketAutomation.cs is found", root.Length > 0 && File.Exists(path), path);
      if (File.Exists(path))
      {
        var src = File.ReadAllText(path);
        var prov = File.ReadAllText(Path.Combine(root, "UniversalisPriceProvider.cs"));
        int Count(string s, string needle) => (s.Length - s.Replace(needle, "").Length) / needle.Length;

        check("169 scan: every row step is wrapped in the stall guard",
          new[] { "OpenItemContextMenu", "ClickAdjustPrice", "DelayMarketBoard", "ClickComparePrice", "SetNewPrice" }
            .All(st => src.Contains($"Bounded(\"{st}\"")),
          "a row step is not wrapped");
        check("169 scan: no HTTP timeout is swallowed as a cancellation (every catch is filtered on the caller's own token)",
          Count(src, "catch (OperationCanceledException) { return; }") == 0, $"{Count(src, "catch (OperationCanceledException) { return; }")} unfiltered swallow(s)");
        check("169 scan: the AutoRetainer release goes through the dialog recovery",
          src.Contains("new DialogRecovery(") && src.Contains("FinishArRelease"), "recovery not wired before the release");
        check("169 scan: the session cap scales with the rows walked", src.Contains("StallPolicy.SessionCapMs("), "flat cap");
        check("169 scan: the buyback park stops the sweep with a plain report instead of timing out on the next retainer",
          src.Contains("BuybackParkStop("), "park still falls through to the next ClickRetainer");
        _ = prov;
      }
    }
  }
}
