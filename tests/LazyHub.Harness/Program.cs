using System.Text.Json;
using LazyHub.Core;
using Lalalazy.Hub;

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


// ===== hub UI logic (pure): formatting, stepping, confirmation, detail rows ================================
{
    var toggle = new ParsedControl { Id = "t", Label = "T", Kind = ControlKind.Toggle };
    var step = new ParsedControl { Id = "s", Label = "S", Kind = ControlKind.Stepper, Min = 1, Max = 99, Step = 5, Unit = "%" };
    var dec = new ParsedControl { Id = "d", Label = "D", Kind = ControlKind.Stepper, Min = 0, Max = 1, Step = 0.05, Decimals = 2 };
    var choice = new ParsedControl { Id = "c", Label = "C", Kind = ControlKind.Choice };
    choice.Choices.AddRange(new[] { "Auto", "Manual", "Off" });
    var button = new ParsedControl { Id = "b", Label = "B", Kind = ControlKind.Button };

    Check("toggle text is ON or OFF", ControlFormat.ValueText(toggle, new ParsedState { Value = true }) == "ON" && ControlFormat.ValueText(toggle, new ParsedState { Value = false }) == "OFF");
    Check("stepper text carries its unit", ControlFormat.ValueText(step, new ParsedState { Value = 60.0 }) == "60%", ControlFormat.ValueText(step, new ParsedState { Value = 60.0 }));
    Check("stepper text honours decimals", ControlFormat.ValueText(dec, new ParsedState { Value = 0.15 }) == "0.15");
    Check("choice text is the option label", ControlFormat.ValueText(choice, new ParsedState { Value = 1.0 }) == "Manual");
    Check("choice text for a bad index is a dash, not a crash", ControlFormat.ValueText(choice, new ParsedState { Value = 9.0 }) == "-");
    Check("a control with no value shows a dash", ControlFormat.ValueText(toggle, null) == "-" && ControlFormat.ValueText(step, new ParsedState()) == "-");
    Check("a button has no value text", ControlFormat.ValueText(button, new ParsedState()) == "");

    Check("stepper steps up by its step", ControlFormat.StepperNext(step, 60, +1) == 65);
    Check("stepper steps down by its step", ControlFormat.StepperNext(step, 60, -1) == 55);
    Check("stepper stops at max", ControlFormat.StepperNext(step, 97, +1) == 99);
    Check("stepper stops at min", ControlFormat.StepperNext(step, 3, -1) == 1);
    Check("stepper keeps decimals clean (0.15 + 0.05 = 0.2)", ControlFormat.StepperNext(dec, 0.15, +1) == 0.2);
    Check("choice steps forward and wraps", ControlFormat.ChoiceNext(choice, 2, +1) == 0 && ControlFormat.ChoiceNext(choice, 0, +1) == 1);
    Check("choice steps back and wraps", ControlFormat.ChoiceNext(choice, 0, -1) == 2);

    var gate = new ConfirmGate(5000);
    Check("no confirmation needed: proceeds at once", gate.ShouldProceed("a", false, 0));
    Check("confirmation: the first click does not proceed", !gate.ShouldProceed("a", true, 1000) && gate.IsPending("a", 1500));
    Check("confirmation: the second click within the window proceeds", gate.ShouldProceed("a", true, 3000) && !gate.IsPending("a", 3001));
    Check("confirmation: it must be asked for again afterwards", !gate.ShouldProceed("a", true, 3500));
    Check("confirmation expires", !gate.IsPending("a", 3500 + 5001));
    Check("confirmation: a late second click is treated as a first click", !gate.ShouldProceed("a", true, 20000) && gate.IsPending("a", 20001));
    Check("confirmation: another control resets the pending one", !gate.ShouldProceed("b", true, 20100) && !gate.IsPending("a", 20101) && gate.IsPending("b", 20101));
    gate.Reset();
    Check("reset clears a pending confirmation", !gate.IsPending("b", 20102));

    var d = new ParsedDescriptor { Plugin = "X", Version = "1" };
    ParsedControl C(string id, string group, ControlKind k = ControlKind.Toggle) => new ParsedControl { Id = id, Label = id, Group = group, Kind = k };
    d.Controls.Add(C("m", "General")); d.Controls.Add(C("a", "General")); d.Controls.Add(C("b", "Tuning"));
    d.Controls.Add(C("c", "General")); d.Controls.Add(C("pick1", "Hotbar buttons", ControlKind.Button)); d.Controls.Add(C("e", ""));
    var rows = DetailRows.Build(d, 18, "Hotbar buttons", out var hidden);
    Check("detail rows put a header before each group", rows.Count(r => r.IsHeader) == 3, string.Join(",", rows.Select(r => r.IsHeader ? "[" + r.Header + "]" : r.Control!.Id)));
    Check("detail rows keep declaration order within a group and group controls together",
        string.Join(",", rows.Select(r => r.IsHeader ? "[" + r.Header + "]" : r.Control!.Id)) == "[General],m,a,c,[Tuning],b,[Other],e");
    Check("detail rows leave out the excluded group", rows.All(r => r.IsHeader || r.Control!.Id != "pick1"));
    Check("nothing hidden when everything fits", hidden == 0);
    var small = DetailRows.Build(d, 4, "Hotbar buttons", out var hidden2);
    Check("detail rows are capped and the overflow is counted", small.Count == 4 && hidden2 > 0, "count=" + small.Count + " hidden=" + hidden2);
    Check("an empty descriptor gives no rows", DetailRows.Build(new ParsedDescriptor(), 18, "", out var h3).Count == 0 && h3 == 0);
}


