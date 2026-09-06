namespace LazyCrafter.Core;

/// <summary>
/// One vendor's outcome from the Phase 6 walk-to-vendor spike, and the paste block the player copies back.
/// Pure: no game types, so <c>tests/LazyCrafter.Harness</c> asserts the exact text Joey is asked to paste.
/// <para>
/// The gate is 5/5 (Joey's rule, card t_933683a5). A verdict that only says "3/5" is useless to the next lane -
/// every failing line therefore names the STAGE that broke, from <see cref="SpikeStage"/>, plus the reason.
/// </para>
/// </summary>
public static class SpikeStage
{
    public const string Preflight = "preflight";
    public const string Teleport = "teleport";
    public const string ZoneSettle = "zone-settle";
    public const string Navmesh = "navmesh";
    public const string Dismount = "dismount";
    public const string Pathfind = "pathfind";
    public const string WalkTimeout = "walk-timeout";
    public const string Interact = "interact";
    public const string Menu = "menu";
    public const string ShopOpen = "shop-open";

    /// <summary>Every stage name, in run order. The harness asserts a failing result always names one of these.</summary>
    public static readonly string[] All =
        [Preflight, Teleport, ZoneSettle, Navmesh, Dismount, Pathfind, WalkTimeout, Interact, Menu, ShopOpen];
}

/// <param name="N">1-based vendor ordinal.</param>
/// <param name="Npc">NPC display name.</param>
/// <param name="Zone">Zone display name.</param>
/// <param name="Pass">Shop opened.</param>
/// <param name="Seconds">Wall clock for this vendor, teleport to shop.</param>
/// <param name="FailedStage">One of <see cref="SpikeStage"/>; <c>null</c> exactly when <paramref name="Pass"/>.</param>
/// <param name="Why">Plain-language reason for the failure; <c>null</c> on a pass.</param>
/// <param name="Timings">Per-stage seconds, already formatted (tp / nav / walk / interact).</param>
/// <param name="Notes">Jank observed even on a pass (nudged, menu, slow navmesh...). Empty means clean.</param>
public sealed record SpikeResult(
    int N,
    string Npc,
    string Zone,
    bool Pass,
    double Seconds,
    string? FailedStage,
    string? Why,
    string Timings,
    IReadOnlyList<string> Notes);

public static class SpikeReport
{
    public const int Gate = 5;

    /// <summary>The block the player copies back. One header, two lines per vendor, one verdict.</summary>
    public static string Render(string version, IReadOnlyList<SpikeResult> results)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("LazyCrafter P6 walk-to-vendor spike - v").Append(version).Append('\n');
        foreach (var r in results.OrderBy(r => r.N))
        {
            sb.Append(r.N).Append('/').Append(Gate).Append(' ').Append(r.Npc).Append(" (").Append(r.Zone).Append("): ")
              .Append(r.Pass ? "PASS" : "FAIL").Append(' ').Append(Fmt(r.Seconds)).Append('s');
            if (!r.Pass) sb.Append(" at ").Append(r.FailedStage ?? "unknown").Append(" - ").Append(r.Why ?? "no reason recorded");
            sb.Append('\n');
            sb.Append("    ").Append(r.Timings);
            sb.Append(" | notes: ").Append(r.Notes.Count == 0 ? "none" : string.Join("; ", r.Notes));
            sb.Append('\n');
        }
        var passed = results.Count(r => r.Pass);
        sb.Append("RESULT: ").Append(passed).Append('/').Append(Gate).Append(' ');
        if (results.Count < Gate) sb.Append("INCOMPLETE - ").Append(Gate - results.Count).Append(" vendor(s) not run yet");
        else sb.Append(passed >= Gate ? "PASS - gate met" : "FAIL - gate not met (needs 5/5)");
        return sb.ToString();
    }

    private static string Fmt(double seconds) => seconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
}
