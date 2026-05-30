using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System.Runtime.CompilerServices;

namespace clib.Extensions;

public static unsafe class GameObjectExtensions {
    public static BattleChara* BattleChara(ref this GameObject obj) => (BattleChara*)Unsafe.AsPointer(ref obj);
    public static Character* Character(ref this GameObject obj) => (Character*)Unsafe.AsPointer(ref obj);
}