// ===== end to end: a real adapter endpoint -> the hub's real parser, formatter and row builder -> a write back =====
{
    // An adapter shaped like Gluttony Combo's: a master, a lock, a choice, a stepper, and hotbar buttons.
    bool auto = false, fate = false; int target = 0; double aoe = 3; int picked = 0; bool lockedByOther = false;
    var ep = new HubEndpoint("GluttonyCombo", "1.0.4.259");
    ep.Toggle("auto_rotation", "Auto-Rotation", () => auto, v => { auto = v; }, group: "Auto-Rotation", master: true,
        state: () => lockedByOther ? new ControlState(Locked: true, Why: "Controlled by Another Plugin.") : new ControlState());
    ep.Toggle("fate_priority", "FATE priority", () => fate, v => { fate = v; }, group: "Auto-Rotation");
    ep.Choice("dps_target", "DPS targeting mode", new[] { "Manual", "Highest max HP", "Nearest" }, () => target, i => { target = i; }, group: "Targeting");
    ep.Stepper("aoe_targets", "AoE needs at least", 0, 8, 1, () => aoe, v => { aoe = v; }, unit: " targets", group: "Targeting");
    ep.Button("pick_auto_on", "Auto-Rotation On", () => { picked++; }, group: "Hotbar buttons");
    ep.Button("pick_aoe", "AoE DPS", () => { picked++; }, group: "Hotbar buttons");

    var desc = DescriptorParser.ParseDescriptor(ep.Describe());
    var values = DescriptorParser.ParseState(ep.GetAll());
    Check("e2e: the hub reads the adapter's descriptor and state", desc != null && values != null && desc.Controls.Count == 6 && values.Count == 6);

    if (desc != null && values != null)
    {
        var master = desc.Controls.FirstOrDefault(c => c.Master);
        Check("e2e: the master is the Auto-Rotation toggle", master != null && master.Id == "auto_rotation");

        var rows = DetailRows.Build(desc, 18, "Hotbar buttons", out var hid);
        Check("e2e: the settings page shows the groups without the hotbar buttons",
            string.Join(",", rows.Select(r => r.IsHeader ? "[" + r.Header + "]" : r.Control!.Id)) == "[Auto-Rotation],auto_rotation,fate_priority,[Targeting],dps_target,aoe_targets" && hid == 0);
        Check("e2e: the tray finds both hotbar buttons by their group",
            desc.Controls.Count(c => c.Kind == ControlKind.Button && c.Group == "Hotbar buttons") == 2);

        Check("e2e: values read as the player sees them",
            ControlFormat.ValueText(desc.Controls.First(c => c.Id == "dps_target"), values["dps_target"]) == "Manual" &&
            ControlFormat.ValueText(desc.Controls.First(c => c.Id == "aoe_targets"), values["aoe_targets"]) == "3 targets" &&
            ControlFormat.ValueText(master!, values["auto_rotation"]) == "OFF");

        // A click on the master: the hub inverts the value and sends it back.
        var res = DescriptorParser.ParseResult(ep.Set("auto_rotation", DescriptorParser.ValueToJson(!values["auto_rotation"].Bool)));
        Check("e2e: toggling the master through the wire changes the plugin's own value", res.Ok && auto);

        // A click on + for the stepper, then on the choice's next button.
        var aoeC = desc.Controls.First(c => c.Id == "aoe_targets");
        Check("e2e: + on the stepper moves it one step", DescriptorParser.ParseResult(ep.Set("aoe_targets", DescriptorParser.ValueToJson(ControlFormat.StepperNext(aoeC, 3, +1)))).Ok && aoe == 4);
        var tC = desc.Controls.First(c => c.Id == "dps_target");
        Check("e2e: the choice's next button cycles the mode",
            DescriptorParser.ParseResult(ep.Set("dps_target", DescriptorParser.ValueToJson(ControlFormat.ChoiceNext(tC, 2, +1)))).Ok && target == 0);

        // A hotbar button.
        Check("e2e: a hotbar button runs through Invoke", DescriptorParser.ParseResult(ep.Invoke("pick_auto_on")).Ok && picked == 1);

        // Another plugin takes the lease: the control reads locked, the write is refused, the plugin value is untouched.
        lockedByOther = true;
        var after = DescriptorParser.ParseState(ep.GetAll())!;
        var refused = DescriptorParser.ParseResult(ep.Set("auto_rotation", "false"));
        Check("e2e: a lease held elsewhere shows as locked and refuses the hub's write",
            after["auto_rotation"].Locked && after["auto_rotation"].Why.Contains("Another Plugin") && !refused.Ok && refused.Locked && auto == true);
        lockedByOther = false;

        // The window's two-click confirmation on a master that declares a confirm text.
        var gate = new ConfirmGate(5000);
        Check("e2e: a confirm-protected master needs two clicks", !gate.ShouldProceed("GluttonyCombo/auto_rotation", true, 100) && gate.ShouldProceed("GluttonyCombo/auto_rotation", true, 900));
    }
}
// A description that grew past the limits is clipped by the hub, not trusted.
Check("e2e: the hub clips over-long text from an adapter instead of trusting it",
    DescriptorParser.ParseDescriptor("{\"p\":1,\"plugin\":\"X\",\"version\":\"1\",\"controls\":[{\"id\":\"a\",\"label\":\"" + new string('x', 500) + "\",\"kind\":\"toggle\"}]}") is var clip
    && clip != null && clip.Controls.Count == 1 && clip.Controls[0].Label.Length <= 60);

