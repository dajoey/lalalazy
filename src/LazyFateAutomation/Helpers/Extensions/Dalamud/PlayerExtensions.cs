using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace LazyFateAutomation.Helpers.Extensions;

public static unsafe class PlayerExtensions {
    extension(Player) {
        public static byte ReviveState => Player.IsDead ? AgentRevive.Instance()->ReviveState : (byte)0;
    }
}
