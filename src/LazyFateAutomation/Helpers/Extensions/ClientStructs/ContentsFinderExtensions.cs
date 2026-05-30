using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace clib.Extensions;

public static class ContentsFinderExtensions {
    public static void ResetFlags(ref this ContentsFinder cf) {
        cf.IsExplorerMode = false;
        cf.IsLevelSync = false;
        cf.IsLimitedLevelingRoulette = false;
        cf.IsMinimalIL = false;
        cf.IsSilenceEcho = false;
        cf.IsUnrestrictedParty = false;
        cf.LootRules = ContentsFinder.LootRule.Normal;
    }
}
