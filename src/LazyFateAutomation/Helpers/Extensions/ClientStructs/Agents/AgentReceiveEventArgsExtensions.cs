using Dalamud.Game.Agent.AgentArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LazyFateAutomation.Helpers.Extensions;

public static unsafe class AgentReceiveEventArgsExtensions {
    extension(AgentReceiveEventArgs args) {
        public Span<AtkValue> GetAtkValues() => new((void*)args.AtkValues, (int)args.ValueCount);
    }
}
