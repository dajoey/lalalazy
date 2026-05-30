using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;

namespace LazyFateAutomation.Helpers.Extensions;

public static unsafe class AtkEventManagerExtensions {
    extension(AtkEventManager em) {
        public List<Pointer<AtkEvent>> Events {
            get {
                var evt = em.Event;
                var list = new List<Pointer<AtkEvent>>();
                while (evt != null)
                    list.Add(evt);
                return list;
            }
        }
    }
}
