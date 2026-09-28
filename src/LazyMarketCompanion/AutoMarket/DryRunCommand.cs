using System;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free command parsing and result formatting for /lmc dryrun (0.2.3.0).
public static class DryRunCommand
{
  public record Result(bool NewState, bool StateChanged, string Message);

  public static Result ParseAndApply(string sub, bool currentState)
  {
    var trimmed = (sub ?? string.Empty).Trim().ToLowerInvariant();
    bool? wanted = trimmed switch
    {
      "on" or "enable" or "1" => true,
      "off" or "disable" or "0" => false,
      "toggle" => !currentState,
      _ => null,
    };

    if (wanted is null)
    {
      var usage = trimmed.Length > 0 && trimmed != "status"
        ? "Usage: /lmc dryrun <on|off|toggle|status>. "
        : string.Empty;
      var msg = $"{usage}Auto-Market dry-run mode is currently {(currentState ? "ON (simulation only)" : "OFF (live execution)")}. " +
                (currentState ? "Use '/lmc dryrun off' to enable live listing execution." : "Use '/lmc dryrun on' to return to simulation mode.");
      return new Result(currentState, false, msg);
    }

    var changed = wanted.Value != currentState;
    var resultMsg = wanted.Value
      ? "Auto-Market dry-run mode ON (simulation only; no live listings or inventory moves will execute)."
      : "Auto-Market dry-run mode OFF (live execution enabled; listings and inventory moves will execute).";

    return new Result(wanted.Value, changed, resultMsg);
  }
}
