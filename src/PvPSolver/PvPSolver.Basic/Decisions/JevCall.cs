using Stj = System.Text.Json;

namespace RotationSolver.Decisions;

/// <summary>One enemy in the snapshot sent to the Jev relay: job and numbers only, never a name.</summary>
public sealed record JevEnemy(ulong Id, string Job, float Hp, float Distance, bool Guard, bool Invulnerable, bool Controlled, bool OnMe);

/// <summary>One ally near the player in the snapshot.</summary>
public sealed record JevAlly(string Job, float Hp, float Distance);

/// <summary>The relay's latest answer: an enemy to focus, or none.</summary>
public sealed record JevAnswer(ulong? Focus, double ReceivedAt);

/// <summary>
/// "Let Jev call the shots": the pure part. The plugin sends a fight snapshot to the PvP Jev relay on
/// the home network about once a second; the relay asks Jev which enemy to focus. Jev may move that one
/// enemy to the front of the attack order, among the enemies the current targeting mode allows. An answer
/// older than <see cref="FreshSeconds"/> counts as no answer, so a slow or missing relay leaves PvP Solver
/// on its own logic. (PvP Solver never fires a PvP Limit Break, so Jev has no Limit Break to call.)
/// </summary>
public static class JevCall
{
    public const double FreshSeconds = 3.0;
    public const double IntervalSeconds = 1.0;
    public const int MaxEnemies = 8;
    public const string DefaultRelayUrl = "http://192.168.10.44:8797/decide";

    private static readonly Stj.JsonSerializerOptions Json = new();

    /// <summary>The enemy Jev wants attacked first, when its answer is still fresh.</summary>
    public static ulong? FocusPick(JevAnswer? answer, double now) =>
        answer != null && Fresh(answer, now) ? answer.Focus : null;

    private static bool Fresh(JevAnswer answer, double now) => now >= answer.ReceivedAt && now - answer.ReceivedAt <= FreshSeconds;

    /// <summary>The request body for the relay; the nearest <see cref="MaxEnemies"/> enemies, rounded numbers.</summary>
    public static string SnapshotJson(string job, float hp, IEnumerable<JevEnemy> enemies, IEnumerable<JevAlly> allies)
    {
        var body = new
        {
            v = 1,
            job,
            hp = Round(hp),
            enemies = enemies.OrderBy(e => e.Distance).Take(MaxEnemies).Select(e => new
            {
                id = e.Id,
                job = e.Job,
                hp = Round(e.Hp),
                dist = MathF.Round(e.Distance, 1),
                guard = e.Guard,
                invuln = e.Invulnerable,
                cc = e.Controlled,
                on_me = e.OnMe,
            }).ToArray(),
            allies = allies.OrderBy(a => a.Distance).Take(4).Select(a => new { job = a.Job, hp = Round(a.Hp), dist = MathF.Round(a.Distance, 1) }).ToArray(),
        };
        return Stj.JsonSerializer.Serialize(body, Json);
    }

    /// <summary>The relay's reply as an answer; null for anything that is not a well-formed reply.</summary>
    public static JevAnswer? Parse(string json, double now)
    {
        try
        {
            using var doc = Stj.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != Stj.JsonValueKind.Object)
            {
                return null;
            }

            ulong? focus = root.TryGetProperty("focus", out var f) && f.ValueKind == Stj.JsonValueKind.Number && f.TryGetUInt64(out var id) ? id : null;
            return new JevAnswer(focus, now);
        }
        catch (Stj.JsonException)
        {
            return null;
        }
    }

    private static float Round(float ratio) => MathF.Round(Math.Clamp(ratio, 0f, 1f), 2);
}
