using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LazyMarketCompanion.Inventory;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (Inventory cases).
//
// The Inventory tab's own memory, kept in a SIDECAR file (<pluginConfigs>/LazyMarketCompanion/
// lmc_inventory_state.json), never in Configuration: it is observed game state that changes every retainer
// visit, and writing it through SavePluginConfig would churn the settings file. Per character (content id):
//   - each retainer's pages as last seen in a settled retainer session (stacks + used/capacity),
//   - each retainer's item count as last read from the summoning-bell retainer list,
//   - the saddlebags as last seen loaded,
//   - the last gear-mover batch, so Undo survives a reload.
// A missing or corrupt file is an empty state (the tab then shows "never seen"), never an error.

public sealed class SeenStack
{
  public int C { get; set; }
  public int S { get; set; }
  public uint I { get; set; }
  public bool H { get; set; }
  public int Q { get; set; }
  public bool X { get; set; }
}

public sealed class RetainerSeen
{
  public string Name { get; set; } = string.Empty;
  public long PagesSeenUnixMs { get; set; }
  public int Capacity { get; set; } = SpaceMath.RetainerCapacity;
  public int Used { get; set; }
  public List<SeenStack> Stacks { get; set; } = [];
  public long BellSeenUnixMs { get; set; }
  public int BellItemCount { get; set; } = -1;
}

public sealed class ContainerSeen
{
  public long SeenUnixMs { get; set; }
  public int Size { get; set; }
  public int Used { get; set; }
}

public sealed class CharacterInventoryState
{
  public Dictionary<string, RetainerSeen> Retainers { get; set; } = new(StringComparer.Ordinal);
  public ContainerSeen? Saddlebag { get; set; }
  public ContainerSeen? PremiumSaddlebag { get; set; }
  public List<GearMoveRecord> LastGearBatch { get; set; } = [];
  public long LastGearBatchUnixMs { get; set; }
}

public sealed class InventoryState
{
  public const string FileName = "lmc_inventory_state.json";
  public int Version { get; set; } = 1;
  public Dictionary<string, CharacterInventoryState> Characters { get; set; } = new(StringComparer.Ordinal);

  private static readonly JsonSerializerOptions Json = new()
  {
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
  };

  public static string KeyOf(ulong contentId) => contentId.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);

  public CharacterInventoryState For(ulong contentId)
  {
    var key = KeyOf(contentId);
    if (!Characters.TryGetValue(key, out var c))
      Characters[key] = c = new CharacterInventoryState();
    return c;
  }

  public static InventoryState Load(string? json)
  {
    if (string.IsNullOrWhiteSpace(json))
      return new InventoryState();
    try
    {
      var s = JsonSerializer.Deserialize<InventoryState>(json, Json);
      if (s == null)
        return new InventoryState();
      s.Characters = new Dictionary<string, CharacterInventoryState>(s.Characters ?? new(), StringComparer.Ordinal);
      foreach (var c in s.Characters.Values)
      {
        c.Retainers = new Dictionary<string, RetainerSeen>(c.Retainers ?? new(), StringComparer.Ordinal);
        c.LastGearBatch ??= [];
      }
      return s;
    }
    catch (JsonException)
    {
      return new InventoryState();
    }
    catch (NotSupportedException)
    {
      return new InventoryState();
    }
  }

  public string Save() => JsonSerializer.Serialize(this, Json);

  /// <summary>
  /// The best-known space for a retainer: the page snapshot when it is at least as new as the bell count
  /// (it is exact), otherwise the bell count against the standard 175-slot capacity. Null = never seen.
  /// </summary>
  public static (int Used, int Capacity, long SeenUnixMs, string Source)? BestSpace(RetainerSeen r)
  {
    var pages = r.PagesSeenUnixMs > 0;
    var bell = r.BellSeenUnixMs > 0 && r.BellItemCount >= 0;
    if (!pages && !bell)
      return null;
    if (pages && (!bell || r.PagesSeenUnixMs >= r.BellSeenUnixMs))
      return (r.Used, r.Capacity > 0 ? r.Capacity : SpaceMath.RetainerCapacity, r.PagesSeenUnixMs, "pages");
    return (r.BellItemCount, SpaceMath.RetainerCapacity, r.BellSeenUnixMs, "bell");
  }

  /// <summary>Item ids each retainer held when its pages were last seen (for AutoRetainer's duplicates rule).</summary>
  public static Dictionary<string, IReadOnlySet<uint>> Holdings(CharacterInventoryState c)
  {
    var result = new Dictionary<string, IReadOnlySet<uint>>(StringComparer.Ordinal);
    foreach (var (name, r) in c.Retainers)
    {
      if (r.PagesSeenUnixMs <= 0)
        continue;
      result[name] = r.Stacks.Where(s => s.I != 0).Select(s => s.I).ToHashSet();
    }
    return result;
  }

  public static List<RetainerStack> StacksOf(RetainerSeen r)
    => r.Stacks.Select(s => new RetainerStack(s.C, s.S, s.I, s.H, s.Q, s.X)).ToList();
}
