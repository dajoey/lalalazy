// Fork (bst1013): what stock BossMod Reborn's public IPC says about its own state, turned into the
// conflict-check verdicts. Pure, deliberately free of Dalamud and ECommons types:
// tests/GluttonyCombo.ConflictCheckHarness compiles THIS file and feeds it real-shaped replies, so any
// game type leaking in breaks that build, which is the point.
//
// Why: the BossModReborn branch of the conflict check used to reflect into BMR's private
// RotationModuleManager.ActiveModules (a list of ActiveModule(DataIndex, Definition, Module) records) and read
// a "Type" property off each RotationModule, which has none, so the probe threw a NullReferenceException on
// every 8 s check while any preset was active and the rest of the check (HasEntries included) never ran. It
// also read Preset."Module" where the field is "Modules", so the active preset's module list was never seen.
// Stock BMR publishes everything the check needs: Presets.GetActive / GetForceDisabled / Get(name) (the preset
// as JSON, BMR Autorotation/Preset.cs JsonPresetConverter.Write: {"Name":..,"Modules":{"<Type.FullName>":[..]}}),
// AI.GetPreset, and Configuration([node, field], false) for any displayed config field.

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace GluttonyCombo.Services.IPC_Subscriber;

/// <summary> What the active BossMod Reborn preset would do beside Gluttony. </summary>
/// <param name="PresetName">The active preset, or null when none is active (or rotation is force-disabled).</param>
/// <param name="AutoTarget">The preset carries an automatic-targeting module.</param>
/// <param name="JobRotation">The preset carries a module outside MiscAI (a job rotation or utility module).</param>
/// <param name="JobActionModule">
///     The first module that presses Beastmaster job actions beside Gluttony: a BST rotation module or a Crucible AI
///     module. Null when there is none.
/// </param>
/// <param name="ModuleCount">How many modules the preset lists.</param>
/// <param name="ParseError">Why the preset JSON could not be read, or null.</param>
internal readonly record struct BossModPresetVerdict(
    string? PresetName,
    bool AutoTarget,
    bool JobRotation,
    string? JobActionModule,
    int ModuleCount,
    string? ParseError)
{
    /// <summary> Whether the preset runs anything that queues actions on its own. </summary>
    internal bool Autorotation => JobRotation || JobActionModule is not null;
}

internal static class BossModRebornState
{
    /// <summary> Multiboxing preset the targeting check has always skipped. </summary>
    internal const string MultiboxPresetName = "VBM Multibox";

    /// <summary>
    ///     A cached preset verdict is re-read after this long even when the name and BMR's
    ///     Configuration.LastModified are unchanged: editing a preset's modules fires BMR's PresetModified event,
    ///     not its config Modified event, so LastModified alone would miss it.
    /// </summary>
    internal static readonly TimeSpan PresetMaxAge = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     The verdict for the active preset, from <c>Presets.GetForceDisabled</c>, <c>Presets.GetActive</c> and
    ///     <c>Presets.Get(name)</c>. Automatic targeting = any module key containing "AutoTarget" (skipped for the
    ///     multibox preset, as before); job rotation = any module key outside "MiscAI".
    /// </summary>
    internal static BossModPresetVerdict EvaluatePreset(string? activeName, bool forceDisabled, string? presetJson)
    {
        // ForceDisable is an empty preset named ""; no preset at all reads null.
        if (forceDisabled || string.IsNullOrEmpty(activeName))
            return default;

        // The name resolved to nothing in BMR's database (deleted or renamed while active).
        if (presetJson is null)
            return new BossModPresetVerdict(activeName, false, false, null, 0, null);

        List<string> keys;
        try
        {
            keys = ModuleKeys(presetJson);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new BossModPresetVerdict(activeName, false, false, null, 0, e.GetType().Name);
        }

        var autoTarget = false;
        var jobRotation = false;
        string? jobActionModule = null;
        foreach (var key in keys)
        {
            if (key.Contains("AutoTarget", StringComparison.Ordinal))
                autoTarget = true;
            if (!key.Contains("MiscAI", StringComparison.Ordinal))
                jobRotation = true;
            if (jobActionModule is null && IsJobActionModule(key))
                jobActionModule = key;
        }

        if (string.Equals(activeName, MultiboxPresetName, StringComparison.Ordinal))
            autoTarget = false;

        return new BossModPresetVerdict(activeName, autoTarget, jobRotation, jobActionModule, keys.Count, null);
    }

