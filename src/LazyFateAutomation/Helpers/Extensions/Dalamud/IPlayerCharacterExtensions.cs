using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Dalamud.Plugin.Services;

namespace clib.Extensions;

public static unsafe class IPlayerCharacterExtensions {
    public static Character* Character(this IPlayerCharacter? pc) => pc != null ? (Character*)pc.Address : null;
    public static bool Available(this IPlayerCharacter? pc) => pc != null;
    public static bool Interactable(this IPlayerCharacter? pc) => pc?.IsTargetable ?? false;
    public static bool IsMoving(this IPlayerCharacter? pc) => pc.Available() && (AgentMap.Instance()->IsPlayerMoving || pc.IsJumping());
    public static bool IsJumping(this IPlayerCharacter? pc) => pc.Available() && (Svc.Condition[ConditionFlag.Jumping] || Svc.Condition[ConditionFlag.Jumping61] || pc.Character()->IsJumping());
    
    public static bool IsAirDismountable(this IPlayerCharacter? pc) {
        var ground = new FFXIVClientStructs.FFXIV.Common.Math.Vector3();
        return UIState.Instance()->GetIsAirDismountable(&ground);
    }

    public static bool IsBusy(this IPlayerCharacter? pc)
        => Svc.Condition.IsUnavailable() ||
        !pc.Interactable() ||
        (pc?.IsCasting ?? false) ||
        pc.IsMoving() ||
        ActionManager.Instance()->AnimationLock > 0 ||
        Svc.Condition[ConditionFlag.InCombat] ||
        !GameMainExtensions.IsTerritoryLoaded;

    public static RowRef<TerritoryType> Territory(this IPlayerCharacter? pc) => Svc.Data.GetRef<TerritoryType>(Svc.ClientState.TerritoryType);

    public static bool CanMount(this IPlayerCharacter? pc) => pc != null && pc.Territory().Value.Mount && PlayerState.Instance()->NumOwnedMounts > 0;
    public static bool Mounted(this IPlayerCharacter? pc) => Svc.Condition[ConditionFlag.Mounted];
    public static bool InFlight(this IPlayerCharacter? pc) => Svc.Condition[ConditionFlag.InFlight];
    
    public static float PackedRotation(this IPlayerCharacter? pc) => (ushort)(((Svc.Objects.LocalPlayer?.Rotation + Math.PI) / (2 * Math.PI) * 65536) ?? 0);

    public static bool Revivable(this IPlayerCharacter? pc) => ECommons.GameHelpers.Player.Revivable;
    public static byte ReviveState(this IPlayerCharacter? pc) => pc != null && pc.IsDead ? AgentRevive.Instance()->ReviveState : (byte)0;
}
