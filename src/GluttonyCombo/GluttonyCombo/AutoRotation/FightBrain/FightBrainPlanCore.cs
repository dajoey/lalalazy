// Fork (Fight Brain, pre-pull plan reader — card 609a863c, Part A): the pure pieces of the
// plan reader, deliberately free of Dalamud and ECommons types so the offline harness
// (tests/GluttonyCombo.FightBrainHarness) compiles THIS file and asserts the exact
// semantics that ship. House rules (notebook Projects/Jev Builds): code owns facts,
// Jev handles judgment, log first, act later. Nothing here presses an action.

namespace GluttonyCombo.AutoRotation;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary> One candidate plan code built for the pull, in words Jev can judge the group's text against. </summary>
/// <param name="Id">Stable short id the plugin logs (code default is always <c>default</c>).</param>
/// <param name="Does">What the plan does, one line, consequences included.</param>
internal sealed record PlanCandidate(string Id, string Does);

internal static class FightBrainPlanCore
{
    /// <summary> The code default plan id: what the plugin's own rules would do without the text. </summary>
    internal const string CodeDefault = "default";

    /// <summary>
    ///     The candidate plans for a pull, built from the live forecast (any duty): the code
    ///     default, plus generic alternatives the group's text can actually choose between —
    ///     hold the strongest mitigation for the biggest hit, and pre-position early for the
    ///     forecast zones. Small on purpose: one Jev question per pull, one pick out of these.
    /// </summary>
    internal static IReadOnlyList<PlanCandidate> BuildCandidates(IReadOnlyList<FightBrainHit> hits)
    {
        List<PlanCandidate> candidates =
        [
            new(CodeDefault,
                "default: use mitigations in strength order on each incoming hit as it comes, and move when the zone appears"),
        ];

        bool hasTankbuster = false, hasZone = false;
        foreach (var h in hits)
        {
            hasTankbuster |= h.Kind == FightBrainHitKind.Tankbuster;
            hasZone |= h.Kind is FightBrainHitKind.Raidwide or FightBrainHitKind.CastHit;
        }

        if (hasTankbuster)
            candidates.Add(new("hold-strong",
                "hold-strong: keep the strongest mitigation for the tankbuster or biggest hit, cover the rest with weaker ones"));
        if (hasZone)
            candidates.Add(new("early-safe",
                "early-safe: step to the safe spot as soon as the zone is forecast, giving up a little uptime"));
        return candidates;
    }

    /// <summary>
    ///     The human text the plan question is judged against: party finder name and description
    ///     plus the player's own one-line note. One log line: no newlines, no pipe characters
    ///     (the FB| convention uses pipes as separators), no player names are added by this code.
    /// </summary>
    internal static string BuildText(string? pfName, string? pfDescription, string? note)
    {
        var sb = new StringBuilder();
        AppendPart(sb, "pf", pfName, 80);
        AppendPart(sb, "desc", pfDescription, 400);
        AppendPart(sb, "note", note, 120);
        return sb.Length == 0 ? "(no text)" : sb.ToString();
    }

    /// <summary> Truncate a snapshot body's text field for the log line. </summary>
    internal static string LogText(string text, int max = 160) =>
        text.Length <= max ? text : text[..max] + "…";

    private static void AppendPart(StringBuilder sb, string label, string? raw, int max)
    {
        var clean = Sanitize(raw);
        if (clean.Length == 0)
            return;
        if (clean.Length > max)
            clean = clean[..max] + "…";
        if (sb.Length > 0)
            sb.Append(' ');
        sb.Append(label).Append('=').Append(clean);
    }

    /// <summary> Strip newlines, pipes, control characters and collapse every separator run to one space. </summary>
    internal static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var sb = new StringBuilder(raw.Length);
        var pendingSpace = false;
        foreach (var ch in raw)
        {
            if (char.IsControl(ch) || ch is '|' or '\t' or ' ')
            {
                // Any separator run collapses to a single space between words.
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }
            sb.Append(ch);
        }
        return sb.ToString().Trim();
    }
}
