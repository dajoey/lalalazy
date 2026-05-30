using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace clib.Extensions;

public static class ItemFinderModuleExtensions {
    extension(ref ItemFinderModule module) {
        public Span<uint> GlamourDresserBaseItemIds => module.GlamourDresserItemIds.ToArray().Select(id => ItemUtil.GetBaseId(id).ItemId).ToArray();
    }
}
