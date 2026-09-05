namespace LazyFishSitter.Core;

/// <summary>
/// The whole "sit and stay" decision, with no Dalamud in it, so it can be replayed offline
/// against scripted fishing sessions (tests/LazyFishSitter.Harness). Three releases in a row
/// shipped a policy bug that only showed up in game; this type exists so the next one does not.
///
/// THE GAME BEHAVIOUR THIS IS BUILT ON (Joey, 2026-09-05 - and it is the opposite of what
/// v0.1.2.0's notes claimed): if the character is SEATED when the line goes out, the game keeps
/// them seated for the whole fishing loop. A hook or catch may stand them for a moment, but the
/// game puts them back down by itself. There is no such thing as needing a /sit per cast.
///
/// So the job is: get seated ONCE, at a moment the game will accept the emote, then do nothing.
/// The moment the game accepts it is the standby beat - rod out, no line in the water
/// (<see cref="FishState.PoleReady"/>). v0.1.1.0 sent its single /sit the instant the Fishing
/// condition came on, which is after the line is already out, and the emote was swallowed every
/// time (ffxivdb: ten sends across ten sessions, zero effect). v0.1.2.0 still counted
/// <see cref="FishState.LineInWater"/> as an acceptable moment and would have done the same.
///
/// Everything else is a loop guard: /sit on a seated character STANDS them, so a wrong
/// "you are standing" read is the one failure that must never produce a send.
/// </summary>
public sealed class SitPolicy
{
    // --- policy constants ------------------------------------------------------------
    /// <summary>Hard floor between two sends, whatever else is true.</summary>
    public static readonly TimeSpan MinSendSpacing = TimeSpan.FromSeconds(10);
    /// <summary>"Standing" must be read continuously for this long before it is believed.</summary>
    public static readonly TimeSpan StandConfirm = TimeSpan.FromSeconds(3);
    /// <summary>PoleReady must have been held this long - lets the cast/reel animation finish.</summary>
    public static readonly TimeSpan StateSettle = TimeSpan.FromSeconds(1);
    /// <summary>A stand read this soon after a bite/hook/reel is the game's animation, not the player.</summary>
    public static readonly TimeSpan HookTransientWindow = TimeSpan.FromSeconds(3);
    /// <summary>If the game took our /sit, ChangingPosition flips true inside this window.</summary>
    public static readonly TimeSpan AcceptWindow = TimeSpan.FromSeconds(3);
    /// <summary>The trip only ends once the hole has been out of reach this long (stops cap churn).</summary>
    public static readonly TimeSpan TripEndGrace = TimeSpan.FromSeconds(20);
    public const int MaxSendsPerTrip = 3;
    public const int MaxTransitionLogsPerMinute = 40;

    // --- per fishing trip (one visit to a hole, NOT one cast) -------------------------
    private bool _tripActive;
    private DateTime? _leftHoleAt;
    private int _sendsThisTrip;
    private bool _capLogged;
    /// <summary>The character is seated as far as we can tell: a seated read, or the game took our /sit.</summary>
    private bool _sitBelieved;
    /// <summary>A seated read happened WITH THE ROD OUT, so "not seated" means something in this context.</summary>
    private bool _detectorProvenWithRodOut;
    private int _seatedReadsWithRodOut;
    private DateTime _lastSitSent = DateTime.MinValue;
    private DateTime? _pendingOutcomeAt;

    // --- clocks, updated every frame ---------------------------------------------------
    private DateTime? _notSeatedSince;
    private DateTime? _poleReadySince;
    private DateTime? _lastHookish;

    // --- transitions / rate-limited logging --------------------------------------------
    private FishingSnapshot _last;
    private bool _haveLast;
    private DateTime? _logWindowStart;
    private int _logsThisWindow;
    private int _logsSuppressed;

    private readonly List<string> _logs = new(4);
    private string _skipReason = "(no tick yet)";

    // --- observable state (settings window / debug dump) --------------------------------
    public bool TripActive => _tripActive;
    public int SendsThisTrip => _sendsThisTrip;
    public bool SitBelieved => _sitBelieved;
    public bool SeatedReadWorksWithRodOut => _detectorProvenWithRodOut;
    public int SeatedReadsWithRodOut => _seatedReadsWithRodOut;
    public DateTime LastSitSentUtc => _lastSitSent;
    public string SkipReason => _skipReason;

