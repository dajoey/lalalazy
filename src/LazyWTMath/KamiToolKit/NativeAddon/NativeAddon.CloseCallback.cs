using System.Linq;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using Lalalazy.HookGuard;

namespace KamiToolKit;

public abstract unsafe partial class NativeAddon {

    private static Hook<AtkUnitBase.Delegates.FireCallback>? fireCallbackHook;

    // OnFireCallback detours running right now; DisposeCloseCallback drains this before pulling the
    // hook (a detour that outlives its hook is the 2026-08-31 / 2026-10-03 game crash family).
    private static readonly HookInFlight InFlight = new();
    
    private static void InitializeCloseCallback() {
        fireCallbackHook ??= DalamudInterface.Instance.GameInteropProvider
            .HookFromAddress<AtkUnitBase.Delegates.FireCallback>(AtkUnitBase.Addresses.FireCallback.Value, OnFireCallback);
        fireCallbackHook.Enable();
    }
    
    private static bool OnFireCallback(AtkUnitBase* thisPtr, uint valueCount, AtkValue* values, bool close) {
        // Count this detour so teardown can wait for it, and never touch a torn-down hook: an
        // exception escaping here takes the game down (2026-10-03 crash family).
        using var inFlight = InFlight.Enter();
        var hook = fireCallbackHook;
        if (hook == null || hook.IsDisposed) {
            return false;
        }

        Log.Excessive($"[{thisPtr->NameString}] OnFireCallback");
        
        foreach (var addon in CreatedAddons) {
            if (addon == thisPtr && close && addon is { RespectCloseAll: true, IsOverlayAddon: false }) {
                addon.Close();
                return true;
            }
        }

        return hook.Original(thisPtr, valueCount, values, close);
    }

    private static void DisposeCloseCallback() {
        if (CreatedAddons.Count is 0 || CreatedAddons.All(addon => addon.IsOverlayAddon)) {
            // Stop new calls, let the detours already inside finish, only then dispose and drop.
            fireCallbackHook?.Disable();
            InFlight.Drain("KamiToolKit.NativeAddon", static m => Log.Warning(m));
            fireCallbackHook?.Dispose();
            fireCallbackHook = null;
        }
    }
}
