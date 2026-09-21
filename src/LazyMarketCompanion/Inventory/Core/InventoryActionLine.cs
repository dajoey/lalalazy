using System;
using Lalalazy.Telemetry;

namespace LazyMarketCompanion.Inventory;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (Inventory cases).
//
// IV| - the Inventory tab's action lines. Every action the tab takes (or refuses) is ONE line, written three
// places: the plugin log (INF), the telemetry ring (so a problem report carries it), and the append-only
// actions file <pluginConfigs>/LazyMarketCompanion/lmc_inventory_actions.log - the audit trail for anything
// that changed an item. Same record grammar as ER| (docs/telemetry-lines.md section 2: key=value fields,
// escaped values, fixed key order, trailing tr= on truncation), built with the shared TelemetryLineBuilder.
//
//   IV|<unixms>|vendor|v=<ver>|r=<retainer>|src=<container>:<slot>|i=<item>|hq=0/1|q=<qty>|est=<gil>|ok=0/1|why=<text>
//   IV|<unixms>|move  |v=<ver>|src=<container>:<slot>|dst=<container>:<slot>|i=<item>|hq=0/1|ok=0/1|rc=<rc>|why=<text>
//   IV|<unixms>|undo  |  (same keys as move)
//   IV|<unixms>|batch |v=<ver>|what=vendor/move/undo|ev=begin/end/refused|n=<planned>|okn=<done>|fail=<failed>|why=<text>
//   IV|<unixms>|optin |v=<ver>|i=<item>|on=0/1|rail=<unique/untradable/rare>
//
// Keys are fixed; a future version may add keys before tr= but never renames one. Container names are the
// FFXIVClientStructs InventoryType names ("RetainerPage2", "Inventory1", "ArmoryBody"), never raw numbers.

public static class InventoryActionLine
{
  public const string Prefix = "IV|";
  public const string ActionsFileName = "lmc_inventory_actions.log";

  public static string Vendor(long unixMs, string version, string retainer, string src, uint item, bool hq, int qty, long est, bool ok, string why)
    => new TelemetryLineBuilder(Prefix, unixMs, "vendor")
      .Field("v", version, 32)
      .Field("r", retainer, 64)
      .Field("src", src, 48)
      .Field("i", item)
      .Field("hq", hq)
      .Field("q", qty)
      .Field("est", est)
      .Field("ok", ok)
      .Field("why", why, 300)
      .ToString();

  public static string Move(long unixMs, string kind, string version, string src, string dst, uint item, bool hq, bool ok, int rc, string why)
    => new TelemetryLineBuilder(Prefix, unixMs, kind == "undo" ? "undo" : "move")
      .Field("v", version, 32)
      .Field("src", src, 48)
      .Field("dst", dst, 48)
      .Field("i", item)
      .Field("hq", hq)
      .Field("ok", ok)
      .Field("rc", rc)
      .Field("why", why, 300)
      .ToString();

  public static string Batch(long unixMs, string version, string what, string ev, int planned, int done, int failed, string why)
    => new TelemetryLineBuilder(Prefix, unixMs, "batch")
      .Field("v", version, 32)
      .Field("what", what, 16)
      .Field("ev", ev, 16)
      .Field("n", planned)
      .Field("okn", done)
      .Field("fail", failed)
      .Field("why", why, 300)
      .ToString();

  public static string OptIn(long unixMs, string version, uint item, bool on, string rail)
    => new TelemetryLineBuilder(Prefix, unixMs, "optin")
      .Field("v", version, 32)
      .Field("i", item)
      .Field("on", on)
      .Field("rail", rail, 16)
      .ToString();

  /// <summary>"RetainerPage2:14" - the name mirrors VendorOp.ContainerName and the FFXIVClientStructs InventoryType names.</summary>
  public static string Slot(int container, int slot) => ContainerName(container) + ":" + slot;

  public static string ContainerName(int container) => container switch
  {
    >= 0 and <= 3 => $"Inventory{container + 1}",
    2001 => "Crystals",
    3200 => "ArmoryOffHand",
    3201 => "ArmoryHead",
    3202 => "ArmoryBody",
    3203 => "ArmoryHands",
    3204 => "ArmoryWaist",
    3205 => "ArmoryLegs",
    3206 => "ArmoryFeets",
    3207 => "ArmoryEar",
    3208 => "ArmoryNeck",
    3209 => "ArmoryWrist",
    3300 => "ArmoryRings",
    3400 => "ArmorySoulCrystal",
    3500 => "ArmoryMainHand",
    4000 => "SaddleBag1",
    4001 => "SaddleBag2",
    4100 => "PremiumSaddleBag1",
    4101 => "PremiumSaddleBag2",
    >= 10000 and <= 10006 => $"RetainerPage{container - 10000 + 1}",
    12001 => "RetainerCrystals",
    _ => $"Unknown({container})",
  };
}
