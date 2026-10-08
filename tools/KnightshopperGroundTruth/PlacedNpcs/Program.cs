using ArmoireAutoFill.Data;
using System.IO.Compression;
using System.Text.Json;
using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Excel.Sheets;

// usage: PlacedNpcs <sqpack dir> <out .json.gz>
// Writes {"territories": n, "files": n, "seconds": s, "placedNpcs": [sorted ENpcBase ids]}.
// This is the same scan ArmoireAutoFill runs in-game (KnightshopperVendorPlacement), so the
// number it prints is the real cost of that scan on this install.
var gd = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Language.English });
var bgPaths = gd.GetExcelSheet<TerritoryType>()!.Select(t => t.Bg.ExtractText()).ToList();
var result = KnightshopperVendorPlacement.Scan(bgPaths, path => gd.GetFile<LgbFile>(path));
Console.WriteLine($"territories={result.Territories} lgbFiles={result.FilesRead} failed={result.FilesFailed} "
                  + $"placedNpcs={result.PlacedNpcs.Count} seconds={result.ElapsedMs / 1000:F1}");
using var fs = File.Create(args[1]);
using var gz = new GZipStream(fs, CompressionLevel.Optimal);
JsonSerializer.Serialize(gz, new Dictionary<string, object>
{
    ["territories"] = result.Territories,
    ["files"] = result.FilesRead,
    ["placedNpcs"] = result.PlacedNpcs.OrderBy(x => x).ToArray(),
});
