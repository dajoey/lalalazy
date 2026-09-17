using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.
//
// 0.1.40.0: the routing MOVER (Helm t-joey-1789190796770, Joey chose "build-mover"). Category routing
// as shipped in 0.1.37.0 is a GATE - CategoryRouter.FilterForRetainer decides "may this item list on
// THIS retainer" and an item sitting in the wrong retainer's inventory is skipped there, invisible
// during the right retainer's session, so it never lists anywhere and never says why. That silence is
// what read as "it has not worked once since we set it up". This pass is the missing other half:
// during each retainer's own session it MOVES stock so the division the rules describe actually
// happens -
//   - RetainerToBags: an enabled, marketable, routed item sitting in THIS retainer's pages whose
//     category is assigned to a DIFFERENT retainer is pulled into the player's bags, where the
//     assigned retainer's own session can take it in.
//   - BagsToRetainer: the same item sitting in the player's bags while its ASSIGNED retainer's
//     session is open is deposited into that retainer's pages, ahead of the listing plan.
//
// Both legs run inside the same retainer session Auto-Market already drives (the sell list is open,
// which is when the retainer page + bags containers are loaded), as inventory container moves via
// InventoryManager.MoveItemSlot - the same class of call the listing path already makes. Movement
// runs BEFORE BuildPlan's fresh snapshot, so moved stock participates in this same session's listing
// plan (subject to the gate, which has already run on the pre-move fetch - a moved item with no
// quote lists, same as any uncertainty).
//
// BINDING CONSTRAINTS (from the 0.1.37.0 design, still load-bearing here):
//   - Only automarket-ENABLED, MARKETABLE items are ever touched. No rule, no rule match, sheet
//     miss, or ExcludeFromCategoryRouting checkbox tick -> the stack is never moved (fail open -
//     exactly the polarity the gate uses; routing must never be why an item silently disappears).
//   - KeepInRetainer / KeepInBags floors are respected: only the units ABOVE the keep floor move.
//   - Crystals containers (player Crystals 2001 / RetainerCrystals 12001) are deliberately out of
//     scope: crystals auto-stack across their own containers and MoveItemSlot into them is
//     unproven, so the mover leaves them for the planner, which lists them fine from where they sit.
//   - A full destination is a normal state, not an error: the leg stops with a note and the sweep
//     continues (PullPlan.StoppedForSpace precedent).

// 0.1.43.0: a marked, marketable, non-excluded BAGS stack whose category matches no routing rule
// is still never moved (there is no destination; fail-open unchanged) but is now REPORTED on the
// plan (RoutingMovePlan.UnroutedBagsStacks) so the sweep can name it instead of passing over it
// in silence - Helm t-joey-1789190796770: a Heavy Metal Culverin (Machinist's Arms, cat 77) sat
// in the bags sweep after sweep, marked for Auto-Market, touched by nothing, said nothing.

/// <summary>Which way a routing move goes. RetainerToBags frees misplaced stock for the assigned
/// retainer's own session; BagsToRetainer deposits assigned stock into the retainer being visited.</summary>
public enum MoveLeg { RetainerToBags, BagsToRetainer }

/// <summary>One whole-stack container-to-container move. SrcContainer is a game InventoryType value
/// as int (StockStack.Container) - the game-side executor re-reads the slot before firing. There is
/// deliberately no Quantity: InventoryManager.MoveItemSlot moves the WHOLE stack (with no quantity
/// argument to split it), so a keep floor cannot be honoured on a move and the planner skips
/// keep-floored stacks instead of half-honouring one.</summary>
public sealed record RoutingMoveOp(MoveLeg Leg, int SrcContainer, int SrcSlot, uint ItemId, bool HQ)
{
  /// <summary>0.1.44.0: the retainer whose session this move was planned for. Stamped by the
  /// game-side planner; ExecuteRoutingMove refuses to fire when the live active retainer differs
  /// (the retainer-switch window where moves landed against the previous retainer's pages and were
  /// rolled back). Empty means "no session identity known" (a hand-built op), which skips the
  /// check rather than failing it - fail-closed only when identity is actually known.
  /// 0.1.49.0: stamped for every planned op inside PlanCore (the planning session name), not
  /// left for the game side - the 0.1.44.0-0.1.48.0 planner never stamped the ops, only the plan,
  /// so this guard never fired.</summary>
  public string SessionRetainer { get; init; } = "";

