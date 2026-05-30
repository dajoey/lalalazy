using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace clib.Extensions;

public static unsafe class TargetSystemExtensions {
    extension(TargetSystem) {
        public static bool InteractWith(ulong instanceId) {
            var obj = GameObjectManager.Instance()->Objects.GetObjectByGameObjectId(instanceId);
            if (obj == null)
                return false;
            TargetSystem.Instance()->InteractWithObject(obj, false);
            return true;
        }

        public static bool IsInteractingWith(ulong instanceId) {
            var obj = GameObjectManager.Instance()->Objects.GetObjectByGameObjectId(instanceId);
            return obj != null && IsInteractingWith(obj);
        }

        public static bool IsInteractingWith(GameObject* obj) {
            return TargetSystem.Instance()->Target == obj && Conditions.Instance()->OccupiedInQuestEvent;
        }
    }
}
