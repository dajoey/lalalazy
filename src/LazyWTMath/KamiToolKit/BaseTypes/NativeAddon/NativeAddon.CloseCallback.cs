using System;
using System.Linq;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Internal.Classes;
using Lalalazy.HookGuard;

namespace KamiToolKit.BaseTypes;

public unsafe partial class NativeAddon {
    private static Hook<AtkUnitBase.Delegates.FireCallback>? fireCallbackHook;

    // Fork invariant (lalalazy 0b36d987): FireCallback detours count themselves in flight; teardown
    // disables, drains (1 s cap) and only then disposes the hook, and every detour reads the hook
    // into a local and fails soft when it is gone. A detour that outlives its hook is the
    // 2026-08-31 / 2026-10-03 game-crash family on plugin hot-reload. Upstream tip disposes the
    // hook directly and does not cover this pattern; upstream KamiToolKit commit history carries no
    // HookInFlight-equivalent (checked b4b7cab18).
    private static readonly HookInFlight InFlight = new();

    internal static void InitializeCloseCallback() {
        fireCallbackHook = IGameInteropProvider.Get().HookFromAddress<AtkUnitBase.Delegates.FireCallback>(AtkUnitBase.Addresses.FireCallback.Value, OnFireCallback);
        fireCallbackHook.Enable();
    }

    private static bool OnFireCallback(AtkUnitBase* thisPtr, uint valueCount, AtkValue* values, bool close) {
        // Count this detour so teardown can wait for it, and never run the payload against a
        // torn-down or missing hook: an exception escaping here takes the game down.
        using var inFlight = InFlight.Enter();
        var hook = fireCallbackHook;
        if (hook == null || hook.IsDisposed) {
            return false;
        }

        try {
            IPluginLog.Get().Excessive($"[{thisPtr->NameString}] OnFireCallback");

            foreach (var addon in CreatedAddons) {
                if (addon == thisPtr && close && addon is { RespectCloseAll: true, IsOverlayAddon: false }) {
                    addon.Close();
                    return true;
                }
            }
        }
        catch (Exception e) {
            IPluginLog.Get().Exception(e);
        }

        return hook.Original(thisPtr, valueCount, values, close);
    }

    internal static void DisposeCloseCallback() {
        if (CreatedAddons.Count is 0 || CreatedAddons.All(addon => addon.IsOverlayAddon)) {
            // Stop new calls, let the detours already inside finish, only then dispose and drop.
            fireCallbackHook?.Disable();
            InFlight.Drain("KamiToolKit.NativeAddon", static m => IPluginLog.Get().Warning(m));
            fireCallbackHook?.Dispose();
            fireCallbackHook = null;
        }
    }
}
