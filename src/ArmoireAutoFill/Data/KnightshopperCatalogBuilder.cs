using ArmoireAutoFill.Data.Shopping;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using LuminaCabinet = Lumina.Excel.Sheets.Cabinet;

namespace ArmoireAutoFill.Data;

// Wraps the pure catalog scan (KnightshopperCatalogCore) in Dalamud services: fetches the
// sheets, runs the build at most once per window open (a failure backs off until an explicit
// Refresh, so one failed build logs one error instead of one per frame), logs exactly one
// summary line per attempt, and exposes truthful state for the window messages:
//   * SheetsUnavailable — the game sheets are not loaded/available at all;
//   * BuildFailed — the scan itself failed (detail carries the skip counts);
//   * an empty snapshot is a NORMAL result ("nothing to buy"), not an error.
public static class KnightshopperCatalogBuilder
{
    public static bool IsLoaded => CatalogBuildGate.Current == CatalogBuildGate.Phase.Built;

    public static CatalogSnapshot Snapshot { get; private set; } = CatalogSnapshot.Empty;

    public static bool SheetsUnavailable { get; private set; }

    public static bool BuildFailed => CatalogBuildGate.Current == CatalogBuildGate.Phase.Failed;

    // Short, user-safe failure context, e.g. "0 rows skipped" plus the first failure reason.
    public static string? BuildFailureDetail { get; private set; }

    private static readonly object BuildLock = new();
    private static int _buildInFlight;

    // Called from the draw every frame the shopping section is open. The measured full scan
    // takes hundreds of milliseconds (harness: ~451 ms cold against the real sqpack), so the
    // build runs off the draw thread, at most one in flight, and — per the gate — at most
    // once per open; a failure backs off until the Refresh button is pressed.
    public static void RequestBuild()
    {
        if (Interlocked.CompareExchange(ref _buildInFlight, 1, 0) == 1)
            return; // a build is already queued or running
        if (!CatalogBuildGate.ShouldAttempt())
        {
            Volatile.Write(ref _buildInFlight, 0);
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                lock (BuildLock)
                {
                    CatalogBuildGate.ConsumeRefresh(); // consume an explicit refresh
                    AttemptBuild();
                }
            }
            finally
            {
                Volatile.Write(ref _buildInFlight, 0);
            }
        });
    }

    // The Refresh button in the window.
    public static void RequestRefresh() => CatalogBuildGate.RequestRefresh();

    private static void AttemptBuild()
    {
        var gilShopSheet = Svc.Data.GetExcelSheet<GilShop>();
        var specialShopSheet = Svc.Data.GetExcelSheet<SpecialShop>();
        var npcBaseSheet = Svc.Data.GetExcelSheet<ENpcBase>();
        var npcResidentSheet = Svc.Data.GetExcelSheet<ENpcResident>();
        var itemSheet = Svc.Data.GetExcelSheet<Item>();
        var cabinetSheet = Svc.Data.GetExcelSheet<LuminaCabinet>();
        var levelSheet = Svc.Data.GetExcelSheet<Level>();
        if (gilShopSheet == null || specialShopSheet == null || npcBaseSheet == null || npcResidentSheet == null
            || itemSheet == null || levelSheet == null)
        {
            SheetsUnavailable = true;
            CatalogBuildGate.MarkFailed();
            BuildFailureDetail = "game data sheets are not available";
            Svc.Log.Warning("[ArmoireAutoFill] Knightshopper catalog: game data sheets are not available; not building");
            return;
        }

        try
        {
            var stats = KnightshopperCatalogCore.Build(
                gilShopSheet, specialShopSheet, Svc.Data.GetSubrowExcelSheet<GilShopItem>(),
                npcBaseSheet, npcResidentSheet, levelSheet, itemSheet, cabinetSheet, out var snapshot);
            Snapshot = snapshot;
            SheetsUnavailable = false;
            CatalogBuildGate.MarkBuilt();
            BuildFailureDetail = null;

            var perCurrency = snapshot.Entries.GroupBy(e => e.CurrencyId).OrderBy(g => g.Key)
                .Select(g => $"{CurrencyNames.For(g.Key)} {g.Count()}");
            Svc.Log.Information(
                $"[ArmoireAutoFill] Knightshopper catalog: {snapshot.Entries.Count} entries in {stats.ElapsedMs:F0} ms "
                + $"from {stats.SpecialShopRowsScanned} SpecialShop + {stats.GilShopRowsScanned} GilShop rows "
                + $"({string.Join(", ", perCurrency)}) — skipped: {snapshot.SkippedUnlinkedShops} shops without vendor link, "
                + $"{snapshot.SkippedUnplacedVendors} shops behind event-spawned vendors, "
                + $"{snapshot.SkippedGilSpecialShops} gil-priced SpecialShop entries, "
                + $"{stats.EntriesSkippedUnresolvableItem} entries with unresolvable items, "
                + $"{stats.ShopsFailedSpecialShop}+{stats.ShopsFailedGilShop} shops failed "
                + $"{(stats.FirstFailure == null ? "" : $"(first: {stats.FirstFailure})")} — "
                + $"{snapshot.UnderlistedItemCount} armoire pieces in the excluded shops, "
                + $"{snapshot.LeftOutItemCount} left out (Knightshopper cannot buy them)");
        }
        catch (Exception ex)
        {
            CatalogBuildGate.MarkFailed();
            BuildFailureDetail = $"{ex.GetType().Name}: {ex.Message}";
            Svc.Log.Error(ex, "[ArmoireAutoFill] Knightshopper catalog build failed (not retrying until Refresh)");
        }
    }
}
