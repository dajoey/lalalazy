namespace LazyCrucible;

/// <summary> A measured cast that is about to land, for the fight guide window. </summary>
public sealed record IncomingCast(uint CastId, string Name, float RemainingSeconds, int MaxOnCharacter, string Text);

/// <summary>
///     What the fight guide window says out loud, from the measured First / Second Master's Board data
///     (<c>BST_CrucibleHeavyCasts</c>; recorded runs 2026-09-26 .. 09-30). PURE. The plugin never acts on these: a cast the rotation
///     cannot dodge (Atomic Ray, 12.7 s, 4,782 on a 4,060-HP character while the familiar held enmity) and a fight entered with too
///     little HP are things to know, not things to press.
/// </summary>
internal static class FightWarnings
{
    /// <summary> Below this HP percent the entry advisories show. </summary>
    public const int LowEntryHpPercent = 70;

    /// <summary>
    ///     The warning for a cast in progress, or null when no measured row asks for one. <paramref name="remainingSeconds"/> is the
    ///     plugin's own castbar remaining (total minus elapsed).
    /// </summary>
    public static IncomingCast? ForCast(uint castId, float remainingSeconds)
    {
        if (BST_CrucibleData.HeavyCast(castId) is not { Warn: true } row)
            return null;

        var left = Math.Max(0f, remainingSeconds);
        var who = row.Kind == CrucibleHitKind.CastOnly
            ? "a familiar holding enmity does not stop it"
            : "position decides who it hits";
        var text = $"{row.Name} lands in {left:0.0} s: up to {row.MaxOnCharacter:N0} damage to the character, {who}.";
        return new IncomingCast(row.CastId, row.Name, left, row.MaxOnCharacter, text);
    }

    /// <summary>
    ///     Fights whose recorded entries were at low HP and went badly, with what the recording shows. Key = (board, battle).
    ///     First Master's battle 3 is a random-space outcome: the recorded draws after battle 1 were a campsite six times and this fight
    ///     four times; the four entries were at 1%, 20%, 38% and 42% HP and the character was knocked out in every visit.
    ///     Second Master's battle 6 (Durga, Atomic Ray) was entered at 23% HP and the character was knocked out five times in that visit.
    /// </summary>
    private static readonly Dictionary<(int Board, int Battle), string> EntryRisk = new()
    {
        [(4, 3)] = "No campsite comes before this fight when the random space turns into it (4 of 10 recorded draws; the other 6 were a campsite). "
                   + "It was entered at 1%, 20%, 38% and 42% HP and the character was knocked out in all four visits. Heal before stepping onto the random space.",
        [(5, 6)] = "Atomic Ray can deal more than the character's whole HP bar. The recorded visit started at 23% HP and ended in five knock-outs; heal to full first.",
    };

    /// <summary> The entry advisory for the upcoming fight, or null when there is none or the character is healthy enough. </summary>
    public static string? EntryAdvice(int board, int battle, int hpPercent)
    {
        if (hpPercent >= LowEntryHpPercent || !EntryRisk.TryGetValue((board, battle), out var text))
            return null;
        return $"Character at {hpPercent}%: {text}";
    }
}
