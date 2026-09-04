using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using Lumina;

// Proves the PACKAGED zip is self-sufficient: extracts plugins/LazyCrafter/testing/testing.zip to a temp dir,
// loads LazyCrafter.dll from THAT dir in its own AssemblyLoadContext (the way Dalamud loads a plugin - resolving
// only from the plugin folder plus Dalamud's own already-loaded assemblies), and runs LuminaGameData.Load +
// VendorLocator against the real sqpack. Reports drop / desynth counts and the gil-shop index sizes.
//
// Also runs a NEGATIVE CONTROL first: the same zip contents with Sylvan.Data.Csv.dll removed, which must
// reproduce the shipped 0.1.0.0 failure (0 desynth sources) - otherwise the positive result proves nothing.
//
// Usage: LazyCrafter.ZipProbe <zipPath> [sqpack]

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
var zip = args.Length > 0 ? args[0] : Path.Combine(repoRoot, @"plugins\LazyCrafter\testing\testing.zip");
var sqpack = args.Length > 1 ? args[1] : @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack";
var dalamud = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "addon", "Hooks", "dev");

Console.WriteLine($"zip:     {zip}");
Console.WriteLine($"dalamud: {dalamud}");

// The test is only meaningful if Dalamud's own folder does NOT carry the CSV reader - otherwise the plugin
// context could resolve it from there and the zip's contents would not be what is being measured.
var dalamudSylvan = Path.Combine(dalamud, "Sylvan.Data.Csv.dll");
Console.WriteLine($"Hooks\\dev has Sylvan.Data.Csv.dll: {File.Exists(dalamudSylvan)}");
if (File.Exists(dalamudSylvan))
{
    Console.WriteLine("FAIL: Dalamud ships Sylvan.Data.Csv itself; this probe cannot attribute the result to the zip.");
    return 2;
}

var full = Path.Combine(Path.GetTempPath(), "lazycrafter-zipprobe", "full");
var stripped = Path.Combine(Path.GetTempPath(), "lazycrafter-zipprobe", "stripped");
foreach (var d in new[] { full, stripped })
{
    if (Directory.Exists(d)) Directory.Delete(d, true);
    Directory.CreateDirectory(d);
}
ZipFile.ExtractToDirectory(zip, full);
foreach (var f in Directory.GetFiles(full)) File.Copy(f, Path.Combine(stripped, Path.GetFileName(f)));
var strippedSylvan = Path.Combine(stripped, "Sylvan.Data.Csv.dll");
var hadSylvan = File.Exists(strippedSylvan);
if (hadSylvan) File.Delete(strippedSylvan);

Console.WriteLine("zip entries:");
foreach (var f in Directory.GetFiles(full).OrderBy(x => x))
    Console.WriteLine($"  {Path.GetFileName(f),-34} {new FileInfo(f).Length,10:N0}");

// Dalamud's assemblies (Lumina, Dalamud, FFXIVClientStructs...) are already loaded in the game process; here
// the default context stands in for that, so the plugin context shares them exactly as it does in-game.
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var p = Path.Combine(dalamud, name.Name + ".dll");
    return File.Exists(p) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(p) : null;
};

Console.WriteLine($"opening sqpack {sqpack}");
var data = new GameData(sqpack, new LuminaOptions { PanicOnSheetChecksumMismatch = false, LoadMultithreaded = true });

var control = Run(stripped, "CONTROL (Sylvan.Data.Csv.dll deleted - what 0.1.0.0 shipped)");
var real = Run(full, "PACKAGED ZIP AS SHIPPED");

Console.WriteLine();
Console.WriteLine($"control: drops={control.Drops} desynth={control.Desynth} shopItems={control.ShopItems} placedNpcs={control.PlacedNpcs} failures={control.Failures}");
Console.WriteLine($"zip:     drops={real.Drops} desynth={real.Desynth} shopItems={real.ShopItems} placedNpcs={real.PlacedNpcs} failures={real.Failures}");

var ok = true;
if (!hadSylvan) { Console.WriteLine("FAIL: the zip does not contain Sylvan.Data.Csv.dll at all."); ok = false; }
if (control.Desynth != 0 || control.Failures == 0)
    { Console.WriteLine("FAIL: the negative control did not reproduce the shipped failure (expected 0 desynth sources and >0 load failures)."); ok = false; }
