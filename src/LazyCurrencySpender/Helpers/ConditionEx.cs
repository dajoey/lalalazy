using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace CurrencySpender.Helpers;

// Fork addition (upstream sync 2026-10): classic extension-form port of the ICondition
// extension members upstream defines with C# 14 extension() blocks in Helpers/UiHelper.cs
// (a file the fork replaces with its own UIHelper.cs). MovementTask.cs calls
// Service.Condition.IsBetweenAreas().
internal static class ConditionEx
{
    public static bool IsBetweenAreas(this ICondition condition)
        => condition[ConditionFlag.BetweenAreas];
}
