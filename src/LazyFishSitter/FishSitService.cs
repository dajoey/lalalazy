using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using LazyFishSitter.Core;

namespace LazyFishSitter;

/// <summary>
/// Thin Dalamud host around <see cref="SitPolicy"/>: reads the game every frame, hands the read
/// to the policy, and executes whatever the policy asks for. All the behaviour and every guard
/// live in Core/SitPolicy.cs, which has no Dalamud in it and is replayed offline by
/// tests/LazyFishSitter.Harness - three releases in a row shipped a policy bug that only showed
/// up in game, so the policy is no longer allowed to be untestable.
/// </summary>
internal sealed class FishSitService
{
    private readonly Plugin _plugin;
    private readonly SitPolicy _policy = new();

    public FishSitService(Plugin plugin) => _plugin = plugin;

    public DateTime LastSitSentUtc => _policy.LastSitSentUtc;
    public string LastSkipReason => _policy.SkipReason;
    public bool TripActive => _policy.TripActive;
    public int SendsThisTrip => _policy.SendsThisTrip;
    public static int MaxSends => SitPolicy.MaxSendsPerTrip;
    public bool SitBelieved => _policy.SitBelieved;
    public bool SeatedReadWorksWithRodOut => _policy.SeatedReadWorksWithRodOut;

    public void Tick()
    {
        var now = DateTime.UtcNow;

        var local = Plugin.Objects.LocalPlayer;
        if (local == null)
        {
            Apply(_policy.NoPlayer(now));
            return;
        }

        // Every frame, no throttle: every rate limit that matters lives in the policy and is
        // time-based (10 s between sends, 3 s stand-confirm, 3 sends a trip). A poll interval
        // would only make the plugin step over a short standby beat between two quick casts.
        var snap = Read(local.Address, Plugin.Condition[ConditionFlag.Fishing]);
        var ctx = new PolicyContext(_plugin.Config.Enabled, _plugin.Config.SitCommand, BlockReason());
        Apply(_policy.Step(snap, now, ctx));
    }

    private void Apply(PolicyStep step)
    {
        for (var i = 0; i < step.Logs.Count; i++)
            Plugin.Log.Information("[LazyFishSitter] " + step.Logs[i]);

        if (step.SendCommand is { } cmd)
        {
            try { ChatCommand.Execute(cmd); }
            catch (Exception ex) { Plugin.Log.Error(ex, $"[LazyFishSitter] could not send '{cmd}'"); }
        }
    }

    /// <summary>Game conditions where injecting a command would land somewhere unexpected.</summary>
    private static string? BlockReason()
    {
        if (Plugin.Condition[ConditionFlag.BetweenAreas]) return "between areas";
        if (Plugin.Condition[ConditionFlag.OccupiedInEvent]) return "occupied in event";
        if (Plugin.Condition[ConditionFlag.OccupiedInQuestEvent]) return "occupied in quest event";
        if (Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]) return "occupied in cutscene event";
        if (Plugin.Condition[ConditionFlag.WatchingCutscene]) return "watching cutscene";
        if (Plugin.Condition[ConditionFlag.InCombat]) return "in combat";
        if (Plugin.Condition[ConditionFlag.Emoting]) return "mid-emote";
        if (Plugin.Condition[ConditionFlag.Jumping]) return "jumping";
        return null;
    }

    // Set once a sig-resolved call throws, so a missing signature costs one exception, not one per frame.
    private static bool s_eventFrameworkBroken;
    private static bool s_getPostureBroken;

    internal static unsafe FishingSnapshot Read(nint objectAddress, bool fishing)
    {
        var handlerAvailable = false;
        var state = FishState.None;
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
                    state = (FishState)(int)fh->State;
                    changing = fh->ChangingPosition;
                    canFish = fh->CanFish;
                }
            }
            catch (Exception ex)
            {
                s_eventFrameworkBroken = true;
                Plugin.Log.Warning(ex, "[LazyFishSitter] EventFramework/FishingEventHandler unreadable on this client build - the plugin will never see the standby beat and will not send /sit");
            }
        }

        if (objectAddress == nint.Zero)
            return FishingSnapshot.Absent(fishing, handlerAvailable, state, changing, canFish);

        var ch = (Character*)objectAddress;

        // Signal 1: Character.Mode via the CharacterModes enum (never raw bytes - v0.1.0.0
        // compared against 12/13, which are RaceChocobo/TripleTriad). EmoteLoop = persistent
        // emotes such as /sit on the ground, InPositionLoop = chair/bench sits and pose loops.
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

        return new FishingSnapshot(
            fishing, handlerAvailable, state, changing, canFish,
            mode.ToString(), (byte)mode, ch->ModeParam, ch->EmoteController.EmoteId,
            gamePosture, modeSeated, gameSeated);
    }

    public void LogDebugState()
    {
        var local = Plugin.Objects.LocalPlayer;
        var snap = local != null ? Read(local.Address, Plugin.Condition[ConditionFlag.Fishing]).ToString() : "no LocalPlayer";
        Plugin.Log.Information(
            $"[LazyFishSitter] enabled={_plugin.Config.Enabled} cmd={_plugin.Config.SitCommand} " +
            $"block={BlockReason() ?? "none"} [{snap}] {_policy.DebugState()}");
    }
}
