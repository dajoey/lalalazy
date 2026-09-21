using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Dalamud.Plugin;
using GluttonyCombo.AutoRotation;
using GluttonyCombo.CustomComboNS;
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

    private DalamudTelemetry? InstallTelemetry(IDalamudPluginInterface pluginInterface) =>
        DalamudTelemetry.Install(new DalamudTelemetry.Options
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
