using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System.Runtime.CompilerServices;

namespace clib.Extensions;

public static unsafe class InputTimerModuleExtensions {
    public static void ResetAfkTimer(ref this InputTimerModule instance) {
        instance.AfkTimer = 0;
        instance.ContentInputTimer = 0;
        instance.InputTimer = 0;
        
        // Write to 0x1C offset from start of instance using standard unsafe pointer arithmetic
        *(int*)((byte*)Unsafe.AsPointer(ref instance) + 0x1C) = 0;
        
        if (Svc.Objects.LocalPlayer is { OnlineStatus.RowId: 17 }) // away from keyboard
            InfoProxyDetail.Instance()->RefreshOnlineStatus(); // get rid of afk status
    }
}
