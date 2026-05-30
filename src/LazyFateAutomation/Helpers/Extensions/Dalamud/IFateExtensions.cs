using Dalamud.Game.ClientState.Fates;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;

namespace LazyFateAutomation.Helpers.Extensions;

public static class IFateExtensions {
    extension(IFate fate) {
        public ItemHandle? EventItem => fate.GameData.Value.EventItem.RowId is not 0 ? fate.GameData.Value.EventItem : null;
        public int EventItemInventoryCount => fate.EventItem?.GetCount() ?? 0;
    }

    public static unsafe FateContext* Struct(this IFate fate) => (FateContext*)fate.Address;
}
