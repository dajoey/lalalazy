using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;

namespace LazyFATEAutomator;

public class FATESolver
{
    private readonly Plugin _plugin;

    /// <summary>Lumina row ID for the "Twist of Fate" XP-buff status.</summary>
    private const uint TwistOfFateStatusId = 1230;

    public IFate? ActiveTarget { get; private set; }

    public FATESolver(Plugin plugin) { _plugin = plugin; }

    public bool PlayerHasTwistOfFate()
        => Svc.Objects.LocalPlayer?.StatusList?.Any(s => s.StatusId == TwistOfFateStatusId) ?? false;

    /// <summary>
    /// Predicate: a FATE is "eligible" for our automation given the user's filter config.
    /// Used by both the solver and the UI for highlighting.
    /// </summary>
    public bool IsEligible(IFate fate)
    {
        var player = Svc.Objects.LocalPlayer;
        if (player == null) return false;

        if (_plugin.Config.BlacklistedFateIds.Contains(fate.FateId)) return false;
        if (fate.Progress >= _plugin.Config.MaxProgress) return false;

        // Unactivated FATEs report negative TimeRemaining — only filter active ones on time
        if (fate.TimeRemaining >= 0 && fate.TimeRemaining < _plugin.Config.MinTimeRemaining) return false;

        // FATE.Level above us by more than MaxLevelDelta — can't sync UP
        if (fate.Level > player.Level + _plugin.Config.MaxLevelDelta) return false;

        return true;
    }

    /// <summary>Returns FATEs from the current zone, ordered by the configured sort chain.</summary>
    public IEnumerable<IFate> GetSortedEligibleFates()
    {
        var eligible = Svc.Fates.Where(IsEligible);
        return ApplySort(eligible, _plugin.Config.SortRules);
    }

    /// <summary>For UI: ALL FATEs in zone, eligible-first, both subsets sorted independently.</summary>
    public IEnumerable<(IFate Fate, bool Eligible)> GetAllForDisplay()
    {
        var all = Svc.Fates.ToList();
        var eligible   = ApplySort(all.Where(IsEligible),  _plugin.Config.SortRules).ToList();
        var ineligible = ApplySort(all.Where(f => !IsEligible(f)), _plugin.Config.SortRules).ToList();
        foreach (var f in eligible)   yield return (f, true);
        foreach (var f in ineligible) yield return (f, false);
    }

    public IFate? SelectNextTarget()
    {
        ActiveTarget = GetSortedEligibleFates().FirstOrDefault();
        return ActiveTarget;
    }

    public void ClearTarget() => ActiveTarget = null;

    /// <summary>
    /// FATE-starter NPC near the FATE center. Used for FATEs that require an NPC interaction
    /// to spawn the actual encounter (the "Preparing" state). Properly parenthesised.
    /// </summary>
    public bool TryGetValidMotivationNpc(IFate fate, out IGameObject? npc)
    {
        npc = null;
        if (fate == null) return false;
        if (GetDistance(Svc.Objects.LocalPlayer?.Position, fate.Position) > 50f) return false; // half ObjectTable range

        IGameObject? found = null;
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not ICharacter) continue;
            if (obj.IsDead) continue;
            if (System.Numerics.Vector3.Distance(obj.Position, fate.Position) > 30f) continue;

            var name = obj.Name?.TextValue ?? string.Empty;
            // BUG FIX (pre-rewrite): the OR group MUST be parenthesised, otherwise &&-precedence
            // collapses the filter into "Initiate OR Citizen anywhere on the map".
            if (name.Contains("Motivated") || name.Contains("Initiate") || name.Contains("Citizen"))
            {
                found = obj;
                break;
            }
        }

        npc = found;
        return found != null;
    }

    // ------------------------------------------------------------------
    // Sorting
    // ------------------------------------------------------------------

    private System.Numerics.Vector3 _playerPos; // captured per-sort to avoid per-comparator service lookups

    private IOrderedEnumerable<IFate> ApplySort(IEnumerable<IFate> source, IReadOnlyList<FateSortRule> rules)
    {
        var player = Svc.Objects.LocalPlayer;
        _playerPos = player?.Position ?? System.Numerics.Vector3.Zero;
        var twist = PlayerHasTwistOfFate();
        var minToPrio = _plugin.Config.MinTimeToPrioritise;

        IOrderedEnumerable<IFate>? ordered = null;
        foreach (var rule in rules)
        {
            Func<IFate, IComparable> key = rule.Criteria switch
            {
                FateSortCriteria.HasBonusWithTwist  => f => f.HasBonus && twist ? 1 : 0,
                FateSortCriteria.Progress           => f => (int)f.Progress,
                FateSortCriteria.HasBonus           => f => f.HasBonus ? 1 : 0,
                FateSortCriteria.TimeRemainingUrgent=> f => f.TimeRemaining is >= 0 and var t && t < minToPrio ? 1 : 0,
                FateSortCriteria.Distance           => f => System.Numerics.Vector3.Distance(_playerPos, f.Position),
                FateSortCriteria.TimeRemaining      => f => f.TimeRemaining,
                FateSortCriteria.Level              => f => (int)f.Level,
                FateSortCriteria.Name               => f => f.Name?.TextValue ?? string.Empty,
                _                                   => f => 0,
            };
            ordered = ordered == null
                ? (rule.Descending ? source.OrderByDescending(key) : source.OrderBy(key))
                : (rule.Descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
        }

        // Deterministic terminal tiebreaker so output is stable across ticks
        return ordered?.ThenBy(f => f.FateId) ?? source.OrderBy(f => f.FateId);
    }

    private static float GetDistance(System.Numerics.Vector3? a, System.Numerics.Vector3 b)
        => a is { } va ? System.Numerics.Vector3.Distance(va, b) : float.MaxValue;
}
