using System;
using Ocelot.IPC;

namespace LazyOccultCrescent.IPC;

// GluttonyCombo is a Wrath Combo fork and exposes the same lease-based IPC surface
// under its own prefix (see the repo's docs/IPCExample.cs, which inits with
// EzIPC.Init(typeof(WrathIPC), "GluttonyCombo", ...)).
//
// Ocelot only ships a subscriber for "WrathCombo", so this declares the sibling.
// Ocelot's IPCManager discovers every IPCSubscriber implementation by reflection,
// so simply existing is enough to register it.
public class GluttonyCombo() : IPCSubscriber("GluttonyCombo")
{
    [ECommons.EzIpcManager.EzIPC] public readonly Func<string, string, Guid?> RegisterForLease = null!;

    [ECommons.EzIpcManager.EzIPC] public readonly Action<Guid> ReleaseControl = null!;

    [ECommons.EzIpcManager.EzIPC] public readonly Func<Guid, WrathCombo.AutoRotationConfigOption, object, WrathCombo.SetResult> SetAutoRotationConfigState = null!;

    [ECommons.EzIpcManager.EzIPC] public readonly Func<Guid, string, bool, WrathCombo.SetResult> SetComboOptionState = null!;
}
