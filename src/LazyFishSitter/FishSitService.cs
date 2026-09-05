using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace LazyFishSitter;

/// <summary>
/// Sits the player back down after every cast. The game forces a stand on every
/// hooked fish, so "sit and stay" means: one /sit per stand episode, sent only when
/// the fishing state machine is idle (pole ready / line in water), never mid cast or
/// mid hook, and never more than twice per episode. Every posture/fishing state change
/// is logged so the seated detector can be graded from the log alone.
/// </summary>
internal sealed class FishSitService
{
    private readonly Plugin _plugin;

    // Never re-send the sit command more often than this, even across episodes.
    private static readonly TimeSpan MinSitResend = TimeSpan.FromSeconds(3);
    // The fishing state must have been idle (PoleReady/LineInWater) at least this long
    // before we send - lets the cast animation finish first.
    private static readonly TimeSpan IdleStableBeforeSit = TimeSpan.FromSeconds(1);
    // If the game accepted our /sit, FishingEventHandler.ChangingPosition flips true
    // shortly after the send. If it never does within this window the send was refused.
    private static readonly TimeSpan AcceptWindow = TimeSpan.FromSeconds(3);
    // Retry pacing if MaxSendsPerEpisode is ever raised above 1. Kept at 1: a refused
    // send cannot be told apart from an accepted one the detectors missed, and a second
    // /sit on a seated player STANDS him (the 0.1.0.0 yo-yo). A refused send costs one
    // cast; the next hook re-arms.
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(4);
    private const int MaxSendsPerEpisode = 1;
    private const int MaxTransitionLogsPerMinute = 40;

    private DateTime _nextCheckUtc = DateTime.MinValue;
    private DateTime _lastSitSentUtc = DateTime.MinValue;
    private DateTime _pendingOutcomeUtc = DateTime.MaxValue;
    private string _lastSkipReason = "(no tick yet)";

    // Per continuous Fishing session.
    private bool _wasFishing;
    private int _sitsThisSession;

    // Per stand episode (session start, or a forced stand after a hook).
    private int _episode;
    private bool _armed;
    private string _armReason = "";
    private int _sendsThisEpisode;
    private bool _sitAccepted;             // ChangingPosition flipped after our send
    private bool _seatedSeenSinceLastSit;  // a posture read confirmed seated after our send
    private int _notSeatedStreak;
    private bool _standForcedPending;      // saw Hooking/Releasing/Confirming; arm on next idle state
    private DateTime _idleSinceUtc = DateTime.MinValue;

    // Transition tracking / rate-limited logging.
    private Snapshot _last;
    private bool _haveLast;
    private DateTime _logWindowStartUtc = DateTime.MinValue;
    private int _logsThisWindow;
    private int _logsSuppressed;

    public FishSitService(Plugin plugin) => _plugin = plugin;

    public DateTime LastSitSentUtc => _lastSitSentUtc;
    public string LastSkipReason => _lastSkipReason;

    private static bool IsIdle(FishingState s) => s is FishingState.PoleReady or FishingState.LineInWater;
    private static bool ForcesStand(FishingState s) =>
        s is FishingState.Hooking or FishingState.ReleasingCatch or FishingState.ConfirmingCollectable;

    public void Tick()
    {
        var now = DateTime.UtcNow;

        var local = Plugin.Objects.LocalPlayer;
        if (local == null)
        {
            if (_wasFishing) { LogInfo("fishing ended (no LocalPlayer)"); ResetSession(); _wasFishing = false; }
            _haveLast = false;
            _lastSkipReason = "no LocalPlayer";
            return;
        }

        // Read every frame so no transition is missed; decide on the throttled cadence.
        var snap = Read(local.Address, Plugin.Condition[ConditionFlag.Fishing]);
        TrackTransitions(snap, now);

        if (_pendingOutcomeUtc != DateTime.MaxValue && now >= _pendingOutcomeUtc)
        {
            _pendingOutcomeUtc = DateTime.MaxValue;
            LogInfo($"sit #{_sendsThisEpisode} of episode {_episode} outcome: accepted={_sitAccepted} seatedRead={_seatedSeenSinceLastSit} now[{snap}]");
        }

        if (now < _nextCheckUtc) return;
        _nextCheckUtc = now.AddSeconds(Math.Clamp(_plugin.Config.CheckIntervalSeconds, 1, 10));

        if (!_plugin.Config.Enabled) { _lastSkipReason = "disabled"; return; }
        if (!snap.Fishing) { _lastSkipReason = "not fishing"; return; }

        // Never inject a command in states where sitting is wrong or the input
        // could land somewhere unexpected.
        if (Plugin.Condition[ConditionFlag.BetweenAreas]) { _lastSkipReason = "between areas"; return; }
        if (Plugin.Condition[ConditionFlag.OccupiedInEvent]) { _lastSkipReason = "occupied in event"; return; }
        if (Plugin.Condition[ConditionFlag.OccupiedInQuestEvent]) { _lastSkipReason = "occupied in quest event"; return; }
        if (Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]) { _lastSkipReason = "occupied in cutscene event"; return; }
        if (Plugin.Condition[ConditionFlag.WatchingCutscene]) { _lastSkipReason = "watching cutscene"; return; }
        if (Plugin.Condition[ConditionFlag.InCombat]) { _lastSkipReason = "in combat"; return; }
        if (Plugin.Condition[ConditionFlag.Emoting]) { _lastSkipReason = "mid-emote"; return; }
        if (Plugin.Condition[ConditionFlag.Jumping]) { _lastSkipReason = "jumping"; return; }

