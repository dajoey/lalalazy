#region

using ECommons;
using ECommons.DalamudServices;
using ECommons.EzIpcManager;
using ECommons.Logging;
using ECommons.Reflection;
using GluttonyCombo.Services.IPC;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

// ReSharper disable InlineTemporaryVariable

#endregion

namespace GluttonyCombo.Services.IPC_Subscriber;

internal sealed class BossModIPC(
    string pluginName,
    Version validVersion)
    : ReusableIPC(pluginName, validVersion)
{
    public bool HasAutomaticActionsQueued()
    {
        if (!IsEnabled)
        {
            PluginLog.Debug($"[ConflictingPlugins] [{PluginName}] " +
                            $"IPC is not enabled.");
            return false;
        }

        try
        {
            var hasEntries = IpcStallTrace.Time("ActionQueue.HasEntries", _hasEntries);
            PluginLog.Verbose(
                $"[ConflictingPlugins] [{PluginName}] `ActionQueue.HasEntries`: " +
                hasEntries);
            return hasEntries;
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[ConflictingPlugins] [{PluginName}] " +
                              $"`ActionQueue.HasEntries` failed:" +
                              e.ToStringFull());
            return false;
        }
    }

    private object? GetTickService()
    {
        var tickServiceType = Plugin.GetType().Assembly.GetType("BossMod.Services.TickService");
        if (tickServiceType is null)
        {
            PluginLog.Debug(
                    $"[ConflictingPlugins] [{PluginName}] Could not access TickServiceType");
            return null;
        }

        var host = Plugin.GetType().BaseType.GetProperty("Host").GetValue(Plugin);
        if (host is null)
        {
            PluginLog.Debug(
                    $"[ConflictingPlugins] [{PluginName}] Could not access Host");
            return null;
        }

        var services = host.GetType().GetProperty("Services").GetValue(host);
        if (services is null)
        {
            PluginLog.Debug(
                    $"[ConflictingPlugins] [{PluginName}] Could not access Services");
            return null;
        }

        var tickServiceValue = services.GetType().GetMethod("GetService").Invoke(services, [tickServiceType]);
        if (tickServiceValue is null)
        {
            PluginLog.Debug(
                    $"[ConflictingPlugins] [{PluginName}] Could not access TickServiceValue");
            return null;
        }

        return tickServiceValue;
    }

    // Original BossMod (VBM) only: walks TickService._rotation.Presets. BossMod Reborn is read through its
    // stock IPC instead (ActivePresetVerdict); the reborn branch of this walk reflected into BMR internals that
    // stock BMR does not have and threw on every check while a preset was active (bst1013).
    private IEnumerable<object?> GetModules()
    {
        var entryPoint = GetTickService();
        if (entryPoint is null)
        {
            PluginLog.Debug(
                    $"[ConflictingPlugins] [{PluginName}] Could not access entry point");
            yield break;
        }

        var rotationManager = entryPoint.GetFoP("_rotation");
        if (rotationManager is null)
        {
            PluginLog.Debug(
                    $"[ConflictingPlugins] [{PluginName}] Could not access RotationManager");
            yield break;
        }

        var presets = rotationManager.GetFoP("Presets");
        if (presets is null)
        {
            PluginLog.Debug(
                    $"[ConflictingPlugins] [{PluginName}] Could not access Presets");
            yield break;
        }


        var count = (int)presets.GetType().GetProperty("Count")!.GetValue(presets)!;
        for (int i = 0; i < count; i++)
        {
            var item = presets.GetType().GetProperty("Item")!.GetValue(presets, new object[] { i });
            if (item is null)
                continue;

            var modules = item?.GetFoP("Modules");
            if (modules is null)
            {
                continue;
            }

            var modCount = (int)modules.GetType().GetProperty("Count").GetValue(modules)!;

            for (int p = 0; p < modCount; p++)
            {
                var module = modules.GetType().GetProperty("Item")!.GetValue(modules, new object[] { p });
                yield return module;
            }
        }
    }

    private IEnumerable<object?> GetModuleTypes()
    {
        foreach (var module in GetModules())
        {
            var moduleType = module.GetType().GetProperty("Type").GetValue(module);
            yield return moduleType;
        }
    }

    private IEnumerable<object?> GetPresets()
    {
        var entryPoint = GetTickService();
        if (entryPoint is null)
        {
            PluginLog.Debug(
                    $"[ConflictingPlugins] [{PluginName}] Could not access entry point");
            yield break;
        }

        var rotationManager = entryPoint.GetFoP("_rotation");
        if (rotationManager is null)
        {
            PluginLog.Debug(
                    $"[ConflictingPlugins] [{PluginName}] Could not access RotationManager");
            yield break;
        }

        var presets = rotationManager.GetFoP("Presets");
        if (presets is null)
        {
            PluginLog.Debug(
                    $"[ConflictingPlugins] [{PluginName}] Could not access Presets");
            yield break;
        }


        var count = (int)presets.GetType().GetProperty("Count")!.GetValue(presets)!;
        for (int i = 0; i < count; i++)
        {
            var item = presets.GetType().GetProperty("Item")!.GetValue(presets, new object[] { i });
            yield return item;
        }
    }

    // ------------------------------------------------------------------ BossMod Reborn: stock preset IPC

    private bool _presetCached;
    private string? _presetCachedName;
    private DateTime _presetCachedStamp;
    private DateTime _presetCachedAtUtc;
    private BossModPresetVerdict _presetVerdict;

    /// <summary>
    ///     Fork (bst1013): the last preset verdict read by <see cref="ActivePresetVerdict"/> (default before the
    ///     first read). The conflict warnings name the preset and the job-action module from it.
    /// </summary>
    internal BossModPresetVerdict LastPresetVerdict => _presetVerdict;

    /// <summary>
    ///     Fork (bst1013): what BossMod Reborn's active preset does, from stock IPC only:
    ///     <c>Presets.GetForceDisabled</c>, <c>Presets.GetActive</c>, then <c>Presets.Get(name)</c> (the preset as
    ///     JSON) when the name or <c>Configuration.LastModified</c> changed or the cached answer is a minute old.
    ///     Decision logic in <see cref="BossModRebornState"/> (harness-proven). Throws when an IPC call fails or
    ///     the reply cannot be read, so the calling probe reports it by name.
    /// </summary>
    internal BossModPresetVerdict ActivePresetVerdict()
    {
        var forceDisabled = IpcStallTrace.Time("Presets.GetForceDisabled", _presetsGetForceDisabled);
        var name = forceDisabled ? null : IpcStallTrace.Time("Presets.GetActive", _presetsGetActive);
        var stamp = LastModified();
        var now = DateTime.UtcNow;
        if (!BossModRebornState.PresetNeedsRead(_presetCached, _presetCachedName, _presetCachedStamp,
                _presetCachedAtUtc, name, stamp, now))
            return _presetVerdict;

        var json = string.IsNullOrEmpty(name)
            ? null
            : IpcStallTrace.Time("Presets.Get", () => _presetsGet(name));
        _presetVerdict = BossModRebornState.EvaluatePreset(name, forceDisabled, json);
        _presetCached = true;
        _presetCachedName = name;
        _presetCachedStamp = stamp;
        _presetCachedAtUtc = now;
        PluginLog.Verbose(
            $"[ConflictingPlugins] [{PluginName}] Active preset read: {_presetVerdict}");

        if (_presetVerdict.ParseError is { } error)
            throw new InvalidOperationException(
                $"`Presets.Get` reply for the active preset could not be read ({error})");
        return _presetVerdict;
    }

    public bool IsAutoTargetingEnabled(bool isReborn)
    {
        if (!PluginIsLoaded)
        {
            PluginLog.Debug($"[ConflictingPlugins] [{PluginName}] " +
                            $"Plugin is not loaded.");
            return false;
        }

        if (isReborn)
            return ActivePresetVerdict().AutoTarget;

        var presets = GetPresets();
        foreach (var preset in presets)
        {
            if (preset.GetFoP<string>("Name") == "VBM Multibox")
                continue;

            var modules = preset?.GetFoP("Modules");
            if (modules is null)
            {
                continue;
            }

            var modCount = (int)modules.GetType().GetProperty("Count").GetValue(modules)!;

            for (int p = 0; p < modCount; p++)
            {
                var module = modules.GetType().GetProperty("Item")!.GetValue(modules, new object[] { p });
                var moduleType = module.GetType().GetProperty("Type").GetValue(module);
                if (moduleType is Type t && t.FullName.Contains("AutoTarget"))
                    return true;
            }
        }

        return false;
    }

    public bool IsUsingCustomQueuing()
    {
        if (!PluginIsLoaded)
        {
            PluginLog.Debug($"[ConflictingPlugins] [{PluginName}] " +
                            $"Plugin is not loaded.");
            return false;
        }

        var tickServiceValue = GetTickService();

        var amex = tickServiceValue.GetFoP("_amex");
        if (amex == null)
        {
            PluginLog.Debug(
                $"[ConflictingPlugins] [{PluginName}] Could not access AMEx field");
            return false;
        }

        var manualQueue = amex.GetFoP("_manualQueue");
        if (manualQueue == null)
        {
            PluginLog.Debug(
                $"[ConflictingPlugins] [{PluginName}] Could not access AMEx.Manual field");
            return false;
        }

        var manualQueueConfig = manualQueue.GetFoP("_config");
        if (manualQueueConfig == null)
        {
            PluginLog.Debug(
                $"[ConflictingPlugins] [{PluginName}] Could not access AMEx.Manual.Config field");
            return false;
        }

        var customQueuingEnabled = manualQueueConfig.GetFoP<bool>("UseManualQueue");

        PluginLog.Verbose(
            $"[ConflictingPlugins] [{PluginName}] `ManualQueue.Enabled`: {customQueuingEnabled}");

        return customQueuingEnabled;
    }

    public bool IsUsingAutorotation(bool isReborn)
    {
        if (!PluginIsLoaded)
        {
            PluginLog.Debug($"[ConflictingPlugins] [{PluginName}] " +
                            $"Plugin is not loaded.");
            return false;
        }

        if (isReborn)
            return ActivePresetVerdict().Autorotation;

        var modules = GetModuleTypes();
        foreach (var module in modules)
        {
            if (module is Type mt && !mt.FullName.Contains("MiscAI"))
                return true;
        }

        return false;
    }

    public bool HasAutomaticActionsQueuedReborn()
    {
        if (!IsEnabled)
        {
            PluginLog.Debug($"[ConflictingPlugins] [{PluginName}] " +
                            $"IPC is not enabled.");
            return false;
        }

        try
        {
            var hasEntries = IpcStallTrace.Time("ActionQueue.HasEntries", _hasEntries);
            PluginLog.Verbose(
                $"[ConflictingPlugins] [{PluginName}] `ActionQueue.HasEntries`: " +
                hasEntries);
            return hasEntries;
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[ConflictingPlugins] [{PluginName}] " +
                              $"`ActionQueue.HasEntries` failed:" +
                              e.ToStringFull());
            return false;
        }
    }

    /// <summary>
    ///     Fork (bst1013): BossMod Reborn's "Use custom queueing for manually pressed actions", read through the
    ///     stock <c>Configuration</c> console IPC (<c>["ActionTweaksConfig", "UseManualQueue"]</c>, never saved)
    ///     instead of reflecting into <c>_amex._manualQueue._config</c>.
    /// </summary>
    public bool IsUsingCustomQueuingReborn()
    {
        if (!PluginIsLoaded)
        {
            PluginLog.Debug($"[ConflictingPlugins] [{PluginName}] " +
                            $"Plugin is not loaded.");
            return false;
        }

        var reply = IpcStallTrace.Time("Configuration",
            () => _configuration(["ActionTweaksConfig", "UseManualQueue"], false));
        var customQueuingEnabled = BossModRebornState.ConsoleBool(reply) == true;

        PluginLog.Verbose(
            $"[ConflictingPlugins] [{PluginName}] `ManualQueue.Enabled`: {customQueuingEnabled}");

        return customQueuingEnabled;
    }

    public bool IsAITargetingEnabled(bool isReborn)
    {
        if (!PluginIsLoaded)
        {
            PluginLog.Debug($"[ConflictingPlugins] [{PluginName}] " +
                            $"Plugin is not loaded.");
            return false;
        }

        var entryPoint = isReborn ? Plugin : GetTickService();
        if (isReborn)
        {
            // Fork (bst1013): BMR's AI picks targets only with the AI on, an AI preset chosen, ForbidActions off and
            // ManualTarget off (AI/AIBehaviour.cs). AI on/off is not published, so that one bit stays a reflection
            // read of AIManager.Beh; the rest comes from stock IPC (AI.GetPreset, Configuration console reads).
            var aiOn = Plugin.GetFoP("_ai")?.GetFoP("Beh") is not null;
            if (!aiOn)
                return false;

            return BossModRebornState.AiTargetsOnItsOwn(
                aiOn,
                IpcStallTrace.Time("AI.GetPreset", _aiGetPreset),
                IpcStallTrace.Time("Configuration", () => _configuration(["AIConfig", "ManualTarget"], false)),
                IpcStallTrace.Time("Configuration", () => _configuration(["AIConfig", "ForbidActions"], false)));
        }
        else
        {
            var rotationManager = entryPoint.GetFoP("_rotation");
            if (rotationManager is null)
            {
                PluginLog.Debug(
                        $"[ConflictingPlugins] [{PluginName}] Could not access RotationManager");
                return false;
            }

            var ai = isReborn ? Plugin.GetFoP("_aiConfig") : rotationManager.GetFoP("_aiConfig");

            var enabled = ai.GetFoP<bool>("Enabled");
            var target = ai.GetFoP<bool>("ForbidActions");

            return enabled && !target;
        }
    }

    public DateTime LastModified()
    {
        if (!IsEnabled) return DateTime.MinValue;

        try
        {
            return IpcStallTrace.Time("Configuration.LastModified", _lastModified);
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[ConflictingPlugins] [{PluginName}] " +
                              $"`Configuration.LastModified` failed: " +
                              e.ToStringFull());
            return DateTime.MinValue;
        }
    }

    public void SetMaxDistanceToTarget(float distance)
    {
        if (!PluginIsLoaded)
        {
            PluginLog.Debug($"[ConflictingPlugins] [{PluginName}] " +
                            $"Plugin is not loaded.");
            return;
        }

        try
        {
            var ai = Plugin.GetFoP("_ai");
            if (ai == null)
            {
                PluginLog.Debug(
                    $"[ConflictingPlugins] [{PluginName}] Could not access _ai field");
                return;
            }

            var aiConfig = ai.GetFoP("Config") ?? ai.GetFoP("_config");
            if (aiConfig == null)
            {
                PluginLog.Debug(
                    $"[ConflictingPlugins] [{PluginName}] Could not access AI.Config field");
                return;
            }

            aiConfig.SetFoP("MaxDistanceToTarget", distance);
            PluginLog.Debug($"[ConflictingPlugins] [{PluginName}] Set MaxDistanceToTarget to {distance}");
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[ConflictingPlugins] [{PluginName}] " +
                              $"Failed to set MaxDistanceToTarget: " +
                              e.ToStringFull());
        }
    }

    private readonly IpcEndpointLatch _setTransientLatch = new();

    /// <summary>
    ///     Fork (bst1013): BossMod Reborn's AI distance for the current role, as a TRANSIENT override through
    ///     stock <c>Configuration.SetTransient(["AIConfig", "MaxDistanceToTarget", value])</c> (BMR
    ///     Config/ConfigRoot.cs SetTransient): the user's saved value stays in BMR's file and returns on restart,
    ///     and a value the user sets while it is overridden wins. The reflection write this replaces changed the
    ///     live node, which BMR then saved over the user's own value. <c>Configuration.ClearTransient</c> is never
    ///     called: it restores every plugin's overrides, not only ours. BMR builds before 7.5.6.34 do not register
    ///     SetTransient; then nothing is written and that is said once.
    /// </summary>
    public void SetMaxDistanceToTargetTransient(float distance)
    {
        if (!TryGetProviderGeneration(out var generation) || !_setTransientLatch.ShouldCall(generation))
            return;

        // BMR parses the value with float.Parse in the calling thread's culture; IPC runs on this thread.
        var args = BossModRebornState.MaxDistanceTransientArgs(distance, CultureInfo.CurrentCulture);
        try
        {
            var problems = IpcStallTrace.Time("Configuration.SetTransient", () => _setTransient(args));
            _setTransientLatch.RecordSuccess(generation);
            if (problems is { Count: > 0 })
                PluginLog.Warning($"[ConflictingPlugins] [{PluginName}] `Configuration.SetTransient` " +
                                  $"refused MaxDistanceToTarget={args[2]}: {string.Join(" | ", problems)}");
            else
                PluginLog.Debug($"[ConflictingPlugins] [{PluginName}] MaxDistanceToTarget={args[2]} (transient, not saved)");
        }
        catch (Exception e)
        {
            if (_setTransientLatch.RecordFailure(generation, e))
                PluginLog.Information($"[ConflictingPlugins] [{PluginName}] `Configuration.SetTransient` is not " +
                                      $"provided by this BossMod Reborn build ({generation.Split('#')[0]}); its AI " +
                                      $"distance to target is left at the user's own value.");
            else
                PluginLog.Warning($"[ConflictingPlugins] [{PluginName}] `Configuration.SetTransient` failed: " +
                                  $"{e.GetType().Name}: {e.Message}");
        }
    }

    internal bool IsSmartTargetEnabled()
    {
        if (!PluginIsLoaded)
        {
            PluginLog.Debug($"[ConflictingPlugins] [{PluginName}] " +
                            $"Plugin is not loaded.");
            return false;
        }

        var amex = Plugin.GetFoP("_amex");
        if (amex == null)
        {
            PluginLog.Debug(
                $"[ConflictingPlugins] [{PluginName}] Could not access AMEx field");
            return false;
        }

        var manualQueue = amex.GetFoP("_manualQueue");
        if (manualQueue == null)
        {
            PluginLog.Debug(
                $"[ConflictingPlugins] [{PluginName}] Could not access AMEx.Manual field");
            return false;
        }

        var manualQueueConfig = manualQueue.GetFoP("_config");
        if (manualQueueConfig == null)
        {
            PluginLog.Debug(
                $"[ConflictingPlugins] [{PluginName}] Could not access AMEx.Manual.Config field");
            return false;
        }

        var smartTargetEnabled = manualQueueConfig.GetFoP<bool>("SmartTargeting");

        PluginLog.Verbose(
            $"[ConflictingPlugins] [{PluginName}] `SmartTarget.Enabled`: {smartTargetEnabled}");

        return smartTargetEnabled;
    }

    private readonly IpcEndpointLatch _priorityTargetLatch = new();
    private long? _priorityTargetUiCheckedMs; // null = never checked (see IpcEndpointLatch.RecheckDue)
    private bool _priorityTargetUiMissing;

    /// <summary>
    ///     Fork (1.0.4.241): BossMod Reborn's fight-aware combat target for the active
    ///     module — the forced target when a module demands one, otherwise the head of
    ///     BMR's priority-target ordering. Returns 0 when BMR is absent or older than the
    ///     endpoint, has no active module, or the module expresses no attackable opinion;
    ///     callers (the "Use boss-mod targeting when active" checkbox path) fall back to
    ///     their own targeting on 0.
    ///     <para>
    ///         bst1013: stock BossMod Reborn registers no <c>Hints.PriorityTarget</c> (only a fork did). The first
    ///         endpoint-missing error latches it for this BMR load (<see cref="IpcEndpointLatch"/>), writes one
    ///         Information line and stops calling; a BMR reload or update re-arms it.
    ///     </para>
    /// </summary>
    public ulong GetPriorityTargetId()
    {
        if (!TryGetProviderGeneration(out var generation) || !_priorityTargetLatch.ShouldCall(generation))
            return 0ul;

        try
        {
            var id = IpcStallTrace.Time("Hints.PriorityTarget", _priorityTarget);
            _priorityTargetLatch.RecordSuccess(generation);
            return id;
        }
        catch (Exception e)
        {
            if (_priorityTargetLatch.RecordFailure(generation, e))
                PluginLog.Information($"[BossModTargeting] [{PluginName}] `Hints.PriorityTarget` is not provided " +
                                      $"by this BossMod Reborn build ({generation.Split('#')[0]}); boss-mod targeting " +
                                      $"is inactive and the targeting mode applies.");
            else
                PluginLog.Verbose($"[BossModTargeting] [{PluginName}] " +
                                  $"`Hints.PriorityTarget` unavailable: {e.Message}");
            return 0ul;
        }
    }

    /// <summary>
    ///     Fork (bst1013): whether the loaded BossMod Reborn lacks <c>Hints.PriorityTarget</c>, for the settings
    ///     UI. Re-read at most once a second; when this BMR load has not been asked yet, asks once (the answer
    ///     outside a fight is harmless).
    /// </summary>
    public bool IsPriorityTargetEndpointMissing()
    {
        var now = Environment.TickCount64;
        if (!IpcEndpointLatch.RecheckDue(_priorityTargetUiCheckedMs, now, 1000))
            return _priorityTargetUiMissing;
        _priorityTargetUiCheckedMs = now;

        if (!TryGetProviderGeneration(out var generation))
            return _priorityTargetUiMissing = false;
        if (_priorityTargetLatch.StateFor(generation) == IpcEndpointLatch.State.Unknown)
            GetPriorityTargetId();
        return _priorityTargetUiMissing =
            _priorityTargetLatch.StateFor(generation) == IpcEndpointLatch.State.Missing;
    }

    /// <summary>
    ///     Fork (bst1013): the loaded, version-valid provider plugin and its generation (version + instance), in
    ///     one plugin-list scan. A reload creates a new instance, so a latched endpoint is asked again.
    /// </summary>
    private bool TryGetProviderGeneration(out string generation)
    {
        generation = string.Empty;
        if (!DalamudReflector.TryGetDalamudPlugin(PluginName, out var plugin, ignoreCache: true))
            return false;

        var version = plugin.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 1);
        if (version < ValidVersion && version != new Version(0, 0, 0, 0))
            return false;

        generation = $"{version}#{RuntimeHelpers.GetHashCode(plugin)}";
        return true;
    }

    /// <summary>
    ///     Fork (1.0.4.258): whether BossMod Reborn considers a dash from <paramref name="from"/> to
    ///     <paramref name="to"/> safe: inside the arena bounds, not inside a forbidden zone (puddles, telegraphs)
    ///     or temporary obstacle, and dashes not forbidden by the active module. Null when BMR is absent, older
    ///     than the endpoint, or the call fails: callers decide what an unanswered question means.
    /// </summary>
    public bool? IsDashSafe(Vector3 from, Vector3 to)
    {
        if (!IsEnabled || !PluginIsLoaded)
            return null;

        try
        {
            return IpcStallTrace.Time("Hints.IsDashSafe", () => _isDashSafe(from, to));
        }
        catch (Exception e)
        {
            PluginLog.Verbose($"[DashSafety] [{PluginName}] " +
                              $"`Hints.IsDashSafe` unavailable: {e.Message}");
            return null;
        }
    }

