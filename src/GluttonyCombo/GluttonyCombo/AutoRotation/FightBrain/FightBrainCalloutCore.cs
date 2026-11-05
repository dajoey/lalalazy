// Fork (Fight Brain, party-callout reader — card 271c4b4e): the pure pieces of the
// callout reader, deliberately free of Dalamud and ECommons types so the offline harness
// (tests/GluttonyCombo.FightBrainHarness) compiles THIS file and asserts the exact
// semantics that ship. House rules (notebook Projects/Jev Builds): code owns facts,
// Jev handles judgment, log first, act later.
//
// Jev's job here is joining HUMAN TEXT to EXACT game state, never reading a callout on
// its own: the code gathers the live state, builds the candidate responses from that
// state, and the relay asks Jev ONE typed choice question — which candidate the callout
// means. The code-only answer is computed here first and always logged beside Jev's pick.
//
// Conflict order, in code and in every log line: safety rules in code first (never stand
// in a telegraph), then the live callout, then the pre-pull plan. Nothing here presses an
// action or moves anyone — the whole reader is log only.

namespace GluttonyCombo.AutoRotation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary> What the callout's words ask for, classified in code first (Jev only picks between candidates). </summary>
internal enum CalloutIntent
{
    /// <summary>Group up / stack on someone / soak together.</summary>
    Stack,
    /// <summary>A named spot: middle, left, right, a compass point.</summary>
    Position,
    /// <summary>A named person or role label: "with H2", "on the tank".</summary>
    WithPerson,
    /// <summary>Limit Break.</summary>
    Lb,
    /// <summary>Revive someone.</summary>
    Rez,
    /// <summary>Nothing the reader can name; the code schedule stands.</summary>
    Unclear,
}

/// <summary> One party member as the callout reader sees it. Labels are job + slot index only — no player names. </summary>
/// <param name="Slot">The party slot index (0-7; the role-sorted default arrangement).</param>
/// <param name="Label">Job abbreviation + slot, e.g. <c>WHM3</c>.</param>
/// <param name="Role">tank, healer, dps, or unknown.</param>
/// <param name="Marker">The head-marker icon id the member carries, 0 when none.</param>
/// <param name="Tethered">Whether the member is an end of a tether right now.</param>
/// <param name="Offset">Rounded offset from the player in yalms: dx, dy (height), dz.</param>
internal sealed record CalloutMember(int Slot, string Label, string Role, uint Marker, bool Tethered,
    int[]? Offset = null);

/// <summary> The live game state the code gathers for one callout line, plain data only. </summary>
/// <param name="Text">The sanitized callout text (no newlines, no pipes, no player names added).</param>
/// <param name="SenderRole">Who said it: tank, healer, dps, me, or unknown.</param>
/// <param name="Members">The party, in slot order.</param>
/// <param name="MySlot">The player's own party slot, -1 when unknown.</param>
/// <param name="NextKind">The next mechanic on the Stage 1 forecast (kind name), null when none.</param>
/// <param name="NextIn">Seconds to that mechanic; negative when none.</param>
/// <param name="SinceResolved">Seconds since the last forecast entry resolved; negative when none seen.</param>
/// <param name="StandingUnsafe">True when BMR reports the player's current spot is inside a drawn zone.</param>
internal sealed record CalloutState(
    string Text,
    string SenderRole,
    IReadOnlyList<CalloutMember> Members,
    int MySlot,
    string? NextKind,
    double NextIn,
    double SinceResolved,
    bool StandingUnsafe)
{
    public static readonly CalloutState Empty = new(string.Empty, "unknown", [], -1, null, -1, -1, false);
}

/// <summary> One candidate response the callout could mean, in words Jev can judge the text against. </summary>
internal sealed record CalloutCandidate(string Id, string Does);

internal static class FightBrainCalloutCore
{
    /// <summary> The code's standing default when a callout changes nothing: the optimal rotation plus the code's schedule. </summary>
    internal const string CodeDefault = "default";