        if (snap.Seated)
        {
            _notSeatedStreak = 0;
            if (_sendsThisEpisode > 0) { _seatedSeenSinceLastSit = true; _armed = false; }
            _lastSkipReason = $"already seated ({snap.PostureText})";
            return;
        }
        _notSeatedStreak++;

        if (!_armed)
        {
            // Only a new stand episode (fishing start, or a hook) arms a send. There is
            // deliberately NO "detector says not seated again" re-arm: if Mode/GetPosture
            // flicker to not-seated while he is seated with a line out, a re-send would
            // STAND him (the 0.1.0.0 yo-yo). A manual stand mid-cast is left alone.
            _lastSkipReason = _sendsThisEpisode == 0
                ? "not armed (no stand episode yet)"
                : $"not armed: episode {_episode} done (sends={_sendsThisEpisode} accepted={_sitAccepted} seatedRead={_seatedSeenSinceLastSit})";
            return;
        }

        if (!snap.HandlerAvailable) { _lastSkipReason = "fishing handler unavailable"; return; }
        if (!IsIdle(snap.State)) { _lastSkipReason = $"fishing state {snap.State} not idle"; return; }
        if (snap.ChangingPosition) { _lastSkipReason = "changing position"; return; }
        if (now - _idleSinceUtc < IdleStableBeforeSit) { _lastSkipReason = "idle state too fresh"; return; }

        if (_sendsThisEpisode > 0)
        {
            if (_sitAccepted || _seatedSeenSinceLastSit)
            {
                // Game took it; whatever the detector says, do not send again (that would STAND).
                _armed = false;
                _lastSkipReason = $"episode {_episode}: sit accepted, not re-sending";
                return;
            }
            if (now - _lastSitSentUtc < RetryAfter) { _lastSkipReason = "waiting to see if the sit was accepted"; return; }
            if (_sendsThisEpisode >= MaxSendsPerEpisode)
            {
                _armed = false;
                LogInfo($"episode {_episode}: {_sendsThisEpisode} send(s), none accepted - giving up until the next cast/hook");
                _lastSkipReason = $"episode {_episode}: retries exhausted";
                return;
            }
        }

        if (now - _lastSitSentUtc < MinSitResend) { _lastSkipReason = "sit sent recently"; return; }

