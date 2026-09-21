using System;
using System.Collections.Generic;

namespace LazyMarketCompanion.Inventory;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (Inventory cases).
//
// The "when idle" gate for the gear mover (and the precondition set every Inventory-tab action checks).
// Polarity: an unknown state NEVER counts as idle. A plugin that is installed and loaded but whose busy
// signal cannot be read (IPC not ready, renamed, wrong type) blocks exactly like one that says "busy"; only
// a plugin that is not installed at all is out of the way.

/// <summary>What another plugin's busy signal said.</summary>
public enum PluginBusy { NotInstalled, Idle, Busy, Unknown }

/// <param name="Occupied">Any of the game's occupied / between-areas / trade / logging-out states.</param>
/// <param name="LmcBusy">LMC's own retainer automation or an Inventory-tab action is running.</param>
public sealed record IdleFacts(
  bool LoggedIn,
  bool InCombat,
  bool InDuty,
  bool Crafting,
  bool Gathering,
  bool Cutscene,
  bool Occupied,
  bool LmcBusy,
  PluginBusy AutoRetainer,
  PluginBusy AutoDuty,
  PluginBusy Artisan,
  PluginBusy GatherBuddy);

public sealed record IdleVerdict(bool Idle, IReadOnlyList<string> Why);

public static class IdleGate
{
  public static IdleVerdict Decide(IdleFacts f)
  {
    var why = new List<string>();
    if (!f.LoggedIn) why.Add("not logged in");
    if (f.InCombat) why.Add("in combat");
    if (f.InDuty) why.Add("in a duty");
    if (f.Crafting) why.Add("crafting");
    if (f.Gathering) why.Add("gathering or fishing");
    if (f.Cutscene) why.Add("in a cutscene");
    if (f.Occupied) why.Add("busy with an in-game window, event or zone change");
    if (f.LmcBusy) why.Add("LMC automation is running");
    Plugin(why, "AutoRetainer", f.AutoRetainer);
    Plugin(why, "AutoDuty", f.AutoDuty);
    Plugin(why, "Artisan", f.Artisan);
    Plugin(why, "GatherBuddy Reborn", f.GatherBuddy);
    return new IdleVerdict(why.Count == 0, why);
  }

  private static void Plugin(List<string> why, string name, PluginBusy state)
  {
    switch (state)
    {
      case PluginBusy.Busy:
        why.Add($"{name} is busy");
        break;
      case PluginBusy.Unknown:
        why.Add($"{name} is loaded but its busy state cannot be read");
        break;
    }
  }
}

/// <summary>
/// Debounce for the idle mover: acts only after the gate has said idle CONTINUOUSLY for <c>holdMs</c>, and
/// never more often than once per <c>cooldownMs</c>. Any non-idle reading restarts the hold. Pure state
/// machine (the caller passes the clock), so the harness can walk it.
/// </summary>
public sealed class IdleDebounce
{
  private readonly long _holdMs;
  private readonly long _cooldownMs;
  private long _idleSince = -1;
  private long _lastActed = long.MinValue / 2;

  public IdleDebounce(long holdMs, long cooldownMs)
  {
    _holdMs = Math.Max(holdMs, 0);
    _cooldownMs = Math.Max(cooldownMs, 0);
  }

  /// <summary>Feeds one reading; true = act now (and the cooldown starts).</summary>
  public bool Update(bool idle, long nowMs)
  {
    if (!idle)
    {
      _idleSince = -1;
      return false;
    }
    if (_idleSince < 0)
      _idleSince = nowMs;
    if (nowMs - _idleSince < _holdMs)
      return false;
    if (nowMs - _lastActed < _cooldownMs)
      return false;
    _lastActed = nowMs;
    return true;
  }

  public void Reset() => _idleSince = -1;
}

/// <summary>Free-slot arithmetic for the "Space at a glance" section.</summary>
public static class SpaceMath
{
  /// <summary>A retainer's inventory: seven pages of 25.</summary>
  public const int RetainerCapacity = 175;

  public static int Free(int size, int used) => Math.Max(size - Math.Max(used, 0), 0);

  /// <summary>Low when free slots are at or under the threshold (0 = warn only when full). A negative threshold never warns.</summary>
  public static bool Low(int free, int threshold) => threshold >= 0 && free <= threshold;

  /// <summary>Days until a container fills at an intake rate (items per day); null when nothing is coming in.</summary>
  public static double? DaysUntilFull(int free, double perDay)
  {
    if (perDay <= 0)
      return null;
    return Math.Max(free, 0) / perDay;
  }

  /// <summary>"12 min ago" / "3 h ago" / "2 d ago" for a last-seen stamp; "never" for 0.</summary>
  public static string Age(long thenUnixMs, long nowUnixMs)
  {
    if (thenUnixMs <= 0)
      return "never";
    var s = Math.Max((nowUnixMs - thenUnixMs) / 1000, 0);
    if (s < 90) return "just now";
    if (s < 90 * 60) return $"{s / 60} min ago";
    if (s < 36 * 3600) return $"{s / 3600} h ago";
    return $"{s / 86400} d ago";
  }
}
