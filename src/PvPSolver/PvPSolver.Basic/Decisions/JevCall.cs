using Stj = System.Text.Json;

namespace RotationSolver.Decisions;

/// <summary>
/// One enemy in the snapshot sent to the Jev relay: job, numbers and position relative to the player, never a name.
/// <paramref name="LimitBreakOk"/> is the game's own answer to "can the player's Limit Break be used on this enemy now".
/// </summary>
public sealed record JevEnemy(ulong Id, string Job, float Hp, uint HpNow, float Distance, float Dx, float Dz,
    bool Guard, bool Invulnerable, bool Controlled, bool OnMe, int AlliesOn, bool Kuzushi, bool LimitBreakOk);

/// <summary>
/// One ally near the player in the snapshot: its game object id, its real HP (<paramref name="HpNow"/> of
/// <paramref name="HpMax"/>), and how many enemies are attacking it. <paramref name="Alliance"/>: an alliance member
/// (Frontline), not in the player's party.
/// </summary>
public sealed record JevAlly(ulong Id, string Job, uint HpNow, uint HpMax, float Distance, float Dx, float Dz, int EnemiesOn,
    bool Alliance = false);

/// <summary>One action the player actually used since the last snapshot, and on whom (null: no target).</summary>
public sealed record JevUsed(uint Action, ulong? Target);

/// <summary>
/// The player's side of the snapshot, with real HP (<paramref name="HpNow"/> of <paramref name="HpMax"/>).
/// <paramref name="MyTarget"/> is the player's current target and <paramref name="Used"/> the actions actually used
/// since the last snapshot: what the payoff report checks Jev's picks against.
/// </summary>
public sealed record JevSelf(string Job, uint HpNow, uint HpMax, float Facing, bool Controlled, bool LimitBreakSelfOk,
    ulong? MyTarget = null, IReadOnlyList<JevUsed>? Used = null);

/// <summary>
/// The relay's latest answer: an enemy to focus (or none), and whether to fire the Limit Break now and at which
/// enemy (<see cref="LimitBreakTarget"/> null means a Limit Break with no enemy target). <see cref="Heal"/> is the
/// optional healer call ("self", "attack", "guard" or an ally's object id), logged only. <see cref="FocusReceivedAt"/> is set
/// when the focus was kept from an earlier answer, so a kept focus ages from the moment Jev gave it. <see cref="RelayKeeps"/>:
/// the reply came from a relay that keeps picks itself (it carries "kept_why"), so the plugin never re-keeps one.
/// </summary>
public sealed record JevAnswer(ulong? Focus, bool FireLimitBreak, ulong? LimitBreakTarget, double ReceivedAt,
    string? Heal = null, double? FocusReceivedAt = null, bool RelayKeeps = false)
{
    /// <summary>When Jev gave the focus in this answer.</summary>
    public double FocusAt => FocusReceivedAt ?? ReceivedAt;
}

