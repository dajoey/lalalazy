using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyFateAutomation.Helpers.Extensions;

public static class IConditionExtensions {
    public static unsafe bool HasPermission(this ICondition condition, uint id) => Conditions.Instance()->HasPermission(id);
    public static unsafe bool HasPermission(this ICondition condition, IEnumerable<uint> ids) => ids.All(id => Conditions.Instance()->HasPermission(id));

    public static bool CanQueue(this ICondition condition)
        => HasPermission(condition, [119, 120]);

    public static bool CanMoveItems(this ICondition condition)
         => HasPermission(condition, [134]); // checked when calling MoveItemSlot (136/137?)

    public static bool CanLowerItemQuality(this ICondition condition)
        => HasPermission(condition, [135]); // checked when lowering item quality

    public static bool IsBoundByDuty(this ICondition condition)
        => condition.Any(ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95);

    public static bool IsInCombat(this ICondition condition)
        => condition.Any(ConditionFlag.InCombat);

    public static bool IsInCutscene(this ICondition condition)
        => condition.Any(ConditionFlag.OccupiedInCutSceneEvent, ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78);

    public static bool IsBetweenAreas(this ICondition condition)
        => condition.Any(ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51);

    public static bool IsCrafting(this ICondition condition)
        => condition.Any(ConditionFlag.Crafting, ConditionFlag.ExecutingCraftingAction, ConditionFlag.PreparingToCraft);

    public static bool IsGathering(this ICondition condition)
        => condition.Any(ConditionFlag.Gathering, ConditionFlag.ExecutingGatheringAction);

    /// <summary>
    /// In any condition that prevents the player from moving their character
    /// </summary>
    public static bool IsLockedIn(this ICondition condition)
        => condition.Any(ConditionFlag.Occupied,
            ConditionFlag.Occupied30,
            ConditionFlag.Occupied33,
            ConditionFlag.Occupied38,
            ConditionFlag.Occupied39,
            ConditionFlag.OccupiedInCutSceneEvent,
            ConditionFlag.OccupiedInEvent,
            ConditionFlag.OccupiedInQuestEvent,
            ConditionFlag.OccupiedSummoningBell,
            ConditionFlag.WatchingCutscene,
            ConditionFlag.WatchingCutscene78,
            ConditionFlag.BetweenAreas,
            ConditionFlag.BetweenAreas51,
            ConditionFlag.InThatPosition,
            ConditionFlag.PreparingToCraft,
            ConditionFlag.BeingMoved,
            ConditionFlag.RidingPillion,
            ConditionFlag.Fishing);

    /// <summary>
    /// In any situation where the character is unable to move or is doing something that would prevent most automation
    /// </summary>
    public static bool IsUnavailable(this ICondition condition)
        => condition.IsLockedIn() || condition.Any(
            ConditionFlag.TradeOpen,
            ConditionFlag.Crafting,
            ConditionFlag.ExecutingCraftingAction,
            ConditionFlag.Unconscious,
            ConditionFlag.MeldingMateria,
            ConditionFlag.Gathering,
            ConditionFlag.OperatingSiegeMachine,
            ConditionFlag.CarryingItem,
            ConditionFlag.CarryingObject,
            ConditionFlag.Mounting,
            ConditionFlag.Mounting71,
            ConditionFlag.ParticipatingInCustomMatch,
            ConditionFlag.PlayingLordOfVerminion,
            ConditionFlag.ChocoboRacing,
            ConditionFlag.PlayingMiniGame,
            ConditionFlag.Performing,
            ConditionFlag.Transformed,
            ConditionFlag.UsingHousingFunctions);
}