  /// <summary>0.1.49.0: the retainer the item's category is assigned to (the rule that authorized
  /// this move), and the market-board search category that matched. Log/audit only - the executor
  /// never decides from these - so the next "what moved, from/to whom, which rule authorized it"
  /// report is evidenced, not guessed.</summary>
  public string MappedRetainer { get; init; } = "";

  /// <summary>0.1.49.0: market-board search category id that authorized this move. See <see cref="MappedRetainer"/>.</summary>
  public uint CategoryId { get; init; }
}

/// <summary>The mover's plan. Notes carry the per-retainer summary lines; StoppedForBags /
/// StoppedForRetainer are true when that leg ended early because its destination filled up - the
/// sweep itself is never stopped by either.</summary>
public sealed record RoutingMovePlan(
  IReadOnlyList<RoutingMoveOp> Ops,
  IReadOnlyList<string> Notes,
  bool StoppedForBags,
  bool StoppedForRetainer)
{
  /// <summary>0.1.44.0: the retainer whose open session this plan was built against. The executor
  /// compares it to the live active retainer for every move, so a plan built mid-switch cannot fire
  /// its moves against the wrong retainer's pages.</summary>
  public string SessionRetainer { get; init; } = "";

  /// <summary>0.1.43.0: enabled, marketable, non-excluded BAGS stacks whose category matches no
  /// routing rule. Never moved (fail-open: the gate keeps them eligible on every retainer, so they
  /// list from the bags whenever a free market slot reaches them) - reported so a marked item the
  /// rules do not cover can never again pass a whole sweep untouched and unmentioned.</summary>
  public IReadOnlyList<(uint ItemId, bool HQ)> UnroutedBagsStacks { get; init; } =
    Array.Empty<(uint ItemId, bool HQ)>();
}

/// <summary>
/// Decides which stacks move during THIS retainer's session. Pure function of the stock snapshot,
/// the enabled rule list, the category lookups, and the routing rules - the game-side caller
/// (AutoMarketService.PlanRoutingMoves) builds those exactly as ApplyCategoryRouting does, so the
/// mover and the gate can never disagree about which retainer a category belongs to.
/// </summary>
public static class RoutingMove
{
  // InventoryType.Crystals / InventoryType.RetainerCrystals as raw ints - this file is Dalamud-free.
  private const int CrystalsContainer = 2001;
  private const int RetainerCrystalsContainer = 12001;

  /// <summary>
  /// 0.1.50.0: session-qualified key for the post-move reconciliation ledger. The game side
  /// records this key for every routing move that reports OK and drops any later plan op with
  /// the same key: a stack that was moved OK yet is back in the same source slot did not stick
  /// server-side (the 20:34-20:39 ET double-sweep re-move loop), so re-firing it every sweep is
  /// never progress. Session-qualified because retainer page containers are the same
  /// InventoryType values for every retainer - the open retainer's pages - so a bare
  /// container:slot key would collide across sessions. The session is trimmed (see
  /// CategoryRouter.RetainerNamesEqual) so a whitespace-variant read of the same retainer
  /// still matches. Format pinned by harness case 99.
  /// </summary>
  public static string ReconcileKey(string session, int container, int slot, uint itemId, bool hq)
    => $"{session?.Trim() ?? string.Empty}|{container}:{slot}:{itemId}:{(hq ? "hq" : "nq")}";

  /// <summary>
  /// 0.1.51.0: sticky reconciliation window. The 0.1.50.0 log proved prune-on-absence is racy:
  /// a move can verify empty seconds after firing (source slot empty on re-read, entry pruned
  /// at the next lap as "landed") yet be back in the same slot on the next sweep - the server
  /// rollback propagates slower than the prune, so the next sweep re-fires a move it already
  /// made. A ledger entry therefore survives absence for this window after its last OK: a stack
  /// still misplaced when the window lapses re-plans (genuinely new work retries), while a
  /// slow rollback keeps hitting a live entry and stays skipped with the did-not-stick note.
  /// Entries for stacks still sitting in place are never pruned (unchanged 0.1.50.0 behavior).
  /// Pure function of timestamps so the harness pins the boundaries (cases 103-105).
  /// </summary>
  public static readonly TimeSpan ReconcileStickyWindow = TimeSpan.FromMinutes(30);

  public static bool ShouldPruneReconcileEntry(DateTime recordedAtUtc, bool stillPresent, DateTime nowUtc)
    => !stillPresent && (nowUtc - recordedAtUtc) >= ReconcileStickyWindow;

