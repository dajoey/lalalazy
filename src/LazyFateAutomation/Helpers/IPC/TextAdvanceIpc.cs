using ECommons.EzIpcManager;

namespace LazyFateAutomation.Helpers.IPC;

#nullable disable
[Ipc(Ipc.TextAdvance)]
public class TextAdvanceIpc : BaseIPC {
    public override string Name => "TextAdvance";
    public override string Repo => Nightmare;
    public TextAdvanceIpc() => EzIPC.Init(this, Name);

    [EzIPC] public readonly Func<string, ExternalTerritoryConfig, bool> EnableExternalControl;
    [EzIPC] public readonly Func<string, bool> DisableExternalControl;
    [EzIPC] public readonly Func<bool> IsInExternalControl;

    public sealed class ExternalTerritoryConfig {
        public bool? EnableQuestAccept;
        public bool? EnableQuestComplete;
        public bool? EnableRewardPick;
        public bool? EnableRequestHandin;
        public bool? EnableCutsceneEsc;
        public bool? EnableCutsceneSkipConfirm;
        public bool? EnableTalkSkip;
        public bool? EnableRequestFill;
        public bool? EnableAutoInteract;
    }
}