// ---- review round 1 (2026-10-03): written failing first ---------------------------------------
{
    // A pending "Confirm?" label stayed on screen until the next 1 s poll after the 5 s window had passed, and a click in that gap re-armed instead of confirming.
    var gate = new ConfirmGate(5000);
    gate.ShouldProceed("k", true, 1000);
    Check("ClearIfExpired does nothing while the confirmation is still open", !gate.ClearIfExpired(5999) && gate.IsPending("k", 5999));
    Check("ClearIfExpired reports the expiry once and clears the pending state", gate.ClearIfExpired(6001) && !gate.IsPending("k", 6001) && !gate.ClearIfExpired(6002));
    Check("after an expiry the next click arms again instead of confirming", !gate.ShouldProceed("k", true, 6100) && gate.IsPending("k", 6100));
    var idle = new ConfirmGate(5000);
    Check("ClearIfExpired is false when nothing was ever pending", !idle.ClearIfExpired(1_000_000));
}

// ---- responsive native shell layout -------------------------------------------------------------
Check("layout uses one column below the responsive threshold", HubLayout.Columns(HubLayout.TwoColumnMinWidth - 1) == 1);
Check("layout uses two columns at the responsive threshold", HubLayout.Columns(HubLayout.TwoColumnMinWidth) == 2);
Check("detail pages can force a single column", HubLayout.Columns(1200, forceSingle: true) == 1);
Check("two-column cells alternate without overlap",
    HubLayout.Cell(0, 1000, 2, 60) is { X: 0, Y: 0 } first &&
    HubLayout.Cell(1, 1000, 2, 60) is { X: > 0, Y: 0 } second &&
    first.Width == second.Width && second.X >= first.X + first.Width);
