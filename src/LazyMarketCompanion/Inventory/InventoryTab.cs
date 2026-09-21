using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lalalazy.Telemetry;
using LazyMarketCompanion.AutoMarket;

namespace LazyMarketCompanion.Inventory;

/// <summary>
/// The "Inventory" tab of the LMC window. Four sections: who handles a stack (the hover box's switch and
/// AutoRetainer coupling status), space at a glance (read-only), the venture-loot sorter (preview; the VENDOR
/// bucket is the only live button, GC delivery and desynth are hand-off previews), and the bags -> Armoury
/// gear mover (button, undo, optional idle mode, default off). Drawing only - every decision is in
/// Inventory/Core and every action in <see cref="InventoryService"/>.
/// </summary>
internal sealed class InventoryTab
{
  private static readonly Vector4 Muted = new(0.7f, 0.7f, 0.7f, 1);
  private static readonly Vector4 Warn = new(1, 0.75f, 0.3f, 1);
  private static readonly Vector4 Bad = new(1, 0.45f, 0.4f, 1);
  private static readonly Vector4 Good = new(0.45f, 0.9f, 0.45f, 1);

  private readonly InventoryService _s;
  private readonly TelemetryGuard _guard = LalaTelemetry.CreateGuard("inventory.tab", "Inventory tab");
  private string _retainer = string.Empty;

  public InventoryTab(InventoryService service) => _s = service;

  public void Draw()
  {
    if (!_guard.TryEnter())
    {
      ImGui.TextColored(Bad, "The Inventory tab stopped after repeated errors and will retry on its own (details in the plugin log).");
      return;
    }
    try
    {
      DrawInner();
    }
    catch (Exception ex)
    {
      _guard.Failed(ex);
    }
  }

  private void DrawInner()
  {
    if (_s.BatchRunning)
    {
      ImGui.TextColored(Warn, "Running: " + _s.BatchStatus);
      ImGui.SameLine();
      if (ImGui.Button("Stop##invstop"))
        _s.Cancel();
      ImGui.Separator();
    }

    if (ImGui.CollapsingHeader("Who handles this stack", ImGuiTreeNodeFlags.DefaultOpen))
      DrawOwners();
    if (ImGui.CollapsingHeader("Space at a glance", ImGuiTreeNodeFlags.DefaultOpen))
      DrawSpace();
    if (ImGui.CollapsingHeader("Venture loot", ImGuiTreeNodeFlags.DefaultOpen))
      DrawVentureLoot();
    if (ImGui.CollapsingHeader("Gear to the Armoury Chest"))
      DrawGearMover();
    if (ImGui.CollapsingHeader("Actions log"))
      DrawLog();
  }

  // =====================================================================================

  private void DrawOwners()
  {
    var c = Plugin.Configuration;
    var tip = c.InventoryOwnerTooltip;
    if (ImGui.Checkbox("Show who handles a stack under the game's item tooltip", ref tip)) { c.InventoryOwnerTooltip = tip; c.Save(); }
    Tip("Hovering a stack in the bags, a retainer's inventory or the Armoury Chest adds a box listing every plugin that\r\n" +
        "moves, sells, discards or protects that item: LMC Auto-Market and category routing, AutoRetainer entrust plans,\r\n" +
        "vendor / discard / desynth / protect lists, and gearsets. \"Nothing handles this stack\" when none acts on it.");

    var ar = _s.Ar;
    if (ar.Available)
    {
      var im = ar.Im;
      ImGui.TextColored(Muted, $"AutoRetainer (read from its config file): {ar.Plans.Count} entrust plan(s), {ar.PlanByRetainer.Count} assigned; " +
        $"vendor list {im.VendorHard.Count} (+{im.VendorSoft.Count} soft, auto-vendor {(im.AutoVendorEnabled ? "on" : "off")}), " +
        $"discard {im.Discard.Count}, desynth {im.Desynth.Count}, protect {im.Protect.Count}; venture log {(ar.RecordStats ? "on" : "OFF")}.");
    }
    else
    {
      ImGui.TextColored(Warn, $"AutoRetainer: {ar.Status}. Its lists are not shown, and venture loot is not sorted while this lasts.");
    }
    ImGui.SameLine();
    if (ImGui.SmallButton("Re-read##arreread"))
      _s.RequestArRefresh();
  }

