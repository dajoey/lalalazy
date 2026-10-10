using LazyMarketCompanion;
using LazyMarketCompanion.AutoMarket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Offline cases 170-173: the Auto-Market panel's filter dropdowns remember their setting (0.2.8.15).
//
// The bug: the panel's status and category dropdowns were plain window fields, so every plugin
// reload or game restart reset them to All / every category, even though every other knob on the
// panel saves through the plugin configuration. The fix persists both in the Configuration and
// restores them on draw.
//
// Everything here compiles against the PRE-fix tree too, so the red run is a RUN, not a compile
// error: the round trip goes through the real Configuration and the real Newtonsoft serializer
// with the new keys only as JSON strings and reflection, never as compiled member references.
// Case 170 is the failing-test-first proof and the config round-trip half of the live-path check
// (a config saved by the previous version opens with All / every category; a saved "not on list"
// + category survives save -> reload -> save). Case 171 pins the new members and their defaults,
// case 172 the Dalamud-free helpers the window resolves the saved values with, and case 173 the
// source-scan wiring pin (case-46 control form: a missing source read FAILS, never passes
// vacuously).
internal static class PanelFilterCases
{
  public static void Run(StallCases.CheckFn check)
  {
    // ---- 170. The saved filters survive save -> load -> save through the REAL Configuration ----
    {
      // The control half of the live-path check: a config in the exact shape 0.2.8.14 writes
      // (no panel-filter keys at all) must open with All / every category, on every tree.
      var oldShaped = JsonConvert.DeserializeObject<Configuration>("{\"Version\":4,\"AutoMarketItems\":[]}");
      check("170 a previous-version config (no panel keys) opens with All / every category",
        oldShaped != null && Equals(GetMember(oldShaped, "AutoMarketPanelStatus"), AutoMarketPanelModel.StatusFilter.All)
          && Equals(GetMember(oldShaped, "AutoMarketPanelCategory"), 0u),
        oldShaped == null ? "deserialization returned null" : "");

      // The red half: the config the NEW panel writes after the player sets the dropdowns
      // ("Not on list" = 2, a real search category id). On the pre-fix tree the deserializer
      // drops both unknown keys, so the re-save loses them - that is the reported reset.
      const string savedWithFilters = "{\"Version\":4,\"AutoMarketItems\":[],\"AutoMarketPanelStatus\":2,\"AutoMarketPanelCategory\":1234}";
      var loaded = JsonConvert.DeserializeObject<Configuration>(savedWithFilters);
      if (loaded != null)
        loaded.Save(); // the real Save(): stub serializes with the real Newtonsoft, as the game does
      var saved1 = Plugin.PluginInterface.LastConfigJson;
      var reloaded = saved1 == null ? null : JsonConvert.DeserializeObject<Configuration>(saved1);
      if (reloaded != null)
        reloaded.Save();
      var saved2 = Plugin.PluginInterface.LastConfigJson;

      check("170 scan: the round trip actually produced two saves and a reload (control: not vacuous)",
        saved1 != null && saved2 != null && reloaded != null, "");

      var final = saved2 == null ? new JObject() : JObject.Parse(saved2);
      check("170 the panel filters survive save -> reload -> save (set: Not on list + category 1234)",
        (int?)final["AutoMarketPanelStatus"] == 2 && (int?)final["AutoMarketPanelCategory"] == 1234,
        saved2 ?? "no save produced");
    }

    // ---- 171. The Configuration members exist, with the old behaviour as their defaults ----
    {
      var pStatus = typeof(Configuration).GetProperty("AutoMarketPanelStatus");
      var pCategory = typeof(Configuration).GetProperty("AutoMarketPanelCategory");
      check("171 Configuration carries the panel status filter, typed as the model's StatusFilter",
        pStatus != null && pStatus.PropertyType == typeof(AutoMarketPanelModel.StatusFilter),
        pStatus == null ? "AutoMarketPanelStatus not found on Configuration" : $"type={pStatus!.PropertyType.Name}");
      check("171 Configuration carries the panel category filter, typed uint",
        pCategory != null && pCategory.PropertyType == typeof(uint),
        pCategory == null ? "AutoMarketPanelCategory not found on Configuration" : $"type={pCategory!.PropertyType.Name}");

      var fresh = Activator.CreateInstance<Configuration>();
      check("171 the default status is All - the behaviour of builds before the filter was remembered",
        pStatus != null && Equals(pStatus.GetValue(fresh), AutoMarketPanelModel.StatusFilter.All),
        pStatus == null ? "" : $"default={pStatus.GetValue(fresh)}");
      check("171 the default category is every category (0)",
        pCategory != null && Equals(pCategory.GetValue(fresh), 0u),
        pCategory == null ? "" : $"default={pCategory.GetValue(fresh)}");
    }

    // ---- 172. The Dalamud-free helpers the window resolves the saved values with ----
    {
      var mStatus = typeof(AutoMarketPanelModel).GetMethod("ResolveVisibleStatus", [typeof(AutoMarketPanelModel.StatusFilter)]);
      check("172 ResolveVisibleStatus exists on the panel model", mStatus != null, "");
      if (mStatus != null)
      {
        check("172 a saved status inside the enum passes through",
          Equals(mStatus.Invoke(null, [(object)AutoMarketPanelModel.StatusFilter.NotOnList]), AutoMarketPanelModel.StatusFilter.NotOnList), "");
        check("172 a saved status outside the enum (hand-edited config) shows as All, never an invisible filter",
          Equals(mStatus.Invoke(null, [(object)(AutoMarketPanelModel.StatusFilter)99]), AutoMarketPanelModel.StatusFilter.All), "");
      }

      var mCat = typeof(AutoMarketPanelModel).GetMethod("ResolveVisibleCategory", [typeof(uint), typeof(IReadOnlyList<uint>)]);
      check("172 ResolveVisibleCategory exists on the panel model", mCat != null, "");
      if (mCat != null)
      {
        var present = new List<uint> { 10, 20, 30 };
        check("172 a saved category that is present now passes through",
          Equals(mCat.Invoke(null, [(object)20u, present]), 20u), "");
        check("172 a saved category that is not in the current bags shows as every category - it must not hide everything",
          Equals(mCat.Invoke(null, [(object)99u, present]), 0u), "");
        check("172 every-category (0) stays every category",
          Equals(mCat.Invoke(null, [(object)0u, present]), 0u), "");
        check("172 with no bags at all, any saved category shows as every category",
          Equals(mCat.Invoke(null, [(object)20u, new List<uint>()]), 0u), "");
      }

      var mPresent = typeof(AutoMarketPanelModel).GetMethod("PresentCategories", [typeof(IReadOnlyList<AutoMarketPanelModel.Row>)]);
      check("172 PresentCategories exists on the panel model", mPresent != null, "");
      if (mPresent != null)
      {
        var rows = new List<AutoMarketPanelModel.Row>
        {
          MakeRow(30), MakeRow(10), MakeRow(10), MakeRow(0), MakeRow(20),
        };
        var got = mPresent.Invoke(null, [rows]) as List<uint>;
        check("172 PresentCategories: distinct, sorted, no zero (the same list the dropdown shows)",
          got != null && got.SequenceEqual(new List<uint> { 10, 20, 30 }),
          got == null ? "null" : string.Join(",", got));
      }
    }

    // ---- 173. Source-scan wiring pin: the window really restores and saves (case-46 control form) ----
    {
      var roots = new[]
      {
        Path.Combine("..", "..", "..", "..", "..", "src", "LazyMarketCompanion"),
        Path.Combine("src", "LazyMarketCompanion"),
      };
      var root = roots.Where(Directory.Exists).FirstOrDefault() ?? "";
      check("173 scan: plugin source tree found (run from repo root or bin)",
        root.Length > 0, "src/LazyMarketCompanion not found from either candidate path");
      var winPath = Path.Combine(root, "Windows", "AutoMarketPanelWindow.cs");
      var cfgPath = Path.Combine(root, "Configuration.cs");
      check("173 scan: the panel window and Configuration sources are found",
        root.Length > 0 && File.Exists(winPath) && File.Exists(cfgPath), winPath);

      if (File.Exists(winPath) && File.Exists(cfgPath))
      {
        var win = File.ReadAllText(winPath);
        var cfg = File.ReadAllText(cfgPath);

        // Control: the scan found the two dropdowns it grades, so a refactor that renames them
        // fails loudly here instead of letting every pin below pass vacuously.
        check("173 scan: finds the two filter dropdowns it grades",
          win.Contains("##lmcPanelStatus") && win.Contains("##lmcPanelCategory"), "");

        check("173 scan: the panel resolves the saved status filter from the configuration",
          win.Contains("ResolveVisibleStatus(c.AutoMarketPanelStatus)"), "");
        check("173 scan: the panel resolves the saved category against the bags actually present",
          win.Contains("ResolveVisibleCategory(c.AutoMarketPanelCategory"), "");

        var filters = SliceMethod(win, "private void DrawFilters");
        check("173 scan: the DrawFilters method is found", filters.Length > 0, "");
        check("173 scan: picking a status writes the configuration and saves",
          filters.Contains("Configuration.AutoMarketPanelStatus = _status") && filters.Contains(".Save()"), "");
        check("173 scan: picking a category (or every-category) writes the configuration and saves",
          filters.Contains("Configuration.AutoMarketPanelCategory = 0")
            && filters.Contains("Configuration.AutoMarketPanelCategory = catId") && filters.Contains(".Save()"), "");

        check("173 scan: Configuration declares both remembered filter settings",
          cfg.Contains("AutoMarketPanelStatus { get; set; }") && cfg.Contains("AutoMarketPanelCategory { get; set; }"), "");
      }
    }
  }

  // The saved filter values, read by reflection only: on the pre-fix tree both properties are
  // absent, so this returns null and the checks above FAIL - a red run, not a crash.
  private static object? GetMember(Configuration cfg, string name) =>
    name == "AutoMarketPanelStatus"
      ? typeof(Configuration).GetProperty(name)?.GetValue(cfg) is AutoMarketPanelModel.StatusFilter s ? (object?)s : null
      : typeof(Configuration).GetProperty(name)?.GetValue(cfg) is uint c ? (object?)c : null;

  private static AutoMarketPanelModel.Row MakeRow(uint categoryId) =>
    new(1001, false, "item", categoryId, 1, 1, AutoMarketPanelModel.RowKind.NotListed, false, false, 0, 0);

  private static string SliceMethod(string src, string header)
  {
    var start = src.IndexOf(header, StringComparison.Ordinal);
    if (start < 0)
      return string.Empty;
    var next = src.IndexOf("\n  private ", start + header.Length);
    var end = next < 0 ? src.Length : next;
    return src[start..end];
  }
}