    /// <summary> The module type names of a <c>Presets.Get</c> reply, in preset order. </summary>
    /// <exception cref="JsonException">The reply is not JSON.</exception>
    /// <exception cref="KeyNotFoundException">The reply has no <c>Modules</c> object.</exception>
    internal static List<string> ModuleKeys(string presetJson)
    {
        using var doc = JsonDocument.Parse(presetJson);
        var modules = doc.RootElement.GetProperty("Modules"); // KeyNotFoundException when absent
        if (modules.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Modules is not an object");

        var keys = new List<string>();
        foreach (var module in modules.EnumerateObject())
            keys.Add(module.Name);
        return keys;
    }

    /// <summary>
    ///     A module that presses Beastmaster job actions beside Gluttony: a BST rotation module (e.g.
    ///     <c>BossMod.Autorotation.xan.BST</c>) or a Crucible AI module (one that presses Snarl, Challenge,
    ///     Quelling Wave or Soul Crush). Boss modules never appear in a preset, so "Crucible" only matches these.
    /// </summary>
    internal static bool IsJobActionModule(string key) =>
        (key.Contains(".Autorotation.", StringComparison.Ordinal) && key.Contains(".BST", StringComparison.Ordinal))
        || key.Contains("Crucible", StringComparison.Ordinal);

    /// <summary> The last segment of a module type name, for warning text (<c>xan.BST</c> reads "BST"). </summary>
    internal static string ShortModuleName(string key)
    {
        var dot = key.LastIndexOf('.');
        return dot < 0 || dot == key.Length - 1 ? key : key[(dot + 1)..];
    }

    /// <summary>
    ///     Whether the cached preset verdict must be re-read: no cache yet, another active preset, BMR's
    ///     Configuration.LastModified moved, or the cache is <see cref="PresetMaxAge"/> old.
    /// </summary>
    internal static bool PresetNeedsRead(
        bool hasCache, string? cachedName, DateTime cachedLastModified, DateTime cachedAtUtc,
        string? activeName, DateTime lastModified, DateTime nowUtc) =>
        !hasCache
        || !string.Equals(cachedName, activeName, StringComparison.Ordinal)
        || cachedLastModified != lastModified
        || nowUtc - cachedAtUtc >= PresetMaxAge;

    /// <summary>
    ///     A boolean read back through <c>BossMod.Configuration([node, field], false)</c>: BMR answers a one-line
    ///     list holding the value's ToString() ("True" / "False"), or error lines. Null when it is not one of those.
    /// </summary>
    internal static bool? ConsoleBool(IReadOnlyList<string>? reply) =>
        reply is { Count: 1 } && bool.TryParse(reply[0], out var value) ? value : null;

    /// <summary>
    ///     Whether BMR's AI picks targets on its own. Its behaviour (AI/AIBehaviour.cs) sets a forced target only
    ///     with the AI on, an AI preset chosen, ForbidActions off; with ManualTarget on it keeps the player's own
    ///     target. Unreadable values count as "not retargeting", so a failed read never raises a conflict.
    /// </summary>
    internal static bool AiTargetsOnItsOwn(
        bool aiOn, string? aiPreset, IReadOnlyList<string>? manualTarget, IReadOnlyList<string>? forbidActions) =>
        aiOn
        && !string.IsNullOrEmpty(aiPreset)
        && ConsoleBool(manualTarget) == false
        && ConsoleBool(forbidActions) == false;

    /// <summary>
    ///     Arguments for <c>BossMod.Configuration.SetTransient</c>: the console's [node, field, value] (BMR
    ///     Config/ConfigRoot.cs SetTransient -> ResolveField; the value goes through float.Parse with the calling
    ///     thread's culture, and IPC runs on the caller's thread, so it is written in the caller's culture).
    /// </summary>
    internal static List<string> MaxDistanceTransientArgs(float distance, IFormatProvider culture) =>
        ["AIConfig", "MaxDistanceToTarget", distance.ToString(culture)];
}
