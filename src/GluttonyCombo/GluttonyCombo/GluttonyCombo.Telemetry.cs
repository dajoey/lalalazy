using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GluttonyCombo.AutoRotation;
using GluttonyCombo.Core;
using GluttonyCombo.CustomComboNS;
using GluttonyCombo.Data;
using GluttonyCombo.Services;
using Lalalazy.Telemetry;

namespace GluttonyCombo;

/// <summary>
///     Fork (error reporting, 2026-09-21): wiring for the shared src/Shared/LalaTelemetry library -
///     install, what a <c>/gluttony report</c> writes about this plugin's state, and the report command.
///     Kept in its own fork-owned file so upstream WrathCombo merges never touch it.
/// </summary>
public sealed partial class GluttonyCombo
{
    /// <summary> Most enabled-preset names listed for the current job in a report's state section. </summary>
    private const int ReportMaxPresets = 150;

    private DalamudTelemetry? InstallTelemetry(IDalamudPluginInterface pluginInterface)
    {
        var telemetry = DalamudTelemetry.Install(new DalamudTelemetry.Options
        {
            PluginInterface = pluginInterface,
            PluginAssembly = typeof(GluttonyCombo).Assembly,
            DisplayName = "Gluttony Combo",
            Command = Command,
            Log = Svc.Log,
            Framework = Svc.Framework,
            ClientState = Svc.ClientState,
            Condition = Svc.Condition,
            Chat = Svc.Chat,
            PlayerState = Svc.PlayerState,
            Objects = Svc.Objects,
            Targets = Svc.Targets,
            // Scalars and collection counts of the whole configuration, two levels deep (RotationConfig.X).
            ConfigSummary = () => ConfigSummary.Describe(Service.Configuration, maxDepth: 3),
            StateSummary = TelemetryStateSummary,
        });
        return telemetry;
    }

    /// <summary>
    ///     Fork (2026-11-05): install the movement tap. Lives in
    ///     this fork-owned file so the entry file stays out of the movement feature entirely.
    ///     The tap's Dispose rides PluginInterface.Disposing.
    ///     2026-11-06 fix (movement.install NullReferenceException at Telemetry.cs:61, every
    ///     load of 1.0.4.328-1.0.4.332): the tap now installs after the configuration exists
    ///     (the caller in GluttonyCombo.cs), reads it null-safe through
    ///     <see cref="MovementFormat.ResolveRelayBaseUrl"/>, and a failed install is retried
    ///     on the framework tick and on every territory change instead of staying dead.
    /// </summary>
    private static void InstallMovementTap()
    {
        try
        {
            MovementUploader.RelayBaseUrl = MovementFormat.ResolveRelayBaseUrl(Service.Configuration?.MovementRelayUrl);
            // Fight Brain join (Stage 1a landed 1.0.4.327): the recorder's forecast field reads
            // the forecast's own cache - one short token for the next hit, change-gated to 1/s.
            MovementRecorder.ForecastHint = () =>
            {
                var entries = FightBrain.StripEntries();
                return entries.Count == 0
                    ? null
                    : $"act{entries[0].Hit.SourceId}in{entries[0].Seconds:F1}s";
            };
            MovementRecorder.Install();
            DisarmMovementTapRetry(installedOnRetry: _movementTapTickHooked || _movementTapTerritoryHooked);
        }
        catch (Exception ex)
        {
            LalaTelemetry.Error("movement.install", ex);
            ArmMovementTapRetry();
        }
    }

    /// <summary> Framework ticks spent retrying the install (the tick runs about 60/s; 600 = ~10 s). </summary>
    private const int MovementTapMaxTickRetries = 600;

    private static int _movementTapTickAttempts;
    private static bool _movementTapTickHooked;
    private static bool _movementTapTerritoryHooked;

    /// <summary>
    ///     Never once only (2026-11-06): a failed install retries every framework tick for a
    ///     window and, once that window is spent, on every territory change (a login is one),
    ///     until it succeeds. Each failed retry re-arms the tick window.
    /// </summary>
    private static void ArmMovementTapRetry()
    {
        var firstArm = !_movementTapTickHooked && !_movementTapTerritoryHooked;
        try
        {
            if (!_movementTapTickHooked)
            {
                Svc.Framework.Update += OnMovementTapRetryTick;
                _movementTapTickHooked = true;
                _movementTapTickAttempts = 0;
            }

            if (!_movementTapTerritoryHooked)
            {
                Svc.ClientState.TerritoryChanged += OnMovementTapRetryTerritory;
                _movementTapTerritoryHooked = true;
            }
        }
        catch (Exception ex)
        {
            LalaTelemetry.Swallowed("movement.install.retryarm", ex);
            return;
        }

        if (firstArm)
            LalaTelemetry.Swallowed("movement.install.retry",
                new Exception("movement tap not installed yet; retrying on framework ticks and territory changes"));
    }

