using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace LazyMarketCompanion.Inventory;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (Inventory cases).
//
// FILE COUPLING to AutoRetainer (read-only). AutoRetainer exposes no IPC for its entrust plans or its
// inventory-cleanup lists (its IPC surface is venture / postprocess / multi-mode / PluginState only,
// read from the installed 4.6.1.34 build), so - exactly like AllaganFlags.cs does for AllaganMarket -
// the lists are read from AutoRetainer's own config file, <pluginConfigs>/AutoRetainer/DefaultConfig.json.
// Shapes pinned against the decompiled AutoRetainerAPI.Configuration types of that build:
//
//   EntrustPlans[]         { Guid, Name, Duplicates, DuplicatesMultiStack, EntrustCategories[{ID, AmountToKeep}],
//                            EntrustItems[uint], ManualPlan, ExcludeProtected, ... }
//                            EntrustCategories[].ID is Item.ItemUICategory's row id (TaskEntrustDuplicates
//                            compares it to item.ItemUICategory.RowId), NOT the market search category.
//   AdditionalData         { "#<CID as X16> <RetainerName>": { EntrustPlan: "<guid>", ... } }
//   OfflineData[]          { CID, InventoryCleanupPlan: "<guid>", RetainerData[{ Name, ... }], ... }
//   DefaultIMSettings      { IMAutoVendorHard[], IMAutoVendorHardIgnoreStack[], IMAutoVendorSoft[], IMDiscardList[],
//                            IMDiscardIgnoreStack[], IMDesynth[], IMProtectList[], IMAutoVendorHardStackLimit,
//                            IMDiscardStackLimit, IMEnableAutoVendor, IMEnableItemDesynthesis,
//                            AdditionMode{ProtectList,SoftSellList,HardSellList,DiscardList,DesynthList} }
//   AdditionalIMSettings[] same shape + GUID; a character whose InventoryCleanupPlan names one uses it, with the
//                          DEFAULT lists merged in for every AdditionMode flag it sets (AutoRetainer's own
//                          GetIMSettings); every other character uses DefaultIMSettings.
//   RecordStats            whether AutoRetainer writes the venture statistics files ArVentureStats reads.
//
// Every read is tolerant: a missing key is the type default, a malformed file is Unavailable with the
// reason, never a throw. A format change in AutoRetainer degrades to "unknown", and every consumer treats
// unknown on the side that touches nothing (the Inventory tab says so).

/// <summary>One AutoRetainer entrust plan.</summary>
public sealed record ArEntrustPlan(
  string Guid,
  string Name,
  bool Duplicates,
  bool DuplicatesMultiStack,
  IReadOnlySet<uint> Items,
  IReadOnlySet<uint> UiCategories,
  bool ManualPlan,
  bool ExcludeProtected);

/// <summary>The inventory-cleanup lists AutoRetainer applies to the CURRENT character.</summary>
public sealed record ArImLists(
  IReadOnlySet<uint> VendorHard,
  IReadOnlySet<uint> VendorHardIgnoreStack,
  IReadOnlySet<uint> VendorSoft,
  IReadOnlySet<uint> Discard,
  IReadOnlySet<uint> DiscardIgnoreStack,
  IReadOnlySet<uint> Desynth,
  IReadOnlySet<uint> Protect,
  int VendorHardStackLimit,
  int DiscardStackLimit,
  bool AutoVendorEnabled,
  bool DesynthEnabled)
{
  public static ArImLists Empty { get; } = new(
    new HashSet<uint>(), new HashSet<uint>(), new HashSet<uint>(), new HashSet<uint>(), new HashSet<uint>(),
    new HashSet<uint>(), new HashSet<uint>(), 0, 0, false, false);
}

/// <summary>Everything the Inventory tab reads from AutoRetainer's config, for one character.</summary>
public sealed class ArSnapshot
{
  /// <summary>False when the file was absent or unreadable; <see cref="Status"/> says which.</summary>
  public bool Available { get; init; }

  /// <summary>"ok", or a short human-readable reason the snapshot is empty.</summary>
  public string Status { get; init; } = "ok";

  public IReadOnlyList<ArEntrustPlan> Plans { get; init; } = [];