  // =====================================================================================

  private void DrawSpace()
  {
    var c = Plugin.Configuration;
    ImGui.SetNextItemWidth(90);
    var low = c.InventoryLowSpaceSlots;
    if (ImGui.InputInt("Warn at this many free bag / retainer slots##invlow", ref low)) { c.InventoryLowSpaceSlots = Math.Clamp(low, 0, 175); c.Save(); }
    ImGui.SetNextItemWidth(90);
    var lowA = c.InventoryArmouryLowSlots;
    if (ImGui.InputInt("Warn at this many free slots on an Armoury page##invlowa", ref lowA)) { c.InventoryArmouryLowSlots = Math.Clamp(lowA, 0, 50); c.Save(); }

    if (!ImGui.BeginTable("##invspace", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
      return;
    ImGui.TableSetupColumn("Where", ImGuiTableColumnFlags.WidthStretch, 2.2f);
    ImGui.TableSetupColumn("Free", ImGuiTableColumnFlags.WidthFixed, 50);
    ImGui.TableSetupColumn("Used / size", ImGuiTableColumnFlags.WidthFixed, 90);
    ImGui.TableSetupColumn("Seen", ImGuiTableColumnFlags.WidthFixed, 110);
    ImGui.TableSetupColumn("Venture intake", ImGuiTableColumnFlags.WidthStretch, 2.5f);
    ImGui.TableHeadersRow();

    var now = InventoryService.NowMs;
    var bagSize = 0;
    var bagUsed = 0;
    foreach (var t in new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 })
    {
      var (s, u) = _s.Container(t);
      bagSize += s;
      bagUsed += u;
    }
    Row("Bags", bagSize, bagUsed, "live", c.InventoryLowSpaceSlots, null);
    foreach (var (slot, type) in InventoryService.ArmouryPages)
    {
      var (s, u) = _s.Container(type);
      Row("Armoury: " + ArmourySlots.Label(slot), s, u, "live", c.InventoryArmouryLowSlots, null);
    }

    var ch = _s.Character;
    RowSeen("Saddlebag", ch?.Saddlebag, now, c.InventoryLowSpaceSlots);
    if (ch?.PremiumSaddlebag != null)
      RowSeen("Saddlebag (premium)", ch.PremiumSaddlebag, now, c.InventoryLowSpaceSlots);

    foreach (var name in _s.RetainerNames())
    {
      var seen = ch != null && ch.Retainers.TryGetValue(name, out var r) ? r : null;
      var best = seen == null ? null : InventoryState.BestSpace(seen);
      var intake = ArVentureStats.Summarize(_s.VentureRecords(name), now / 1000);
      var intakeText = intake.PerDay7d <= 0 && intake.Last24h == 0
        ? "-"
        : $"{intake.PerDay7d:0.#}/day (7 d), {intake.Last24h} in 24 h";
      if (best is { } b)
      {
        var free = SpaceMath.Free(b.Capacity, b.Used);
        var days = SpaceMath.DaysUntilFull(free, intake.PerDay7d);
        if (days is { } d)
          intakeText += $" - full in ~{d:0.#} d";
        Row("Retainer " + name, b.Capacity, b.Used, SpaceMath.Age(b.SeenUnixMs, now) + (b.Source == "bell" ? " (bell)" : string.Empty), c.InventoryLowSpaceSlots, intakeText);
      }
      else
      {
        ImGui.TableNextRow();
        ImGui.TableNextColumn(); ImGui.TextUnformatted("Retainer " + name);
        ImGui.TableNextColumn(); ImGui.TextColored(Muted, "?");
        ImGui.TableNextColumn(); ImGui.TextColored(Muted, "-");
        ImGui.TableNextColumn(); ImGui.TextColored(Muted, "never (visit the bell)");
        ImGui.TableNextColumn(); ImGui.TextUnformatted(intakeText);
      }
    }
    ImGui.EndTable();
  }

  private static void Row(string label, int size, int used, string seen, int threshold, string? intake)
  {
    ImGui.TableNextRow();
    ImGui.TableNextColumn();
    ImGui.TextUnformatted(label);
    ImGui.TableNextColumn();
    if (size <= 0)
    {
      ImGui.TextColored(Muted, "?");
      ImGui.TableNextColumn(); ImGui.TextColored(Muted, "not loaded");
      ImGui.TableNextColumn(); ImGui.TextColored(Muted, seen);
      ImGui.TableNextColumn(); ImGui.TextUnformatted(intake ?? string.Empty);
      return;
    }
    var free = SpaceMath.Free(size, used);
    if (SpaceMath.Low(free, threshold)) ImGui.TextColored(free == 0 ? Bad : Warn, free.ToString());
    else ImGui.TextUnformatted(free.ToString());
    ImGui.TableNextColumn(); ImGui.TextUnformatted($"{used} / {size}");
    ImGui.TableNextColumn(); ImGui.TextColored(Muted, seen);
    ImGui.TableNextColumn(); ImGui.TextUnformatted(intake ?? string.Empty);
  }

  private static void RowSeen(string label, ContainerSeen? seen, long now, int threshold)
  {
    if (seen == null)
    {
      ImGui.TableNextRow();
      ImGui.TableNextColumn(); ImGui.TextUnformatted(label);
      ImGui.TableNextColumn(); ImGui.TextColored(Muted, "?");
      ImGui.TableNextColumn(); ImGui.TextColored(Muted, "-");
      ImGui.TableNextColumn(); ImGui.TextColored(Muted, "open it once");
      ImGui.TableNextColumn();
      return;
    }
    Row(label, seen.Size, seen.Used, SpaceMath.Age(seen.SeenUnixMs, now), threshold, null);
  }

  // =====================================================================================

  private void DrawVentureLoot()
  {
    var c = Plugin.Configuration;
    ImGui.TextWrapped("Venture loot = a stack in a retainer's own inventory that AutoRetainer's venture log for that retainer records as a " +
      "Quick Exploration reward within the look-back window, with no more of it in the retainer than those rewards delivered. " +
      "Market stock, AutoRetainer entrust-plan items, gearset items and anything uncertain are always KEEP.");

    ImGui.SetNextItemWidth(90);
    var days = c.VentureLootLookbackDays;
    if (ImGui.InputInt("Look-back (days)##vllook", ref days)) { c.VentureLootLookbackDays = Math.Clamp(days, 1, 365); c.Save(); }
    ImGui.SameLine(0, 24);
    var hq = c.VentureLootAllowHq;
    if (ImGui.Checkbox("Sort HQ venture loot##vlhq", ref hq)) { c.VentureLootAllowHq = hq; c.Save(); }
    Tip("Off (default): HQ venture loot is always kept.");

    if (c.AutoMarketValueGateEnabled && c.AutoMarketValueGateThresholdGil > 0)
      ImGui.TextColored(Muted, $"Worth-listing check: the Auto-Market value gate ({c.AutoMarketValueGateThresholdGil:N0} gil net, data under {c.AutoMarketGateFreshnessHours} h old). " +
        "Only stock it proves is at or under that is ever vendored.");
    else
      ImGui.TextColored(Warn, "The Auto-Market value gate is off (Auto-Market tab): nothing marketable is vendored - uncertainty never vendors.");

    var names = _s.RetainerNames();
    if (names.Count == 0)
    {
      ImGui.TextColored(Muted, "No retainers seen yet - visit a summoning bell.");
      return;
    }
    if (!names.Contains(_retainer))
      _retainer = names.FirstOrDefault(n => n == _s.SettledRetainer()) ?? names[0];

    // Summary across retainers + the price check.
    var plans = names.ToDictionary(n => n, n => _s.VenturePreview(n, out _));
    if (ImGui.Button("Check prices##vlprices"))
    {
      var ids = plans.Values.SelectMany(p => p.Rows).Where(r => r.IsLoot && _s.Facts(r.Stack.ItemId) is { Marketable: true })
        .Select(r => r.Stack.ItemId).ToHashSet();
      _s.CheckPrices(ids);
    }
    Tip("Asks Universalis about every marketable venture-loot item (the same request the Auto-Market value gate makes).\r\n" +
        "Nothing is vendored from a check older than 30 minutes.");
    ImGui.SameLine();
    var age = _s.QuotesFetchedMs > 0 ? SpaceMath.Age(_s.QuotesFetchedMs, InventoryService.NowMs) : "never";
    ImGui.TextColored(Muted, $"{_s.QuotesStatus} (checked {age})");

    if (ImGui.BeginTable("##vlsum", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
    {
      ImGui.TableSetupColumn("Retainer", ImGuiTableColumnFlags.WidthStretch, 2);
      ImGui.TableSetupColumn("Vendor", ImGuiTableColumnFlags.WidthFixed, 60);
      ImGui.TableSetupColumn("GC", ImGuiTableColumnFlags.WidthFixed, 40);
      ImGui.TableSetupColumn("Desynth", ImGuiTableColumnFlags.WidthFixed, 60);
      ImGui.TableSetupColumn("Keep", ImGuiTableColumnFlags.WidthFixed, 45);
      ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 60);
      ImGui.TableHeadersRow();
      foreach (var n in names)
      {
        var p = plans[n];
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (ImGui.Selectable(n + "##vlsel" + n, n == _retainer, ImGuiSelectableFlags.SpanAllColumns))
          _retainer = n;
        ImGui.TableNextColumn(); ImGui.TextUnformatted(p.Count(LootBucket.Vendor).ToString());
        ImGui.TableNextColumn(); ImGui.TextUnformatted(p.Count(LootBucket.GcDelivery).ToString());
        ImGui.TableNextColumn(); ImGui.TextUnformatted(p.Count(LootBucket.Desynth).ToString());
        ImGui.TableNextColumn(); ImGui.TextUnformatted(p.Count(LootBucket.Keep).ToString());
        ImGui.TableNextColumn(); ImGui.TextColored(Muted, n == _s.SettledRetainer() ? "open" : string.Empty);
      }
      ImGui.EndTable();
    }

    DrawRetainerLoot(_retainer);
  }

  private void DrawRetainerLoot(string retainer)
  {
    var plan = _s.VenturePreview(retainer, out var live);
    var seen = _s.Character is { } ch && ch.Retainers.TryGetValue(retainer, out var r) ? r.PagesSeenUnixMs : 0;
    ImGui.Spacing();
    ImGui.TextUnformatted($"Retainer {retainer}: ");
    ImGui.SameLine(0, 0);
    ImGui.TextColored(live ? Good : Muted, live ? "live (its inventory is open)" : $"last seen {SpaceMath.Age(seen, InventoryService.NowMs)} - open its inventory to act");
    foreach (var note in plan.Notes)
      ImGui.TextColored(Warn, note);

    // ONE button per bucket. VENDOR is the only live one; GC delivery and desynth hand off to the plugins
    // that already do them from the bags; KEEP is the absence of an action.
    var ops = VentureLoot.VendorOps(plan);
    var est = ops.Sum(o => o.EstGil);
    var blocker = ops.Count == 0 ? "nothing in the VENDOR bucket" : _s.VendorBlocker(retainer);
    if (blocker != null) ImGui.BeginDisabled();
    if (ImGui.Button($"Vendor {ops.Count} stack(s) (~{est:N0} gil)##vlvendor"))
      _s.StartVendor(retainer, ops);
    if (blocker != null) ImGui.EndDisabled();
    TipAlways(blocker == null
      ? "Sells these stacks through this retainer (\"Have Retainer Sell Items\", the same call Auto-Market's value gate uses).\r\n" +
        "Each stack is re-read right before it is sold; the first failure stops the run. Every sale is written to the actions log.\r\n" +
        "Sold stacks can be bought back from the retainer until the retainer is dismissed."
      : "Not now: " + blocker);

    ImGui.SameLine();
    ImGui.BeginDisabled();
    ImGui.Button($"GC delivery: {plan.Count(LootBucket.GcDelivery)} (hand off)##vlgc");
    ImGui.EndDisabled();
    TipAlways("Preview only. Green gear goes to Grand Company expert delivery: move it to the bags and let AutoDuty's or\r\n" +
              "AutoRetainer's GC delivery turn it in. LMC does not deliver (that would need new, untested machinery).");
    ImGui.SameLine();
    ImGui.BeginDisabled();
    ImGui.Button($"Desynth: {plan.Count(LootBucket.Desynth)} (hand off)##vldesynth");
    ImGui.EndDisabled();
    TipAlways("Preview only. PandorasBox (Desynth All) or AutoRetainer desynthesis handle these from the bags.\r\n" +
              "LMC does not desynthesize (that would need new, untested machinery).");
    ImGui.SameLine();
    ImGui.TextColored(Muted, $"Keep: {plan.Count(LootBucket.Keep)}");

    if (plan.Rows.Count == 0)
    {
      ImGui.TextColored(Muted, "No venture loot in this retainer.");
      return;
    }

    if (!ImGui.BeginTable("##vlrows", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY, new Vector2(-1, 260)))
      return;
    ImGui.TableSetupScrollFreeze(0, 1);
    ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2);
    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 40);
    ImGui.TableSetupColumn("Bucket", ImGuiTableColumnFlags.WidthFixed, 80);
    ImGui.TableSetupColumn("Why", ImGuiTableColumnFlags.WidthStretch, 4);
    ImGui.TableSetupColumn("Opt in", ImGuiTableColumnFlags.WidthFixed, 50);
    ImGui.TableHeadersRow();
    var optIns = Plugin.Configuration.VentureLootOptIns;
    foreach (var row in plan.Rows.OrderBy(x => x.Bucket).ThenBy(x => x.Stack.Container).ThenBy(x => x.Stack.Slot))
    {
      ImGui.PushID($"{row.Stack.Container}:{row.Stack.Slot}");
      ImGui.TableNextRow();
      ImGui.TableNextColumn();
      ImGui.TextUnformatted(ItemNameResolver.GetItemName(row.Stack.ItemId) + (row.Stack.Hq ? " (HQ)" : string.Empty));
      Tip($"Item {row.Stack.ItemId}, {InventoryActionLine.Slot(row.Stack.Container, row.Stack.Slot)}, Quick Exploration delivered {row.Delivered} in the window");
      ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Stack.Quantity.ToString());
      ImGui.TableNextColumn();
      var (label, color) = row.Bucket switch
      {
        LootBucket.Vendor => ("VENDOR", Good),
        LootBucket.GcDelivery => ("GC", Warn),
        LootBucket.Desynth => ("DESYNTH", Warn),
        _ => (row.IsLoot ? "KEEP" : "not loot", Muted),
      };
      ImGui.TextColored(color, label);
      ImGui.TableNextColumn();
      ImGui.TextWrapped(row.Reason + (row.Bucket == LootBucket.Vendor ? $" (~{row.EstGil:N0} gil)" : string.Empty));
      ImGui.TableNextColumn();
      var opted = optIns.Contains(row.Stack.ItemId);
      if (row.OptInRail != null || opted)
      {
        var v = opted;
        if (ImGui.Checkbox("##optin", ref v))
          _s.SetOptIn(row.Stack.ItemId, v, row.OptInRail ?? "opted");
        Tip("Allow this item past the unique / untradable / rare rail. Every other rail still applies.");
      }
      ImGui.PopID();
    }
    ImGui.EndTable();
  }

  // =====================================================================================

  private void DrawGearMover()
  {
    var c = Plugin.Configuration;
    ImGui.TextWrapped("Moves equippable gear from the bags into its Armoury Chest page. Never moves a stack LMC or AutoRetainer acts on " +
      "(Auto-Market list, category routing, entrust plans, vendor / discard / desynth lists), a gearset item, or into a full page. " +
      "Every move is logged and the last batch can be undone.");

    var idle = c.GearMoverWhenIdle;
    if (ImGui.Checkbox("Move gear on its own when everything is idle##gmidle", ref idle)) { c.GearMoverWhenIdle = idle; c.Save(); }
    Tip("Off by default. Idle = not in combat, a duty, crafting, gathering, a cutscene or an in-game window/event, and\r\n" +
        "AutoRetainer, AutoDuty, Artisan, GatherBuddy Reborn and LMC's own automation all idle, continuously for 20 s.\r\n" +
        $"At most {InventoryService.IdleMoveCap} pieces per pass, one pass a minute.");
    var verdict = _s.LastIdle;
    ImGui.SameLine();
    ImGui.TextColored(verdict.Idle ? Good : Muted, verdict.Idle ? "idle now" : "not idle: " + string.Join("; ", verdict.Why));
    if (_s.IdleBackoffMinutesLeft > 0)
      ImGui.TextColored(Warn, $"The last idle pass had a failed move; idle mode waits {_s.IdleBackoffMinutesLeft} more minute(s) (the actions log says why).");

    var preview = _s.GearPreview(0);
    var blocker = preview.Ops.Count == 0 ? "no gear to move" : _s.MoveBlocker();
    var n = Math.Min(preview.Ops.Count, InventoryService.ManualMoveCap);
    if (blocker != null) ImGui.BeginDisabled();
    if (ImGui.Button($"Move {n} piece(s) to the Armoury##gmmove"))
      _s.StartGearMoves("manual");
    if (blocker != null) ImGui.EndDisabled();
    TipAlways(blocker == null ? $"Moves up to {InventoryService.ManualMoveCap} pieces, re-checking each one first." : "Not now: " + blocker);

    ImGui.SameLine();
    var last = _s.LastGearBatch;
    var undoBlocker = last.Count == 0 ? "nothing to undo" : _s.MoveBlocker();
    if (undoBlocker != null) ImGui.BeginDisabled();
    if (ImGui.Button($"Undo last batch ({last.Count})##gmundo"))
      _s.StartUndo();
    if (undoBlocker != null) ImGui.EndDisabled();
    TipAlways(undoBlocker == null ? "Moves the last batch back to the bags (only pieces still sitting where they were put)." : "Not now: " + undoBlocker);

    if (preview.Ops.Count > 0 && ImGui.TreeNode($"Would move ({preview.Ops.Count})##gmops"))
    {
      foreach (var op in preview.Ops)
        ImGui.TextUnformatted($"{ItemNameResolver.GetItemName(op.ItemId)}{(op.Hq ? " (HQ)" : string.Empty)}: {InventoryActionLine.Slot(op.SrcContainer, op.SrcSlot)} -> Armoury {ArmourySlots.Label(op.Target)}");
      ImGui.TreePop();
    }
    if (preview.Skipped.Count > 0 && ImGui.TreeNode($"Left in the bags ({preview.Skipped.Count})##gmskip"))
    {
      foreach (var sk in preview.Skipped)
        ImGui.TextColored(Muted, $"{ItemNameResolver.GetItemName(sk.Stack.ItemId)}{(sk.Stack.Hq ? " (HQ)" : string.Empty)}: {sk.Reason}");
      ImGui.TreePop();
    }
  }

  // =====================================================================================

  private void DrawLog()
  {
    ImGui.TextColored(Muted, "Append-only audit file: " + _s.ActionsPath);
    ImGui.TextColored(Muted, "Every Inventory action is one IV| line there and in the plugin log.");
    var lines = _s.RecentLines;
    if (lines.Count == 0)
    {
      ImGui.TextColored(Muted, "Nothing this session.");
      return;
    }
    if (ImGui.BeginChild("##invlog", new Vector2(-1, 160), true))
    {
      for (var i = lines.Count - 1; i >= 0; i--)
        ImGui.TextWrapped(lines[i]);
    }
    ImGui.EndChild();
  }

  private static void Tip(string text)
  {
    if (ImGui.IsItemHovered())
      ImGui.SetTooltip(text);
  }

  /// <summary>Tooltip that also shows on a disabled item (why a button is greyed out matters most).</summary>
  private static void TipAlways(string text)
  {
    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
      ImGui.SetTooltip(text);
  }
}