#pragma warning disable CS0649, CS8618 // Complaints of the method
    [EzIPC("BossMod.Rotation.ActionQueue.HasEntries", false)]
    private readonly Func<bool> _hasEntries = null!;

    [EzIPC("BossMod.Configuration.LastModified", false)]
    private readonly Func<DateTime> _lastModified = null!;

    [EzIPC("BossMod.Hints.PriorityTarget", false)]
    private readonly Func<ulong> _priorityTarget = null!;

    [EzIPC("BossMod.Hints.IsDashSafe", false)]
    private readonly Func<Vector3, Vector3, bool> _isDashSafe = null!;

    // Fork (bst1013): stock BossMod Reborn endpoints for the conflict check and the AI distance
    // (BMR Framework/IPCProvider.cs at 7.5.6.35; SetTransient from 7.5.6.34).
    [EzIPC("BossMod.Presets.GetActive", false)]
    private readonly Func<string?> _presetsGetActive = null!;

    [EzIPC("BossMod.Presets.GetForceDisabled", false)]
    private readonly Func<bool> _presetsGetForceDisabled = null!;

    [EzIPC("BossMod.Presets.Get", false)]
    private readonly Func<string, string?> _presetsGet = null!;

    [EzIPC("BossMod.AI.GetPreset", false)]
    private readonly Func<string> _aiGetPreset = null!;

    [EzIPC("BossMod.Configuration", false)]
    private readonly Func<List<string>, bool, List<string>> _configuration = null!;

    [EzIPC("BossMod.Configuration.SetTransient", false)]
    private readonly Func<List<string>, List<string>> _setTransient = null!;
#pragma warning restore CS8618, CS0649
}