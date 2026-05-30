using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Lumina.Excel.Sheets;

namespace clib.Extensions;

public static class IGameObjectExtensions {
    public static unsafe BattleChara* BattleChara(this IGameObject obj) => (BattleChara*)obj.Address;
    public static unsafe Character* Character(this IGameObject obj) => (Character*)obj.Address;

    public static float DistanceTo(this IGameObject? obj, Vector3 position) => obj is not null ? Vector3.Distance(obj.Position, position) : 0f;
    public static float FlatDistanceTo(this IGameObject? obj, Vector3 position) {
        if (obj is null) return 0f;
        var dx = obj.Position.X - position.X;
        var dz = obj.Position.Z - position.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
    public static bool WithinRange(this IGameObject? obj, Vector3 position, float range) => obj is not null && Vector3.Distance(obj.Position, position) < range;
    public static unsafe bool IsTargetingPlayer(this IGameObject obj) => obj.TargetObjectId == GameObjectManager.Instance()->Objects.IndexSorted[0].Value->GetGameObjectId().ObjectId;
    public static unsafe EventHandlerInfo? EventInfo(this IGameObject obj) {
        if (obj == null) return null;
        var cs = (GameObject*)obj.Address;
        return cs == null || cs->EventHandler == null ? null : cs->EventHandler->Info;
    }
    public static unsafe bool IsInInteractRange(this IGameObject obj) => EventFramework.Instance()->CheckInteractRange((GameObject*)Svc.Objects.LocalPlayer!.Address, (GameObject*)obj.Address, 1, false);
    public static unsafe bool IsInLineOfSight(this IGameObject? obj, Vector3 point) {
        if (obj is null) return false;
        var adjustedOrigin = obj.Position.AddY(2);
        var adjustedTarget = point.AddY(2);
        return !BGCollisionModule.RaycastMaterialFilter(adjustedOrigin, Vector3.Normalize(adjustedTarget - adjustedOrigin), out _, Vector3.Distance(adjustedOrigin, adjustedTarget));
    }

    public static unsafe bool CanRidePillion(this IGameObject? obj) {
        if (obj == null) return false;
        var cont = obj.Character()->Mount;
        return cont.MountedEntityIds[1..].ToArray().Count(x => x != 0) < (Svc.Data.GetRef<Mount>(cont.MountId).ValueNullable?.ExtraSeats ?? 0);
    }
}