    /// <summary> A stack the forecast shows this many seconds out lets a no-marker callout mean pre-positioning. </summary>
    internal const double PrePositionWindowSeconds = 6.0;

    /// <summary> The safety-first answer id, used whenever the player currently stands in a drawn zone. </summary>
    internal const string SafeFirst = "safe-first";

    // ---- intent classification (code first, word-boundary matches only) ----------------

    private static readonly Regex StackWords = new(
        @"\b(stack|stacks|stacked|stacking|group up|groupup|pile|pile up|ball up|soak|soaks|soaking|on me|with me|in with|all in)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PositionWords = new(
        @"\b(middle|mid|center|centre|go left|go right|left|right|north|south|east|west|go in|go out|out side|outside|inside|under|behind|front|back|clock|ccw|cw)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PersonWords = new(
        @"\b(with|on|to|follow)\s+(h[1-8]|t[1-4]|d[1-8]|m[1-8]|r[1-2])\b|\b(h[1-8]|t[1-4]|d[1-8]|m[1-8])\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LbWords = new(
        @"\b(lb|lb[1-3]|limit break|limitbreak)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RezWords = new(
        @"\b(rez|res|rezz|raise|raises|revive|rev)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary> The intent of one sanitized callout line, checked most specific first. </summary>
    internal static CalloutIntent Classify(string text)
    {
        var t = text ?? string.Empty;
        if (LbWords.IsMatch(t))
            return CalloutIntent.Lb;
        if (RezWords.IsMatch(t))
            return CalloutIntent.Rez;
        if (StackWords.IsMatch(t))
            return CalloutIntent.Stack;
        if (PersonWords.IsMatch(t))
            return CalloutIntent.WithPerson;
        if (PositionWords.IsMatch(t))
            return CalloutIntent.Position;
        return CalloutIntent.Unclear;
    }

    // ---- candidates from live state ----------------------------------------------------

    /// <summary>
    ///     The light party the player belongs to under the default role-split arrangement:
    ///     slots 0-3 are light party 1, slots 4-7 are light party 2. Returns 0 or 1, or null
    ///     when the party is not a full eight (no light parties exist).
    /// </summary>
    internal static int? MyLightParty(IReadOnlyList<CalloutMember> members, int mySlot)
    {
        if (members.Count != 8 || mySlot is < 0 or > 7)
            return null;
        return mySlot < 4 ? 0 : 1;
    }

    /// <summary>
    ///     The candidate responses for one callout, built from the live state. Small on
    ///     purpose: one Jev question per line, one pick out of these. The code default
    ///     (<see cref="CodeDefault"/>) is always present; everything else only when the
    ///     state offers it.
    /// </summary>
    internal static IReadOnlyList<CalloutCandidate> BuildCandidates(CalloutState s)
    {
        List<CalloutCandidate> candidates =
        [
            new(CodeDefault,
                "default: nothing changes - keep the optimal rotation and the code's schedule; the callout adds nothing the code can act on"),
        ];

        var marked = s.Members.Where(m => m.Marker != 0).ToList();
        foreach (var m in marked)
            candidates.Add(new($"go-m{m.Slot}",
                $"go-m{m.Slot}: move to {m.Label}, the player carrying the marker"));

        var lp = MyLightParty(s.Members, s.MySlot);
        if (marked.Count == 2 && lp is { } mine)
        {
            var myMarked = marked.Where(m => Mine(m.Slot, mine)).ToList();
            if (myMarked.Count > 0)
                candidates.Add(new($"go-lp{mine + 1}",
                    $"go-lp{mine + 1}: move to the light party {mine + 1} marker (the player's own half of the party)"));
            var other = 1 - mine;
            var otherMarked = marked.Where(m => Mine(m.Slot, other)).ToList();
            if (otherMarked.Count > 0)
                candidates.Add(new($"go-lp{other + 1}",
                    $"go-lp{other + 1}: move to the light party {other + 1} marker (the other half of the party)"));
        }

        if (marked.Count == 0 && s.NextKind is { } kind && s.NextIn is >= 0 and <= PrePositionWindowSeconds)
            candidates.Add(new("pre-position",
                $"pre-position: no marker is up, but the forecast shows {kind} in about {Math.Round(s.NextIn, 1)} s - move to the stack spot early"));

        if (Classify(s.Text) == CalloutIntent.Position)
            candidates.Add(new("position",
                "position: move to the spot the words name (middle, left, right, a compass point) as the callout says"));

        if (Classify(s.Text) == CalloutIntent.WithPerson)
            candidates.Add(new("with-person",
                "with-person: move to the party member the words name (a role label such as H2 or a player the state can match)"));

        if (Classify(s.Text) == CalloutIntent.Lb)
            candidates.Add(new("lb",
                "lb: the callout asks for the Limit Break - not a movement, the reader only logs it"));

        if (Classify(s.Text) == CalloutIntent.Rez)
            candidates.Add(new("rez",
                "rez: the callout asks for a revival - not a movement, the reader only logs it"));

        candidates.Add(new("ignore",
            "ignore: the line looks stale or mistaken - no marker is up and no stack is forecast soon, so nothing should move"));
        return candidates;
    }

    private static bool Mine(int slot, int lightParty) =>
        lightParty == 0 ? slot is >= 0 and <= 3 : slot is >= 4 and <= 7;

    /// <summary>
    ///     The code-only answer: which candidate the code would pick from the state alone.
    ///     Safety first (never stand in a telegraph), then the live callout's meaning under
    ///     the state, then the standing schedule. Never null while candidates exist.
    /// </summary>
    internal static string CodeAnswer(CalloutState s, CalloutIntent intent, IReadOnlyList<CalloutCandidate> candidates)
    {
        var ids = candidates.Select(c => c.Id).ToHashSet();
        if (ids.Count == 0)
            return CodeDefault;

        // Safety rules in code first: a drawn zone under the player outranks every callout.
        // This is a code-only override, never a Jev candidate - the log line shows it as
        // code=safe-first beside whatever the callout still means to Jev.
        if (s.StandingUnsafe)
            return SafeFirst;

        switch (intent)
        {
            case CalloutIntent.Stack:
            {
                var marked = s.Members.Where(m => m.Marker != 0).ToList();
                if (marked.Count == 1)
                    return Pick(ids, $"go-m{marked[0].Slot}") ?? Ignore;
                if (marked.Count >= 2)
                {
                    if (MyLightParty(s.Members, s.MySlot) is { } mine)
                    {
                        var myMarked = marked.Where(m => Mine(m.Slot, mine)).ToList();
                        if (myMarked.Count > 0)
                            return Pick(ids, $"go-lp{mine + 1}") ?? Pick(ids, $"go-m{myMarked[0].Slot}") ?? Ignore;
                        // The player's own half carries no marker: the other half's marker is
                        // the only concrete target, but the honest default is ignore.
                        return Ignore;
                    }
                    return Ignore; // no full party: no light-party split exists
                }
                if (s.NextKind is not null && s.NextIn is >= 0 and <= PrePositionWindowSeconds)
                    return Pick(ids, "pre-position") ?? Ignore;
                return Ignore;
            }
            case CalloutIntent.Position:
                return Pick(ids, "position") ?? Ignore;
            case CalloutIntent.WithPerson:
                return Pick(ids, "with-person") ?? Ignore;
            case CalloutIntent.Lb:
                return Pick(ids, "lb") ?? Ignore;
            case CalloutIntent.Rez:
                return Pick(ids, "rez") ?? Ignore;
            default:
                return Pick(ids, CodeDefault) ?? CodeDefault;
        }
    }

    private const string Ignore = "ignore";

    private static string? Pick(IReadOnlySet<string> ids, string id) => ids.Contains(id) ? id : null;

    /// <summary> The conflict-order suffix every callout log line carries, so the order is on record, not in memory. </summary>
    internal const string OrderNote = "order=safe>callout>plan";
}