  /// <summary>Retainer name -> entrust-plan guid, for the character the snapshot was parsed for. Plans with no retainer are absent.</summary>
  public IReadOnlyDictionary<string, string> PlanByRetainer { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

  public ArImLists Im { get; init; } = ArImLists.Empty;

  /// <summary>AutoRetainer's "Record Venture Statistics" switch. Off = no venture log, so nothing is venture loot.</summary>
  public bool RecordStats { get; init; }

  public static ArSnapshot Unavailable(string why) => new() { Available = false, Status = why };

  /// <summary>The plan assigned to a retainer, or null.</summary>
  public ArEntrustPlan? PlanFor(string retainerName)
  {
    foreach (var (name, guid) in PlanByRetainer)
    {
      if (!CategoryRouter.RetainerNamesEqual(name, retainerName))
        continue;
      return Plans.FirstOrDefault(p => string.Equals(p.Guid, guid, StringComparison.OrdinalIgnoreCase));
    }
    return null;
  }

  /// <summary>
  /// True when ANY entrust plan in the file lists this item explicitly or through its ItemUICategory -
  /// assigned to a retainer or not. The conservative reading the venture-loot rails use: a plan the player
  /// built around an item is a statement about that item even while it is unassigned.
  /// </summary>
  public bool AnyPlanNames(uint itemId, uint uiCategory)
    => Plans.Any(p => p.Items.Contains(itemId) || (uiCategory != 0 && p.UiCategories.Contains(uiCategory)));
}

public static class ArConfigParser
{
  public const string ConfigFileName = "DefaultConfig.json";
  public const string EmptyGuid = "00000000-0000-0000-0000-000000000000";

  /// <summary>
  /// Parses AutoRetainer's DefaultConfig.json for the character <paramref name="contentId"/> (0 = unknown:
  /// plans still parse, but no retainer assignment and the DEFAULT cleanup lists are used).
  /// </summary>
  public static ArSnapshot Parse(string? json, ulong contentId)
  {
    if (string.IsNullOrWhiteSpace(json))
      return ArSnapshot.Unavailable("AutoRetainer config not found");
    json = json.TrimStart('\uFEFF');

    try
    {
      using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
      var root = doc.RootElement;
      if (root.ValueKind != JsonValueKind.Object)
        return ArSnapshot.Unavailable("AutoRetainer config is not a JSON object");

      var plans = new List<ArEntrustPlan>();
      if (root.TryGetProperty("EntrustPlans", out var plansEl) && plansEl.ValueKind == JsonValueKind.Array)
      {
        foreach (var p in plansEl.EnumerateArray())
        {
          if (p.ValueKind != JsonValueKind.Object)
            continue;
          var guid = Str(p, "Guid");
          if (string.IsNullOrEmpty(guid))
            continue;
          var cats = new HashSet<uint>();
          if (p.TryGetProperty("EntrustCategories", out var catsEl) && catsEl.ValueKind == JsonValueKind.Array)
          {
            foreach (var c in catsEl.EnumerateArray())
            {
              if (c.ValueKind == JsonValueKind.Object && c.TryGetProperty("ID", out var idEl) && U32(idEl, out var id) && id != 0)
                cats.Add(id);
            }
          }
          plans.Add(new ArEntrustPlan(
            guid,
            Str(p, "Name"),
            Bool(p, "Duplicates"),
            Bool(p, "DuplicatesMultiStack"),
            UIntSet(p, "EntrustItems"),
            cats,
            Bool(p, "ManualPlan"),
            Bool(p, "ExcludeProtected")));
        }
      }

      // Per-retainer plan assignment for THIS character: AdditionalData keys are "#<CID:X16> <name>".
      var byRetainer = new Dictionary<string, string>(StringComparer.Ordinal);
      if (contentId != 0 && root.TryGetProperty("AdditionalData", out var addEl) && addEl.ValueKind == JsonValueKind.Object)
      {
        var prefix = "#" + contentId.ToString("X16", CultureInfo.InvariantCulture) + " ";
        foreach (var prop in addEl.EnumerateObject())
        {
          if (!prop.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || prop.Value.ValueKind != JsonValueKind.Object)
            continue;
          var name = prop.Name[prefix.Length..];
          var planGuid = Str(prop.Value, "EntrustPlan");
          if (name.Length == 0 || string.IsNullOrEmpty(planGuid) || string.Equals(planGuid, EmptyGuid, StringComparison.OrdinalIgnoreCase))
            continue;
          byRetainer[name] = planGuid;
        }
      }

      // The character's cleanup plan guid (OfflineData[].InventoryCleanupPlan for the matching CID).
      string? cleanupPlan = null;
      if (contentId != 0 && root.TryGetProperty("OfflineData", out var offEl) && offEl.ValueKind == JsonValueKind.Array)
      {
        foreach (var c in offEl.EnumerateArray())
        {
          if (c.ValueKind != JsonValueKind.Object || !c.TryGetProperty("CID", out var cidEl) || cidEl.ValueKind != JsonValueKind.Number || !cidEl.TryGetUInt64(out var cid) || cid != contentId)
            continue;
          cleanupPlan = Str(c, "InventoryCleanupPlan");
          break;
        }
      }

      var defaults = root.TryGetProperty("DefaultIMSettings", out var defEl) && defEl.ValueKind == JsonValueKind.Object ? (JsonElement?)defEl : null;
      JsonElement? additional = null;
      if (!string.IsNullOrEmpty(cleanupPlan) && !string.Equals(cleanupPlan, EmptyGuid, StringComparison.OrdinalIgnoreCase)
          && root.TryGetProperty("AdditionalIMSettings", out var addImEl) && addImEl.ValueKind == JsonValueKind.Array)
      {
        foreach (var s in addImEl.EnumerateArray())
        {
          if (s.ValueKind == JsonValueKind.Object && string.Equals(Str(s, "GUID"), cleanupPlan, StringComparison.OrdinalIgnoreCase))
          {
            additional = s;
            break;
          }
        }
      }

      return new ArSnapshot
      {
        Available = true,
        Status = "ok",
        Plans = plans,
        PlanByRetainer = byRetainer,
        Im = BuildIm(additional, defaults),
        RecordStats = Bool(root, "RecordStats"),
      };
    }
    catch (JsonException ex)
    {
      return ArSnapshot.Unavailable($"AutoRetainer config could not be parsed ({ex.GetType().Name})");
    }
    catch (InvalidOperationException ex)
    {
      return ArSnapshot.Unavailable($"AutoRetainer config has an unexpected shape ({ex.GetType().Name})");
    }
  }

