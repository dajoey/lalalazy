using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace clib.Extensions;

public static unsafe class InfoProxyNoviceNetworkExtensions {
    extension(InfoProxyNoviceNetwork) {
        public static bool IsInNoviceNetwork() => InfoProxyNoviceNetwork.Instance()->Flags == 1;
    }
}
