using System.Text;

namespace LazyCrafter.Core;

/// <summary>
/// Plain-text rendering of a <see cref="RunSnapshot"/> (card t_c360953f). One renderer, three consumers: the Run
/// tab's <b>Copy report</b> button, <c>/lcraft status</c> in chat, and <c>tests/LazyCrafter.Probe</c>, which renders a
/// synthetic Blocked run offline and asserts every blocked item and every reason is named. Pure: no Dalamud, no game
/// state, no clock - the caller passes the elapsed time it wants shown (the UI recomputes it per frame from
/// <see cref="RunSnapshot.StartedAt"/>; the snapshot's own <see cref="RunSnapshot.Elapsed"/> is used otherwise).
/// </summary>
public static class RunReport
{
    public static string Elapsed(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    public static string KindName(StepKind k) => k switch
    {
        StepKind.Retrieve => "retrieve",
        StepKind.Venture => "venture",
        StepKind.Gather => "gather",
        StepKind.Craft => "craft",
        StepKind.Vendor => "vendor",
        StepKind.Market => "market",
        StepKind.Manual => "manual",
        StepKind.CurrencyShop => "currency shop",
        _ => k.ToString().ToLowerInvariant(),
    };

    public static string StateName(StepState s) => s switch
    {
        StepState.Pending => "pending",
        StepState.Running => "running",
        StepState.Done => "done",
        StepState.Failed => "failed",
        StepState.Blocked => "blocked",
        _ => s.ToString().ToLowerInvariant(),
    };

    /// <summary>"Blocked - cart (Alpine Chandelier) - started 12:19:24, elapsed 16:42, pass 2".</summary>
    public static string Headline(RunSnapshot s, TimeSpan? elapsed = null)
    {
        if (s.State == RunState.Idle) return "no run yet - press Dispatch on a cart, or a Craft / Gather / Retrieve button in the ingredient tree.";
        var sb = new StringBuilder(s.PhaseLabel);
        if (!string.IsNullOrEmpty(s.Phase) && !string.Equals(s.Phase, s.PhaseLabel, StringComparison.OrdinalIgnoreCase)) sb.Append(" (").Append(s.Phase).Append(')');
        if (!string.IsNullOrEmpty(s.What)) sb.Append(" - ").Append(s.What);
        if (s.CartNames.Count > 0) sb.Append(" (").AppendJoin(", ", s.CartNames).Append(')');
        if (s.StartedAt != DateTime.MinValue)
            sb.Append(" - started ").Append(s.StartedAt.ToLocalTime().ToString("HH:mm:ss")).Append(", elapsed ").Append(Elapsed(elapsed ?? s.Elapsed));
        if (s.Pass > 0) sb.Append(", pass ").Append(s.Pass);
        return sb.ToString();
    }

    /// <summary>"[running] craft  Hardsilver Nugget x1 (Artisan busy 1:12)" / "[blocked] craft  Titanium Ingot x3 - needs market Titanium Ore x15".</summary>
    public static string StepLine(RunStep st)
    {
        var sb = new StringBuilder("[").Append(StateName(st.State)).Append("] ").Append(KindName(st.Kind)).Append("  ").Append(st.Name).Append(" x").Append(st.Quantity);
        if (!string.IsNullOrEmpty(st.Reason)) sb.Append(" - ").Append(st.Reason);
        if (!string.IsNullOrEmpty(st.ExternalStatus)) sb.Append(" (").Append(st.ExternalStatus).Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// The blocked lists, one line per channel, the same shape the chat block prints at the end of a run:
    /// market items with the gil estimate, vendor items grouped by NPC, manual items with their sources, and a
    /// generic line for anything else. Empty when nothing is blocked.
    /// </summary>
    public static IReadOnlyList<string> BlockedLines(RunSnapshot s)
    {
        var lines = new List<string>();
        if (s.Blocked.Count == 0) return lines;

        var market = s.Blocked.Where(b => b.Kind == StepKind.Market).ToList();
        if (market.Count > 0)
        {
            long total = 0;
            var complete = true;
            var parts = new List<string>();
            foreach (var b in market)
            {
                // The currency-vendor clause (card t_b431de3a part C) rides in Where and is appended to the item
                // it belongs to, not to the line, so "or the Ixali vendor" can never be read as applying to a
                // different material in the same list. Empty for every item with no known currency vendor, which
                // keeps this line byte-identical to 0.1.6.6 for everything that had no vendor to name.
                var also = string.IsNullOrEmpty(b.Where) ? "" : $" - {b.Where}";
                if (b.EstimatedGil is { } g) { total += g; parts.Add($"{b.Name} x{b.Quantity} (~{g:N0} gil){also}"); }
                else { complete = false; parts.Add($"{b.Name} x{b.Quantity}{also}"); }
            }
            var est = total > 0 || complete ? $" - est. {(complete ? "" : ">")}{total:N0} gil" : "";
            lines.Add($"buy on the market board: {string.Join(", ", parts)}{est}");
        }

        foreach (var group in s.Blocked.Where(b => b.Kind == StepKind.Vendor).GroupBy(b => b.Where ?? ""))
        {
            var items = string.Join(", ", group.Select(b => $"{b.Name} x{b.Quantity}"));
            lines.Add(string.IsNullOrEmpty(group.Key) ? $"buy from a gil vendor (no placed vendor found): {items}" : $"buy from vendor {group.Key}: {items}");
        }

        var manual = s.Blocked.Where(b => b.Kind == StepKind.Manual).ToList();
        if (manual.Count > 0)
            lines.Add("needs a manual source: " + string.Join(", ", manual.Select(b => $"{b.Name} x{b.Quantity}{(string.IsNullOrEmpty(b.Where) ? "" : $" ({b.Where})")}")));
        // Currency shops get their own line, above the generic tail, because the instruction is different in kind:
        // it names a counter and a price rather than a place to look. Where is always populated here - the routing
        // only ever sends a fully resolved offer down this channel (DispatchPlan, decision D1).
        var currency = s.Blocked.Where(b => b.Kind == StepKind.CurrencyShop).ToList();
        if (currency.Count > 0)
            lines.Add("trade for at a currency shop: " + string.Join(", ", currency.Select(b => $"{b.Name} x{b.Quantity}{(string.IsNullOrEmpty(b.Where) ? "" : $" - {b.Where}")}")));

        foreach (var b in s.Blocked.Where(b => b.Kind is not (StepKind.Market or StepKind.Vendor or StepKind.Manual or StepKind.CurrencyShop)))
            lines.Add($"{KindName(b.Kind)}: {b.Name} x{b.Quantity}{(string.IsNullOrEmpty(b.Where) ? "" : $" - {b.Where}")}");

        return lines;
    }

    /// <summary>The full report: headline, status, every step, the blocked lists, the stop reason, the resume hint. Clipboard / Helm-note shape.</summary>
    public static string Render(RunSnapshot s, TimeSpan? elapsed = null)
    {
        var sb = new StringBuilder();
        sb.Append("LazyCrafter run: ").AppendLine(Headline(s, elapsed));
        if (s.State == RunState.Idle) return sb.ToString().TrimEnd();
        if (!string.IsNullOrEmpty(s.Status)) sb.Append("status: ").AppendLine(s.Status);
        if (s.Steps.Count > 0)
        {
            var done = s.Steps.Count(st => st.State == StepState.Done);
            sb.Append("steps (").Append(done).Append('/').Append(s.Steps.Count).AppendLine(" done):");
            foreach (var st in s.Steps) sb.Append("  ").AppendLine(StepLine(st));
        }
        var blocked = BlockedLines(s);
        if (blocked.Count > 0)
        {
            sb.AppendLine(s.State == RunState.Blocked ? "blocked - to continue:" : "still outstanding:");
            foreach (var l in blocked) sb.Append("  ").AppendLine(l);
        }
        if (!string.IsNullOrEmpty(s.StoppedReason)) sb.Append("stopped: ").AppendLine(s.StoppedReason);
        if (s.CanResume) sb.AppendLine("Press Resume (or /lcraft resume) once the items above are in your bags.");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The chat-sized version for <c>/lcraft status</c>: headline, status, only the steps that need attention
    /// (running / blocked / failed) plus a done/pending count, the blocked lists, the stop reason, the resume hint.
    /// Every line is already prefixed for chat by the caller.
    /// </summary>
    public static IReadOnlyList<string> ChatLines(RunSnapshot s, TimeSpan? elapsed = null)
    {
        var lines = new List<string> { Headline(s, elapsed) };
        if (s.State == RunState.Idle) return lines;
        if (!string.IsNullOrEmpty(s.Status)) lines.Add("status: " + s.Status);
        if (s.Steps.Count > 0)
        {
            var done = s.Steps.Count(st => st.State == StepState.Done);
            var pending = s.Steps.Count(st => st.State == StepState.Pending);
            lines.Add($"steps: {done} done, {pending} pending, {s.Steps.Count - done - pending} running/blocked/failed of {s.Steps.Count}");
            foreach (var st in s.Steps.Where(st => st.State is StepState.Running or StepState.Blocked or StepState.Failed)) lines.Add("  " + StepLine(st));
        }
        lines.AddRange(BlockedLines(s));
        if (!string.IsNullOrEmpty(s.StoppedReason)) lines.Add("stopped: " + s.StoppedReason);
        if (s.CanResume) lines.Add("press Resume (or /lcraft resume) once the items above are in your bags.");
        return lines;
    }
}
