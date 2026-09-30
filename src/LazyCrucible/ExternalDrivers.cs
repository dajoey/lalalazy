using ECommons.DalamudServices;
using Lalalazy.Crucible;
using LazyCrucible.Policy;

namespace LazyCrucible;

/// <summary>
///     Other automation that drives Crucible runs. AutoDuty (a fork with Crucible support) runs whole boards,
///     including its own familiar team (leveling / recommended); two writers on the same screen fight, as the
///     live 12:50 run showed (its roster rebuilt four times over). Its published EzIPC surface includes
///     <c>AutoDuty.IsStopped</c>, <c>IsLooping</c> and <c>IsNavigating</c>. While it is stepping, AutoDuty owns the
///     run. <c>IsStopped</c> alone is not enough: a loop can keep reporting "running" long after its last step
///     (live 2026-09-29), so <see cref="AutoDutyWatch"/> also reads the other two and where the player stands.
/// </summary>
internal static class ExternalDrivers
{
    private static readonly AutoDutyWatch Watch = new();
    private static DateTime _nextCheck = DateTime.MinValue;
    private static bool _stopped = true;
    private static bool? _looping;
    private static bool? _navigating;
    private static string _lastLogged = "";
    private static bool _presetHandledThisBoard;

    /// <summary> Whether AutoDuty is running AND stepping (checked at most once a second; false when not installed). </summary>
    public static bool AutoDutyRunning
    {
        get
        {
            Poll();
            return Watch.Status == AutoDutyStatus.Driving;
        }
    }

    /// <summary> Why AutoDuty is judged stalled, or null while it is stopped or stepping. </summary>
    public static string? StallReason
    {
        get
        {
            Poll();
            return Watch.Reason;
        }
    }

    /// <summary> What the stand-down saw, for its note: <c>ad=driving|nav=1|loop=1</c>. </summary>
    public static string Detail => $"ad={StateName(Watch.Status)}|nav={Flag(_navigating)}|loop={Flag(_looping)}";

    private static void Poll()
    {
        var now = DateTime.UtcNow;
        if (now < _nextCheck)
            return;
        _nextCheck = now.AddSeconds(1);

        _stopped = Ipc("AutoDuty.IsStopped") ?? true; // not installed, not loaded, or no such IPC
        _looping = _stopped ? null : Ipc("AutoDuty.IsLooping");
        _navigating = _stopped ? null : Ipc("AutoDuty.IsNavigating");

        var territory = (uint)Svc.ClientState.TerritoryType;
        var board = BST_CrucibleData.BoardOfTerritory(territory);
        Watch.Step(_stopped, _looping, _navigating, territory, board, Environment.TickCount64 / 1000.0);

        var state = $"{Watch.Status}|{Watch.Reason}";
        if (state != _lastLogged)
        {
            _lastLogged = state;
            CrucibleLog.Line($"PS|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|note=autoduty|running={(_stopped ? 0 : 1)}" +
                             $"|state={StateName(Watch.Status)}|why={Watch.Reason ?? "none"}|nav={Flag(_navigating)}|loop={Flag(_looping)}" +
                             $"|b={board}|terr={territory}|calls=0");
        }

        if (Watch.Status != AutoDutyStatus.Stalled || board == 0)
            _presetHandledThisBoard = false;
        else if (!_presetHandledThisBoard)
        {
            _presetHandledThisBoard = true;
            ClearStaleAutoDutyPreset(board);
        }
    }

    /// <summary>
    ///     AutoDuty activates BossMod Reborn's "AutoDuty" preset for a run (it retargets to enemies of its own
    ///     choosing and walks the character to them) and only clears it when the run stops or finishes. A stalled
    ///     loop never does, and BossMod Reborn keeps the preset until something clears it, so the player's manual
    ///     fights were steered by it. Clears it, but only while AutoDuty is judged stalled, the player is in a board
    ///     and the active preset is AutoDuty's own.
    /// </summary>
    private static void ClearStaleAutoDutyPreset(int board)
    {
        string? active;
        try
        {
            active = Svc.PluginInterface.GetIpcSubscriber<string>("BossMod.Presets.GetActive").InvokeFunc();
        }
        catch
        {
            return; // BossMod Reborn not installed or not ready: nothing of AutoDuty's to clear
        }

        if (active is null || !active.StartsWith("AutoDuty", StringComparison.OrdinalIgnoreCase))
            return;

        var cleared = false;
        try
        {
            cleared = Svc.PluginInterface.GetIpcSubscriber<bool>("BossMod.Presets.ClearActive").InvokeFunc();
        }
        catch
        {
            // logged below as cleared=0
        }

        CrucibleLog.Line($"PS|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|note=autoduty_preset|active={active}|cleared={(cleared ? 1 : 0)}" +
                         $"|why={Watch.Reason ?? "none"}|b={board}|calls=0");
    }

    private static bool? Ipc(string name)
    {
        try
        {
            return Svc.PluginInterface.GetIpcSubscriber<bool>(name).InvokeFunc();
        }
        catch
        {
            return null;
        }
    }

    private static string Flag(bool? value) => value is null ? "x" : value.Value ? "1" : "0";

    private static string StateName(AutoDutyStatus status) => status.ToString().ToLowerInvariant();
}
