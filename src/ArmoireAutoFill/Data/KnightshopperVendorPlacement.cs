using System.Diagnostics;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;

namespace ArmoireAutoFill.Data;

// Which NPCs does the game actually place in the world? Knightshopper's native vendor
// discovery treats an ENpcBase as a vendor only when an ENPC instance object names it in a
// territory's LGB layers (see KnightshopperCatalogCore for the evidence). This is that scan,
// kept free of Dalamud/ECommons so the in-game builder (Svc.Data.GetFile) and the offline
// tool (tools/KnightshopperGroundTruth/PlacedNpcs, which prints the real cost) share one
// implementation.
public static class KnightshopperVendorPlacement
{
    // The layer files that carry ENPC instances. Every vendor in Knightshopper's catalog is
    // placed in one of these two (ground-truth run: 540 vendors, all found in them).
    private static readonly string[] LayerFiles = ["planevent", "planner"];

    public sealed record Result(HashSet<uint> PlacedNpcs, int Territories, int FilesRead, int FilesFailed, double ElapsedMs);

    // bgPaths: TerritoryType.Bg values, e.g. "ffxiv/sea_s1/twn/s1t1/level/s1t1".
    public static Result Scan(IEnumerable<string> bgPaths, Func<string, LgbFile?> loadLgb)
    {
        var clock = Stopwatch.StartNew();
        var placed = new HashSet<uint>();
        var territories = 0;
        var read = 0;
        var failed = 0;
        var seenDirs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bg in bgPaths)
        {
            var i = bg.IndexOf("/level/", StringComparison.Ordinal);
            if (i < 0)
                continue;
            var dir = "bg/" + bg[..i] + "/level/";
            if (!seenDirs.Add(dir))
                continue; // several territories share one level folder
            territories++;
            foreach (var name in LayerFiles)
            {
                LgbFile? file;
                try
                {
                    file = loadLgb(dir + name + ".lgb");
                }
                catch (Exception)
                {
                    failed++;
                    continue;
                }
                if (file == null)
                    continue; // this territory has no such layer file
                read++;
                foreach (var layer in file.Layers)
                    foreach (var instance in layer.InstanceObjects)
                        if (instance.AssetType == LayerEntryType.EventNPC
                            && instance.Object is LayerCommon.ENPCInstanceObject npc)
                            placed.Add(npc.ParentData.ParentData.BaseId);
            }
        }
        clock.Stop();
        return new Result(placed, territories, read, failed, clock.Elapsed.TotalMilliseconds);
    }
}