  /// <summary>
  /// 0.1.52.0: maximum age of a persisted reconciliation-ledger entry on load. The in-memory
  /// ledger never prunes a stack still sitting in place - but it also never survives a plugin
  /// reload, so the first sweep after a reload re-fires every quarantined move at once (the
  /// 22:40 ET run re-planned the identical pull-outs the 21:38 ET run had already moved OK,
  /// filling the bags to zero free slots). Persisting the ledger closes that hole; the cap
  /// bounds the slot-reuse alias on the key (a different stack of the same item later sitting
  /// in the same slot): anything older than this re-plans once and re-records, instead of
  /// staying skipped forever.
  /// </summary>
  public static readonly TimeSpan ReconcileLedgerMaxAge = TimeSpan.FromHours(24);

  /// <summary>
  /// 0.1.52.0: deterministic text form of the reconciliation ledger for disk persistence -
  /// one `key\tISO-8601-UTC` line per entry, sorted by key. Dalamud-free so the harness
  /// pins the round trip (cases 106-108); the game side owns the file itself.
  /// </summary>
  public static string SerializeReconcileLedger(IReadOnlyDictionary<string, DateTime> ledger)
  {
    var lines = new List<string>(ledger.Count);
    foreach (var key in ledger.Keys.OrderBy(k => k, StringComparer.Ordinal))
      lines.Add($"{key}\t{ledger[key].ToUniversalTime():o}");
    return string.Join("\n", lines);
  }

  /// <summary>
  /// 0.1.52.0: tolerant inverse of <see cref="SerializeReconcileLedger"/>. Malformed lines
  /// (no tab, empty key, unparseable timestamp) are skipped, never fatal - a hand-edited or
  /// half-written file must degrade to "some quarantine lost", not to a sweep that throws.
  /// Entries older than <see cref="ReconcileLedgerMaxAge"/> are dropped: a stack the ledger
  /// has not seen move OK in a full day re-plans once rather than staying skipped on a stale
  /// key. Duplicate keys keep the newest stamp.
  /// </summary>
  public static Dictionary<string, DateTime> ParseReconcileLedger(string? text, DateTime nowUtc)
  {
    var ledger = new Dictionary<string, DateTime>(StringComparer.Ordinal);
    if (string.IsNullOrEmpty(text))
      return ledger;
    foreach (var rawLine in text.Split('\n'))
    {
      var line = rawLine.TrimEnd('\r');
      var tab = line.IndexOf('\t');
      if (tab <= 0 || tab == line.Length - 1)
        continue;
      var key = line.Substring(0, tab);
      if (string.IsNullOrEmpty(key))
        continue;
      if (!DateTime.TryParse(line.Substring(tab + 1), null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var stamp))
        continue;
      var utc = stamp.ToUniversalTime();
      if ((nowUtc - utc) >= ReconcileLedgerMaxAge)
        continue;
      if (!ledger.TryGetValue(key, out var existing) || utc > existing)
        ledger[key] = utc;
    }
    return ledger;
  }

  public static RoutingMovePlan Plan(
    IReadOnlyList<StockStack> stock,
    IReadOnlyList<ItemRule> rules,
    IReadOnlyDictionary<string, ItemCategoryInfo> categoryByKey,
    IReadOnlyDictionary<string, bool> excludeByKey,
    IReadOnlyList<CategoryRetainerRule> categoryRules,
    string retainerName,
    Func<int> freeBagSlots,
    Func<int> freeRetainerSlots)
  {
    return PlanCore(stock, rules, categoryByKey, excludeByKey, categoryRules, retainerName,
      freeBagSlots, freeRetainerSlots, depositsOnly: false);
  }

