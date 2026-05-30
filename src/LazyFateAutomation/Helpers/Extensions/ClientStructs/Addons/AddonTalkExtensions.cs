using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LazyFateAutomation.Helpers.Extensions;

public static unsafe class AddonTalkExtensions {
    extension(AddonTalk) {
        public static void Progress() {
            var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("Talk");
            if (addon != null && addon->IsReady) {
                var evt = new AtkEvent() { Listener = &addon->AtkEventListener, Target = &AtkStage.Instance()->AtkEventTarget };
                var data = new AtkEventData();
                addon->ReceiveEvent(AtkEventType.MouseClick, 0, &evt, &data);
            }
        }
    }
}