  /// <summary>
  /// AutoRetainer's GetIMSettings: the additional plan when the character names one (with the default
  /// list merged in for each AdditionMode flag it sets), otherwise the default settings.
  /// </summary>
  private static ArImLists BuildIm(JsonElement? additional, JsonElement? defaults)
  {
    if (additional is null && defaults is null)
      return ArImLists.Empty;

    var src = additional ?? defaults!.Value;
    HashSet<uint> Merge(string key, string modeFlag)
    {
      var set = UIntSet(src, key);
      if (additional is not null && defaults is not null && Bool(src, modeFlag))
        set.UnionWith(UIntSet(defaults.Value, key));
      return set;
    }

    return new ArImLists(
      Merge("IMAutoVendorHard", "AdditionModeHardSellList"),
      UIntSet(src, "IMAutoVendorHardIgnoreStack"),
      Merge("IMAutoVendorSoft", "AdditionModeSoftSellList"),
      Merge("IMDiscardList", "AdditionModeDiscardList"),
      UIntSet(src, "IMDiscardIgnoreStack"),
      Merge("IMDesynth", "AdditionModeDesynthList"),
      Merge("IMProtectList", "AdditionModeProtectList"),
      Int(src, "IMAutoVendorHardStackLimit", 20),
      Int(src, "IMDiscardStackLimit", 20),
      Bool(src, "IMEnableAutoVendor"),
      Bool(src, "IMEnableItemDesynthesis"));
  }

  /// <summary>TryGetUInt32 throws on a non-number element; a wrong-typed value must read as "absent" instead.</summary>
  internal static bool U32(JsonElement e, out uint value)
  {
    value = 0;
    return e.ValueKind == JsonValueKind.Number && e.TryGetUInt32(out value);
  }

  private static string Str(JsonElement e, string key)
    => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

