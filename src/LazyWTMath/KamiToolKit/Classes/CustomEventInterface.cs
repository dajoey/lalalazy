using System;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static FFXIVClientStructs.FFXIV.Component.GUI.AtkModuleInterface;

namespace KamiToolKit.Classes;

public unsafe class CustomEventInterface : IDisposable {

    private readonly AtkEventInterface* eventInterface;

    private AtkEventInterface.Delegates.ReceiveEvent? receiveEventDelegate;
    private AtkEventInterface.Delegates.ReceiveEventWithResult? receiveEventDelegate2;

    public CustomEventInterface(AtkEventInterface.Delegates.ReceiveEvent eventHandler, AtkEventInterface.Delegates.ReceiveEventWithResult? eventHandler2 = null) {
        receiveEventDelegate = eventHandler;
        receiveEventDelegate2 = eventHandler2;

        eventInterface = NativeMemoryHelper.UiAlloc<AtkEventInterface>();
        eventInterface->VirtualTable = (AtkEventInterface.AtkEventInterfaceVirtualTable*)NativeMemoryHelper.Malloc((ulong)sizeof(void*) * 3);
        eventInterface->VirtualTable->ReceiveEvent = (delegate* unmanaged<AtkEventInterface*, AtkValue*, AtkValue*, uint, ulong, AtkValue*>)Marshal.GetFunctionPointerForDelegate(receiveEventDelegate);

        if (receiveEventDelegate2 is not null) {
            var ptr = (delegate* unmanaged<AtkEventInterface*, AtkValue*, AtkValue*, uint, ulong, AtkValue*>)Marshal.GetFunctionPointerForDelegate(receiveEventDelegate2);
            eventInterface->VirtualTable->ReceiveEventWithResult = ptr;
        }
        else {
            var nullPtr = (delegate* unmanaged<AtkEventInterface*, AtkValue*, AtkValue*, uint, ulong, AtkValue*>)(delegate* unmanaged<void>)&NullSub;
            eventInterface->VirtualTable->ReceiveEventWithResult = nullPtr;
        }
    }

    public void Dispose() {
        if (eventInterface is null) return;

        NativeMemoryHelper.Free(eventInterface->VirtualTable, (ulong)sizeof(void*) * 3);
        NativeMemoryHelper.UiFree(eventInterface);

        receiveEventDelegate = null;
        receiveEventDelegate2 = null;
    }

    [UnmanagedCallersOnly] private static void NullSub() { }

    public static implicit operator AtkEventInterface*(CustomEventInterface listener) => listener.eventInterface;
}
