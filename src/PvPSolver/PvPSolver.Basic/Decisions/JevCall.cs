using Stj = System.Text.Json;

namespace RotationSolver.Decisions;

/// <summary>
/// One enemy in the snapshot sent to the Jev relay: job, numbers and position relative to the player, never a name.
/// <paramref name="LimitBreakOk"/> is the game's own answer to "can the player's Limit Break be used on this enemy now".
/// </summary>
public sealed record JevEnemy(ulong Id, string Job, float Hp, uint HpNow, float Distance, float Dx, float Dz,
    bool Guard, bool Invulnerable, bool Controlled, bool OnMe, int AlliesOn, bool Kuzushi, bool LimitBreakOk);

/// <summary>One ally near the player in the snapshot, with how many enemies are attacking it.</summary>
public sealed record JevAlly(string Job, float Hp, float Distance, float Dx, float Dz, int EnemiesOn);

/// <summary>The player's side of the snapshot.</summary>
public sealed record JevSelf(string Job, float Hp, uint HpNow, float Facing, bool Controlled, bool LimitBreakSelfOk);

/// <summary>
/// The relay's latest answer: an enemy to focus (or none), and whether to fire the Limit Break now and at which
/// enemy (<see cref="LimitBreakTarget"/> null means a Limit Break with no enemy target).
/// </summary>
public sealed record JevAnswer(ulong? Focus, bool FireLimitBreak, ulong? LimitBreakTarget, double ReceivedAt);

/// <summary>
/// "Let Jev call the shots": the pure part. About once a second in a PvP fight the plugin sends a snapshot to the
/// PvP Jev relay on the home network; the relay asks Jev which enemy to focus and, when the game says the player's
/// Limit Break can be used, whether to fire it now and at whom (questions: ~/ops/pvp-jev/questions.py). An answer
/// older than <see cref="FreshSeconds"/> counts as no answer, so a slow or missing relay leaves PvP Solver on its
/// own logic, which never fires a PvP Limit Break.
/// </summary>
public static class JevCall
{
    public const double FreshSeconds = 3.0;
    public const double IntervalSeconds = 1.0;
    /// <summary>After one Limit Break attempt, no other for this long (a failed use must never stall the rotation).</summary>
    public const double FireRetrySeconds = 2.5;
    public const int MaxEnemies = 8;
    public const string DefaultRelayUrl = "http://192.168.10.44:8797/decide";

    private static readonly Stj.JsonSerializerOptions Json = new();

    /// <summary>The enemy Jev wants attacked first, when its answer is still fresh.</summary>
    public static ulong? FocusPick(JevAnswer? answer, double now) =>
        answer != null && Fresh(answer, now) ? answer.Focus : null;

    /// <summary>
    /// True when a fresh answer says fire the Limit Break and no attempt was made in the last
    /// <see cref="FireRetrySeconds"/>; <paramref name="target"/> is the enemy, or null for a Limit Break with no enemy target.
    /// </summary>
    public static bool ShouldFireLimitBreak(JevAnswer? answer, double now, double lastAttempt, out ulong? target)
    {
        target = null;
        if (answer == null || !Fresh(answer, now) || !answer.FireLimitBreak || now - lastAttempt < FireRetrySeconds)
        {
            return false;
        }

        target = answer.LimitBreakTarget;
        return true;
    }

    private static bool Fresh(JevAnswer answer, double now) => now >= answer.ReceivedAt && now - answer.ReceivedAt <= FreshSeconds;

    /// <summary>The v2 request body for the relay: the nearest <see cref="MaxEnemies"/> enemies, rounded numbers.</summary>
    public static string SnapshotJson(JevSelf self, IEnumerable<JevEnemy> enemies, IEnumerable<JevAlly> allies)
    {
        var near = enemies.OrderBy(e => e.Distance).Take(MaxEnemies).ToArray();
        var body = new
        {
            v = 2,
            job = self.Job,
            hp = Round(self.Hp),
            hp_now = self.HpNow,
            facing = MathF.Round(self.Facing, 3),
            me_cc = self.Controlled,
            lb = new { ready = self.LimitBreakSelfOk || near.Any(e => e.LimitBreakOk), self_ok = self.LimitBreakSelfOk },
            enemies = near.Select(e => new
            {
                id = e.Id,
                job = e.Job,
                hp = Round(e.Hp),
                hp_now = e.HpNow,
                dist = MathF.Round(e.Distance, 1),
                dx = MathF.Round(e.Dx, 1),
                dz = MathF.Round(e.Dz, 1),
                guard = e.Guard,
                invuln = e.Invulnerable,
                cc = e.Controlled,
                on_me = e.OnMe,
                allies_on = e.AlliesOn,
                kuzushi = e.Kuzushi,
                lb_ok = e.LimitBreakOk,
            }).ToArray(),
            allies = allies.OrderBy(a => a.Distance).Take(4).Select(a => new
            {
                job = a.Job,
                hp = Round(a.Hp),
                dist = MathF.Round(a.Distance, 1),
                dx = MathF.Round(a.Dx, 1),
                dz = MathF.Round(a.Dz, 1),
                enemies_on = a.EnemiesOn,
            }).ToArray(),
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

            ulong? focus = Id(root, "focus");
            bool fire = root.TryGetProperty("lb", out var lb) && lb.ValueKind == Stj.JsonValueKind.String && lb.GetString() == "fire";
            return new JevAnswer(focus, fire, fire ? Id(root, "lb_target") : null, now);
        }
        catch (Stj.JsonException)
        {
            return null;
        }
    }

    private static ulong? Id(Stj.JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == Stj.JsonValueKind.Number && v.TryGetUInt64(out var id) ? id : null;

    private static float Round(float ratio) => MathF.Round(Math.Clamp(ratio, 0f, 1f), 2);
}
