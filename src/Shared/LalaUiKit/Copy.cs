// LalaUiKit/Copy.cs — live-state vocabulary and copy helpers (Brief R5, R11).
// Exactly these words, no synonyms: Ready, Running, Idle, Blocked, Failed, Empty,
// Needs setup, Missing <thing>. "In progress", "working", "busy" are banned.

namespace LalaUiKit;

using System;
using System.Collections.Generic;
using System.Linq;

using System.Numerics;

/// <summary>The suite's live-state vocabulary (R5). One enum, one word each.</summary>
public enum UiState
{
    Ready,
    Running,
    Idle,
    Blocked,
    Failed,
    Empty,
    NeedsSetup,
    Missing,
}

public static class Copy
{
    /// <summary>The exact word for a state (R5). "NeedsSetup" renders as "Needs setup".</summary>
    public static string Word(UiState s) => s switch
    {
        UiState.Ready => "Ready",
        UiState.Running => "Running",
        UiState.Idle => "Idle",
        UiState.Blocked => "Blocked",
        UiState.Failed => "Failed",
        UiState.Empty => "Empty",
        UiState.NeedsSetup => "Needs setup",
        UiState.Missing => "Missing",
        _ => s.ToString(),
    };

    /// <summary>"Missing &lt;thing&gt;" (R5) — the only state that carries an object.</summary>
    public static string Missing(string thing) => $"Missing {thing}";

    /// <summary>
    ///     Mark colour per R4/R5: green only for Ready/done, amber for in-progress or
    ///     waiting, red ONLY for Failed (never progress, never waiting), neutral when no
    ///     state meaning is carried.
    /// </summary>
    public static Vector4 MarkColor(UiState s) => s switch
    {
        UiState.Ready => Theme.StateGreen,
        UiState.Running => Theme.StateAmber,
        UiState.Idle => Theme.StateNeutral,
        UiState.Blocked => Theme.StateAmber,
        UiState.Failed => Theme.StateRed,
        UiState.Empty => Theme.StateNeutral,
        UiState.NeedsSetup => Theme.StateNeutral,
        UiState.Missing => Theme.StateAmber,
        _ => Theme.StateNeutral,
    };

    /// <summary>One-line reason formatting: "action — reason" (R3 announces once, R11 names exact controls).</summary>
    public static string WithReason(string action, string reason) =>
        string.IsNullOrEmpty(reason) ? action : $"{action} — {reason}";

    /// <summary>Banned synonyms (R5) — the harness asserts no rendered copy contains these.</summary>
    public static readonly string[] BannedWords = { "in progress", "working", "busy" };

    /// <summary>R5 copy lint: returns every banned word found in a UI string (empty = clean).</summary>
    public static string[] FindBannedWords(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();
        var hits = new List<string>();
        foreach (var w in BannedWords)
        {
            if (text.Contains(w, StringComparison.OrdinalIgnoreCase))
                hits.Add(w);
        }

        return hits.ToArray();
    }
}