/// <summary>How one relay call ended, for the per-fight summary line.</summary>
public enum JevCallOutcome
{
    Ok,
    Timeout,
    Non200,
    Error,
}

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
    /// <summary>Party members first, then the nearest alliance members, this many in all.</summary>
    public const int MaxAllies = 8;
    /// <summary>The same action on the same target within this many seconds is one use (the game reports each twice).</summary>
    public const double UsedDedupeSeconds = 1.5;
    public const int SnapshotVersion = 3;
    public const string DefaultRelayUrl = "http://192.168.10.44:8797/decide";

    private static readonly Stj.JsonSerializerOptions Json = new();

    /// <summary>The enemy Jev wants attacked first, when Jev gave it no more than <see cref="FreshSeconds"/> ago.</summary>
    public static ulong? FocusPick(JevAnswer? answer, double now) =>
        answer?.Focus != null && Fresh(answer.FocusAt, now) ? answer.Focus : null;

    /// <summary>
    /// True when a fresh answer says fire the Limit Break and no attempt was made in the last
    /// <see cref="FireRetrySeconds"/>; <paramref name="target"/> is the enemy, or null for a Limit Break with no enemy target.
    /// </summary>
    public static bool ShouldFireLimitBreak(JevAnswer? answer, double now, double lastAttempt, out ulong? target)
    {
        target = null;
        if (answer == null || !Fresh(answer.ReceivedAt, now) || !answer.FireLimitBreak || now - lastAttempt < FireRetrySeconds)
        {
            return false;
        }

        target = answer.LimitBreakTarget;
        return true;
    }

    /// <summary>
    /// The answer to keep when <paramref name="incoming"/> arrives. An unsure answer (no focus) keeps the previous focus
    /// while that focus is fresh (aged from when Jev gave it) and <paramref name="stillHostile"/> says it is still a
    /// valid hostile target; an answer with a focus, or a stale or gone previous focus, replaces it. The Limit Break
    /// call and the heal call always come from <paramref name="incoming"/>. A reply from a relay that keeps picks itself
    /// (<see cref="JevAnswer.RelayKeeps"/>) is used as is: that relay already dropped the old pick on purpose (Guarded,
    /// out of reach or too old), and only an older relay needs this fallback.
    /// </summary>
    public static JevAnswer KeepLastPick(JevAnswer? previous, JevAnswer incoming, Func<ulong, bool> stillHostile)
    {
        if (incoming.RelayKeeps || incoming.Focus != null || previous?.Focus is not ulong kept
            || !Fresh(previous.FocusAt, incoming.ReceivedAt) || !stillHostile(kept))
        {
            return incoming;
        }

        return incoming with { Focus = kept, FocusReceivedAt = previous.FocusAt };
    }

    private static bool Fresh(double at, double now) => now >= at && now - at <= FreshSeconds;

    /// <summary>Real HP as a ratio: current over max, 0 when max is 0. Never a predicted value with pending heals added.</summary>
    public static float HpRatio(uint now, uint max) => max == 0 ? 0f : Math.Clamp((float)now / max, 0f, 1f);

    /// <summary>
    /// The v3 request body for the relay: the nearest <see cref="MaxEnemies"/> enemies; party members (nearest first)
    /// then the nearest alliance members, <see cref="MaxAllies"/> in all; real HP; rounded numbers.
    /// </summary>
    public static string SnapshotJson(JevSelf self, IEnumerable<JevEnemy> enemies, IEnumerable<JevAlly> allies)
    {
        var near = enemies.OrderBy(e => e.Distance).Take(MaxEnemies).ToArray();
        var body = new
        {
            v = SnapshotVersion,
            job = self.Job,
            hp = Round(HpRatio(self.HpNow, self.HpMax)),
            hp_now = self.HpNow,
            hp_max = self.HpMax,
            facing = MathF.Round(self.Facing, 3),
            me_cc = self.Controlled,
            my_target = self.MyTarget,
            used = (self.Used ?? []).Take(16).Select(u => new { a = u.Action, t = u.Target }).ToArray(),
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
            allies = allies.OrderBy(a => a.Alliance).ThenBy(a => a.Distance).Take(MaxAllies).Select(a => new
            {
                id = a.Id,
                job = a.Job,
                hp = Round(HpRatio(a.HpNow, a.HpMax)),
                dist = MathF.Round(a.Distance, 1),
                dx = MathF.Round(a.Dx, 1),
                dz = MathF.Round(a.Dz, 1),
                enemies_on = a.EnemiesOn,
                alliance = a.Alliance,
            }).ToArray(),
        };
        return Stj.JsonSerializer.Serialize(body, Json);
    }

    /// <summary>
    /// The relay's reply as an answer; null for anything that is not a well-formed reply. Only the fields read here
    /// count; any other field, of any shape, is ignored.
    /// </summary>
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
            bool fire = root.TryGetProperty("lb", out var lb) && Text(lb) == "fire";
            return new JevAnswer(focus, fire, fire ? Id(root, "lb_target") : null, now, HealCall(root),
                RelayKeeps: root.TryGetProperty("kept_why", out _));
        }
        catch (Exception ex) when (ex is Stj.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A string field's text; null when it is not a string or its text cannot be read (a broken escape such as a lone surrogate).</summary>
    private static string? Text(Stj.JsonElement value)
    {
        if (value.ValueKind != Stj.JsonValueKind.String)
        {
            return null;
        }

        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The optional "heal" field: an ally's object id (as text), "self", "attack" or "guard"; anything else is none.</summary>
    private static string? HealCall(Stj.JsonElement root)
    {
        if (!root.TryGetProperty("heal", out var heal))
        {
            return null;
        }

        if (heal.ValueKind == Stj.JsonValueKind.Number)
        {
            return heal.TryGetUInt64(out ulong id) ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        string? text = Text(heal);
        return text is "self" or "attack" or "guard" ? text : null;
    }

    /// <summary>
    /// How a failed relay call is counted: an HTTP timeout or cancel (anywhere in the exception chain) is a timeout,
    /// anything else (relay down, connection refused, bad reply) an error.
    /// </summary>
    public static JevCallOutcome Classify(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is OperationCanceledException or TimeoutException)
            {
                return JevCallOutcome.Timeout;
            }
        }

        return JevCallOutcome.Error;
    }

    private static ulong? Id(Stj.JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == Stj.JsonValueKind.Number && v.TryGetUInt64(out var id) ? id : null;

    private static float Round(float ratio) => MathF.Round(Math.Clamp(ratio, 0f, 1f), 2);
}

/// <summary>
/// The player's recent uses, so the game's two reports of one action are recorded once: the same action on the same
/// target (or on none) within <see cref="JevCall.UsedDedupeSeconds"/> of the first report counts once.
/// Not thread-safe; the caller holds its lock.
/// </summary>
public sealed class JevRecentUses
{
    private readonly List<(uint Action, ulong? Target, double At)> _seen = [];

    /// <summary>True when this use counts (and is remembered); false for a repeat report of one already counted.</summary>
    public bool IsNew(uint action, ulong? target, double now)
    {
        _seen.RemoveAll(s => now < s.At || now - s.At >= JevCall.UsedDedupeSeconds);
        foreach (var s in _seen)
        {
            if (s.Action == action && s.Target == target)
            {
                return false;
            }
        }

        _seen.Add((action, target, now));
        return true;
    }
}

/// <summary>
/// What the log says about one Jev Limit Break fire window: <c>JV|lb-fired</c> once when the Limit Break is actually
/// used in it, or <c>JV|lb-fire-skip reason=...</c> once when it closed without firing (unusable, follow-up,
/// out-of-range, no-target, timeout). A frame refused by PvP Solver's own timing is not a reason: the next frame may fire. The
/// game's report of a use can land just after the window closed (using the Limit Break is what makes it unusable),
/// so the verdict waits <see cref="GraceSeconds"/> after the close. Each method returns the line to log, or null.
/// Not thread-safe; the caller holds its lock.
/// </summary>
public sealed class JevFireLog
{
    /// <summary>Long enough for a use report delayed by a lag spike; below <see cref="JevCall.FireRetrySeconds"/>, so a verdict is always written before the next window opens.</summary>
    public const double GraceSeconds = 2.0;

    private bool _pending;
    private bool _closed;
    private uint _action;
    private double _openedAt;
    private double _until;
    private double _closedAt;
    private string? _refusal;
    private string _reason = "timeout";

    /// <summary>A window opened for <paramref name="action"/> until <paramref name="until"/>; returns the old window's skip, if one was pending.</summary>
    public string? Open(uint action, double now, double until)
    {
        string? flushed = _pending ? Skip() : null;
        _pending = true;
        _closed = false;
        _action = action;
        _openedAt = now;
        _until = until;
        _refusal = null;
        _reason = "timeout";
        return flushed;
    }

    /// <summary>This frame's refusal inside the window (out-of-range, no-target), or null for a frame with no reason to report.</summary>
    public void Refused(string? reason)
    {
        if (_pending && !_closed)
        {
            _refusal = reason;
        }
    }

    /// <summary>The window closed before its end (the Limit Break was used, or is unusable): the use report decides which.</summary>
    public void Close(double now, string reason)
    {
        if (_pending && !_closed)
        {
            _closed = true;
            _closedAt = now;
            _reason = reason;
        }
    }

    /// <summary>The game reported the player's <paramref name="action"/>: the fired line when it is this window's Limit Break.</summary>
    public string? Used(uint action, double now)
    {
        if (!_pending || action != _action || now < _openedAt || (_closed && now - _closedAt > GraceSeconds))
        {
            return null;
        }

        _pending = false;
        return $"JV|lb-fired action={action} after_ms={(int)Math.Round((now - _openedAt) * 1000)}";
    }

    /// <summary>Called every frame: the skip line once the window has ended and the grace passed without a use.</summary>
    public string? Tick(double now)
    {
        if (!_pending)
        {
            return null;
        }

        if (!_closed && now >= _until)
        {
            _closed = true;
            _closedAt = _until;
            _reason = _refusal ?? "timeout";
        }

        return _closed && now - _closedAt > GraceSeconds ? Skip() : null;
    }

    private string Skip()
    {
        _pending = false;
        return "JV|lb-fire-skip reason=" + (_closed ? _reason : _refusal ?? "timeout");
    }
}

/// <summary>
/// The shadow log for Jev's heal call: one <c>JV|heal-shadow</c> line when the call changes to a new value. An answer
/// with no heal call is not a change (the last call stands), and <see cref="Reset"/> (no Jev fight on) starts over, so
/// each fight's first call is logged. Nothing acts on the call. Thread-safe: answers arrive on pool threads.
/// </summary>
public sealed class JevHealShadow
{
    private readonly object _lock = new();
    private string? _last;

    /// <summary>The line to log for this answer's heal call, or null.</summary>
    public string? Next(string? heal)
    {
        lock (_lock)
        {
            if (heal == null || heal == _last)
            {
                return null;
            }

            _last = heal;
            return "JV|heal-shadow heal=" + heal;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _last = null;
        }
    }
}

/// <summary>
/// Relay call counts for the log: one summary line per fight (when combat ends) and, while failures occur, one every
/// <see cref="ReportSeconds"/>; never a line per call. Thread-safe: calls are counted on pool threads.
/// </summary>
public sealed class JevCallStats
{
    public const double ReportSeconds = 60.0;

    private readonly object _lock = new();
    private int _ok;
    private int _timeout;
    private int _non200;
    private int _error;
    private bool _fighting;
    private double _since;

    public void Count(JevCallOutcome outcome)
    {
        lock (_lock)
        {
            switch (outcome)
            {
                case JevCallOutcome.Ok: _ok++; break;
                case JevCallOutcome.Timeout: _timeout++; break;
                case JevCallOutcome.Non200: _non200++; break;
                default: _error++; break;
            }
        }
    }

    /// <summary>Called every frame with whether a Jev fight is on: the summary line when one is due, else null.</summary>
    public string? Tick(double now, bool fighting)
    {
        lock (_lock)
        {
            bool started = fighting && !_fighting;
            bool ended = !fighting && _fighting;
            _fighting = fighting;
            if (started)
            {
                // A call still in flight when the last fight ended is counted after its summary: it belongs to no fight.
                _ok = _timeout = _non200 = _error = 0;
                _since = now;
                return null;
            }

            int failures = _timeout + _non200 + _error;
            if (ended ? _ok + failures == 0 : !fighting || failures == 0 || now - _since < ReportSeconds)
            {
                return null;
            }

            string line = $"JV|relay-calls ok={_ok} timeout={_timeout} non200={_non200} error={_error} span_s={(int)Math.Round(now - _since)} end={(ended ? "combat" : "60s")}";
            _ok = _timeout = _non200 = _error = 0;
            _since = now;
            return line;
        }
    }
}
