using Dalamud.Plugin.Services;

namespace LazyFateAutomation.Helpers.Extensions;

public static class IPartyListExtensions {
    public static bool AllTargetable(this IPartyList party) => party.All(p => p.GameObject?.IsTargetable ?? false);
}
