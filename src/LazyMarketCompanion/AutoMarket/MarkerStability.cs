using System;
using System.Collections.Generic;

namespace LazyMarketCompanion.AutoMarket;

/// <summary>
/// The display-order stability gate behind the Auto-Market marker dots (0.2.8.6).
///
/// The dots follow the game's own display-order read (SlotOrder over the InventorySorter
/// entries). The 0.2.8.5 probe caught that read CHURNING: for five consecutive frames the same
/// grid resolved a different slot-to-container map each frame (item ids migrating between
/// grids) while the screen anchors never moved - the game was mid-rewrite of the very
/// structures both it and this plugin read. Drawing on every frame therefore painted the
/// verdict of whatever the read happened to say that frame, which is exactly the
/// "dots are not staying" report.
///
/// Fail-closed, same doctrine as the origin and screen gates: a grid draws only once its
/// resolved map has been IDENTICAL for <see cref="FramesRequired"/> consecutive frames. A real
/// sort blinks the dots off for its duration and brings them back settled; a churn episode
/// keeps them off until the read settles. The episodes are logged (bounded) so a log can tell
/// "the mapping churned and the dots held" apart from "the dots vanished".
///
/// Pure logic, no Dalamud types - exercised by harness case 160d.
/// </summary>
public static class MarkerStability
{
  /// <summary>Consecutive identical maps required before a grid draws again (3 frames ~ 50 ms).</summary>
  public const int FramesRequired = 3;

  /// <summary>Churn episodes logged per (addon, container) per session; past this the gate still runs but stays silent.</summary>
  public const int EpisodeCap = 8;

  /// <summary>Frames into one still-open episode that trigger the one "still churning" line.</summary>
  public const int StillChurningFrame = 120;

  /// <summary>Per (addon, container) gate state.</summary>
  public sealed class State
  {
    public string? LastSignature;
    public int StableFrames;
    public int SuppressedFrames;
    public bool EpisodeOpen;     // an actual churn episode is running (a settled state was interrupted)
    public int Episodes;
    public bool HasDrawn;
    public bool StillChurningLogged;
  }

  /// <summary>One frame's gate decision. EpisodeOpened/EpisodeClosed are the log triggers.</summary>
  public sealed class Decision
  {
    public bool Draw;
    public bool EpisodeOpened;
    public bool EpisodeClosed;
    public int EpisodeFrames;
    public bool StillChurning;
  }

  /// <summary>
  /// Advance the gate one frame for one (addon, container). <paramref name="signature"/> is the
  /// resolved map's compact text (<see cref="MapSignature"/>); identical text frame after frame
  /// is a stable read, and draws are allowed only once the map has been identical for
  /// <see cref="FramesRequired"/> consecutive frames. An episode opens when a change interrupts
  /// a state that was already drawing (startup is not churn) and closes when drawing resumes.
  /// </summary>
  public static Decision Evaluate(State state, string signature)
  {
    var wasDrawn = state.HasDrawn;
    var d = new Decision();
    if (string.Equals(state.LastSignature, signature, StringComparison.Ordinal))
      state.StableFrames++;
    else
    {
      state.LastSignature = signature;
      state.StableFrames = 1;
    }

    if (state.StableFrames >= FramesRequired)
    {
      state.HasDrawn = true;
      d.Draw = true;
      if (state.EpisodeOpen)
      {
        // The read settled: this frame draws again and the episode closes with its count.
        d.EpisodeClosed = true;
        d.EpisodeFrames = state.SuppressedFrames;
        state.EpisodeOpen = false;
        state.SuppressedFrames = 0;
        state.StillChurningLogged = false;
      }
      else
        state.SuppressedFrames = 0; // startup hold: not an episode, no close line, just reset
    }
    else
    {
      if (state.SuppressedFrames == 0 && wasDrawn)
      {
        d.EpisodeOpened = true; // a settled state was interrupted by a change - churn, not startup
        state.EpisodeOpen = true;
      }
      state.SuppressedFrames++;
      if (!state.StillChurningLogged && state.SuppressedFrames >= StillChurningFrame)
      {
        d.StillChurning = true;
        state.StillChurningLogged = true;
      }
    }
    return d;
  }

  /// <summary>
  /// Whether an episode line may still be logged for this (addon, container): the first
  /// <see cref="EpisodeCap"/> episodes only. <see cref="Evaluate"/> never logs and never counts
  /// episodes - the caller logs and advances <see cref="State.Episodes"/> when it actually emits
  /// the open line. Past the cap the gate keeps running; only the lines stop.
  /// </summary>
  public static bool MayLogEpisode(State state) => state.Episodes < EpisodeCap;

  /// <summary>
  /// The map's compact signature: "s0:page.slot;s1:page.slot;..." in display-slot order. Two
  /// frames read the same layout exactly when this text is equal. Item ids are deliberately NOT
  /// part of it: the churn's signature is the slot-to-container mapping, and a stack moving
  /// between containers (AR deposit) is a real mapping change, while a quantity change inside
  /// one cell must not be mistaken for one.
  /// </summary>
  public static string MapSignature(IReadOnlyDictionary<int, SlotOrder.Cell> map)
  {
    var sb = new System.Text.StringBuilder(map.Count * 10);
    foreach (var kv in Ordered(map))
      sb.Append(kv.Key).Append(':').Append(kv.Value.BagIndex).Append('.').Append(kv.Value.ContainerSlot).Append(';');
    return sb.ToString();
  }

  /// <summary>The first <paramref name="maxCells"/> cells of a signature, for a bounded log line.</summary>
  public static string SignatureHead(string signature, int maxCells = 6)
  {
    var parts = signature.Split(';', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length <= maxCells)
      return signature;
    return string.Join(";", parts, 0, maxCells) + ";...";
  }

  private static IEnumerable<KeyValuePair<int, SlotOrder.Cell>> Ordered(IReadOnlyDictionary<int, SlotOrder.Cell> map)
  {
    var keys = new List<int>(map.Keys);
    keys.Sort();
    foreach (var k in keys)
      yield return new(k, map[k]);
  }
}
