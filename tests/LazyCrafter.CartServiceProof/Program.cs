using System.Diagnostics;
using LazyCrafter.Catalog;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;
using LazyCrafter.Adapters;
using LazyCrafter;
using Dalamud.Plugin.Services;

namespace LazyCrafter.CartServiceProof;

/// <summary>
/// Proofs for the catalog service (t_9f646f4c + t_410dee8a), against the REAL CatalogService.cs compiled
/// against fakes. t_410dee8a acceptance, as the card states it:
/// (1) an inventory event does NOT trigger the crafting-log scan - the 13,892-flag sweep runs once per
///     invalidated cache (login / settings / Refresh), then never on the counts path;
/// (2) a counts pass still refreshes rows from fresh inventory numbers;
/// (3) a first-craft patches the cached log set for that recipe only and the row follows - no relog,
///     no full pass, no second sweep.
/// Plus the t_9f646f4c regression set: instant cart edits mid-pass, live-cart dispatch reads, no
/// post-run recompute, and the concurrent-edit hammer.
/// </summary>
internal static class Program
{
    private static int _failures;

    private static void Check(bool cond, string name, string detail = "")
    {
        Console.WriteLine($"{(cond ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  " + detail : "")}");
        if (!cond) _failures++;
    }

    // ---- a small world: N recipes, each 2 materials ----
    private const uint ResultBase = 10_000, MatA = 20_000, MatB = 20_001;

    private sealed class BigGameData : IGameData
    {
        private readonly List<RecipeRow> _recipes = new();
        public HashSet<uint> Marketable = new();

        public BigGameData(int count)
        {
            for (uint i = 0; i < count; i++)
                _recipes.Add(new RecipeRow(ResultBase + i, ResultBase + i, 1, 10, 100, new[] { (MatA, 2), (MatB, 1) }));
            Marketable.Add(MatA);
        }
        public IEnumerable<RecipeRow> Recipes() => _recipes;
        public bool IsGilVendor(uint itemId, out uint gil) { gil = 0; return false; }
        public bool IsSpecialShop(uint itemId) => false;
        public GatherInfo? Gather(uint itemId) => null;
        public bool IsFish(uint itemId) => false;
        public IEnumerable<VentureRow> Ventures() => Array.Empty<VentureRow>();
        public bool IsMarketable(uint itemId) => Marketable.Contains(itemId);
        public bool IsDrop(uint itemId) => false;
        public CollectableInfo? Collectable(uint itemId) => null;
        public IReadOnlyList<DesynthResult> Desynth(uint itemId) => Array.Empty<DesynthResult>();
    }

    private static (CatalogService svc, FakePluginAdapter plugin) Build(int recipes)
    {
        var plugin = new FakePluginAdapter
        {
            GameData = new LuminaGameData(new BigGameData(recipes)),
            Prices = { Scope = "dc" },
        };
        Plugin.Pi = plugin;
        var svc = new CatalogService(new Plugin(), plugin.Framework, new NullLog());   // SAME instance the tests measure
        svc.Invalidate();   // what the real plugin does on login (OnLogin) / first window Request - wakes the worker
        return (svc, plugin);
    }

    private sealed class NullLog : IPluginLog
    {
        public void Debug(string m) { }
        public void Debug(Exception e, string m) { }
        public void Debug(string t, params object[] v) { }
        public void Debug(Exception e, string t, params object[] v) { }
        public void Information(string m) { }
        public void Information(Exception e, string m) { }
        public void Information(string t, params object[] v) { }
        public void Information(Exception e, string t, params object[] v) { }
        public void Warning(string m) => Console.WriteLine("   [wrn] " + m);
        public void Warning(Exception e, string m) => Console.WriteLine("   [wrn] " + e.Message + ": " + m);
        public void Warning(string t, params object[] v) { }
        public void Warning(Exception e, string t, params object[] v) => Console.WriteLine("   [wrn] " + (e?.Message ?? "") + ": " + t);
        public void Error(string m) => Console.WriteLine("   [err] " + m);
        public void Error(Exception e, string m) => Console.WriteLine("   [err] " + m);
        public void Error(string t, params object[] v) { }
        public void Error(Exception e, string t, params object[] v) { }
    }

    private static void Main()
    {
        Console.WriteLine("== LazyCrafter CatalogServiceProof (t_9f646f4c + t_410dee8a) ==");

        // ------------------------------------------------------------------ test 1
        Console.WriteLine("\n[1] cart edit is visible IMMEDIATELY while a full pass is in flight (the freeze)");
        var gate = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var (svc, plugin) = Build(300);
        SpinWait.SpinUntil(() => svc.Snapshot.Generation > 0, 20_000);
        Check(svc.Snapshot.Generation > 0, "first full pass published (gen > 0)");
        var g0 = svc.Snapshot.Generation;

        // Hang the NEXT full pass inside the Universalis round: everything stale + a blocking IsStale.
        plugin.Prices.PrimeBlock = release;
        plugin.Prices.IsStaleOverride = _ => true;
        plugin.Inventory.Counts = ids => { gate.Set(); return new Dictionary<uint, int> { [MatA] = 5, [MatB] = 5 }; };
        svc.Invalidate();   // full pass: prologue -> compute -> hang inside PrimeAndRefine on `release`
        Check(gate.Wait(10_000), "full pass reached the inventory snapshot (in flight)");
        Thread.Sleep(200);  // let it get into the price round

        // THE assertion: while the pass is hung, a cart edit lands NOW.
        var sw = Stopwatch.StartNew();
        svc.AddToCart(ResultBase + 7, 3);
        var editMs = sw.Elapsed.TotalMilliseconds;
        var cart = svc.Snapshot.Cart;
        Check(cart.Any(l => l.RecipeId == ResultBase + 7 && l.Crafts == 3), "cart edit visible in the snapshot DURING the pass", $"line count {cart.Count}");
        Check(editMs < 250, "cart edit returned in < 250 ms", $"{editMs:F1} ms");
        Check(svc.Snapshot.Generation > g0, "snapshot generation advanced by the cart-only republish", $"gen {g0} -> {svc.Snapshot.Generation}");

        // And dispatch would see it: LiveCart is what DispatchCart reads.
        var live = svc.LiveCart();
        Check(live.Lines.Any(l => l.RecipeId == ResultBase + 7 && l.Crafts == 3), "LiveCart() returns the just-typed line (what Dispatch acts on)");

        release.Set();   // let the hung pass finish
        SpinWait.SpinUntil(() => !svc.Busy, 20_000);
        Check(!svc.Busy, "full pass completed after release");

        // post-pass stability: worker publish read the live cart too
        Check(svc.Snapshot.Cart.Any(l => l.RecipeId == ResultBase + 7 && l.Crafts == 3), "worker's own publish kept the edited line (live cart read under lock)");
        svc.Dispose();

        // ------------------------------------------------------------------ test 2
        Console.WriteLine("\n[2] a finished run no longer queues a full catalog pass");
        var (svc2, plugin2) = Build(300);
        SpinWait.SpinUntil(() => svc2.Snapshot.Generation > 0, 20_000);
        var genBefore = svc2.Snapshot.Generation;

        // What Finish() does now: DropMemo() and NO Catalog.Invalidate() (not degraded). Nothing pokes the
        // worker, so no pass may start. The 1-minute price timer is disabled by waiting well under a minute.
        System.Threading.Thread.Sleep(600);
        Check(svc2.Snapshot.Generation == genBefore, "no recompute happens without an invalidate (post-run path pokes nothing)", $"gen {genBefore} -> {svc2.Snapshot.Generation}");
        Check(!svc2.Busy, "worker stayed idle");
        svc2.Dispose();

        // ------------------------------------------------------------------ test 3
        Console.WriteLine("\n[3] SetCartQuantity / ClearCart are also instant, and the cart persists");
        var (svc3, plugin3) = Build(300);
        SpinWait.SpinUntil(() => svc3.Snapshot.Generation > 0, 20_000);
        svc3.AddToCart(ResultBase + 1, 2);
        svc3.AddToCart(ResultBase + 2, 5);
        sw.Restart();
        svc3.SetCartQuantity(ResultBase + 1, 9);
        var setMs = sw.Elapsed.TotalMilliseconds;
        Check(svc3.Snapshot.Cart.Single(l => l.RecipeId == ResultBase + 1).Crafts == 9, "SetCartQuantity visible immediately");
        Check(setMs < 250, "SetCartQuantity < 250 ms", $"{setMs:F1} ms");
        sw.Restart();
        svc3.ClearCart();
        Check(svc3.Snapshot.Cart.Count == 0, "ClearCart visible immediately");
        Check(sw.Elapsed.TotalMilliseconds < 250, "ClearCart < 250 ms", $"{sw.Elapsed.TotalMilliseconds:F1} ms");
        Check(plugin3.Config.Saves >= 3, "cart persisted to config on every mutation", $"{plugin3.Config.Saves} saves");
        svc3.Dispose();

        // ------------------------------------------------------------------ test 4
        Console.WriteLine("\n[4] hammer: 20 UI-thread edits vs a hung refine - no exceptions, no lost edits");
        var release4 = new ManualResetEventSlim(false);
        var (svc4, plugin4) = Build(400);
        SpinWait.SpinUntil(() => svc4.Snapshot.Generation > 0, 20_000);
        plugin4.Prices.PrimeBlock = release4;
        plugin4.Prices.IsStaleOverride = _ => true;
        svc4.RefreshPrices();   // hangs the worker in PrimeAndRefine (a real refine mutates _rows in place)
        Thread.Sleep(200);      // let it get in there

        var errors = 0;
        var made = 0;
        var t1 = new Thread(() => { try { for (var i = 0; i < 20; i++) { svc4.AddToCart(ResultBase + (uint)i, i + 1); made++; } } catch (Exception e) { errors++; Console.WriteLine("   [exc] " + e.GetType().Name + ": " + e.Message); } });
        t1.Start();
        t1.Join();
        release4.Set();
        SpinWait.SpinUntil(() => !svc4.Busy, 20_000);
        Check(errors == 0, "no exceptions during concurrent edits vs refine", $"{errors} errors");
        Check(svc4.Snapshot.Cart.Count == made, "every concurrent edit survived the racing worker publish", $"{svc4.Snapshot.Cart.Count}/{made}");
        var last = svc4.LiveCart();
        Check(last.Lines.Count == 20 && last.Totals.Lines.Count == 20, "LiveCart agrees after the race");
        svc4.Dispose();

        // ------------------------------------------------------------------ test 5  (t_410dee8a)
        Console.WriteLine("\n[5] t_410dee8a: an inventory event does NOT re-trigger the crafting-log scan");
        var (svc5, plugin5) = Build(300);
        SpinWait.SpinUntil(() => svc5.Snapshot.Generation > 0, 20_000);
        var sweeps5 = plugin5.Player.IsRecipeCompleteCalls;
        Check(sweeps5 > 0, "first full pass performed the log sweep", $"{sweeps5} IsRecipeComplete calls");
        Check(plugin5.Framework.MaxInFlightFrameworkBodyMs < 50, "no framework body took anywhere near hitch territory during the first pass", $"max {plugin5.Framework.MaxInFlightFrameworkBodyMs:F1} ms");

        // The gather scenario: a burst of inventory changes, one per node, a couple of seconds apart.
        var stock = 5;
        for (var i = 0; i < 5; i++)
        {
            stock += 3;
            var s = stock;
            plugin5.Inventory.Counts = ids => new Dictionary<uint, int> { [MatA] = s, [MatB] = 5 };
            svc5.InvalidateCounts();       // what the debounced AllaganTools event now calls
            // deterministically wait for THIS pass to land: HowMany = min(MatA/2, MatB) = s/2
            SpinWait.SpinUntil(() => svc5.Snapshot.Rows.First(r => r.RecipeId == ResultBase).HowMany >= s / 2, 20_000);
        }
        Thread.Sleep(200);   // let any tail work settle before reading the counters
        var sweepsAfter = plugin5.Player.IsRecipeCompleteCalls;
        Check(sweepsAfter == sweeps5, "inventory-driven passes performed ZERO crafting-log reads", $"{sweeps5} -> {sweepsAfter}");
        Check(plugin5.Framework.MaxInFlightFrameworkBodyMs < 50, "every counts-pass framework hop stayed in microseconds", $"max {plugin5.Framework.MaxInFlightFrameworkBodyMs:F1} ms");
        Check(svc5.Snapshot.Rows.First(r => r.RecipeId == ResultBase).HowMany >= 2, "rows were still rebuilt with the fresh counts", $"HowMany={svc5.Snapshot.Rows.First(r => r.RecipeId == ResultBase).HowMany}");

        // ...but a REAL invalidate (login / settings / Refresh button) does resweep.
        svc5.Invalidate();
        SpinWait.SpinUntil(() => !svc5.Busy, 20_000);
        Check(plugin5.Player.IsRecipeCompleteCalls > sweepsAfter, "a full Invalidate() re-reads the crafting log (Refresh still refreshes)", $"{sweepsAfter} -> {plugin5.Player.IsRecipeCompleteCalls}");
        svc5.Dispose();

        // ------------------------------------------------------------------ test 6  (t_410dee8a)
        Console.WriteLine("\n[6] t_410dee8a: a first craft patches the cached log set - row updates, no sweep, no full pass");
        var (svc6, plugin6) = Build(300);
        SpinWait.SpinUntil(() => svc6.Snapshot.Generation > 0, 20_000);
        var firstId = ResultBase + 3;
        Check(!svc6.Snapshot.ByRecipe[firstId].LogComplete, "recipe starts as not-crafted");
        var sweeps6 = plugin6.Player.IsRecipeCompleteCalls;

        plugin6.Player.Complete.Add(firstId);      // the game flips the flag
        svc6.NoteCraftCompleted(firstId);          // what WaitCraftEnd now calls
        SpinWait.SpinUntil(() => svc6.Snapshot.ByRecipe[firstId].LogComplete, 20_000);
        Check(svc6.Snapshot.ByRecipe[firstId].LogComplete, "LogComplete updated WITHOUT a relog or manual refresh");
        Thread.Sleep(300);
        Check(plugin6.Player.IsRecipeCompleteCalls == sweeps6 + 1, "exactly ONE extra log read for the crafted recipe (no sweep)", $"{sweeps6} -> {plugin6.Player.IsRecipeCompleteCalls}");
        Check(svc6.Snapshot.NotYetCrafted == 299, "not-yet-crafted counter followed the flip", $"{svc6.Snapshot.NotYetCrafted}");
        svc6.Dispose();

        // ------------------------------------------------------------------ test 7  (t_410dee8a)
        Console.WriteLine("\n[7] t_410dee8a: the counts pass publishes even while a full pass hangs elsewhere");
        var release7 = new ManualResetEventSlim(false);
        var gate7 = new ManualResetEventSlim(false);
        var (svc7, plugin7) = Build(300);
        SpinWait.SpinUntil(() => svc7.Snapshot.Generation > 0, 20_000);
        // hang a refine
        plugin7.Prices.PrimeBlock = release7;
        plugin7.Prices.IsStaleOverride = _ => true;
        svc7.RefreshPrices();
        Thread.Sleep(200);
        plugin7.Inventory.Counts = ids => { gate7.Set(); return new Dictionary<uint, int> { [MatA] = 11, [MatB] = 5 }; };
        // while the worker is hung in the refine, an inventory event arrives:
        svc7.InvalidateCounts();
        // The counts pass queues behind the hung refine (same worker) - it must complete once released,
        // still without any log reads.
        var sweeps7 = plugin7.Player.IsRecipeCompleteCalls;
        release7.Set();
        SpinWait.SpinUntil(() => !svc7.Busy && svc7.Snapshot.Rows.First(r => r.RecipeId == ResultBase).HowMany >= 5, 20_000);
        Check(svc7.Snapshot.Rows.First(r => r.RecipeId == ResultBase).HowMany >= 5, "counts pass landed after the hung refine released (HowMany reflects 11 units)", $"HowMany={svc7.Snapshot.Rows.First(r => r.RecipeId == ResultBase).HowMany}");
        Check(plugin7.Player.IsRecipeCompleteCalls == sweeps7, "still zero log reads on the queued counts pass", $"{sweeps7} -> {plugin7.Player.IsRecipeCompleteCalls}");
        svc7.Dispose();

        Console.WriteLine($"\n{(_failures == 0 ? "OK" : "FAILED")}");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }
}
