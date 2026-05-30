using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System.Runtime.CompilerServices;

namespace LazyFateAutomation.Helpers.Extensions;

public static unsafe class InputTimerModuleExtensions {
    public static void ResetAfkTimer(ref this InputTimerModule instance) {
        instance.AfkTimer = 0;
        instance.ContentInputTimer = 0;
        instance.InputTimer = 0;
        MemoryHelper.WriteField(Unsafe.AsPointer(ref instance), 0x1C, 0); // this being set doesn't affect getting kicked, but just in case
        if (Svc.Objects.LocalPlayer is { OnlineStatus.RowId: 17 }) // away from keyboard
            InfoProxyDetail.Instance()->RefreshOnlineStatus(); // get rid of afk status
    }
}