    /// <summary> Removes the retry handlers (success or dispose). Safe to call any time. </summary>
    private static void DisarmMovementTapRetry(bool installedOnRetry = false)
    {
        if (!_movementTapTickHooked && !_movementTapTerritoryHooked)
            return;
        _movementTapTickHooked = false;
        _movementTapTerritoryHooked = false;
        try
        {
            Svc.Framework.Update -= OnMovementTapRetryTick;
            Svc.ClientState.TerritoryChanged -= OnMovementTapRetryTerritory;
        }
        catch (Exception ex)
        {
            LalaTelemetry.Swallowed("movement.install.retrydisarm", ex);
        }

        if (installedOnRetry)
            DuoLog.Information("Movement tap installed on retry.");
    }

    private static void OnMovementTapRetryTick(IFramework framework)
    {
        _movementTapTickAttempts++;
        if (_movementTapTickAttempts <= MovementTapMaxTickRetries)
        {
            InstallMovementTap();
            return;
        }

        // Window spent: stop ticking; the territory handler stays armed and re-arms us.
        try
        {
            Svc.Framework.Update -= OnMovementTapRetryTick;
            _movementTapTickHooked = false;
        }
        catch
        {
            // handler bookkeeping only; the next territory change re-arms everything
        }

        LalaTelemetry.Swallowed("movement.install.retryslow",
            new Exception($"movement tap install still failing after {MovementTapMaxTickRetries} tick retries; will retry on territory changes"));
    }

    private static void OnMovementTapRetryTerritory(uint territoryId) => InstallMovementTap();

    /// <summary>
    ///     What the plugin thinks it is doing, for a report: auto-rotation on/paused/locked, the opener,
    ///     the telemetry switch, and every preset enabled for the current job (<c>*</c> = in auto-rotation).
    /// </summary>
    private string TelemetryStateSummary()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(1024);
        var cfg = Service.Configuration;

        sb.Append("auto=").Append(cfg.RotationConfig.Enabled ? 1 : 0)
          .Append(";paused=").Append(AutoRotationController.Paused ? 1 : 0)
          .Append(";locked=").Append(UIHelper?.AutoRotationStateControlled() is not null ? 1 : 0)
          .Append(";actionChanging=").Append(cfg.ActionChanging ? 1 : 0)
          .Append(";comboTelemetry=").Append(cfg.ComboTelemetry ? 1 : 0)
          .Append(";activeJobPresets=").Append((IPCSearch?.ActiveJobPresets ?? 0).ToString(inv))
          .Append(";opener=").Append(SafeOpenerStatus());

        if (Player.Available)
        {
            var prefix = Player.Job.ToString() + "_";
            var enabled = cfg.EnabledActions
                .Select(p => (Preset: p, Name: p.ToString()))
                .Where(x => x.Name.StartsWith(prefix, StringComparison.Ordinal) || x.Name.StartsWith("ALL_", StringComparison.Ordinal))
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Take(ReportMaxPresets)
                .Select(x => cfg.AutoActions.TryGetValue(x.Preset, out var auto) && auto ? x.Name + "*" : x.Name);
            sb.Append(";presets=").Append(string.Join(",", enabled));
        }

        return sb.ToString();
    }

    private static string SafeOpenerStatus()
    {
        try
        {
            return WrathOpener.OpenerStatus();
        }
        catch (Exception ex)
        {
            return "unreadable(" + ex.GetType().Name + ")";
        }
    }

    /// <summary> <c>/gluttony report &lt;what happened&gt;</c>: writes an RP| problem report to the plugin log. </summary>
    private void HandleReportCommand(string text)
    {
        if (Telemetry is null)
        {
            DuoLog.Error("Problem reports are unavailable this session: error reporting did not start (the reason is in the plugin log).");
            return;
        }
        Telemetry.FileReport(text);
    }
}