    /// <summary>Rod out, no line in the water: the standby beat where the game accepts /sit.</summary>
    public static bool IsStandbyBeat(FishState s) => s == FishState.PoleReady;

    /// <summary>
    /// States where the game stands you for a catch and then re-seats you afterwards, so a
    /// "standing" read within <see cref="HookTransientWindow"/> of one is transient, not the player.
    ///
    /// Deliberately NOT <see cref="FishState.PullingPoleIn"/>: FFXIVClientStructs documents that
    /// state as "fish slips, no bite, briefly after reeling in, or Rest", so it ends EVERY cast,
    /// bite or not. Including it put a 3 s dead window on the front of every standby beat, which
    /// swallowed every retry of a refused /sit (caught by harness cases 08/11 before shipping).
    /// </summary>
    public static bool IsHookish(FishState s) =>
        s is FishState.Bite or FishState.Hooking or FishState.ReleasingCatch
          or FishState.ConfirmingCollectable;

    /// <summary>
    /// Advance the policy by one read. Call this EVERY frame.
    ///
    /// There is deliberately no check-cadence throttle here. v0.1.0.0-v0.1.2.0 polled every
    /// 2 s, which was their only rate limit; now every limit is time-based and hard (10 s
    /// between sends, 3 s stand-confirm, 1 s state-settle, 3 s hook-transient, 3 sends a trip),
    /// so a throttle adds nothing except missed windows. The standby beat between two quick
    /// casts can be under two seconds, and a 2 s poll steps straight over it - which is what
    /// harness cases 08/11 caught.
    /// </summary>
    /// <param name="snap">This frame's game read.</param>
    /// <param name="now">UTC now.</param>
    /// <param name="ctx">Enabled flag, sit command, and any condition block.</param>
    public PolicyStep Step(FishingSnapshot snap, DateTime now, PolicyContext ctx)
    {
        _logs.Clear();

        Observe(snap, now);

        if (_pendingOutcomeAt is { } due && now >= due)
        {
            _pendingOutcomeAt = null;
            Log($"/sit #{_sendsThisTrip} of this trip outcome: gameTookIt={_sitBelieved} " +
                $"seatedReadWorksWithRodOut={_detectorProvenWithRodOut} now[{snap}]");
        }

        var send = Decide(snap, now, ctx, out var reason);
        _skipReason = reason;
        return new PolicyStep(_logs, send, reason);
    }

    /// <summary>The player left the world (logout, zone wipe, plugin unload). Closes any open trip.</summary>
    public PolicyStep NoPlayer(DateTime now)
    {
        _logs.Clear();
        if (_tripActive) EndTrip(now, "no local player");
        _haveLast = false;
        _skipReason = "no local player";
        return new PolicyStep(_logs, null, _skipReason);
    }