Check("two-column cells wrap to the next row", HubLayout.Cell(2, 1000, 2, 60).Y == 72);
Check("row height counts every real item", HubLayout.RowsHeight(15, 2, 60) == 564);
Check("row height preserves leading content", HubLayout.RowsHeight(1, 2, 60, leading: 200) == 260);
Check("empty content keeps only its leading height", HubLayout.RowsHeight(0, 2, 60, leading: 200) == 200);
Check("column widths never become negative", HubLayout.ColumnWidth(0, 2) == 40);

// ---- native hub geometry: alignment, fit, grid, panels ------------------------------------------------
{
    const float W = 1068f;
    var g = HubLayout.Gutter(W);
    var inner = HubLayout.InnerWidth(W);
    var cols = HubLayout.Columns(inner);
    bool OnGrid(float v) => v % 1f == 0f;
    bool OnGrid4(float v) => v % 4f == 0f;

    // two columns align at both edges, rows are equal height, nothing overlaps
    var cells = Enumerable.Range(0, 8).Select(i => HubLayout.Cell(i, inner, cols, HubLayout.RowHeight, HubLayout.ColumnGap, HubLayout.TrayLeading, g, HubLayout.RowGap)).ToList();
    Check("columns start at the left gutter and end at the right gutter",
        cells.Where((_, i) => i % 2 == 0).All(c => c.X == g) && cells.Where((_, i) => i % 2 == 1).All(c => c.X + c.Width == W - g));
    Check("every row cell has the same size", cells.All(c => c.Width == cells[0].Width && c.Height == HubLayout.RowHeight));
    Check("row cells never overlap",
        cells.Select((c, i) => (c, i)).All(a => cells.Select((d, j) => (d, j)).Where(b => b.j > a.i).All(b =>
            !new HubRect(a.c.X, a.c.Y, a.c.Width, a.c.Height).Overlaps(new HubRect(b.d.X, b.d.Y, b.d.Width, b.d.Height)))));
    Check("row cells sit on whole units and the 4 grid vertically",
        cells.All(c => OnGrid(c.X) && OnGrid(c.Width) && OnGrid4(c.Y) && OnGrid4(c.Height)));
    // odd widths still end flush
    var odd = Enumerable.Range(0, 2).Select(i => HubLayout.Cell(i, 1001f, 2, 60)).ToList();
    Check("columns end flush even for an odd width", odd[1].X + odd[1].Width == 1001f && odd[0].Width == odd[1].Width);

    // the Quick page fits the window it asks for, with no leftover blank body
    var windowH = HubLayout.WindowHeightForQuick(8);
    var bodyH = HubLayout.BodyHeight(windowH - HubLayout.FrameOverheadY);
    var contentH = HubLayout.RowsHeight(8, cols, HubLayout.RowHeight, HubLayout.ColumnGap, HubLayout.TrayLeading, HubLayout.RowGap) + HubLayout.ScrollPadBottom;
    Check("content height equals the body height for the Quick page", contentH == bodyH, $"content={contentH} body={bodyH}");
    Check("the window height is on the 4 grid", OnGrid4(windowH), windowH.ToString());

    // shell bands stack on the 4 grid
    Check("shell bands and page header sit on the 4 grid",
        new[] { HubLayout.HeaderHeight, HubLayout.NavHeight, HubLayout.PageTop, HubLayout.BodyTop, HubLayout.FooterHeight, HubLayout.TrayHeight, HubLayout.RowHeight, HubLayout.ButtonHeight, HubLayout.TileSize, HubLayout.ToggleWidth, HubLayout.ToggleHeight }.All(OnGrid4));
    Check("the nav pills sit inside the nav band", HubLayout.NavPill(0, W).Y >= HubLayout.HeaderHeight && HubLayout.NavPill(0, W).Bottom <= HubLayout.HeaderHeight + HubLayout.NavHeight);
    Check("the nav pills do not overlap each other", !HubLayout.NavPill(0, W).Overlaps(HubLayout.NavPill(1, W)));
    Check("the status pill sits inside the header band on the right gutter",
        HubLayout.StatusPill(W).Right == W - g && HubLayout.StatusPill(W).Y >= 0 && HubLayout.StatusPill(W).Bottom <= HubLayout.HeaderHeight && !HubLayout.StatusPill(W).Overlaps(HubLayout.Brand(W)));
    Check("the page header starts below the nav band", HubLayout.PageTile(W).Y > HubLayout.HeaderHeight + HubLayout.NavHeight);
    Check("the page title and the meta do not overlap", !HubLayout.PageTitle(W).Overlaps(HubLayout.PageMeta(W)));
    Check("the page header ends before the scrolling body begins", HubLayout.PageTile(W).Bottom <= HubLayout.BodyTop && HubLayout.PageDescription(W).Bottom <= HubLayout.BodyTop);
    Check("the footer sits at the bottom, below the body", HubLayout.Footer(W, 748f).Bottom == 748f && HubLayout.Footer(W, 748f).Y == HubLayout.BodyTop + HubLayout.BodyHeight(748f));

    // labels fit their buttons
    foreach (var label in new[] { "Settings", "Open", "Confirm?", "Run", "-", "+", "<", ">" })
    {
        var width = label.Length <= 1 ? HubLayout.StepButtonWidth : HubLayout.ButtonWidth(label, label == "Open" ? HubLayout.SmallButtonWidth : HubLayout.ButtonMinWidth);
        Check($"the '{label}' label fits its button", HubLayout.LabelFits(label, width), $"width={width}");
    }
    Check("Settings is not truncated in the Quick row or the library row",
        HubLayout.LabelFits("Settings", HubLayout.MasterRow(cells[0]).ButtonA.Width) && HubLayout.LabelFits("Settings", HubLayout.LibraryRow(cells[0]).ButtonA.Width));
    Check("a long label widens its button on the 4 grid", HubLayout.ButtonWidth("Quick controls extra") % 4f == 0f && HubLayout.LabelFits("Quick controls extra", HubLayout.ButtonWidth("Quick controls extra")));

    // row interiors: inside the card, no overlaps, vertically centred
    var cell = cells[0];
    var card = new HubRect(cell.X, cell.Y, cell.Width, cell.Height);
    foreach (var (name, parts) in new[] { ("master", HubLayout.MasterRow(cell)), ("library", HubLayout.LibraryRow(cell)) })
    {
        var rects = new[] { parts.Icon, parts.Text, parts.ButtonA, parts.ButtonB };
        Check($"{name} row parts sit inside the card", rects.All(r => card.Contains(r)));
        Check($"{name} row parts do not overlap", rects.Select((a, i) => (a, i)).All(a => rects.Select((b, j) => (b, j)).Where(b => b.j > a.i).All(b => !a.a.Overlaps(b.b))));
        Check($"{name} row controls share the row's middle line",
            Math.Abs((parts.ButtonA.Y + (parts.ButtonA.Height / 2f)) - (card.Y + (card.Height / 2f))) <= 1f &&
            Math.Abs((parts.ButtonB.Y + (parts.ButtonB.Height / 2f)) - (card.Y + (card.Height / 2f))) <= 1f &&
            Math.Abs((parts.Icon.Y + (parts.Icon.Height / 2f)) - (card.Y + (card.Height / 2f))) <= 1f);
    }
    foreach (var (name, parts) in new[] { ("toggle", HubLayout.ToggleRow(cell)), ("step", HubLayout.StepRow(cell)), ("action", HubLayout.ActionRow(cell)) })
    {
        var rects = new[] { parts.Text, parts.ButtonA, parts.Value, parts.ButtonB }.Where(r => r.Width > 0).ToArray();
        Check($"{name} row parts sit inside the card and do not overlap", rects.All(r => card.Contains(r)) &&
            rects.Select((a, i) => (a, i)).All(a => rects.Select((b, j) => (b, j)).Where(b => b.j > a.i).All(b => !a.a.Overlaps(b.b))));
    }
    var (labelRect, infoRect) = HubLayout.TextLines(HubLayout.MasterRow(cell).Text, true);
    Check("a name and a note stack without overlap inside the row", !labelRect.Overlaps(infoRect) && card.Contains(labelRect) && card.Contains(infoRect));
    Check("a lone name is centred over the whole row height", HubLayout.TextLines(HubLayout.MasterRow(cell).Text, false).Label.Height == HubLayout.RowHeight);

    // the tray: four equal columns ending on the card's inner edge, inside the card, above the first plugin row
    var tray = Enumerable.Range(0, 7).Select(i => HubLayout.TrayButton(i, W)).ToList();
    Check("tray buttons are equal width and end on the card's inner edge",
        tray.Take(4).All(r => r.Width == tray[0].Width) && tray[3].Right == W - g - HubLayout.CardPad && tray[0].X == g + HubLayout.CardPad);
    Check("tray buttons do not overlap and stay inside the tray",
        tray.Select((a, i) => (a, i)).All(a => tray.Select((b, j) => (b, j)).Where(b => b.j > a.i).All(b => !a.a.Overlaps(b.b))) &&
        tray.All(r => r.Bottom + HubLayout.CardPad <= HubLayout.TrayHeight));
    Check("the first plugin row starts below the tray", cells[0].Y >= HubLayout.TrayHeight);
    Check("tray labels fit the tray buttons", new[] { "Single Target DPS", "Auto-Rotation Toggle" }.All(l => HubLayout.LabelFits(l, tray[0].Width)));

    // the switch: the knob stays inside the track in both positions
    Check("the knob fits the track at both ends",
        HubLayout.KnobInset + HubLayout.KnobSize + HubLayout.KnobInset == HubLayout.ToggleHeight &&
        HubLayout.ToggleWidth - HubLayout.KnobInset - HubLayout.KnobSize > HubLayout.KnobInset);

    // gutters step down with the window width and cards still fit
    Check("the gutter steps 24 / 16 / 12", HubLayout.Gutter(1080) == 24 && HubLayout.Gutter(700) == 16 && HubLayout.Gutter(400) == 12);
}

