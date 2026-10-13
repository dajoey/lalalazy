using Dalamud.Plugin.Ipc.Exceptions;

namespace LazyFateAutomation;

/// <summary>
///     Live half of FateBossModPreset / FateBossModRelease: every BossMod preset call the FATE grind makes, through
///     stock BossMod Reborn IPC only (Presets.GetActive / SetActive / ClearActive and the transient-strategy calls,
///     BMR Framework/IPCProvider.cs). BMR has no Presets.Activate / Deactivate: SetActive is what turns the preset on.
///
///     - Release() clears the active preset only when it is still the plugin's own, so a preset AutoDuty or the
///       player set is never dropped.
///     - A failed call is logged once per endpoint per run (BossModEndpointLatch); an endpoint BMR does not
///       register is skipped for the rest of the run instead of throwing on every loop iteration.
///     - Calls are skipped while BossMod is not loaded, so a late-loading BossMod is not mistaken for a missing endpoint.
/// </summary>
internal static class FateBossModControl {
    private static readonly BossModEndpointLatch _latch = new();

    /// <summary>Start of a grind run: endpoints that failed last run are tried again.</summary>
    internal static void ResetForRun() => _latch.Reset();

    internal static string? ActivePreset() => Call("Presets.GetActive", () => Service.BossMod.GetActive(), null);

    internal static bool IsOwnActive() => ActivePreset() == FateBossModPreset.Name;

    /// <summary>Makes the plugin's preset the active one (no-op when it already is).</summary>
    internal static void Activate() {
        if (!Service.BossMod.IsLoaded || IsOwnActive())
            return;
        Call("Presets.SetActive", () => Service.BossMod.SetActive(FateBossModPreset.Name), false);
    }

    /// <summary>Clears BossMod's active preset, but only when it is the plugin's own (FateBossModRelease).</summary>
    internal static void Release() {
        if (!Service.BossMod.IsLoaded || !FateBossModRelease.ShouldClearActive(ActivePreset()))
            return;
        Call("Presets.ClearActive", () => Service.BossMod.ClearActive(), false);
    }

    internal static bool SetTransient(string module, string track, string value)
        => Call("Presets.AddTransientStrategy", () => Service.BossMod.AddTransientStrategy(FateBossModPreset.Name, module, track, value), false);

    internal static bool ClearTransient(string module, string track)
        => Call("Presets.ClearTransientStrategy", () => Service.BossMod.ClearTransientStrategy(FateBossModPreset.Name, module, track), false);

    internal static bool ClearAllTransient()
        => Call("Presets.ClearTransientPresetStrategies", () => Service.BossMod.ClearTransientPresetStrategies(FateBossModPreset.Name), false);

    private static T Call<T>(string endpoint, Func<T> call, T fallback) {
        if (!Service.BossMod.IsLoaded || !_latch.ShouldCall(endpoint))
            return fallback;
        try {
            return call();
        } catch (Exception ex) {
            var missing = ex is IpcNotReadyError;
            if (_latch.ReportFailure(endpoint, missing))
                Svc.Log.PrintWarning(missing
                    ? $"[FateBossMod] BossMod IPC {endpoint} is not registered; skipping it until the next run."
                    : $"[FateBossMod] BossMod IPC {endpoint} failed: {ex.Message} (further failures this run are not logged).");
            return fallback;
        }
    }
}
