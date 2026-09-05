using System.Diagnostics;
using LazyCrafter.Catalog;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;
using LazyCrafter.Adapters;
using LazyCrafter;
using Dalamud.Plugin.Services;

namespace LazyCrafter.CartServiceProof;

/// <summary>
/// t_9f646f4c proof: the REAL CatalogService.cs compiled against fakes. Proves the acceptance contract:
/// while a full pass (prologue + compute + Universalis refine) is in flight, a cart edit (1) is visible
/// immediately, (2) is what Dispatch would act on (LiveCart), and (3) a finished run pokes nothing - no
/// second full pass. Plus a concurrency hammer against an in-place refine.
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
        var svc = new CatalogService(new Plugin(), new FakeFramework(), new NullLog());
        svc.Invalidate();   // what the real plugin does on login (OnLogin) / first window Request - wakes the worker
        return (svc, plugin);
    }

    private sealed class NullLog : IPluginLog
    {
        public void Debug(string m) { }
        public void Debug(Exception e, string m) { }
        public void Debug(string t, params object[] v) { }
        public void Information(string m) { }
        public void Information(Exception e, string m) { }
        public void Information(string t, params object[] v) { }
        public void Warning(string m) => Console.WriteLine("   [wrn] " + m);
        public void Warning(Exception e, string m) => Console.WriteLine("   [wrn] " + e.Message + ": " + m);
        public void Warning(string t, params object[] v) { }
        public void Error(string m) => Console.WriteLine("   [err] " + m);
        public void Error(Exception e, string m) => Console.WriteLine("   [err] " + m);
        public void Error(string t, params object[] v) { }
    }

    private static void Main()
    {
        Console.WriteLine("== t_9f646f4c CartServiceProof ==");

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

        Console.WriteLine($"\n{(_failures == 0 ? "OK" : "FAILED")}");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }
}
