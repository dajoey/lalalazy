using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace LazyFishSitter;

internal sealed class FishSitService
{
    private readonly Plugin _plugin;

    // Never re-send the sit command more often than this, even across checks.
    // If the game silently rejects a /sit, this keeps us at worst one retry per
    // few seconds instead of a command every check.
    private static readonly TimeSpan MinSitResend = TimeSpan.FromSeconds(3);

    private DateTime _nextCheckUtc = DateTime.MinValue;
    private DateTime _lastSitSentUtc = DateTime.MinValue;
    private string _lastSkipReason = "(no tick yet)";

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

        // Only act while the game says we are fishing.
        if (!Plugin.Condition[ConditionFlag.Fishing]) { _lastSkipReason = "not fishing"; return; }

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

        // Already in a posture loop (ground sit, chair/bench sit, pose)? Leave it be.
        if (IsInPostureLoop(local.Address)) { _lastSkipReason = "already seated/in pose"; return; }

        if (now - _lastSitSentUtc < MinSitResend) { _lastSkipReason = "sit sent recently"; return; }

        var cmd = string.IsNullOrWhiteSpace(_plugin.Config.SitCommand) ? "/sit" : _plugin.Config.SitCommand;
        _lastSitSentUtc = now;
        _lastSkipReason = $"sent {cmd}";
        Plugin.Log.Information($"[LazyFishSitter] standing while fishing - sending {cmd}");
        ChatCommand.Execute(cmd);
    }

    private static unsafe bool IsInPostureLoop(nint objectAddress)
    {
        if (objectAddress == nint.Zero) return false;
        var ch = (Character*)objectAddress;
        var mode = (byte)ch->Mode;
        // Same read SimpleHeels' EmoteIdentifier uses: persistent emotes (/sit on
        // the ground) put the character in mode 12 (EmoteLoop); chair/bench sits
        // and pose loops use 13 (InPositionLoop). Anything else = standing/moving.
        return mode is 12 or 13;
    }

    public unsafe void LogDebugState()
    {
        var local = Plugin.Objects.LocalPlayer;
        byte mode = 255;
        if (local != null && local.Address != nint.Zero)
            mode = (byte)((Character*)local.Address)->Mode;
        Plugin.Log.Information(
            $"[LazyFishSitter] enabled={_plugin.Config.Enabled} interval={_plugin.Config.CheckIntervalSeconds}s " +
            $"cmd={_plugin.Config.SitCommand} fishing={Plugin.Condition[ConditionFlag.Fishing]} mode={mode} " +
            $"lastSitUtc={_lastSitSentUtc:HH:mm:ss} skip={_lastSkipReason}");
    }
}