    /// <summary>
    /// The whole policy in one place. Guards are ordered so that everything which could
    /// stand the player up is refused before anything that could send.
    /// </summary>
    private string? Decide(FishingSnapshot snap, DateTime now, PolicyContext ctx, out string reason)
    {
        if (!ctx.Enabled) { reason = "disabled"; return null; }
        if (!_tripActive) { reason = "not at a fishing hole"; return null; }
        if (ctx.BlockReason is { } blocked) { reason = blocked; return null; }

        // GUARD 1 - any seated signal at all. A /sit here would STAND him. This one must never
        // be wrong in the permissive direction, so it is first and absolute.
        if (snap.Seated) { reason = $"already seated ({snap.PostureText})"; return null; }

        // GUARD 2 - sit once and stay. Once the game has taken a sit on this trip we are done:
        // the game re-seats him after every catch by itself. The only way we would send again is
        // if the seated read had proved it still reports seated with the rod out - otherwise a
        // "standing" read is an unproven read, and acting on it is exactly the v0.1.0.0 yo-yo.
        if (_sitBelieved && !_detectorProvenWithRodOut)
        {
            reason = "already sat you once this trip; the seated read is blind with the rod out, " +
                     "so a \"standing\" read cannot be trusted";
            return null;
        }

        // GUARD 3 - only at the standby beat. Not mid-cast, not with a line in the water, not
        // mid-bite/hook/reel. This is what v0.1.1.0 and v0.1.2.0 both got wrong.
        if (!snap.HandlerAvailable) { reason = "fishing handler unavailable"; return null; }
        if (!IsStandbyBeat(snap.State)) { reason = $"fishing state {snap.State}({(int)snap.State}) - not the standby beat"; return null; }
        if (snap.ChangingPosition) { reason = "sitting down / standing up"; return null; }
        if (_poleReadySince is not { } ready || now - ready < StateSettle) { reason = "standby beat too fresh"; return null; }

        // GUARD 4 - the game stands you for a moment around a catch and puts you back down.
        // Anything inside that window is the animation, not the player choosing to stand.
        if (_lastHookish is { } hook && now - hook < HookTransientWindow) { reason = "just hooked/reeled - the game re-seats you itself"; return null; }
        if (_notSeatedSince is not { } standing || now - standing < StandConfirm) { reason = "standing read not held long enough"; return null; }

        // GUARD 5 - hard loop bounds.
        if (now - _lastSitSent < MinSendSpacing) { reason = "sent /sit recently"; return null; }
        if (_sendsThisTrip >= MaxSendsPerTrip)
        {
            if (!_capLogged)
            {
                _capLogged = true;
                Log($"cap reached: {MaxSendsPerTrip} /sit send(s) this trip and none of them stuck - " +
                    $"not sending again until you leave the hole [{snap}]");
            }
            reason = $"cap reached ({MaxSendsPerTrip} sends this trip)";
            return null;
        }

        var cmd = string.IsNullOrWhiteSpace(ctx.SitCommand) ? "/sit" : ctx.SitCommand;
        _lastSitSent = now;
        _sendsThisTrip++;
        _pendingOutcomeAt = now + AcceptWindow;
        Log($"sending {cmd} (#{_sendsThisTrip} of {MaxSendsPerTrip} this trip, standing for " +
            $"{(now - standing).TotalSeconds:F1}s at the standby beat) [{snap}]");
        reason = $"sent {cmd} (#{_sendsThisTrip} this trip)";
        return cmd;
    }

    /// <summary>Every-frame bookkeeping: seated/standing clocks, trip boundaries, transitions.</summary>
    private void Observe(FishingSnapshot snap, DateTime now)
    {
        // --- seated / standing clocks ---------------------------------------------------
        if (snap.Seated)
        {
            _notSeatedSince = null;
            _sitBelieved = true;
            if (snap.HandlerAvailable && snap.State != FishState.None)
            {
                _seatedReadsWithRodOut++;
                if (!_detectorProvenWithRodOut)
                {
                    _detectorProvenWithRodOut = true;
                    Log($"seated read PROVEN with the rod out (fishing state {snap.State}) - " +
                        $"a later \"standing\" read can be trusted this trip [{snap}]");
                }
            }
        }
        else
        {
            _notSeatedSince ??= now;
        }

        // --- fishing state clocks --------------------------------------------------------
        if (snap.HandlerAvailable)
        {
            if (IsStandbyBeat(snap.State)) _poleReadySince ??= now;
            else _poleReadySince = null;

            if (IsHookish(snap.State)) _lastHookish = now;
        }

        // --- trip boundaries -------------------------------------------------------------
        // A "trip" is one visit to a hole, not one cast: the Fishing condition drops between
        // casts (v0.1.1.0 counted ten "sessions" in 21 minutes), which would reset the cap
        // constantly and turn a 3-send cap into 3 sends per cast.
        var atHole = snap.HandlerAvailable && (snap.Fishing || snap.CanFish);
        if (atHole)
        {
            _leftHoleAt = null;
            if (!_tripActive) StartTrip(now, snap);
        }
        else if (_tripActive)
        {
            if (_leftHoleAt is not { } left) _leftHoleAt = now;
            else if (now - left >= TripEndGrace) EndTrip(now, "left the fishing hole");
        }

        // --- transitions ------------------------------------------------------------------
        if (!_haveLast)
        {
            _last = snap;
            _haveLast = true;
            Log($"first read [{snap}]");
            return;
        }
        if (snap.Equals(_last)) return;

        var changes = new List<string>(8);
        if (snap.Fishing != _last.Fishing) changes.Add($"fishing {_last.Fishing}->{snap.Fishing}");
        if (snap.State != _last.State) changes.Add($"fstate {_last.State}({(int)_last.State})->{snap.State}({(int)snap.State})");
        if (snap.ChangingPosition != _last.ChangingPosition) changes.Add($"changingPosition {_last.ChangingPosition}->{snap.ChangingPosition}");
        if (snap.CanFish != _last.CanFish) changes.Add($"canFish {_last.CanFish}->{snap.CanFish}");
        if (snap.HandlerAvailable != _last.HandlerAvailable) changes.Add($"handler {_last.HandlerAvailable}->{snap.HandlerAvailable}");
        if (snap.ModeValue != _last.ModeValue || snap.ModeParam != _last.ModeParam) changes.Add($"mode {_last.ModeName}({_last.ModeValue})/{_last.ModeParam}->{snap.ModeName}({snap.ModeValue})/{snap.ModeParam}");
        if (snap.EmoteId != _last.EmoteId) changes.Add($"emote {_last.EmoteId}->{snap.EmoteId}");
        if (snap.GamePosture != _last.GamePosture) changes.Add($"posture {_last.GamePosture}->{snap.GamePosture}");
        if (snap.Seated != _last.Seated) changes.Add($"SEATED {_last.Seated}->{snap.Seated}");
        LogTransition($"[{snap}] changed: {string.Join(", ", changes)}", now);

        // Proof the game accepted our /sit: it starts changing position shortly after the send.
        // This is the only acceptance signal that works even if Mode/GetPosture turn out to be
        // decorative while the rod is out, which is still an open question (see the trip summary).
        if (snap.ChangingPosition && !_last.ChangingPosition && _sendsThisTrip > 0 && !_sitBelieved
            && now - _lastSitSent <= AcceptWindow)
        {
            _sitBelieved = true;
            Log($"the game took our /sit (started changing position {(now - _lastSitSent).TotalMilliseconds:F0} ms after the send) - " +
                "not sending again this trip");
        }

        _last = snap;
    }

