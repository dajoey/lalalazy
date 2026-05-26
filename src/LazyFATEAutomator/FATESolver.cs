using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;

namespace LazyFATEAutomator;

public class FATESolver
{
    private readonly Plugin _plugin;
    private const uint TwistOfFateStatusId = 1230;

    public IFate? ActiveTarget { get; private set; }

    public FATESolver(Plugin plugin)
    {
        _plugin = plugin;
    }

    /// <summary>
    /// Checks if the player currently has the "Twist of Fate" experience bonus buff.
    /// </summary>
    public bool PlayerHasTwistOfFate()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player == null) return false;

        return player.StatusList.Any(s => s.StatusId == TwistOfFateStatusId);
    }

    /// <summary>
    /// Checks if a given FATE has an inherent experience/gemstone bonus.
    /// </summary>
    public bool HasBonus(uint fateId)
    {
        var fate = Svc.Fates.FirstOrDefault(f => f.FateId == fateId);
        if (fate == null) return false;

        return fate.HasBonus;
    }

    /// <summary>
    /// Scans the zone and returns a list of FATEs that match the user's filtering rules.
    /// </summary>
    public IEnumerable<IFate> GetFilteredFates()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player == null) return Enumerable.Empty<IFate>();

        return Svc.Fates.Where(fate =>
        {
            // 1. Exclude if blacklisted in configuration
            if (_plugin.Config.BlacklistedFateIds.Contains(fate.FateId))
                return false;

            // 2. Exclude if progress is already complete or past threshold
            if (fate.Progress >= _plugin.Config.MaxProgress)
                return false;

            // 3. Exclude if time remaining is too low
            if (fate.TimeRemaining < _plugin.Config.MinTimeRemaining)
                return false;

            // 4. Exclude if level difference is too high (unless we sync)
            if (fate.Level > player.Level + 5) // Ignore FATEs that are too high level
                return false;

            return true;
        });
    }

    /// <summary>
    /// Sorts and returns the available FATEs based on the configured priority criteria.
    /// </summary>
    public IOrderedEnumerable<IFate> GetSortedAvailableFates()
    {
        var filtered = GetFilteredFates();
        var twist = PlayerHasTwistOfFate();

        // Perform multi-criteria sorting based on configured ordering
        IOrderedEnumerable<IFate> sorted = filtered.OrderBy(f => 0); // Identity starting ordered enumerable

        // Apply our custom sorting chain
        foreach (var criterion in _plugin.Config.SortCriteria)
        {
            switch (criterion)
            {
                case "HasTwistOfFate":
                    // If player has Twist of Fate, prioritize inherent Bonus FATEs for maximum stacking
                    if (twist)
                    {
                        sorted = sorted.ThenByDescending(f => f.HasBonus);
                    }
                    break;
                case "Progress":
                    // Prioritize FATEs that are already highly complete to clear them fast
                    sorted = sorted.ThenByDescending(f => f.Progress);
                    break;
                case "HasBonus":
                    // Prioritize FATEs with the inherent golden bonus icon on the map
                    sorted = sorted.ThenByDescending(f => f.HasBonus);
                    break;
                case "Distance":
                    // Prioritize FATEs that are physically closer to the player
                    sorted = sorted.ThenBy(f => _plugin.Navigation.GetDistanceTo(f.Position));
                    break;
            }
        }

        return sorted;
    }

    /// <summary>
    /// Updates the current active target FATE from the sorted available ones.
    /// </summary>
    public IFate? SelectNextTarget()
    {
        var sorted = GetSortedAvailableFates().ToList();
        ActiveTarget = sorted.FirstOrDefault();
        return ActiveTarget;
    }

    /// <summary>
    /// Clears the active target FATE.
    /// </summary>
    public void ClearTarget()
    {
        ActiveTarget = null;
    }

    /// <summary>
    /// Attempts to find a valid FATE starter NPC in the Object Table for FATEs that require interaction to begin.
    /// </summary>
    public bool TryGetValidMotivationNpc(IFate fate, out IGameObject? npc)
    {
        npc = null;
        if (fate == null) return false;

        // Search ObjectTable for hostiles/NPCs near the center of the FATE matching common starter markers.
        // FATE-starter NPCs usually have special Names or are marked near the center point.
        var targetNpc = Plugin.ObjectTable.FirstOrDefault(obj =>
            obj is ICharacter &&
            !obj.IsDead &&
            Vector3.Distance(obj.Position, fate.Position) < 30.0f &&
            obj.Name.TextValue.Contains("Motivated") || obj.Name.TextValue.Contains("Initiate") || obj.Name.TextValue.Contains("Citizen"));

        if (targetNpc != null)
        {
            npc = targetNpc;
            return true;
        }

        return false;
    }
}