        var cmd = string.IsNullOrWhiteSpace(_plugin.Config.SitCommand) ? "/sit" : _plugin.Config.SitCommand;
        _lastSitSentUtc = now;
        _sitsThisSession++;
        _sendsThisEpisode++;
        _sitAccepted = false;
        _seatedSeenSinceLastSit = false;
        _notSeatedStreak = 0;
        _pendingOutcomeUtc = now + AcceptWindow;
        _lastSkipReason = $"sent {cmd} (episode {_episode} send #{_sendsThisEpisode}, #{_sitsThisSession} this session)";
        LogInfo($"sending {cmd} (episode {_episode} send #{_sendsThisEpisode}, #{_sitsThisSession} this session, armed because: {_armReason}) [{snap}]");
        ChatCommand.Execute(cmd);
    }

    private void TrackTransitions(Snapshot snap, DateTime now)
    {
        if (!_haveLast)
        {
            _last = snap;
            _haveLast = true;
            _idleSinceUtc = now;
            if (snap.Fishing) { _wasFishing = true; ResetSession(); Arm("plugin started while already fishing", now); }
            LogInfo($"first read [{snap}]");
            return;
        }
        if (snap.Equals(_last)) return;

        var changes = new List<string>(4);
        if (snap.Fishing != _last.Fishing) changes.Add($"fishing {_last.Fishing}->{snap.Fishing}");
        if (snap.State != _last.State) changes.Add($"fstate {_last.State}({(int)_last.State})->{snap.State}({(int)snap.State})");
        if (snap.ChangingPosition != _last.ChangingPosition) changes.Add($"changingPosition {_last.ChangingPosition}->{snap.ChangingPosition}");
        if (snap.CanFish != _last.CanFish) changes.Add($"canFish {_last.CanFish}->{snap.CanFish}");
        if (snap.HandlerAvailable != _last.HandlerAvailable) changes.Add($"handler {_last.HandlerAvailable}->{snap.HandlerAvailable}");
        if (snap.Mode != _last.Mode || snap.ModeParam != _last.ModeParam) changes.Add($"mode {_last.Mode}({(byte)_last.Mode})/{_last.ModeParam}->{snap.Mode}({(byte)snap.Mode})/{snap.ModeParam}");
        if (snap.EmoteId != _last.EmoteId) changes.Add($"emote {_last.EmoteId}->{snap.EmoteId}");
        if (snap.GamePosture != _last.GamePosture) changes.Add($"posture {_last.GamePosture}->{snap.GamePosture}");
        if (snap.Seated != _last.Seated) changes.Add($"SEATED {_last.Seated}->{snap.Seated}");
        LogTransition($"[{snap}] changed: {string.Join(", ", changes)}", now);

        // Session boundaries.
        if (snap.Fishing && !_last.Fishing)
        {
            _wasFishing = true;
            ResetSession();
            _idleSinceUtc = now;
            Arm("fishing started", now);
        }
        else if (!snap.Fishing && _last.Fishing)
        {
            _wasFishing = false;
            LogInfo($"fishing ended: {_sitsThisSession} sit(s) sent over {_episode} episode(s) this session");
            ResetSession();
        }

        // Fishing state machine: a hook forces a stand; arm for the next idle state.
        if (snap.State != _last.State)
        {
            if (ForcesStand(snap.State)) _standForcedPending = true;
            if (IsIdle(snap.State))
            {
                if (!IsIdle(_last.State)) _idleSinceUtc = now;
                if (_standForcedPending) { _standForcedPending = false; Arm($"back to {snap.State} after a hook", now); }
            }
        }

        // Proof the game accepted our /sit: it starts changing position shortly after.
        if (snap.ChangingPosition && !_last.ChangingPosition && _sendsThisEpisode > 0 && !_sitAccepted
            && now - _lastSitSentUtc <= AcceptWindow)
        {
            _sitAccepted = true;
            _armed = false;
            LogInfo($"game accepted our sit (changingPosition went true {(now - _lastSitSentUtc).TotalMilliseconds:F0} ms after send)");
        }

        if (snap.Seated && !_last.Seated && _sendsThisEpisode > 0)
        {
            _seatedSeenSinceLastSit = true;
            _notSeatedStreak = 0;
            _armed = false;
        }

        _last = snap;
    }

    private void Arm(string reason, DateTime now)
    {
        _episode++;
        _armed = true;
        _armReason = reason;
        _sendsThisEpisode = 0;
        _sitAccepted = false;
        _seatedSeenSinceLastSit = false;
        _notSeatedStreak = 0;
        LogInfo($"armed episode {_episode}: {reason}");
    }

    private void ResetSession()
    {
        _sitsThisSession = 0;
        _episode = 0;
        _armed = false;
        _armReason = "";
        _sendsThisEpisode = 0;
        _sitAccepted = false;
        _seatedSeenSinceLastSit = false;
        _notSeatedStreak = 0;
        _standForcedPending = false;
        _pendingOutcomeUtc = DateTime.MaxValue;
    }

    private static void LogInfo(string msg) => Plugin.Log.Information("[LazyFishSitter] " + msg);

    private void LogTransition(string msg, DateTime now)
    {
        if (now - _logWindowStartUtc >= TimeSpan.FromMinutes(1))
        {
            if (_logsSuppressed > 0) LogInfo($"({_logsSuppressed} state changes not logged in the last minute - rate limit)");
            _logWindowStartUtc = now;
            _logsThisWindow = 0;
            _logsSuppressed = 0;
        }
        if (_logsThisWindow >= MaxTransitionLogsPerMinute) { _logsSuppressed++; return; }
        _logsThisWindow++;
        LogInfo("state " + msg);
    }

    /// <summary>One read of everything the decision looks at. Equality = "nothing changed".</summary>
    internal readonly record struct Snapshot(
        bool Fishing, bool HandlerAvailable, FishingState State, bool ChangingPosition, bool CanFish,
        CharacterModes Mode, byte ModeParam, ushort EmoteId, string GamePosture, bool ModeSeated, bool GameSeated)
    {
        public bool Seated => ModeSeated || GameSeated;
        public string PostureText => $"mode={Mode}({(byte)Mode}) param={ModeParam} emote={EmoteId} posture={GamePosture} seated={Seated}";
        public override string ToString() =>
            $"fishing={Fishing} fstate={(HandlerAvailable ? $"{State}({(int)State})" : "n/a")} chg={ChangingPosition} canFish={CanFish} {PostureText}";
    }

    // Set once a sig-resolved call throws, so a missing signature costs one exception, not one per frame.
    private static bool s_eventFrameworkBroken;
    private static bool s_getPostureBroken;

    internal static unsafe Snapshot Read(nint objectAddress, bool fishing)
    {
        var handlerAvailable = false;
        var state = FishingState.None;
        var changing = false;
        var canFish = false;
        if (!s_eventFrameworkBroken)
        {
            try
            {
                var ef = EventFramework.Instance();
                var fh = ef != null ? ef->EventHandlerModule.FishingEventHandler : null;
                if (fh != null)
                {
                    handlerAvailable = true;
                    state = fh->State;
                    changing = fh->ChangingPosition;
                    canFish = fh->CanFish;
                }
            }
            catch (Exception ex)
            {
                s_eventFrameworkBroken = true;
                Plugin.Log.Warning(ex, "[LazyFishSitter] EventFramework/FishingEventHandler unreadable on this client build - the plugin will never see an idle fishing state and will not send /sit");
            }
        }

        if (objectAddress == nint.Zero)
            return new Snapshot(fishing, handlerAvailable, state, changing, canFish, CharacterModes.None, 0, 0, "n/a", false, false);

        var ch = (Character*)objectAddress;

        // Signal 1: Character.Mode via the CharacterModes enum (never raw bytes - 0.1.0.0
        // compared against 12/13 = RaceChocobo/TripleTriad). EmoteLoop = persistent emotes
        // such as /sit on the ground, InPositionLoop = chair/bench sits and pose loops.
        var mode = ch->Mode;
        var modeSeated = mode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop;

        // Signal 2: the game's own EmoteController.GetPosture().
        var gamePosture = "unavailable";
        var gameSeated = false;
        if (!s_getPostureBroken)
        {
            try
            {
                var p = ch->EmoteController.GetPosture();
                gamePosture = p.ToString();
                gameSeated = p is EmoteController.Posture.SittingOnGround or EmoteController.Posture.SittingInChair;
            }
            catch (Exception ex)
            {
                s_getPostureBroken = true;
                Plugin.Log.Warning(ex, "[LazyFishSitter] EmoteController.GetPosture unavailable on this client build - seated detection falls back to Character.Mode alone");
            }
        }

        return new Snapshot(fishing, handlerAvailable, state, changing, canFish,
            mode, ch->ModeParam, ch->EmoteController.EmoteId, gamePosture, modeSeated, gameSeated);
    }

    public void LogDebugState()
    {
        var local = Plugin.Objects.LocalPlayer;
        var snap = local != null ? Read(local.Address, Plugin.Condition[ConditionFlag.Fishing]).ToString() : "no LocalPlayer";
        LogInfo(
            $"enabled={_plugin.Config.Enabled} interval={_plugin.Config.CheckIntervalSeconds}s cmd={_plugin.Config.SitCommand} [{snap}] " +
            $"episode={_episode} armed={_armed} ({_armReason}) sendsThisEpisode={_sendsThisEpisode} accepted={_sitAccepted} " +
            $"seatedSeen={_seatedSeenSinceLastSit} notSeatedStreak={_notSeatedStreak} sitsThisSession={_sitsThisSession} " +
            $"lastSitUtc={_lastSitSentUtc:HH:mm:ss} skip={_lastSkipReason}");
    }
}