  /// <summary>
  /// 0.1.44.0: Plan with the once-per-run pull guard. movedThisRun carries the stack keys this run
  /// already pulled to bags (filled by the executor as pulls succeed); a stack already pulled this
  /// run is never pulled again, so a deposit that the server rolls back during a retainer switch
  /// cannot turn into an endless bags->retainer->bags cycle across subsequent passes and laps.
  /// Deposits are NOT guarded - depositing an item twice is impossible (after the first deposit the
  /// stack is no longer in the bags) and the lap exists precisely to retry deposits that stranded.
  /// 0.1.50.0: reconciledSkip carries the session-qualified ReconcileKeys of moves that reported
  /// OK on an earlier sweep (filled by the executor, pruned when the stack leaves its slot). A
  /// plan op with the same key means the stack is back where a previous sweep moved it from -
  /// the move did not stick - so it is skipped on BOTH legs instead of re-fired every sweep.
  /// </summary>
  public static RoutingMovePlan Plan(
    IReadOnlyList<StockStack> stock,
    IReadOnlyList<ItemRule> rules,
    IReadOnlyDictionary<string, ItemCategoryInfo> categoryByKey,
    IReadOnlyDictionary<string, bool> excludeByKey,
    IReadOnlyList<CategoryRetainerRule> categoryRules,
    string retainerName,
    Func<int> freeBagSlots,
    Func<int> freeRetainerSlots,
    IReadOnlyCollection<string>? movedThisRun,
    IReadOnlyCollection<string>? reconciledSkip = null)
  {
    return PlanCore(stock, rules, categoryByKey, excludeByKey, categoryRules, retainerName,
      freeBagSlots, freeRetainerSlots, depositsOnly: false, movedThisRun: movedThisRun, reconciledSkip: reconciledSkip);
  }

  /// <summary>
  /// 0.1.42.0: Runs the mover in deposit-only mode for the final deposit lap.
  /// Skips all RetainerToBags pull moves so stock is only ever deposited.
  /// 0.1.50.0: reconciledSkip threaded through (see Plan) - a rolled-back deposit reappearing
  /// in the bags is skipped the same way a rolled-back pull-out is.
  /// </summary>
  public static RoutingMovePlan PlanDepositsOnly(
    IReadOnlyList<StockStack> stock,
    IReadOnlyList<ItemRule> rules,
    IReadOnlyDictionary<string, ItemCategoryInfo> categoryByKey,
    IReadOnlyDictionary<string, bool> excludeByKey,
    IReadOnlyList<CategoryRetainerRule> categoryRules,
    string retainerName,
    Func<int> freeBagSlots,
    Func<int> freeRetainerSlots,
    IReadOnlyCollection<string>? reconciledSkip = null)
  {
    return PlanCore(stock, rules, categoryByKey, excludeByKey, categoryRules, retainerName,
      freeBagSlots, freeRetainerSlots, depositsOnly: true, reconciledSkip: reconciledSkip);
  }

  /// <summary>
  /// 0.1.42.0: True when at least one bags stack would generate a deposit op for retainerName under the decision rules.
  /// </summary>
  public static bool HasPendingDeposits(
    IReadOnlyList<StockStack> stock,
    IReadOnlyList<ItemRule> rules,
    IReadOnlyDictionary<string, ItemCategoryInfo> categoryByKey,
    IReadOnlyDictionary<string, bool> excludeByKey,
    IReadOnlyList<CategoryRetainerRule> categoryRules,
    string retainerName)
  {
    // Capacity is unknowable before the retainer is opened; the lap decides capacity at execution time like every other move.
    return HasPendingDeposits(stock, rules, categoryByKey, excludeByKey, categoryRules, retainerName, () => 0, () => int.MaxValue);
  }

  public static bool HasPendingDeposits(
    IReadOnlyList<StockStack> stock,
    IReadOnlyList<ItemRule> rules,
    IReadOnlyDictionary<string, ItemCategoryInfo> categoryByKey,
    IReadOnlyDictionary<string, bool> excludeByKey,
    IReadOnlyList<CategoryRetainerRule> categoryRules,
    string retainerName,
    Func<int> freeRetainerSlots)
  {
    // Capacity is unknowable before the retainer is opened; the lap decides capacity at execution time like every other move.
    return HasPendingDeposits(stock, rules, categoryByKey, excludeByKey, categoryRules, retainerName, () => 0, freeRetainerSlots);
  }

  public static bool HasPendingDeposits(
    IReadOnlyList<StockStack> stock,
    IReadOnlyList<ItemRule> rules,
    IReadOnlyDictionary<string, ItemCategoryInfo> categoryByKey,
    IReadOnlyDictionary<string, bool> excludeByKey,
    IReadOnlyList<CategoryRetainerRule> categoryRules,
    string retainerName,
    Func<int> freeBagSlots,
    Func<int> freeRetainerSlots)
  {
    // Capacity is unknowable before the retainer is opened; the lap decides capacity at execution time like every other move.
    var plan = PlanCore(stock, rules, categoryByKey, excludeByKey, categoryRules, retainerName,
      freeBagSlots, () => int.MaxValue, depositsOnly: true, earlyExitOnFirstOp: true);
    return plan.Ops.Count > 0;
  }

