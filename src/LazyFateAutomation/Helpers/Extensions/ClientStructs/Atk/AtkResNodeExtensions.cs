using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LazyFateAutomation.Helpers.Extensions;

public static unsafe class AtkResNodeExtensions {
    extension(ref AtkResNode node) {
        public AtkUnitBase* OwnerAddon {
            get {
                fixed (AtkResNode* ptr = &node)
                    return RaptureAtkUnitManager.Instance()->GetAddonByNode(ptr);
            }
        }
    }
}
