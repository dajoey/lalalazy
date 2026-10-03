#nullable enable
using System;
using System.Collections.Generic;

namespace Lalalazy.Hub;

/// <summary>
/// Shared SOURCE (never a shared DLL: every plugin lives in its own load context). The lalalazy hub
/// protocol, version 1. A plugin that wants the hub window to control it declares controls on a
/// <see cref="HubEndpoint"/>; the hub reads and writes them over Dalamud IPC as JSON strings, so no
/// type ever crosses a load context. This folder holds the pure logic and has no Dalamud types, so
/// tests/LalaHub.Harness compiles it directly.
/// </summary>
public static class HubProtocol
{
    /// <summary>Version of the descriptor, state and result JSON. The hub ignores any other value.</summary>
    public const int Version = 1;

    public const int MaxControls = 32;
    public const int MaxIdLength = 40;
    public const int MaxLabelLength = 60;
    public const int MaxTipLength = 200;
    public const int MaxChoices = 12;
    public const int MaxChoiceLength = 40;
    public const int MaxConfirmLength = 200;
    public const int MaxPluginLength = 64;
    public const int MaxVersionLength = 32;

    public static string EndpointName(string internalName, string method) => "Lala." + internalName + "." + method;
}

public enum ControlKind
{
    /// <summary>On or off.</summary>
    Toggle,

    /// <summary>A number between min and max, changed in steps.</summary>
    Stepper,

    /// <summary>One of a short list, picked by index.</summary>
    Choice,

    /// <summary>A one-shot action with no value.</summary>
    Button,
}

/// <summary>What a setter or button reports back. A refusal carries a reason shown to the player.</summary>
public readonly struct SetOutcome
{
    public bool Ok { get; }
    public string Why { get; }
    public bool Locked { get; }

    public SetOutcome(bool ok, string why = "", bool locked = false)
    {
        Ok = ok;
        Why = why ?? "";
        Locked = locked;
    }

    public static SetOutcome Success => new SetOutcome(true);

    public static SetOutcome Refuse(string why, bool locked = false) => new SetOutcome(false, why, locked);
}

/// <summary>
/// Whether a control can be used right now. <c>Locked</c> means something else owns the setting
/// (for example another plugin holds Gluttony Combo's auto-rotation); <c>Enabled</c> false means it
/// cannot be used at the moment for another reason. Either way a write is refused and the setter does not run.
/// </summary>
public sealed record ControlState(bool Enabled = true, bool Locked = false, string Why = "");

/// <summary>One declared control. Built through <see cref="HubEndpoint"/>; not constructed by adapters.</summary>
internal sealed class ControlDef
{
    public string Id = "";
    public string Label = "";
    public ControlKind Kind;
    public string Group = "";
    public string Tip = "";
    public bool Master;
    public double Min;
    public double Max;
    public double Step = 1;
    public string Unit = "";
    public int Decimals;
    public IReadOnlyList<string> Choices = Array.Empty<string>();
    public string Confirm = "";

    /// <summary>Current value: bool for a toggle, double for a stepper, int index for a choice, null for a button.</summary>
    public Func<object?>? Get;

    /// <summary>Applies an already validated and clamped value.</summary>
    public Func<object, SetOutcome>? Set;

    /// <summary>The action of a button.</summary>
    public Func<SetOutcome>? Run;

    public Func<ControlState>? State;
}