  private static RoutingMovePlan PlanCore(
    IReadOnlyList<StockStack> stock,
    IReadOnlyList<ItemRule> rules,
    IReadOnlyDictionary<string, ItemCategoryInfo> categoryByKey,
    IReadOnlyDictionary<string, bool> excludeByKey,
    IReadOnlyList<CategoryRetainerRule> categoryRules,
    string retainerName,
    Func<int> freeBagSlots,
    Func<int> freeRetainerSlots,
    bool depositsOnly,
    bool earlyExitOnFirstOp = false,
    IReadOnlyCollection<string>? movedThisRun = null,
    IReadOnlyCollection<string>? reconciledSkip = null)
  {
    var ops = new List<RoutingMoveOp>();
    var notes = new List<string>();
    if (categoryRules.Count == 0)
      return new RoutingMovePlan(ops, notes, false, false);

    var freeBags = depositsOnly ? 0 : Math.Max(0, freeBagSlots());
    var freeRet = Math.Max(0, freeRetainerSlots());
    var stoppedBags = false;
    var stoppedRet = false;
    var skippedCrystals = 0;
    var alreadyPulledSkip = 0;
    var reconciledSkipCount = 0;
    var unrouted = new List<(uint ItemId, bool HQ)>();
    var unroutedKeys = new HashSet<string>();

    foreach (var stack in stock)
    {
      if (stack.Container == CrystalsContainer || stack.Container == RetainerCrystalsContainer)
      {
        if (rules.Any(r => r.ItemId == stack.ItemId && r.HQ == stack.HQ))
          skippedCrystals++;
        continue;
      }

      var rule = rules.FirstOrDefault(r => r.ItemId == stack.ItemId && r.HQ == stack.HQ);
      if (rule == null)
        continue;

      var key = $"{stack.ItemId}:{(stack.HQ ? "hq" : "nq")}";
      if (!categoryByKey.TryGetValue(key, out var info))
        continue; // sheet miss: fail open, never move what cannot be positively identified
      if (!info.Marketable)
        continue;
      if (excludeByKey.TryGetValue(key, out var ex) && ex)
        continue; // per-item opt-out: sells normally from wherever it sits, on every retainer

      var mapped = categoryRules.FirstOrDefault(r => r.CategoryId == info.CategoryId);
      if (mapped == null)
      {
        // 0.1.43.0: the stack is marked for Auto-Market and perfectly sellable, but no routing
        // rule names a retainer for its category, so the mover has no destination for it. It is
        // NOT moved (fail-open above still applies) - it is REPORTED, once per item, so the gap
        // between "marked for automarket" and "no rule covers it" is visible in the sweep output.
        if (stack.Origin != StockOrigin.Retainer && unroutedKeys.Add(key))
          unrouted.Add((stack.ItemId, stack.HQ));
        continue; // unrouted category: no movement restriction, so no movement duty either
      }

      if (stack.Origin == StockOrigin.Retainer)
      {
        if (depositsOnly)
          continue;

        // 0.1.49.0: normalized identity (CategoryRouter.RetainerNamesEqual) - the rule name is
        // persisted from the RetainerList UI while the session name comes from RetainerManager,
        // so a stray-whitespace mismatch here re-pulled correctly-placed stock on every sweep.
        if (CategoryRouter.RetainerNamesEqual(mapped.RetainerName, retainerName))
          continue; // already in the right retainer

        // MarketPull precedent: never strand a stack. A rule that never sells from bags would leave
        // a pulled-out stack sitting unlistable in the bags whenever the assigned retainer's own
        // deposit leg cannot take it (full pages, session ordering) - strictly worse than leaving
        // it listed-elsewhere-blocked in the retainer it sits in, so it stays put.
        if (!rule.SellFromBags)
          continue;

        // MoveItemSlot moves whole stacks: a keep floor that would leave part of the stack behind
        // cannot be expressed, so the stack stays put entirely (the gate still lists nothing of it
        // here - the keep floor and the mover agree by construction).
        if (stack.Quantity <= rule.KeepInRetainer)
          continue;

        // 0.1.44.0: the once-per-run pull guard. A stack key already pulled to bags this run is
        // never pulled again - even if a later deposit was rolled back during a retainer switch
        // and the stack reappeared in the retainer, re-pulling it just restarts the cycle the
        // guard exists to break. The stack stays put; the NEXT sweep (fresh movedThisRun) retries.
        var pullKey = $"{stack.Container}:{stack.Slot}:{stack.ItemId}:{(stack.HQ ? "hq" : "nq")}";
        if (movedThisRun != null && movedThisRun.Contains(pullKey))
        {
          alreadyPulledSkip++;
          continue;
        }

        // 0.1.50.0: post-move reconciliation. A stack back in the exact slot an earlier sweep
        // moved OK did not stick server-side (the 20:34-20:39 ET double-sweep re-move loop) -
        // re-firing it every sweep is never progress, so it stays put until it leaves the slot
        // (the game side prunes the ledger then) instead of shuffling forever.
        if (reconciledSkip != null && reconciledSkip.Contains(ReconcileKey(retainerName, stack.Container, stack.Slot, stack.ItemId, stack.HQ)))
        {
          reconciledSkipCount++;
          continue;
        }

        if (freeBags <= 0)
        {
          stoppedBags = true;
          continue; // deposits may still be possible - do not break the whole pass
        }

        ops.Add(new RoutingMoveOp(MoveLeg.RetainerToBags, stack.Container, stack.Slot, stack.ItemId, stack.HQ)
          with { SessionRetainer = retainerName, MappedRetainer = mapped.RetainerName, CategoryId = info.CategoryId });
        if (earlyExitOnFirstOp)
          return new RoutingMovePlan(ops, notes, stoppedBags, stoppedRet) { UnroutedBagsStacks = unrouted };
        freeBags--;
      }
      else
      {
        if (!CategoryRouter.RetainerNamesEqual(mapped.RetainerName, retainerName))
          continue; // belongs to a different retainer's session - that retainer takes it in

        // 0.1.50.0: reconciliation on the deposit leg too (see the pull-out branch) - a deposit
        // the server rolled back reappears in the bags and must not be re-fired every sweep.
        if (reconciledSkip != null && reconciledSkip.Contains(ReconcileKey(retainerName, stack.Container, stack.Slot, stack.ItemId, stack.HQ)))
        {
          reconciledSkipCount++;
          continue;
        }

        // Same stranding guard, deposit side: a RetainerOnly rule lists deposited stock fine, but a
        // bags-only rule's deposit would be pointless (it lists from bags, not the retainer) - and
        // a rule that sells from NEITHER cannot list it anywhere, so the stack never moves.
        if (!rule.SellFromRetainer)
          continue;

        if (stack.Quantity <= rule.KeepInBags)
          continue; // whole-stack move only - see the RetainerToBags branch

        if (freeRet <= 0)
        {
          stoppedRet = true;
          continue;
        }

        ops.Add(new RoutingMoveOp(MoveLeg.BagsToRetainer, stack.Container, stack.Slot, stack.ItemId, stack.HQ)
          with { SessionRetainer = retainerName, MappedRetainer = mapped.RetainerName, CategoryId = info.CategoryId });
        if (earlyExitOnFirstOp)
          return new RoutingMovePlan(ops, notes, stoppedBags, stoppedRet) { UnroutedBagsStacks = unrouted };
        freeRet--;
      }
    }

    if (alreadyPulledSkip > 0)
      notes.Add($"routing move: {alreadyPulledSkip} stack(s) were left in place because they were already pulled to bags once this run (a deposit was rolled back mid-switch; the next sweep retries)");
    if (reconciledSkipCount > 0)
      notes.Add($"routing move: {reconciledSkipCount} stack(s) skipped - an earlier sweep moved them OK but they are still in place (the move did not stick server-side); leaving them put instead of re-moving every sweep");
    if (stoppedBags)
      notes.Add("routing move: the player's bags have no free slot for further pull-outs; those items stay in this retainer for now (the sweep continues)");
    if (stoppedRet)
      notes.Add("routing move: this retainer's pages are full, so no further bags stock can be deposited this session (the sweep continues)");
    if (skippedCrystals > 0)
      notes.Add($"routing move: {skippedCrystals} crystal stack(s) were left where they are (crystals move and stack through their own containers; Auto-Market still lists them from there)");

    return new RoutingMovePlan(ops, notes, stoppedBags, stoppedRet) { UnroutedBagsStacks = unrouted };
  }

  /// <summary>The one-line summary for the log/chat announce: "3 pull-out(s), 2 deposit(s)".</summary>
  public static string Summarize(IReadOnlyList<RoutingMoveOp> ops)
  {
    var out_ = ops.Count(o => o.Leg == MoveLeg.RetainerToBags);
    var in_ = ops.Count(o => o.Leg == MoveLeg.BagsToRetainer);
    return $"{out_} pull-out(s) to bags, {in_} deposit(s) from bags";
  }
}
