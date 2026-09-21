using ECommons.DalamudServices;

namespace LazyCrucible;

/// <summary>
///     Other automation that drives Crucible runs. AutoDuty (a fork with Crucible support) runs whole boards,
///     including its own familiar team (leveling / recommended); two writers on the same screen fight, as the
///     live 12:50 run showed (its roster rebuilt four times over). Its published EzIPC surface includes
///     <c>AutoDuty.IsStopped</c>; while that is false, AutoDuty owns the run.
/// </summary>
internal static class ExternalDrivers
{
    private static DateTime _nextCheck = DateTime.MinValue;
    private static bool _autoDutyRunning;
    private static bool? _lastLogged;

    /// <summary> Whether AutoDuty reports a run in progress (checked at most once a second; false when not installed). </summary>
    public static bool AutoDutyRunning
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now < _nextCheck)
                return _autoDutyRunning;
            _nextCheck = now.AddSeconds(1);
            try
            {
                _autoDutyRunning = !Svc.PluginInterface.GetIpcSubscriber<bool>("AutoDuty.IsStopped").InvokeFunc();
            }
            catch
            {
                _autoDutyRunning = false; // not installed, not loaded, or no such IPC
            }
            if (_lastLogged != _autoDutyRunning)
            {
                _lastLogged = _autoDutyRunning;
                CrucibleLog.Line($"PS|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|note=autoduty|running={(_autoDutyRunning ? 1 : 0)}|calls=0");
            }
            return _autoDutyRunning;
        }
    }
}