    private void StartTrip(DateTime now, FishingSnapshot snap)
    {
        _tripActive = true;
        _leftHoleAt = null;
        _sendsThisTrip = 0;
        _capLogged = false;
        _sitBelieved = snap.Seated;
        _detectorProvenWithRodOut = false;
        _seatedReadsWithRodOut = 0;
        _notSeatedSince = snap.Seated ? null : now;
        _pendingOutcomeAt = null;
        Log($"fishing trip started (seatedAlready={snap.Seated}) [{snap}]");
    }

    private void EndTrip(DateTime now, string why)
    {
        // Don't let a rate-limited burst take its own suppressed count to the grave: if the trip
        // ends mid-window, the count is only ever reported when the NEXT window opens, which may
        // be minutes away or never (harness case 15b).
        if (_logsSuppressed > 0)
        {
            Log($"({_logsSuppressed} state changes not logged - rate limit)");
            _logsSuppressed = 0;
        }

        // This line is the answer to the still-open question "does the game ever report you as
        // seated while a line is out" - gradeable from ffxivdb with nothing typed in game.
        Log($"fishing trip ended ({why}): sends={_sendsThisTrip}/{MaxSendsPerTrip} " +
            $"gameTookASit={_sitBelieved} seatedReadsWithRodOut={_seatedReadsWithRodOut} " +
            $"seatedReadWorksWithRodOut={_detectorProvenWithRodOut}");
        _tripActive = false;
        _leftHoleAt = null;
        _sendsThisTrip = 0;
        _capLogged = false;
        _sitBelieved = false;
        _detectorProvenWithRodOut = false;
        _seatedReadsWithRodOut = 0;
        _notSeatedSince = null;
        _poleReadySince = null;
        _lastHookish = null;
        _pendingOutcomeAt = null;
    }

    private void Log(string msg) => _logs.Add(msg);

    private void LogTransition(string msg, DateTime now)
    {
        if (_logWindowStart is not { } start || now - start >= TimeSpan.FromMinutes(1))
        {
            if (_logsSuppressed > 0) Log($"({_logsSuppressed} state changes not logged in the last minute - rate limit)");
            _logWindowStart = now;
            _logsThisWindow = 0;
            _logsSuppressed = 0;
        }
        if (_logsThisWindow >= MaxTransitionLogsPerMinute) { _logsSuppressed++; return; }
        _logsThisWindow++;
        Log("state " + msg);
    }

    public string DebugState() =>
        $"tripActive={_tripActive} sends={_sendsThisTrip}/{MaxSendsPerTrip} gameTookASit={_sitBelieved} " +
        $"seatedReadWorksWithRodOut={_detectorProvenWithRodOut} seatedReadsWithRodOut={_seatedReadsWithRodOut} " +
        $"lastSitUtc={_lastSitSent:HH:mm:ss} skip={_skipReason}";
}
