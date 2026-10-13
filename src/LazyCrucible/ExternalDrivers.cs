using ECommons.DalamudServices;
using ECommons.GameHelpers;
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
///     Which screens AutoDuty owns while it runs (<see cref="DrivenSurfaces"/>): all of them, except the battlehorns while its
///     "Fight picks" is off, read from its active profile over its <c>AutoDuty.GetConfig</c> IPC (its config file as the fallback).
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

    /// <summary>
    ///     The selection surfaces AutoDuty drives with its own Crucible settings (<see cref="AutoDutyCrucible.Driven"/>: all but the
    ///     battlehorns while Fight picks is off; every surface while the settings are unreadable). Meaningful only while
    ///     <see cref="AutoDutyRunning"/>.
    /// </summary>
    public static CrucibleSurface DrivenSurfaces
    {
        get
        {
            Poll();
            return AutoDutyCrucible.Driven(_config);
        }
    }

    /// <summary> <see cref="DrivenSurfaces"/> for a log field: <c>Roster+Horns</c>, <c>All</c>. </summary>
    public static string DrivenNote => DrivenSurfaces.ToString().Replace(", ", "+");

    // AutoDuty's Crucible settings, never written. The ACTIVE profile over AutoDuty's IPC (AutoDuty.GetConfig) every 5 s while it
    // runs; its config file (the profile it would pick for this character) only while the IPC is missing or fails. The file is
    // re-read only when it or the character changed.
    private static AutoDutyCrucibleConfig? _config;
    private static AutoDutyCrucibleConfig? _fileConfig;
    private static DateTime _configStamp = DateTime.MinValue;
    private static ulong _configCid;
    private static DateTime _nextConfigCheck = DateTime.MinValue;
    private static string _lastConfigLogged = "";
    /// <summary> The IPC failed during this AutoDuty run (missing, older AutoDuty, mismatched): off until AutoDuty stops, logged once. </summary>
    private static bool _configIpcOff;

    private static void Poll()
    {
        var now = DateTime.UtcNow;
        if (now < _nextCheck)
            return;
        _nextCheck = now.AddSeconds(1);

        _stopped = Ipc("AutoDuty.IsStopped") ?? true; // not installed, not loaded, or no such IPC
        _looping = _stopped ? null : Ipc("AutoDuty.IsLooping");
        _navigating = _stopped ? null : Ipc("AutoDuty.IsNavigating");
        if (!_stopped)
            RefreshConfig(now);
        else
            _configIpcOff = false; // the next AutoDuty run tries its IPC again

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
    ///     Read AutoDuty's Crucible settings every 5 s while it runs: the active profile over its IPC (<see cref="AutoDutyCrucible.FromIpc"/>),
    ///     else its config file (<see cref="ReadConfigFile"/>). An IPC that throws or answers something that is not True/False is latched
    ///     off for the rest of the run with one <c>PS|..|note=autoduty_config|ipc=off</c> line. One <c>PS|..|note=autoduty_config</c>
    ///     line per change of what was read, no error lines.
    /// </summary>
    private static void RefreshConfig(DateTime now)
    {
        if (now < _nextConfigCheck)
            return;
        _nextConfigCheck = now.AddSeconds(5);

        AutoDutyCrucibleConfig? read = null;
        var via = "file";
        if (!_configIpcOff)
        {
            string? why = null;
            try
            {
                var ipc = Svc.PluginInterface.GetIpcSubscriber<string, string>("AutoDuty.GetConfig");
                read = AutoDutyCrucible.FromIpc(path => ipc.InvokeFunc(path));
                if (read is null)
                    why = "answer";
                else
                    via = "ipc";
            }
            catch (Exception ex)
            {
                read = null;
                why = ex.GetType().Name;
            }
            if (why is not null)
            {
                _configIpcOff = true;
                CrucibleLog.Line($"PS|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|note=autoduty_config|ipc=off|why={why}|calls=0");
            }
        }
        read ??= ReadConfigFile();
        _config = read;

        var line = read is null
            ? "read=0"
            : $"read=1|via={via}|profile={read.Profile}|team={read.TeamMode}|lvl={read.LevelingMode}|dg={read.Degree}|fp={(read.FightPicks ? 1 : 0)}";
        if (line == _lastConfigLogged)
            return;
        _lastConfigLogged = line;
        CrucibleLog.Line($"PS|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|note=autoduty_config|{line}" +
                         $"|driven={AutoDutyCrucible.Driven(read).ToString().Replace(", ", "+")}|calls=0");
    }

    /// <summary>
    ///     AutoDuty's config file (<c>pluginConfigs/AutoDuty/AutoDutyConfigV2.json</c>, the folder next to this plugin's own config),
    ///     re-read only when the file or the character changed. Opened read-only and shared, so AutoDuty can keep writing it. A missing,
    ///     locked or half-written file reads as unknown (every surface stands by) and is tried again on the next check.
    /// </summary>
    private static AutoDutyCrucibleConfig? ReadConfigFile()
    {
        var cid = Player.CID;
        string? path = null;
        var stamp = DateTime.MinValue;
        try
        {
            var dir = Svc.PluginInterface.ConfigDirectory.Parent?.FullName;
            if (dir is not null)
            {
                path = Path.Combine(dir, "AutoDuty", "AutoDutyConfigV2.json");
                if (File.Exists(path))
                    stamp = File.GetLastWriteTimeUtc(path);
            }
        }
        catch
        {
            stamp = DateTime.MinValue;
        }

        if (stamp == _configStamp && cid == _configCid)
            return _fileConfig;
        _configStamp = stamp;
        _configCid = cid;

        AutoDutyCrucibleConfig? read = null;
        if (path is not null && stamp != DateTime.MinValue)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                read = AutoDutyCrucible.Parse(reader.ReadToEnd(), cid);
            }
            catch
            {
                read = null;
            }
            if (read is null)
                _configStamp = DateTime.MinValue; // half-written or locked: read it again next time
        }
        _fileConfig = read;
        return read;
    }

    /// <summary>
    ///     AutoDuty activates one of its own BossMod Reborn presets for a run and only clears it when the run stops or
    ///     finishes. Which one depends on its rotation setting: with RotationPlugin = WrathCombo (the setup this plugin
    ///     suite runs) it is "AutoDuty Passive", movement only (it walks the character to its target; no AutoTarget
    ///     module, so it does not pick targets); with BossMod or All, and in the Novice Hall, it is "AutoDuty", which
    ///     also carries AutoTarget and BossMod Reborn's own rotation. A stalled loop never clears either, and BossMod
    ///     Reborn keeps the preset until something does, so the player's manual fights were steered by it. Clears it,
    ///     but only while AutoDuty is judged stalled, the player is in a board and the active preset is one of
    ///     AutoDuty's (any name starting with "AutoDuty" covers both).
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
