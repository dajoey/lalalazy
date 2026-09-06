using System;
using System.Collections.Generic;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// Turns the text a UI element is DISPLAYING into an item id - and, which is the entire point of this
/// class, answers 0 ("I cannot tell") instead of a confident wrong answer.
///
/// WHY IT EXISTS. Until 0.1.6.0 this logic lived inline in <c>ItemNameResolver</c> and failed OPEN: after an
/// exact match missed, it returned the LONGEST item name contained anywhere in the row's text. On a row whose
/// text is clipped by a narrow column that is catastrophic, because a clipped name still contains a shorter
/// real item name. On Joey's client on 2026-09-05 at 20:37:48 a row holding
/// <c>Snow Cotton Ushanka of Scouting</c> (41878) was read as <c>Snow Cotton</c> (44024) - both real,
/// distinct, marketable items, one a strict prefix of the other - and that phantom identification vetoed an
/// Auto-Market pass that had otherwise identified its listing correctly.
///
/// THE RULE: a name is only accepted when nothing else could explain the text just as well.
///
///   1. An exact match on the cleaned visible text wins, as before. It is the strongest signal there is.
///   2. Otherwise the longest contained name is a candidate, but it is DISCARDED when
///      a) another, different item of the same name length is also contained (a genuine tie), or
///      b) some item name is a strict extension of the visible text - i.e. the text is equally consistent
///         with a clipped rendering of that longer item. This is the truncation signature.
///   3. Whatever survives is discarded anyway when the caller says which item the game's own container
///      holds there and the answer is a strict prefix of THAT item's name. A shorter name contained in the
///      longer name of the item that is provably in the slot is a clipped rendering, never a sighting of a
///      different item.
///
/// Rule 3 is what closes the hole rule 2 cannot: if a column happens to clip exactly at a word boundary the
/// clipped text can be an EXACT match for a shorter item, and no amount of reasoning about the text alone can
/// tell that apart from genuinely seeing that shorter item.
///
/// Returning 0 is safe by construction: every caller already treats 0 as "leave this row alone".
/// </summary>
public static class ItemNameMatch
{
  /// <summary>The answer when the text cannot be pinned to exactly one item.</summary>
  public const uint Unknown = 0;

  /// <summary>
  /// Resolve displayed text to an item id, or <see cref="Unknown"/>.
  /// </summary>
  /// <param name="cleanedText">The visible text with decoration stripped (see <c>ItemNameResolver.NormalizeItemName</c>).</param>
  /// <param name="haystack">The text to search for a contained item name - usually the raw, undecorated node text.</param>
  /// <param name="catalogue">Every (id, name) pair to consider. Enumerated exactly once.</param>
  /// <param name="expectedItemId">
  /// What the game's own container says is in this place, or 0 when the caller does not know. Only used to
  /// recognise a clipped rendering of that item; a mismatch that is NOT a prefix is still reported as the
  /// other item, because that is a real disagreement the caller must see.
  /// </param>
  public static uint Resolve(
    string? cleanedText,
    string? haystack,
    IEnumerable<(uint Id, string Name)> catalogue,
    uint expectedItemId = 0)
  {
    if (catalogue == null)
      return Unknown;

    var text = cleanedText ?? string.Empty;
    var hay = haystack ?? string.Empty;
    var stem = TruncationStem(text);

    uint exact = Unknown;
    var exactName = string.Empty;
    uint best = Unknown;
    var bestName = string.Empty;
    var bestIsTied = false;
    var couldBeTruncated = false;
    var expectedName = string.Empty;

    foreach (var (id, name) in catalogue)
    {
      if (id == 0 || string.IsNullOrEmpty(name))
        continue;

      if (id == expectedItemId)
        expectedName = name;

      if (exact == Unknown && name.Equals(text, StringComparison.OrdinalIgnoreCase))
      {
        exact = id;
        exactName = name;
      }

      if (hay.Length > 0 && hay.Contains(name, StringComparison.OrdinalIgnoreCase))
      {
        if (name.Length > bestName.Length)
        {
          best = id;
          bestName = name;
          bestIsTied = false;
        }
        else if (name.Length == bestName.Length && id != best)
        {
          bestIsTied = true;
        }
      }

      // The text could be this item's name with its tail cut off. One such item is enough to make the
      // reading ambiguous - we stop asking, but keep enumerating because the exact match may still be ahead.
      if (!couldBeTruncated && stem.Length > 0 && name.Length > stem.Length
          && name.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
        couldBeTruncated = true;
    }

    uint result;
    string resultName;
    if (exact != Unknown)
    {
      result = exact;
      resultName = exactName;
    }
    else if (best == Unknown || bestIsTied || couldBeTruncated)
    {
      return Unknown;
    }
    else
    {
      result = best;
      resultName = bestName;
    }

    // The container is ground truth for what is in the slot. If our answer is that item's name with the tail
    // missing, we read a clipped label, not a different item.
    if (expectedItemId != Unknown && result != expectedItemId
        && expectedName.Length > resultName.Length
        && expectedName.StartsWith(resultName, StringComparison.OrdinalIgnoreCase))
      return Unknown;

    return result;
  }

  /// <summary>
  /// The part of the visible text that a longer name would have to start with for the text to be a clipped
  /// rendering of it: trailing whitespace and any ellipsis the client may append are not part of the name.
  /// Used only to DETECT ambiguity - never to match against.
  /// </summary>
  public static string TruncationStem(string? text)
  {
    if (string.IsNullOrEmpty(text))
      return string.Empty;

    var end = text.Length;
    while (end > 0)
    {
      var ch = text[end - 1];
      if (char.IsWhiteSpace(ch) || ch == '.' || ch == '\u2026')
        end--;
      else
        break;
    }

    return text[..end];
  }
}