  private static bool Bool(JsonElement e, string key)
    => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

  private static int Int(JsonElement e, string key, int fallback)
    => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : fallback;

  private static HashSet<uint> UIntSet(JsonElement e, string key)
  {
    var set = new HashSet<uint>();
    if (!e.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array)
      return set;
    foreach (var x in v.EnumerateArray())
    {
      if (U32(x, out var id) && id != 0)
        set.Add(id);
    }
    return set;
  }
}

/// <summary>One venture reward AutoRetainer recorded (StatisticsRecord: I, H, T, A, V).</summary>
public sealed record VentureRecord(uint ItemId, bool Hq, long UnixSeconds, uint Amount, uint VentureId);

/// <summary>
/// AutoRetainer's per-retainer venture log, <c>&lt;CID:X16&gt;_&lt;RetainerName&gt;.statistic.json</c> in its config
/// directory: <c>{"Records":[{"I":item,"H":1,"T":unixSeconds,"A":amount,"V":ventureId}], ...}</c>. H, A and V are
/// omitted at their defaults (0, 1, 0 - DefaultValue attributes on StatisticsRecord). AutoRetainer appends a
/// record when the retainer's "returns with" chat line names an item at a summoning bell.
/// </summary>
public static class ArVentureStats
{
  /// <summary>Quick Exploration - the random-reward venture the Inventory tab's venture-loot definition is built on.</summary>
  public const uint QuickExplorationVentureId = 395;

  public static string FileName(ulong contentId, string retainerName)
    => contentId.ToString("X16", CultureInfo.InvariantCulture) + "_" + retainerName + ".statistic.json";

  /// <summary>Malformed records are skipped; a malformed file is an empty list.</summary>
  public static List<VentureRecord> Parse(string? json)
  {
    var result = new List<VentureRecord>();
    if (string.IsNullOrWhiteSpace(json))
      return result;
    json = json.TrimStart('\uFEFF');
    try
    {
      using var doc = JsonDocument.Parse(json);
      if (doc.RootElement.ValueKind != JsonValueKind.Object
          || !doc.RootElement.TryGetProperty("Records", out var recs) || recs.ValueKind != JsonValueKind.Array)
        return result;
      foreach (var r in recs.EnumerateArray())
      {
        if (r.ValueKind != JsonValueKind.Object)
          continue;
        if (!r.TryGetProperty("I", out var iEl) || !ArConfigParser.U32(iEl, out var item) || item == 0)
          continue;
        if (!r.TryGetProperty("T", out var tEl) || tEl.ValueKind != JsonValueKind.Number || !tEl.TryGetInt64(out var t) || t <= 0)
          continue;
        var hq = r.TryGetProperty("H", out var hEl) && hEl.ValueKind == JsonValueKind.Number && hEl.TryGetInt32(out var h) && h != 0;
        var amount = r.TryGetProperty("A", out var aEl) && ArConfigParser.U32(aEl, out var a) ? a : 1u;
        var venture = r.TryGetProperty("V", out var vEl) && ArConfigParser.U32(vEl, out var v) ? v : 0u;
        if (amount == 0)
          continue;
        result.Add(new VentureRecord(item, hq, t, amount, venture));
      }
    }
    catch (JsonException)
    {
      result.Clear();
    }
    return result;
  }

  /// <summary>Venture intake summary for one retainer: records in the last 24 h and the 7-day daily average.</summary>
  public sealed record Intake(int Last24h, double PerDay7d, int QuickExploration7d, long NewestUnixSeconds);

  public static Intake Summarize(IReadOnlyList<VentureRecord> records, long nowUnixSeconds)
  {
    var day = 0;
    var week = 0;
    var qe = 0;
    long newest = 0;
    foreach (var r in records)
    {
      if (r.UnixSeconds > newest)
        newest = r.UnixSeconds;
      var age = nowUnixSeconds - r.UnixSeconds;
      if (age < 0)
        continue;
      if (age <= 86_400)
        day++;
      if (age <= 7 * 86_400)
      {
        week++;
        if (r.VentureId == QuickExplorationVentureId)
          qe++;
      }
    }
    return new Intake(day, week / 7.0, qe, newest);
  }
}
