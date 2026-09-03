using System.Numerics;
using Lumina;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;

// P6 spike helper (offline, no client). Usage: VendorProbe [sqpack] [territoryId ...]
// Per territory: every ENpc with a GilShop handler, its world position, and the distance to the nearest
// teleportable aetheryte. NPC positions come from the territory's planevent.lgb (what ItemVendorLocation
// does - the Level sheet only places quest/event NPCs) with the Level sheet as fallback; aetheryte positions
// from Aetheryte.Level[] (falls back to Level rows of Type 12).
var sqpack = args.Length > 0 ? args[0] : @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack";
var wanted = args.Skip(1).Select(uint.Parse).ToHashSet();
var data = new GameData(sqpack, new LuminaOptions { PanicOnSheetChecksumMismatch = false, LoadMultithreaded = true });

var levels = data.GetExcelSheet<Level>()!;
var npcBase = data.GetExcelSheet<ENpcBase>()!;
var npcRes = data.GetExcelSheet<ENpcResident>()!;
var gilShops = data.GetExcelSheet<GilShop>()!;
var specialShops = data.GetExcelSheet<SpecialShop>()!;
var aetherytes = data.GetExcelSheet<Aetheryte>()!;
var territories = data.GetExcelSheet<TerritoryType>()!;

var gilShopIds = gilShops.Select(r => r.RowId).ToHashSet();
var specialShopIds = specialShops.Select(r => r.RowId).ToHashSet();
var shopNpcs = new Dictionary<uint, (int gil, int special, int handlers)>();
foreach (var b in npcBase)
{
    int gil = 0, special = 0, handlers = 0;
    foreach (var h in b.ENpcData)
    {
        if (h.RowId == 0) continue;
        handlers++;
        if (gilShopIds.Contains(h.RowId)) gil++;
        else if (specialShopIds.Contains(h.RowId)) special++;
    }
    if (gil > 0) shopNpcs[b.RowId] = (gil, special, handlers);
}
Console.WriteLine($"ENpcBase rows with a GilShop handler: {shopNpcs.Count}");

// Aetherytes per territory (teleportable ones only).
var aethByTerr = new Dictionary<uint, List<(Aetheryte a, Vector3 pos)>>();
foreach (var a in aetherytes)
{
    if (!a.IsAetheryte || a.Territory.RowId == 0) continue;
    Vector3? pos = null;
    foreach (var lr in a.Level)
        if (lr.RowId != 0 && lr.ValueNullable is { } lv) { pos = new Vector3(lv.X, lv.Y, lv.Z); break; }
    if (pos is null)
    {
        var lv = levels.FirstOrDefault(l => l.Type == 12 && l.Object.RowId == a.RowId);
        if (lv.RowId != 0) pos = new Vector3(lv.X, lv.Y, lv.Z);
    }
    if (pos is null) continue;
    if (!aethByTerr.TryGetValue(a.Territory.RowId, out var list)) aethByTerr[a.Territory.RowId] = list = [];
    list.Add((a, pos.Value));
}

string TerrName(uint id) => territories.GetRowOrDefault(id)?.PlaceName.Value.Name.ToString() ?? "?";
string NpcName(uint id) => npcRes.GetRowOrDefault(id)?.Singular.ToString() ?? "?";

// NPC placements per territory: LGB first, Level sheet fallback.
Dictionary<uint, Vector3> NpcPlacements(TerritoryType terr)
{
    var result = new Dictionary<uint, Vector3>();
    var bg = terr.Bg.ToString();
    var idx = bg.IndexOf("/level/", StringComparison.Ordinal);
    if (idx >= 0)
    {
        var lgb = data.GetFile<LgbFile>("bg/" + bg[..(idx + 1)] + "level/planevent.lgb");
        if (lgb is not null)
            foreach (var layer in lgb.Layers)
                foreach (var obj in layer.InstanceObjects)
                {
                    if (obj.AssetType != LayerEntryType.EventNPC) continue;
                    var npc = (LayerCommon.ENPCInstanceObject)obj.Object;
                    var id = npc.ParentData.ParentData.BaseId;
                    if (id == 0 || !shopNpcs.ContainsKey(id) || result.ContainsKey(id)) continue;
                    result[id] = new Vector3(obj.Transform.Translation.X, obj.Transform.Translation.Y, obj.Transform.Translation.Z);
                }
    }
    foreach (var l in levels)
        if (l.Type == 8 && l.Territory.RowId == terr.RowId && shopNpcs.ContainsKey(l.Object.RowId) && !result.ContainsKey(l.Object.RowId))
            result[l.Object.RowId] = new Vector3(l.X, l.Y, l.Z);
    return result;
}

var terrs = wanted.Count > 0 ? wanted.ToList() : aethByTerr.Keys.OrderBy(t => t).ToList();
foreach (var terrId in terrs)
{
    var terr = territories.GetRowOrDefault(terrId);
    if (terr is null) { Console.WriteLine($"territory {terrId}: no such row"); continue; }
    var npcs = NpcPlacements(terr.Value);
    aethByTerr.TryGetValue(terrId, out var aeths);
    Console.WriteLine($"== territory {terrId} {TerrName(terrId)} bg={terr.Value.Bg}: {npcs.Count} gil-shop NPC placements, {aeths?.Count ?? 0} teleportable aetherytes");
    foreach (var (a, pos) in aeths ?? [])
        Console.WriteLine($"   aetheryte {a.RowId} '{a.PlaceName.Value.Name}' at ({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})");
    var rows = npcs.Select(kv =>
    {
        var near = (aeths ?? []).Select(x => (x.a, d: Vector3.Distance(kv.Value, x.pos))).OrderBy(x => x.d).FirstOrDefault();
        return (id: kv.Key, pos: kv.Value, near, info: shopNpcs[kv.Key]);
    }).OrderBy(r => r.near.d).ToList();
    foreach (var r in rows.Take(wanted.Count > 0 ? 30 : 8))
        Console.WriteLine($"   npc {r.id} '{NpcName(r.id)}' at ({r.pos.X:F1}, {r.pos.Y:F1}, {r.pos.Z:F1}) gil={r.info.gil} special={r.info.special} handlers={r.info.handlers} nearestAetheryte={r.near.a.RowId} dist={r.near.d:F0}y");
}
