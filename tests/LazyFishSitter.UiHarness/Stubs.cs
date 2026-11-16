using Dalamud.Game.ClientState.Conditions;
using LazyFishSitter.Core;

namespace LazyFishSitter;

/// <summary>
/// The game/runtime boundary of the plugin, stubbed. The real <c>Plugin</c> needs the Dalamud plugin host and the real
/// <c>FishSitService</c> reads game memory; the window only touches the members declared here (same names and shapes).
/// </summary>
public sealed class Plugin
{
    internal static HarnessCondition Condition { get; } = new();

    public Configuration Config { get; set; } = new();

    internal FishSitService _service;

    public Plugin() => _service = new FishSitService(this);

    public void SaveConfig() { }
}

/// <summary>Stand-in for the game's condition flags: a flag is set only when a scenario sets it.</summary>
internal sealed class HarnessCondition
{
    private readonly HashSet<ConditionFlag> _set = [];

    public bool this[ConditionFlag flag] => _set.Contains(flag);

    public void Set(ConditionFlag flag, bool on)
    {
        if (on) _set.Add(flag); else _set.Remove(flag);
    }

    public void Clear() => _set.Clear();
}

/// <summary>
/// Same public surface as the real FishSitService. The decision state is the REAL <see cref="SitPolicy"/> (it has no
/// game dependency), so every status text the window shows is produced by the real policy from a scripted fishing
/// session. Only the game read (<c>Read</c>) and the command send are replaced by <see cref="Feed"/>.
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

    /// <summary>One frame of the real tick: policy step with the real context rules; the send is dropped.</summary>
    internal void Feed(FishingSnapshot snapshot, DateTime now)
    {
        var context = new PolicyContext(_plugin.Config.Enabled, _plugin.Config.SitCommand, BlockReason());
        _policy.Step(snapshot, now, context);
    }

    /// <summary>Same condition list as the real service (FishSitService.BlockReason).</summary>
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
}
