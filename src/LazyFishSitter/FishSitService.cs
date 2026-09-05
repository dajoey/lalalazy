using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace LazyFishSitter;

internal sealed class FishSitService
{
    private readonly Plugin _plugin;

    // Never re-send the sit command more often than this, even across checks.
    private static readonly TimeSpan MinSitResend = TimeSpan.FromSeconds(3);

    // How many consecutive "not seated" reads are required before a /sit may be
    // re-sent after one has already gone out. One bad read must never stand him up.
    private const int NotSeatedReadsToRearm = 2;

    private DateTime _nextCheckUtc = DateTime.MinValue;
    private DateTime _lastSitSentUtc = DateTime.MinValue;
    private string _lastSkipReason = "(no tick yet)";

    // Per continuous Fishing session state.
    private bool _wasFishing;
    private int _sitsThisSession;
    private bool _seatedSeenSinceLastSit;   // a posture read confirmed "seated" after our /sit
    private int _notSeatedStreak;           // consecutive "not seated" reads since the last seated read

    public FishSitService(Plugin plugin) => _plugin = plugin;

    public DateTime LastSitSentUtc => _lastSitSentUtc;
    public string LastSkipReason => _lastSkipReason;

    public void Tick()
    {
        var now = DateTime.UtcNow;
        if (now < _nextCheckUtc) return;
        _nextCheckUtc = now.AddSeconds(Math.Clamp(_plugin.Config.CheckIntervalSeconds, 1, 10));

        if (!_plugin.Config.Enabled) { _lastSkipReason = "disabled"; return; }

        var local = Plugin.Objects.LocalPlayer;
        if (local == null) { _lastSkipReason = "no LocalPlayer"; return; }

        // Only act while the game says we are fishing. When the flag drops the
        // session ends and everything re-arms for the next cast.
        var fishing = Plugin.Condition[ConditionFlag.Fishing];
        if (!fishing)
        {
            if (_wasFishing) ResetSession();
            _wasFishing = false;
            _lastSkipReason = "not fishing";
            return;
        }
        if (!_wasFishing) { ResetSession(); _wasFishing = true; }

        // Never inject a command in states where sitting is wrong or the input
        // could land somewhere unexpected. Note: deliberately NOT guarding on
        // plain Occupied - a fishing cast reports occupied-style state and the
        // Fishing flag above is the precise gate.
        if (Plugin.Condition[ConditionFlag.BetweenAreas]) { _lastSkipReason = "between areas"; return; }
        if (Plugin.Condition[ConditionFlag.OccupiedInEvent]) { _lastSkipReason = "occupied in event"; return; }
        if (Plugin.Condition[ConditionFlag.OccupiedInQuestEvent]) { _lastSkipReason = "occupied in quest event"; return; }
        if (Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]) { _lastSkipReason = "occupied in cutscene event"; return; }
        if (Plugin.Condition[ConditionFlag.WatchingCutscene]) { _lastSkipReason = "watching cutscene"; return; }
        if (Plugin.Condition[ConditionFlag.InCombat]) { _lastSkipReason = "in combat"; return; }
        if (Plugin.Condition[ConditionFlag.Emoting]) { _lastSkipReason = "mid-emote"; return; }
        if (Plugin.Condition[ConditionFlag.Jumping]) { _lastSkipReason = "jumping"; return; }

        // Already seated (ground sit, chair/bench sit) or in a pose loop? Leave it be.
        var posture = ReadPosture(local.Address);
        if (posture.Seated)
        {
            _notSeatedStreak = 0;
            if (_sitsThisSession > 0) _seatedSeenSinceLastSit = true;
            _lastSkipReason = $"already seated ({posture})";
            return;
        }
        _notSeatedStreak++;

