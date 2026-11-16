using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lalalazy.Telemetry;
using LalaUiKit;
using LazyMarketCompanion.AutoMarket;
using LazyMarketCompanion.Windows;

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

  private readonly InventoryService _s;
  private readonly TelemetryGuard _guard = LalaTelemetry.CreateGuard("inventory.tab", "Inventory tab");
  private string _retainer = string.Empty;

  public InventoryTab(InventoryService service) => _s = service;

  public void Draw()
  {
    if (!_guard.TryEnter())
    {
      MarketChrome.Notice("Failed · The Inventory tab stopped after repeated errors and will retry on its own (details in the plugin log).", UiState.Failed);
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
      MarketChrome.Notice("Running · " + _s.BatchStatus, UiState.Running);
      ImGui.Dummy(new Vector2(0, 4));
      if (MarketChrome.StopButton("Stop##invstop"))
        _s.Cancel();
      ImGui.Dummy(new Vector2(0, 8));
    }

    MarketChrome.Card("inventory_owners", () =>
    {
      MarketChrome.CardHeader("Who handles this stack", "Ownership and the existing AutoRetainer read-only status.");
      DrawOwners();
    });
    MarketChrome.Card("inventory_space", () =>
    {
      MarketChrome.CardHeader("Space at a glance", "Live and last-seen capacity with configurable low-space warnings.");
      DrawSpace();
    });
    MarketChrome.Card("inventory_venture", () =>
    {
      MarketChrome.CardHeader("Venture loot", "Preview the existing buckets; only the established vendor action can run.");
      DrawVentureLoot();
    });
    MarketChrome.Card("inventory_gear", () =>
    {
      MarketChrome.CardHeader("Gear to the Armoury Chest", "Manual batches, undo, and the off-by-default idle setting.");
      DrawGearMover();
    });
    MarketChrome.Card("inventory_log", () =>
    {
      MarketChrome.CardHeader("Actions log", "Recent inventory actions from the existing append-only audit.", $"{_s.RecentLines.Count} lines");
      DrawLog();
    });
  }

  /// <summary>Places the next button on the current line when it fits, otherwise on the next one.</summary>
  private static void FlowNext(ref bool first, float rightEdge, float width)
  {
    if (!first && ImGui.GetItemRectMax().X + 8 + width <= rightEdge)
      ImGui.SameLine(0, 8);
    first = false;
  }

  private static string Plural(int n, string one, string many) => n == 1 ? one : many;

  // =====================================================================================

  private void DrawOwners()
  {
    var c = Plugin.Configuration;
    var tip = c.InventoryOwnerTooltip;
    if (MarketChrome.Toggle("Show who handles a stack under the game's item tooltip", ref tip)) { c.InventoryOwnerTooltip = tip; c.Save(); }
    Tip("Hovering a stack in the bags, a retainer's inventory or the Armoury Chest adds a box listing every plugin that\r\n" +
        "moves, sells, discards or protects that item: LMC Auto-Market and category routing, AutoRetainer entrust plans,\r\n" +
        "vendor / discard / desynth / protect lists, and gearsets. \"Nothing handles this stack\" when none acts on it.");

    MarketChrome.RowsDone();
    ImGui.Dummy(new Vector2(0, 8));
    var ar = _s.Ar;
    if (ar.Available)
    {
      var im = ar.Im;
      MarketChrome.Secondary($"AutoRetainer (read from its config file): {ar.Plans.Count} entrust plan(s), {ar.PlanByRetainer.Count} assigned; " +
        $"vendor list {im.VendorHard.Count} (+{im.VendorSoft.Count} soft, auto-vendor {(im.AutoVendorEnabled ? "on" : "off")}), " +
        $"discard {im.Discard.Count}, desynth {im.Desynth.Count}, protect {im.Protect.Count}; venture log {(ar.RecordStats ? "on" : "OFF")}.");
    }
    else
    {
      var status = ar.Status.TrimEnd('.');
      var subject = status.StartsWith("AutoRetainer", StringComparison.Ordinal) ? status : $"AutoRetainer: {status}";
      MarketChrome.Notice($"Blocked · {subject}. Its lists are not shown, and venture loot is not sorted while this lasts.", UiState.Blocked);
    }
    ImGui.Dummy(new Vector2(0, 8));
    if (ImGui.Button("Re-read##arreread"))
      _s.RequestArRefresh();
  }

  // =====================================================================================

  private void DrawSpace()
  {
    var c = Plugin.Configuration;
    var low = c.InventoryLowSpaceSlots;
    if (MarketChrome.Field("Warn at this many free bag / retainer slots", width => MarketChrome.IntInput("##invlow", ref low, width)))
    { c.InventoryLowSpaceSlots = Math.Clamp(low, 0, 175); c.Save(); }
    var lowA = c.InventoryArmouryLowSlots;
    if (MarketChrome.Field("Warn at this many free slots on an Armoury page", width => MarketChrome.IntInput("##invlowa", ref lowA, width)))
    { c.InventoryArmouryLowSlots = Math.Clamp(lowA, 0, 50); c.Save(); }
    MarketChrome.RowsDone();
    ImGui.Dummy(new Vector2(0, 8));

    var retainers = _s.RetainerNames();
    var withIntake = retainers.Count > 0;
    var columns = withIntake ? 5 : 4;
    var avail = MarketChrome.Avail();
    var flags = avail < 760 ? ImGuiTableFlags.ScrollX : ImGuiTableFlags.None;
    if (!MarketChrome.BeginTable("##invspace", columns, flags, new Vector2(avail, 0)))
      return;
    ImGui.TableSetupColumn("Where", ImGuiTableColumnFlags.WidthStretch, 2.2f);
    ImGui.TableSetupColumn("Free", ImGuiTableColumnFlags.WidthFixed, 64);
    ImGui.TableSetupColumn("Used / size", ImGuiTableColumnFlags.WidthFixed, 110);
    ImGui.TableSetupColumn("Seen", ImGuiTableColumnFlags.WidthFixed, 190);
    if (withIntake)
      ImGui.TableSetupColumn("Venture intake", ImGuiTableColumnFlags.WidthStretch, 2.5f);
    if (withIntake)
      MarketChrome.TableHeader(("Where", false), ("Free", true), ("Used / size", true), ("Seen", false), ("Venture intake", false));
    else
      MarketChrome.TableHeader(("Where", false), ("Free", true), ("Used / size", true), ("Seen", false));

    var now = InventoryService.NowMs;
    var bagSize = 0;
    var bagUsed = 0;
    foreach (var t in new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 })
    {
      var (s, u) = _s.Container(t);
      bagSize += s;
      bagUsed += u;
    }
    Row("Bags", bagSize, bagUsed, "live", c.InventoryLowSpaceSlots, null, withIntake);
    foreach (var (slot, type) in InventoryService.ArmouryPages)
    {
      var (s, u) = _s.Container(type);
      Row("Armoury: " + ArmourySlots.Label(slot), s, u, "live", c.InventoryArmouryLowSlots, null, withIntake);
    }

    var ch = _s.Character;
    RowSeen("Saddlebag", ch?.Saddlebag, now, c.InventoryLowSpaceSlots, withIntake);
    if (ch?.PremiumSaddlebag != null)
      RowSeen("Saddlebag (premium)", ch.PremiumSaddlebag, now, c.InventoryLowSpaceSlots, withIntake);

    foreach (var name in retainers)
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
        Row("Retainer " + name, b.Capacity, b.Used, SpaceMath.Age(b.SeenUnixMs, now) + (b.Source == "bell" ? " (bell)" : string.Empty), c.InventoryLowSpaceSlots, intakeText, withIntake);
      }
      else
      {
        ImGui.TableNextRow();
        ImGui.TableNextColumn(); MarketChrome.CellText("Retainer " + name);
        ImGui.TableNextColumn(); MarketChrome.CellRight(Dash, Theme.TextSecondary);
        ImGui.TableNextColumn(); MarketChrome.CellRight(Dash, Theme.TextSecondary);
        ImGui.TableNextColumn(); MarketChrome.CellText("never (visit the bell)", Theme.TextSecondary);
        if (withIntake) { ImGui.TableNextColumn(); MarketChrome.CellText(intakeText); }
      }
    }
    ImGui.EndTable();
  }

  private const string Dash = "–";

  private static void Row(string label, int size, int used, string seen, int threshold, string? intake, bool withIntake)
  {
    ImGui.TableNextRow();
    ImGui.TableNextColumn();
    MarketChrome.CellText(label);
    if (size <= 0)
    {
      ImGui.TableNextColumn(); MarketChrome.CellRight(Dash, Theme.TextSecondary);
      ImGui.TableNextColumn(); MarketChrome.CellRight(Dash, Theme.TextSecondary);
      ImGui.TableNextColumn(); MarketChrome.CellText("not loaded", Theme.TextSecondary);
      if (withIntake) { ImGui.TableNextColumn(); MarketChrome.CellText(intake ?? string.Empty); }
      return;
    }
    var free = SpaceMath.Free(size, used);
    ImGui.TableNextColumn();
    MarketChrome.CellRight(free.ToString(), SpaceMath.Low(free, threshold) ? Theme.StateAmber : null);
    ImGui.TableNextColumn(); MarketChrome.CellRight($"{used} / {size}");
    ImGui.TableNextColumn(); MarketChrome.CellText(seen, Theme.TextSecondary);
    if (withIntake) { ImGui.TableNextColumn(); MarketChrome.CellText(intake ?? string.Empty); }
  }

  private static void RowSeen(string label, ContainerSeen? seen, long now, int threshold, bool withIntake)
  {
    if (seen == null)
    {
      ImGui.TableNextRow();
      ImGui.TableNextColumn(); MarketChrome.CellText(label);
      ImGui.TableNextColumn(); MarketChrome.CellRight(Dash, Theme.TextSecondary);
      ImGui.TableNextColumn(); MarketChrome.CellRight(Dash, Theme.TextSecondary);
      ImGui.TableNextColumn(); MarketChrome.CellText("open it once", Theme.TextSecondary);
      if (withIntake) ImGui.TableNextColumn();
      return;
    }
    Row(label, seen.Size, seen.Used, SpaceMath.Age(seen.SeenUnixMs, now), threshold, null, withIntake);
  }

  // =====================================================================================

  private void DrawVentureLoot()
  {
    var c = Plugin.Configuration;
    MarketChrome.Secondary("Venture loot = a stack in a retainer's own inventory that AutoRetainer's venture log for that retainer records as a " +
      "Quick Exploration reward within the look-back window, with no more of it in the retainer than those rewards delivered. " +
      "Marketable stock (grey marker) is never vendored or sorted - it is always KEEP. Auto-Market stock, AutoRetainer entrust-plan " +
      "items, gearset items and anything uncertain are always KEEP too. Only unmarketable loot is bucketed (GC delivery / desynth, preview only).");
    ImGui.Dummy(new Vector2(0, 8));

    var days = c.VentureLootLookbackDays;
    if (MarketChrome.Field("Look-back", width => MarketChrome.IntInput("##vllook", ref days, width), "days")) { c.VentureLootLookbackDays = Math.Clamp(days, 1, 365); c.Save(); }
    var hq = c.VentureLootAllowHq;
    if (MarketChrome.Toggle("Sort HQ venture loot##vlhq", ref hq)) { c.VentureLootAllowHq = hq; c.Save(); }
    Tip("Off (default): HQ venture loot is always kept.");
    MarketChrome.RowsDone();
    ImGui.Dummy(new Vector2(0, 8));

    var names = _s.RetainerNames();
    if (names.Count == 0)
    {
      MarketChrome.Notice("Empty · no retainers seen yet. Visit a summoning bell to populate inventory history.", UiState.Empty);
      return;
    }
    if (!names.Contains(_retainer))
      _retainer = names.FirstOrDefault(n => n == _s.SettledRetainer()) ?? names[0];

    // Summary across retainers. (No price check: marketable stock is never vendored, so nothing here needs a price.)
    var plans = names.ToDictionary(n => n, n => _s.VenturePreview(n, out _));
    if (MarketChrome.BeginTable("##vlsum", 6, ImGuiTableFlags.None, new Vector2(MarketChrome.Avail(), 0)))
    {
      ImGui.TableSetupColumn("Retainer", ImGuiTableColumnFlags.WidthStretch, 2);
      ImGui.TableSetupColumn("Vendor", ImGuiTableColumnFlags.WidthFixed, 80);
      ImGui.TableSetupColumn("GC", ImGuiTableColumnFlags.WidthFixed, 56);
      ImGui.TableSetupColumn("Desynth", ImGuiTableColumnFlags.WidthFixed, 84);
      ImGui.TableSetupColumn("Keep", ImGuiTableColumnFlags.WidthFixed, 64);
      ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 72);
      MarketChrome.TableHeader(("Retainer", false), ("Vendor", true), ("GC", true), ("Desynth", true), ("Keep", true), ("", false));
      foreach (var n in names)
      {
        var p = plans[n];
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0, 0.5f));
        if (ImGui.Selectable(n + "##vlsel" + n, n == _retainer, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, 24)))
          _retainer = n;
        ImGui.PopStyleVar();
        ImGui.TableNextColumn(); MarketChrome.CellRight(p.Count(LootBucket.Vendor).ToString());
        ImGui.TableNextColumn(); MarketChrome.CellRight(p.Count(LootBucket.GcDelivery).ToString());
        ImGui.TableNextColumn(); MarketChrome.CellRight(p.Count(LootBucket.Desynth).ToString());
        ImGui.TableNextColumn(); MarketChrome.CellRight(p.Count(LootBucket.Keep).ToString());
        ImGui.TableNextColumn(); MarketChrome.CellText(n == _s.SettledRetainer() ? "open" : string.Empty, Theme.TextSecondary);
      }
      ImGui.EndTable();
    }

    DrawRetainerLoot(_retainer);
  }

  private void DrawRetainerLoot(string retainer)
  {
    var plan = _s.VenturePreview(retainer, out var live);
    var seen = _s.Character is { } ch && ch.Retainers.TryGetValue(retainer, out var r) ? r.PagesSeenUnixMs : 0;
    ImGui.Dummy(new Vector2(0, 12));
    ImGui.TextUnformatted($"Retainer {retainer}: ");
    ImGui.SameLine(0, 0);
    MarketChrome.StateText(live ? "live (its inventory is open)" : $"last seen {SpaceMath.Age(seen, InventoryService.NowMs)} - open its inventory to act", live ? UiState.Ready : UiState.Idle);
    foreach (var note in plan.Notes)
      MarketChrome.Notice(note, UiState.Blocked);
    ImGui.Dummy(new Vector2(0, 4));

    // ONE button per bucket. VENDOR is the only live one; GC delivery and desynth hand off to the plugins
    // that already do them from the bags; KEEP is the absence of an action.
    var ops = VentureLoot.VendorOps(plan);
    var est = ops.Sum(o => o.EstGil);
    var blocker = ops.Count == 0 ? "nothing in the VENDOR bucket" : _s.VendorBlocker(retainer);
    var right = ImGui.GetCursorScreenPos().X + MarketChrome.Avail();
    var first = true;
    var vendorLabel = $"Vendor {ops.Count} {Plural(ops.Count, "stack", "stacks")} (~{est:N0} gil)";
    FlowNext(ref first, right, MarketChrome.ButtonWidth(vendorLabel));
    if (blocker != null) ImGui.BeginDisabled();
    if (ImGui.Button(vendorLabel + "##vlvendor"))
      _s.StartVendor(retainer, ops);
    if (blocker != null) ImGui.EndDisabled();
    TipAlways(blocker == null
      ? "Sells these stacks through this retainer (\"Have Retainer Sell Items\", the same call Auto-Market's value gate uses).\r\n" +
        "Each stack is re-read right before it is sold; the first failure stops the run. Every sale is written to the actions log.\r\n" +
        "Sold stacks can be bought back from the retainer until the retainer is dismissed."
      : "Not now: " + blocker + ".\r\nMarketable (grey-marker) stock is never vendored, and unmarketable stock cannot be priced, so this bucket stays empty.");

    var gcLabel = $"GC delivery: {plan.Count(LootBucket.GcDelivery)} (hand off)";
    FlowNext(ref first, right, MarketChrome.ButtonWidth(gcLabel));
    ImGui.BeginDisabled();
    ImGui.Button(gcLabel + "##vlgc");
    ImGui.EndDisabled();
    TipAlways("Preview only. Green gear goes to Grand Company expert delivery: move it to the bags and let AutoDuty's or\r\n" +
              "AutoRetainer's GC delivery turn it in. LMC does not deliver (that would need new, untested machinery).");

    var desynthLabel = $"Desynth: {plan.Count(LootBucket.Desynth)} (hand off)";
    FlowNext(ref first, right, MarketChrome.ButtonWidth(desynthLabel));
    ImGui.BeginDisabled();
    ImGui.Button(desynthLabel + "##vldesynth");
    ImGui.EndDisabled();
    TipAlways("Preview only. PandorasBox (Desynth All) or AutoRetainer desynthesis handle these from the bags.\r\n" +
              "LMC does not desynthesize (that would need new, untested machinery).");

    var keepLabel = $"Keep: {plan.Count(LootBucket.Keep)}";
    FlowNext(ref first, right, MarketChrome.ButtonWidth(keepLabel));
    MarketChrome.Tag(keepLabel);
    ImGui.Dummy(new Vector2(0, 8));

    if (plan.Rows.Count == 0)
    {
      MarketChrome.Notice("Empty · no venture loot in this retainer during the selected look-back window.", UiState.Empty);
      return;
    }

    if (!MarketChrome.BeginTable("##vlrows", 5, ImGuiTableFlags.None, new Vector2(MarketChrome.Avail(), 0)))
      return;
    ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2);
    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 56);
    ImGui.TableSetupColumn("Bucket", ImGuiTableColumnFlags.WidthFixed, 100);
    ImGui.TableSetupColumn("Why", ImGuiTableColumnFlags.WidthStretch, 4);
    ImGui.TableSetupColumn("Opt in", ImGuiTableColumnFlags.WidthFixed, 80);
    MarketChrome.TableHeader(("Item", false), ("Qty", true), ("Bucket", false), ("Why", false), ("Opt in", false));
    var optIns = Plugin.Configuration.VentureLootOptIns;
    foreach (var row in plan.Rows.OrderBy(x => x.Bucket).ThenBy(x => x.Stack.Container).ThenBy(x => x.Stack.Slot))
    {
      ImGui.PushID($"{row.Stack.Container}:{row.Stack.Slot}");
      ImGui.TableNextRow();
      ImGui.TableNextColumn();
      ImGui.AlignTextToFramePadding();
      MarketChrome.CellText(ItemNameResolver.GetItemName(row.Stack.ItemId) + (row.Stack.Hq ? " (HQ)" : string.Empty));
      Tip($"Item {row.Stack.ItemId}, {InventoryActionLine.Slot(row.Stack.Container, row.Stack.Slot)}, Quick Exploration delivered {row.Delivered} in the window");
      ImGui.TableNextColumn();
      ImGui.AlignTextToFramePadding();
      MarketChrome.CellRight(row.Stack.Quantity.ToString());
      ImGui.TableNextColumn();
      var (label, state) = row.Bucket switch
      {
        LootBucket.Vendor => ("VENDOR", UiState.Ready),
        LootBucket.GcDelivery => ("GC", UiState.Blocked),
        LootBucket.Desynth => ("DESYNTH", UiState.Blocked),
        _ => (row.IsLoot ? "KEEP" : "not loot", UiState.Idle),
      };
      ImGui.AlignTextToFramePadding();
      MarketChrome.StateText(label, state);
      ImGui.TableNextColumn();
      ImGui.AlignTextToFramePadding();
      ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
      ImGui.TextUnformatted(row.Reason + (row.Bucket == LootBucket.Vendor ? $" (~{row.EstGil:N0} gil)" : string.Empty));
      ImGui.PopTextWrapPos();
      ImGui.TableNextColumn();
      var opted = optIns.Contains(row.Stack.ItemId);
      if (row.OptInRail != null || opted)
      {
        var v = opted;
        if (MarketChrome.Toggle("##optin", ref v))
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
    MarketChrome.Secondary("Moves equippable gear from the bags into its Armoury Chest page. Never moves a stack LMC or AutoRetainer acts on " +
      "(Auto-Market list, category routing, entrust plans, vendor / discard / desynth lists), a gearset item, or into a full page. " +
      "Every move is logged and the last batch can be undone.");
    ImGui.Dummy(new Vector2(0, 8));

    var idle = c.GearMoverWhenIdle;
    if (MarketChrome.Toggle("Move gear on its own when everything is idle##gmidle", ref idle)) { c.GearMoverWhenIdle = idle; c.Save(); }
    Tip("Off by default. Idle = not in combat, a duty, crafting, gathering, a cutscene or an in-game window/event, and\r\n" +
        "AutoRetainer, AutoDuty, Artisan, GatherBuddy Reborn and LMC's own automation all idle, continuously for 20 s.\r\n" +
        $"At most {InventoryService.IdleMoveCap} pieces per pass, one pass a minute.");
    MarketChrome.RowsDone();
    ImGui.Dummy(new Vector2(0, 8));
    var verdict = _s.LastIdle;
    MarketChrome.Notice(verdict.Idle ? "Ready · idle conditions met" : "Idle · " + string.Join("; ", verdict.Why), verdict.Idle ? UiState.Ready : UiState.Idle);
    if (_s.IdleBackoffMinutesLeft > 0)
    {
      ImGui.Dummy(new Vector2(0, 8));
      MarketChrome.Notice($"Blocked · The last idle pass had a failed move; idle mode waits {_s.IdleBackoffMinutesLeft} more minute(s) (the actions log says why).", UiState.Blocked);
    }
    ImGui.Dummy(new Vector2(0, 8));

    var preview = _s.GearPreview(0);
    var blocker = preview.Ops.Count == 0 ? "no gear to move" : _s.MoveBlocker();
    var n = Math.Min(preview.Ops.Count, InventoryService.ManualMoveCap);
    var right = ImGui.GetCursorScreenPos().X + MarketChrome.Avail();
    var first = true;
    var moveLabel = $"Move {n} {Plural(n, "piece", "pieces")} to the Armoury";
    FlowNext(ref first, right, MarketChrome.ButtonWidth(moveLabel));
    if (blocker != null) ImGui.BeginDisabled();
    if (ImGui.Button(moveLabel + "##gmmove"))
      _s.StartGearMoves("manual");
    if (blocker != null) ImGui.EndDisabled();
    TipAlways(blocker == null ? $"Moves up to {InventoryService.ManualMoveCap} pieces, re-checking each one first." : "Not now: " + blocker);

    var last = _s.LastGearBatch;
    var undoBlocker = last.Count == 0 ? "nothing to undo" : _s.MoveBlocker();
    var undoLabel = $"Undo last batch ({last.Count})";
    FlowNext(ref first, right, MarketChrome.ButtonWidth(undoLabel));
    if (undoBlocker != null) ImGui.BeginDisabled();
    if (ImGui.Button(undoLabel + "##gmundo"))
      _s.StartUndo();
    if (undoBlocker != null) ImGui.EndDisabled();
    TipAlways(undoBlocker == null ? "Moves the last batch back to the bags (only pieces still sitting where they were put)." : "Not now: " + undoBlocker);
    ImGui.Dummy(new Vector2(0, 4));

    if (preview.Ops.Count > 0 && ImGui.TreeNode($"Would move ({preview.Ops.Count})##gmops"))
    {
      foreach (var op in preview.Ops)
        ImGui.TextUnformatted($"{ItemNameResolver.GetItemName(op.ItemId)}{(op.Hq ? " (HQ)" : string.Empty)}: {InventoryActionLine.Slot(op.SrcContainer, op.SrcSlot)} -> Armoury {ArmourySlots.Label(op.Target)}");
      ImGui.TreePop();
    }
    if (preview.Skipped.Count > 0 && ImGui.TreeNode($"Left in the bags ({preview.Skipped.Count})##gmskip"))
    {
      foreach (var sk in preview.Skipped)
        MarketChrome.Secondary($"{ItemNameResolver.GetItemName(sk.Stack.ItemId)}{(sk.Stack.Hq ? " (HQ)" : string.Empty)}: {sk.Reason}");
      ImGui.TreePop();
    }
  }

  // =====================================================================================

  private void DrawLog()
  {
    MarketChrome.Secondary("Append-only audit file: " + _s.ActionsPath);
    MarketChrome.Secondary("Every Inventory action is one IV| line there and in the plugin log.");
    var lines = _s.RecentLines;
    if (lines.Count == 0)
    {
      ImGui.Dummy(new Vector2(0, 4));
      MarketChrome.Notice("Empty · no inventory actions this session.", UiState.Empty);
      return;
    }
    ImGui.Dummy(new Vector2(0, 4));
    ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.ShellBar);
    ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 8));
    var open = ImGui.BeginChild("##invlog", new Vector2(MarketChrome.Avail(), 160), true);
    ImGui.PopStyleVar();
    ImGui.PopStyleColor(2);
    if (open)
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
