namespace LazyFashionReport.Core;

/// <summary>One judged Fashion Report week: what was predicted when the player submitted and
/// what Masked Rose actually awarded (P5, the feedback loop). Diff feeds predictor accuracy.</summary>
public sealed record JudgedRecord
{
    public int Week { get; init; }
    public int Predicted { get; init; }
    public int Awarded { get; init; }
    public DateTime JudgedAtUtc { get; init; }

    public int Diff => Awarded - Predicted;
    /// <summary>The v1 acceptance bar: predictor within 1 point of the game.</summary>
    public bool WithinOne => Math.Abs(Diff) <= 1;
}

/// <summary>
/// Pure judged-week bookkeeping (P5): dedupe against history (the result screen stays open
/// across ticks - one judgement, one record), keep a bounded history, and the one-line
/// summary the window renders. Offline-harness-tested.
/// </summary>
public static class JudgedFeedback
{
    public const int MaxHistory = 26;   // half a year of weeks; enough to see accuracy trend

    /// <summary>The record to append, or null when this (week, awarded) pair is already in
    /// history (re-reading the same result screen must not double-count).</summary>
    public static JudgedRecord? Next(
        IReadOnlyList<JudgedRecord> history, int week, int predicted, int awarded, DateTime judgedAtUtc)
    {
        foreach (var r in history)
            if (r.Week == week && r.Awarded == awarded)
                return null;
        return new JudgedRecord { Week = week, Predicted = predicted, Awarded = awarded, JudgedAtUtc = judgedAtUtc };
    }

    /// <summary>History plus the new record, oldest trimmed, newest last.</summary>
    public static IReadOnlyList<JudgedRecord> Append(IReadOnlyList<JudgedRecord> history, JudgedRecord record)
    {
        var list = new List<JudgedRecord>(history) { record };
        if (list.Count > MaxHistory) list.RemoveRange(0, list.Count - MaxHistory);
        return list;
    }

    /// <summary>"week 449: awarded 84, predicted 85 (off by -1)" or "" with no history.</summary>
    public static string SummaryLine(IReadOnlyList<JudgedRecord> history) =>
        history.Count == 0
            ? ""
            : $"week {history[^1].Week}: awarded {history[^1].Awarded}, predicted {history[^1].Predicted} (off by {history[^1].Diff:+0;-#;0})";

    /// <summary>How many of the recorded weeks hit the within-1-point bar, over the last N.</summary>
    public static (int Within, int Total) Accuracy(IReadOnlyList<JudgedRecord> history, int lastN = 8)
    {
        var slice = history.TakeLast(lastN).ToList();
        return (slice.Count(r => r.WithinOne), slice.Count);
    }
}