        if (_sitsThisSession > 0)
        {
            // Belt and braces: a /sit while already seated makes the character STAND.
            // After we have sent one, only send another if (a) a posture read has
            // confirmed the sit actually took and (b) the player has since read as
            // "not seated" on >= NotSeatedReadsToRearm consecutive checks. If the
            // posture read never confirms the sit, we send at most ONE per session
            // rather than yo-yo him (0.1.0.0 defect).
            if (!_seatedSeenSinceLastSit)
            {
                _lastSkipReason = $"sit already sent this session; posture never read seated ({posture}) - not re-sending";
                return;
            }
            if (_notSeatedStreak < NotSeatedReadsToRearm)
            {
                _lastSkipReason = $"stood up? waiting for {NotSeatedReadsToRearm} consecutive not-seated reads ({_notSeatedStreak}/{NotSeatedReadsToRearm})";
                return;
            }
        }

        if (now - _lastSitSentUtc < MinSitResend) { _lastSkipReason = "sit sent recently"; return; }

        var cmd = string.IsNullOrWhiteSpace(_plugin.Config.SitCommand) ? "/sit" : _plugin.Config.SitCommand;
        _lastSitSentUtc = now;
        _sitsThisSession++;
        _seatedSeenSinceLastSit = false;
        _notSeatedStreak = 0;
        _lastSkipReason = $"sent {cmd} (#{_sitsThisSession} this session)";
        Plugin.Log.Information($"[LazyFishSitter] standing while fishing ({posture}) - sending {cmd} (#{_sitsThisSession} this session)");
        ChatCommand.Execute(cmd);
    }

    private void ResetSession()
    {
        _sitsThisSession = 0;
        _seatedSeenSinceLastSit = false;
        _notSeatedStreak = 0;
    }

    /// <summary>Everything we can read about the local character's posture, for the decision and for /debug.</summary>
    internal readonly record struct PostureRead(
        CharacterModes Mode, byte ModeParam, ushort EmoteId, string GamePosture, bool ModeSeated, bool GameSeated)
    {
        public bool Seated => ModeSeated || GameSeated;
        public override string ToString() =>
            $"mode={Mode}({(byte)Mode}) param={ModeParam} emote={EmoteId} posture={GamePosture} seated={Seated}";
    }

    internal static unsafe PostureRead ReadPosture(nint objectAddress)
    {
        if (objectAddress == nint.Zero)
            return new PostureRead(CharacterModes.None, 0, 0, "n/a", false, false);

        var ch = (Character*)objectAddress;

        // Signal 1: Character.Mode (FFXIVClientStructs CharacterModes enum - never raw
        // bytes; 0.1.0.0 compared against 12/13 which are RaceChocobo/TripleTriad).
        // EmoteLoop = persistent emotes such as /sit on the ground (ModeParam = EmoteMode
        // row), InPositionLoop = chair/bench sits and pose loops.
        var mode = ch->Mode;
        var modeSeated = mode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop;

        // Signal 2: the game's own EmoteController.GetPosture(). This is independent of
        // Mode, which may read Gathering while a line is cast even when seated.
        var gamePosture = "unavailable";
        var gameSeated = false;
        try
        {
            var p = ch->EmoteController.GetPosture();
            gamePosture = p.ToString();
            gameSeated = p is EmoteController.Posture.SittingOnGround or EmoteController.Posture.SittingInChair;
        }
        catch (Exception)
        {
            // Signature not resolved on this client build - fall back to Mode alone.
        }

        return new PostureRead(mode, ch->ModeParam, ch->EmoteController.EmoteId, gamePosture, modeSeated, gameSeated);
    }

    public void LogDebugState()
    {
        var local = Plugin.Objects.LocalPlayer;
        var posture = local != null ? ReadPosture(local.Address).ToString() : "no LocalPlayer";
        Plugin.Log.Information(
            $"[LazyFishSitter] enabled={_plugin.Config.Enabled} interval={_plugin.Config.CheckIntervalSeconds}s " +
            $"cmd={_plugin.Config.SitCommand} fishing={Plugin.Condition[ConditionFlag.Fishing]} {posture} " +
            $"sitsThisSession={_sitsThisSession} seatedSeenSinceLastSit={_seatedSeenSinceLastSit} notSeatedStreak={_notSeatedStreak} " +
            $"lastSitUtc={_lastSitSentUtc:HH:mm:ss} skip={_lastSkipReason}");
    }
}
