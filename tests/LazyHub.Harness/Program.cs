using System.Text.Json;
using LazyHub.Core;

// LazyHub.Harness: the catalog of lalalazy plugins and the status logic, with no Dalamud.
// Exit code 0 only when every case passes.

int pass = 0, fail = 0;

void Check(string name, bool ok, string detail = "")
{
    if (ok) { pass++; Console.WriteLine($"PASS  {name}"); }
    else { fail++; Console.WriteLine($"FAIL  {name}{(detail.Length > 0 ? "  -> " + detail : "")}"); }
}

string repoRoot = args.Length > 0 ? args[0] : FindRepoRoot();

string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "pluginmaster.json"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("pluginmaster.json not found above " + AppContext.BaseDirectory);
}

var names = Catalog.Plugins.Select(p => p.InternalName).ToList();

// ---- catalog shape ----------------------------------------------------------------------------
Check("catalog has exactly 15 plugins", names.Count == 15, $"count={names.Count}");
Check("catalog InternalNames are unique", names.Distinct(StringComparer.Ordinal).Count() == names.Count);
Check("catalog does not list the hub itself", !names.Contains("LazyHub"));
Check("catalog does not list the third-party test entry", !names.Contains("ARControlPRTest2"));
Check("catalog order is stable: Gluttony first", names.Count > 0 && names[0] == "GluttonyCombo", names.FirstOrDefault() ?? "<empty>");
Check("every entry has a display name and an icon file name",
    Catalog.Plugins.All(p => p.DisplayName.Length > 0 && p.IconFile.EndsWith(".png", StringComparison.Ordinal)));
Check("Find returns the entry by exact InternalName", Catalog.Find("LazyFoodBuff")?.DisplayName == "LazyFoodBuff");
Check("Find is exact (no case folding) and returns null for unknown", Catalog.Find("lazyfoodbuff") == null && Catalog.Find("Nope") == null);

// ---- drift guard: the catalog equals the repo's real pluginmaster.json -------------------------
{
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "pluginmaster.json")));
    var manifestNames = doc.RootElement.EnumerateArray()
        .Select(e => e.GetProperty("InternalName").GetString()!)
        .Where(n => n != "LazyHub" && n != "ARControlPRTest2")
        .ToHashSet(StringComparer.Ordinal);
    var missing = manifestNames.Except(names).OrderBy(n => n).ToList();
    var extra = names.Except(manifestNames).OrderBy(n => n).ToList();
    Check("catalog equals pluginmaster.json (minus the hub and the third-party test entry)",
        missing.Count == 0 && extra.Count == 0,
        $"missing from catalog: [{string.Join(",", missing)}] extra in catalog: [{string.Join(",", extra)}]");
}

// ---- icons: every catalog icon exists as a 64x64 PNG -------------------------------------------
{
    var resources = Path.Combine(repoRoot, "src", "LazyHub", "LazyHub", "Resources");
    var problems = new List<string>();
    foreach (var p in Catalog.Plugins.Select(p => p.IconFile).Append(Catalog.HubIconFile))
    {
        var path = Path.Combine(resources, p);
        if (!File.Exists(path)) { problems.Add($"{p}: missing"); continue; }
        var b = File.ReadAllBytes(path);
        // PNG: 8-byte signature, then IHDR with width/height as big-endian uint32 at byte 16 and 20.
        bool sig = b.Length > 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
        if (!sig) { problems.Add($"{p}: not a PNG"); continue; }
        int w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
        int h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
        if (w != 64 || h != 64) problems.Add($"{p}: {w}x{h}");
    }
    Check("every catalog icon (and the hub icon) is a 64x64 PNG in Resources", problems.Count == 0, string.Join("; ", problems));
}

// ---- native image nodes must scale to their slot -------------------------------------------------
// A native image node draws its texture at the texture's own size unless FitTexture is set, which
// makes it stretch to the node's Size. Without it the 64px icons ignored their 44px slot, covered the
// first letters of the name and status text and touched the next row (seen in game on 0.1.0.0).
// Native nodes cannot run without the game, so this guards the source: every `new ImGuiImageNode`
// initializer in the plugin must set FitTexture = true.
{
    var pluginDir = Path.Combine(repoRoot, "src", "LazyHub", "LazyHub");
    var offenders = new List<string>();
    int found = 0;
    foreach (var file in Directory.EnumerateFiles(pluginDir, "*.cs", SearchOption.AllDirectories))
    {
        if (file.Contains(Path.DirectorySeparatorChar + "Core" + Path.DirectorySeparatorChar)) continue;
        var text = File.ReadAllText(file);
        int from = 0;
        while (true)
        {
            int at = text.IndexOf("new ImGuiImageNode", from, StringComparison.Ordinal);
            if (at < 0) break;
            found++;
            int open = text.IndexOf('{', at);
            int close = open < 0 ? -1 : text.IndexOf("};", open, StringComparison.Ordinal);
            var block = open >= 0 && close > open ? text[open..close] : "";
            if (!block.Contains("FitTexture = true", StringComparison.Ordinal))
                offenders.Add($"{Path.GetFileName(file)}@{text[..at].Count(c => c == '\n') + 1}");
            from = at + 1;
        }
    }
    Check("every ImGuiImageNode in the plugin sets FitTexture = true (and there is at least one)",
        found > 0 && offenders.Count == 0, $"found={found} without FitTexture: [{string.Join(",", offenders)}]");
}

// ---- status classification ---------------------------------------------------------------------
Check("not installed -> NotInstalled", PluginStatus.Classify(installed: false, loaded: false, testing: false) == PluginState.NotInstalled);
Check("not installed wins even if flags are set", PluginStatus.Classify(installed: false, loaded: true, testing: true) == PluginState.NotInstalled);
Check("installed, not loaded -> InstalledNotLoaded", PluginStatus.Classify(true, false, false) == PluginState.InstalledNotLoaded);
Check("installed testing build that is not loaded -> InstalledNotLoaded", PluginStatus.Classify(true, false, true) == PluginState.InstalledNotLoaded);
Check("loaded -> Loaded", PluginStatus.Classify(true, true, false) == PluginState.Loaded);
Check("loaded testing build -> LoadedTesting", PluginStatus.Classify(true, true, true) == PluginState.LoadedTesting);

// ---- labels and the server-bar summary ----------------------------------------------------------
Check("labels are fixed text, one per state",
    PluginStatus.Label(PluginState.NotInstalled) == "Not installed" &&
    PluginStatus.Label(PluginState.InstalledNotLoaded) == "Not loaded" &&
    PluginStatus.Label(PluginState.Loaded) == "Loaded" &&
    PluginStatus.Label(PluginState.LoadedTesting) == "Loaded (testing)");
Check("only loaded states can be opened",
    PluginStatus.CanOpen(PluginState.Loaded) && PluginStatus.CanOpen(PluginState.LoadedTesting) &&
    !PluginStatus.CanOpen(PluginState.InstalledNotLoaded) && !PluginStatus.CanOpen(PluginState.NotInstalled));
Check("summary counts both loaded states over the total",
    PluginStatus.Summary([PluginState.Loaded, PluginState.LoadedTesting, PluginState.InstalledNotLoaded, PluginState.NotInstalled]) == "2/4");
Check("summary of nothing is 0/0", PluginStatus.Summary([]) == "0/0");

Console.WriteLine($"LazyHub.Harness: {pass} pass, {fail} fail");
return fail == 0 ? 0 : 1;
