using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LazyFateAutomation.Helpers.Extensions;

public static unsafe class AddonArgsExtensions {
    extension(AddonArgs args) {
        public unsafe AtkEventListener* EventListener => &args.GetAddon<AtkUnitBase>()->AtkEventListener;
    }

    public static T* GetAddon<T>(this AddonArgs args) where T : unmanaged => (T*)args.Addon.Address;

    public static AtkEvent* GenerateEvent(this AddonArgs args) {
        var evt = new AtkEvent() { Listener = args.EventListener, Target = &AtkStage.Instance()->AtkEventTarget };
        return &evt;
    }

    public static AtkEventData* GenerateEventData(this AddonArgs _) {
        var data = new AtkEventData();
        return &data;
    }

    public static void ReceiveEvent(this AddonArgs args, AtkEventType eventType, int eventParam, AtkEvent* atkEvent = null, AtkEventData* atkEventData = null) {
        var evt = atkEvent == null ? args.GenerateEvent() : atkEvent;
        var evtData = atkEventData == null ? args.GenerateEventData() : atkEventData;
        args.GetAddon<AtkUnitBase>()->ReceiveEvent(eventType, eventParam, evt, evtData);
    }
}
