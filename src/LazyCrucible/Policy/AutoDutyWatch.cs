namespace LazyCrucible.Policy;

/// <summary> What AutoDuty is doing, as far as its IPC shows. </summary>
internal enum AutoDutyStatus
{
    /// <summary> <c>AutoDuty.IsStopped</c> is true, or AutoDuty is not installed / not loaded. </summary>
    Stopped,

    /// <summary> Not stopped and stepping (or too early to tell): LazyCrucible stands down for it. </summary>
    Driving,

    /// <summary> Not stopped, but it stopped stepping: its loop state outlived its last step. </summary>
    Stalled,
}

/// <summary>
///     Tells an AutoDuty loop that is stepping from one that kept reporting "running" after it stopped. Pure (no
///     Dalamud): <c>ExternalDrivers</c> feeds it once a second.
///     <para>
///         Live 2026-09-29 (First Master's Board, AutoDuty 0.0.0.372): its log's last step is 18:08:35 (the run's exit
///         to the lobby), then nothing until the plugin was reloaded at 18:11:55, while <c>AutoDuty.IsStopped</c> stayed
///         false. The plugin kept standing down the whole time, including on the Second Master Board the player was
///         playing by hand. <c>IsStopped</c> is the only signal that was read, and it cannot tell the two apart, so
///         this adds <c>AutoDuty.IsLooping</c> / <c>AutoDuty.IsNavigating</c> (AutoDuty's own "running" is
///         Looping or Navigating; Navigating is true while it believes it is walking a run) and where the player is.
///         The times come from recorded healthy loops: queue to board entry about 35 s, board exit to its next action
///         about 7 s, Navigating raised about 9 s before the board opens.
///     </para>
///     A stall is latched, not re-evaluated every second: it ends when AutoDuty stops, or when it raises Navigating
///     (or Looping) again because a new run started. The player walking into another board never ends it.
/// </summary>
internal sealed class AutoDutyWatch
{
    /// <summary> The Crucible lobby (Central Shroud, Bentbranch Meadows). </summary>
    internal const uint LobbyTerritory = 148;

    /// <summary> Neither Looping nor Navigating for this long while not stopped: AutoDuty itself is running nothing. </summary>
    internal const double NotRunningSeconds = 5;

    /// <summary> In a board, AutoDuty running but not navigating, for this long: it is not driving this run. </summary>
    internal const double IdleInBoardSeconds = 20;

    /// <summary> AutoDuty navigating while the player is in the lobby, for this long: a run it has already left. </summary>
    internal const double NavigatingInLobbySeconds = 30;

    /// <summary> In the lobby with nothing changing, for this long (a healthy queue to board entry takes about 35 s). </summary>
    internal const double LobbySeconds = 90;

    private bool _running;
    private bool? _prevLooping;
    private bool? _prevNavigating;
    private double _navigatingSince;
    private double _idleSince;
    private uint _territory;
    private double _territorySince;
    private int _driveBoard;
    private bool _stalled;

    public AutoDutyStatus Status { get; private set; } = AutoDutyStatus.Stopped;

    /// <summary>
    ///     Why it is judged stalled (<c>not_running</c>, <c>idle_in_board</c>, <c>navigating_in_lobby</c>,
    ///     <c>other_board</c>, <c>no_progress_in_lobby</c>); null otherwise.
    /// </summary>
    public string? Reason { get; private set; }

    /// <summary>
    ///     One reading. <paramref name="looping"/> and <paramref name="navigating"/> are null when the IPC is missing
    ///     (an older AutoDuty): every rule that needs one stays silent, so that case keeps the old behaviour (Driving
    ///     while not stopped). <paramref name="board"/> is the Crucible board the player stands in (0 outside one);
    ///     <paramref name="now"/> is any monotonic clock in seconds.
    /// </summary>
    public AutoDutyStatus Step(bool stopped, bool? looping, bool? navigating, uint territory, int board, double now)
    {
        if (stopped)
        {
            _running = false;
            _stalled = false;
            Reason = null;
            return Status = AutoDutyStatus.Stopped;
        }

        if (!_running)
        {
            _running = true;
            _prevLooping = looping;
            _prevNavigating = navigating;
            _navigatingSince = now;
            _idleSince = now;
            _territory = territory;
            _territorySince = now;
            _driveBoard = 0;
            _stalled = false;
            Reason = null;
        }

        if (territory != _territory)
        {
            _territory = territory;
            _territorySince = now;
        }

        if (navigating != _prevNavigating)
        {
            _prevNavigating = navigating;
            _navigatingSince = now;
            if (navigating == true)
                Resumed();
        }

        if (looping != _prevLooping)
        {
            _prevLooping = looping;
            if (looping == true)
                Resumed();
        }

        if (looping != false || navigating != false)
            _idleSince = now;

        if (navigating == true && board != 0 && _driveBoard == 0)
            _driveBoard = board;

        if (!_stalled)
        {
            var lobby = territory == LobbyTerritory;
            var sinceChange = now - Math.Max(_navigatingSince, _territorySince);
            if (looping == false && navigating == false && now - _idleSince >= NotRunningSeconds)
                Latch("not_running");
            else if (navigating == false && board != 0 && sinceChange >= IdleInBoardSeconds)
                Latch("idle_in_board");
            else if (navigating == true && lobby && sinceChange >= NavigatingInLobbySeconds)
                Latch("navigating_in_lobby");
            else if (navigating == true && board != 0 && _driveBoard != 0 && board != _driveBoard)
                Latch("other_board");
            else if (navigating == false && lobby && now - _territorySince >= LobbySeconds)
                Latch("no_progress_in_lobby");
        }

        return Status = _stalled ? AutoDutyStatus.Stalled : AutoDutyStatus.Driving;
    }

    /// <summary> AutoDuty started something: it is stepping again, and the board it takes in next is the one it drives. </summary>
    private void Resumed()
    {
        _stalled = false;
        Reason = null;
        _driveBoard = 0;
    }

    private void Latch(string reason)
    {
        _stalled = true;
        Reason = reason;
    }
}
