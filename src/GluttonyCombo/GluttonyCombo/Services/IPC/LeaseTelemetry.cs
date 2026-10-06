using System;
using ECommons.DalamudServices;
using GluttonyCombo.API.Enum;
using GluttonyCombo.Data;
using Lalalazy.Telemetry;
using CancellationReasonEnum = GluttonyCombo.API.Enum.CancellationReason;

namespace GluttonyCombo.Services.IPC;

/// <summary>
///     Writes the <c>LS|</c> lines (format and change gate in <see cref="LeaseTelemetryFormat"/>): which plugin took a lease on
///     Gluttony, what it switched and over which of the player's saved values. A leasing plugin (AutoDuty through the WrathCombo
///     bridge, Questionable, LazyFateAutomation) overrides options only while its lease lives and never writes the player's config,
///     so these lines are the only record of the settings a run really ran with. INF level, a handful of lines a run (a repeated
///     value is not logged again), always on.
/// </summary>
internal static class LeaseTelemetry
{
    private static readonly LeaseTelemetryFormat.ChangeGate Gate = new();

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static void Emit(string line)
    {
        try
        {
            Svc.Log.Information(line);
            LalaTelemetry.Record(line);
        }
        catch (Exception)
        {
            // Telemetry must never break an IPC call.
        }
    }

    public static void Registered(string plugin, Guid lease) =>
        Emit(LeaseTelemetryFormat.Register(Now, plugin, lease.ToString()));

    public static void AutoRotationState(string plugin, bool on)
    {
        if (Gate.ShouldEmit(plugin, "state", on ? "1" : "0"))
            Emit(LeaseTelemetryFormat.State(Now, plugin, on, Service.Configuration.RotationConfig.Enabled));
    }

    public static void ConfigOption(string plugin, AutoRotationConfigOption option, int value)
    {
        var key = option.ToString();
        if (!Gate.ShouldEmit(plugin, key, value.ToString()))
            return;
        Emit(LeaseTelemetryFormat.Config(Now, plugin, key, value, Stored(option)));
    }

    public static void Released(string plugin, CancellationReasonEnum reason)
    {
        Gate.Forget(plugin);
        Emit(LeaseTelemetryFormat.Release(Now, plugin, reason.ToString()));
    }

    /// <summary> The player's saved value as the same 0/1/enum-number int the lease stores, or null when it is unknown. </summary>
    private static int? Stored(AutoRotationConfigOption option)
    {
        try
        {
            return Provider.StoredAutoRotationConfig(option) switch
            {
                bool b => b ? 1 : 0,
                Enum e => Convert.ToInt32(e),
                int i => i,
                float f => (int)f,
                _ => null,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