if (real.Failures != 0) { Console.WriteLine("FAIL: the packaged zip still reports LuminaSupplemental load failures."); ok = false; }
if (real.Drops < 7000) { Console.WriteLine($"FAIL: drops {real.Drops} < 7000 (offline reference 7,843)."); ok = false; }
if (real.Desynth < 20000) { Console.WriteLine($"FAIL: desynth sources {real.Desynth} < 20000 (offline reference 21,997)."); ok = false; }
if (real.PlacedNpcs <= control.PlacedNpcs) { Console.WriteLine($"FAIL: VendorLocator placed NPCs did not improve ({control.PlacedNpcs} -> {real.PlacedNpcs})."); ok = false; }

Console.WriteLine(ok ? "OK" : "FAILED");
return ok ? 0 : 1;

Result Run(string dir, string label)
{
    Console.WriteLine();
    Console.WriteLine($"---- {label}: {dir}");
    var alc = new PluginContext(dir, dalamud);
    var asm = alc.LoadFromAssemblyPath(Path.Combine(dir, "LazyCrafter.dll"));
    Console.WriteLine($"loaded {asm.GetName().Name} {asm.GetName().Version} from {Path.GetFileName(dir)}");

    var t = asm.GetType("LazyCrafter.Adapters.LuminaGameData") ?? throw new InvalidOperationException("LuminaGameData not found");
    var load = t.GetMethod("Load", BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException("Load not found");
    var ps = load.GetParameters();
    var argv = new object?[ps.Length];
    argv[0] = data;
    argv[1] = (Action<string>)(s => Console.WriteLine($"  [lc] {s}"));
    for (var i = 2; i < ps.Length; i++)
        argv[i] = ps[i].ParameterType == typeof(Action<string>) ? (Action<string>)(s => Console.WriteLine($"  [lc WARN] {s}")) : null;

    var gd = load.Invoke(null, argv)!;
    var drops = (int)t.GetProperty("DropCount")!.GetValue(gd)!;
    var desynth = (int)t.GetProperty("DesynthSourceCount")!.GetValue(gd)!;
    var failProp = t.GetProperty("SupplementalFailures");
    var failures = failProp?.GetValue(gd) is System.Collections.IEnumerable e ? e.Cast<object>().Count() : -1;
    if (failures > 0)
        foreach (var f in ((System.Collections.IEnumerable)failProp!.GetValue(gd)!).Cast<object>()) Console.WriteLine($"  [failure] {f}");

    var vt = asm.GetType("LazyCrafter.Adapters.VendorLocator") ?? throw new InvalidOperationException("VendorLocator not found");
    var ctor = vt.GetConstructors().Single();
    var cps = ctor.GetParameters();
    var cargv = new object?[cps.Length];
    cargv[0] = data;
    for (var i = 1; i < cps.Length; i++)
        cargv[i] = cps[i].ParameterType == typeof(Action<string>) ? (Action<string>)(s => Console.WriteLine($"  [vl] {s}")) : null;
    var vl = ctor.Invoke(cargv);
    var shopItems = (int)vt.GetProperty("ShopItemCount")!.GetValue(vl)!;
    var placed = (int)vt.GetProperty("PlacedNpcCount")!.GetValue(vl)!;

    return new Result(drops, desynth, shopItems, placed, failures < 0 ? 0 : failures);
}

record struct Result(int Drops, int Desynth, int ShopItems, int PlacedNpcs, int Failures);

/// <summary>Mirrors Dalamud's per-plugin load context: share what the host already loaded, otherwise resolve
/// from the plugin's own folder (the extracted zip), then Dalamud's dev folder.</summary>
sealed class PluginContext : AssemblyLoadContext
{
    private readonly string _dir;
    private readonly string _dalamud;

    public PluginContext(string dir, string dalamud) : base($"zipprobe:{Path.GetFileName(dir)}", true)
    {
        _dir = dir;
        _dalamud = dalamud;
    }

    protected override Assembly? Load(AssemblyName name)
    {
        var shared = Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
        if (shared is not null) return shared;
        foreach (var root in new[] { _dir, _dalamud })
        {
            var p = Path.Combine(root, name.Name + ".dll");
            if (File.Exists(p)) return LoadFromAssemblyPath(p);
        }
        return null;
    }
}
