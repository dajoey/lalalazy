using System;
using LazyFateAutomation.Helpers.IPC;

namespace LazyFateAutomation;

[AttributeUsage(AttributeTargets.Class)]
public class IpcAttribute(Ipc id) : Attribute {
    public Ipc Id { get; } = id;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresAttribute(Ipc id) : Attribute {
    public Ipc Id { get; } = id;
}

[AttributeUsage(AttributeTargets.Class)]
public class TweakAttribute(bool debug = false, bool outdated = false, bool disabled = false, string? disabledReason = null) : Attribute {
}