// ---- panel rasteriser: crisp corners at the exact size -------------------------------------------------
{
    var fill = new System.Numerics.Vector4(0.1f, 0.2f, 0.3f, 1f);
    var edge = new System.Numerics.Vector4(0.9f, 0.8f, 0.7f, 1f);
    byte[] Px(byte[] b, int w, int x, int y) => b.Skip(((y * w) + x) * 4).Take(4).ToArray();
    var tex = PanelRaster.Render(200, 40, fill, edge, 8f);
    Check("raster: buffer is width x height x 4", tex.Length == 200 * 40 * 4);
    Check("raster: the outer corner pixel is transparent", Px(tex, 200, 0, 0)[3] == 0);
    Check("raster: the middle is the fill, fully opaque", Px(tex, 200, 100, 20) is { } m && m[3] == 255 && Math.Abs(m[2] - 26) <= 1 && Math.Abs(m[1] - 51) <= 1 && Math.Abs(m[0] - 77) <= 1);
    Check("raster: the straight edge is the border colour, opaque", Px(tex, 200, 100, 0) is { } e && e[3] == 255 && Math.Abs(e[2] - 230) <= 1 && Math.Abs(e[1] - 204) <= 1 && Math.Abs(e[0] - 179) <= 1);
    Check("raster: the same corner looks the same on a wide and a narrow panel",
        Px(PanelRaster.Render(40, 40, fill, edge, 8f), 40, 1, 1).SequenceEqual(Px(tex, 200, 1, 1)) &&
        Px(PanelRaster.Render(40, 40, fill, edge, 8f), 40, 4, 1).SequenceEqual(Px(tex, 200, 4, 1)));
    Check("raster: the corner stays within its radius (no smear)",
        Enumerable.Range(0, 40).All(x => Px(tex, 200, x, 20)[3] == 255 || x == 0) && Px(tex, 200, 9, 0)[3] == 255);
    Check("raster: left-right symmetric", Px(tex, 200, 2, 3).SequenceEqual(Px(tex, 200, 197, 3)));
    var flat = PanelRaster.Render(4, 4, fill, null, 0f);
    Check("raster: a flat fill is uniform", Enumerable.Range(0, 16).All(i => flat[(i * 4) + 3] == 255));
    var pill = PanelRaster.Render(44, 24, fill, edge, 12f);
    Check("raster: a pill has transparent corners and opaque ends", Px(pill, 44, 0, 0)[3] == 0 && Px(pill, 44, 0, 12)[3] >= 240);
    Check("raster: a translucent fill keeps its alpha", PanelRaster.Render(40, 40, fill with { W = 0.14f }, null, 8f) is { } t && Px(t, 40, 20, 20)[3] == 36);
    Check("raster: sizes are clamped", PanelRaster.Clamp(0f) == 1 && PanelRaster.Clamp(99999f) == PanelRaster.MaxSide);
}

Console.WriteLine($"LazyHub.Harness: {pass} pass, {fail} fail");
return fail == 0 ? 0 : 1;
