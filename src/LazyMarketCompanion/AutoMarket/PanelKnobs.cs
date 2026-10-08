using System;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (case 161).

/// <summary>
/// The Auto-Market panel's global knobs (0.2.8.11): one mutable snapshot of every value the
/// panel's settings section shows, and the single <see cref="Apply"/> that coerces one knob edit
/// into it. The panel window applies each changed knob to the SAME Configuration field the main
/// window's matching control writes (the write lines are copied from ConfigWindow) and saves once.
///
/// The contract the harness pins: an edit to ONE knob changes EXACTLY that knob's value - no knob
/// may write another knob's field - and every clamp matches the main window's (reserve slots
/// 0..19, gate freshness 1..168 hours, thresholds never negative, enum combos in range).
/// </summary>
public static class PanelKnobs
{
  public enum Knob
  {
    MasterEnabled,
    Source,
    PriceMode,
    PlaceholderPrice,
    ReserveSlots,
    RetainerFirst,
    PartialStacks,
    PinchAllAfter,
    PinchFallback,
    DuringAr,
    InSweep,
    SortMode,
    GateEnabled,
    GateThreshold,
    GateFreshness,
    RoutingMove,
    AutoAssignUnrouted,
    Markers,
    ChatMessages,
    DefaultAmount,
  }

  /// <summary>One snapshot of the panel's knob values, filled from Configuration each frame.</summary>
  public sealed class Values
  {
    public bool MasterEnabled;
    public int Source; // (int)StockSource
    public int PriceMode; // (int)NewListingPriceMode
    public int PlaceholderPrice;
    public int ReserveSlots;
    public bool RetainerFirst;
    public bool PartialStacks;
    public bool PinchAllAfter;
    public int PinchFallback; // (int)PinchFallbackMode
    public bool DuringAr;
    public bool InSweep;
    public int SortMode; // (int)MarketSortMode
    public bool GateEnabled;
    public int GateThreshold;
    public int GateFreshness;
    public bool RoutingMove;
    public bool AutoAssignUnrouted;
    public bool Markers;
    public bool ChatMessages;
    public int DefaultAmount;

    public Values Clone() => (Values)MemberwiseClone();
  }

  /// <summary>Apply a boolean knob edit. Returns true when the value changed (the window saves only on change).</summary>
  public static bool Apply(Knob knob, Values v, bool value)
  {
    var current = knob switch
    {
      Knob.MasterEnabled => v.MasterEnabled,
      Knob.RetainerFirst => v.RetainerFirst,
      Knob.PartialStacks => v.PartialStacks,
      Knob.PinchAllAfter => v.PinchAllAfter,
      Knob.DuringAr => v.DuringAr,
      Knob.InSweep => v.InSweep,
      Knob.GateEnabled => v.GateEnabled,
      Knob.RoutingMove => v.RoutingMove,
      Knob.AutoAssignUnrouted => v.AutoAssignUnrouted,
      Knob.Markers => v.Markers,
      Knob.ChatMessages => v.ChatMessages,
      _ => throw new ArgumentOutOfRangeException(nameof(knob), knob, "not a boolean knob"),
    };

    if (current == value)
      return false;

    switch (knob)
    {
      case Knob.MasterEnabled: v.MasterEnabled = value; break;
      case Knob.RetainerFirst: v.RetainerFirst = value; break;
      case Knob.PartialStacks: v.PartialStacks = value; break;
      case Knob.PinchAllAfter: v.PinchAllAfter = value; break;
      case Knob.DuringAr: v.DuringAr = value; break;
      case Knob.InSweep: v.InSweep = value; break;
      case Knob.GateEnabled: v.GateEnabled = value; break;
      case Knob.RoutingMove: v.RoutingMove = value; break;
      case Knob.AutoAssignUnrouted: v.AutoAssignUnrouted = value; break;
      case Knob.Markers: v.Markers = value; break;
      case Knob.ChatMessages: v.ChatMessages = value; break;
    }

    return true;
  }

  /// <summary>Apply an integer knob edit (combo index, count or threshold), clamped exactly like the main window's control. Returns true when the value changed.</summary>
  public static bool Apply(Knob knob, Values v, int value)
  {
    var clamped = knob switch
    {
      Knob.Source => Math.Clamp(value, 0, 2),
      Knob.PriceMode => Math.Clamp(value, 0, 1),
      Knob.PlaceholderPrice => Math.Max(value, 0),
      Knob.ReserveSlots => Math.Clamp(value, 0, 19),
      Knob.PinchFallback => Math.Clamp(value, 0, 2),
      Knob.SortMode => Math.Clamp(value, 0, 3),
      Knob.GateThreshold => Math.Max(value, 0),
      Knob.GateFreshness => Math.Clamp(value, 1, 168),
      Knob.DefaultAmount => Math.Max(value, 0),
      _ => throw new ArgumentOutOfRangeException(nameof(knob), knob, "not an integer knob"),
    };

    var current = knob switch
    {
      Knob.Source => v.Source,
      Knob.PriceMode => v.PriceMode,
      Knob.PlaceholderPrice => v.PlaceholderPrice,
      Knob.ReserveSlots => v.ReserveSlots,
      Knob.PinchFallback => v.PinchFallback,
      Knob.SortMode => v.SortMode,
      Knob.GateThreshold => v.GateThreshold,
      Knob.GateFreshness => v.GateFreshness,
      Knob.DefaultAmount => v.DefaultAmount,
      _ => throw new ArgumentOutOfRangeException(nameof(knob), knob, "not an integer knob"),
    };

    if (current == clamped)
      return false;

    switch (knob)
    {
      case Knob.Source: v.Source = clamped; break;
      case Knob.PriceMode: v.PriceMode = clamped; break;
      case Knob.PlaceholderPrice: v.PlaceholderPrice = clamped; break;
      case Knob.ReserveSlots: v.ReserveSlots = clamped; break;
      case Knob.PinchFallback: v.PinchFallback = clamped; break;
      case Knob.SortMode: v.SortMode = clamped; break;
      case Knob.GateThreshold: v.GateThreshold = clamped; break;
      case Knob.GateFreshness: v.GateFreshness = clamped; break;
      case Knob.DefaultAmount: v.DefaultAmount = clamped; break;
    }

    return true;
  }
}
