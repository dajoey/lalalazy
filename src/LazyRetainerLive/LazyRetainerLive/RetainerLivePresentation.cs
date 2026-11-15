namespace LazyRetainerLive;

/// <summary>UI-independent status and live-count rules for the settings window.</summary>
internal enum RetainerLiveState
{
    Ready,
    Idle,
    Blocked,
    Failed,
    NeedsSetup,
}

internal readonly record struct RetainerLiveStatus(
    RetainerLiveState State,
    string Action,
    string Detail);

internal static class RetainerLivePresentation
{
    public static RetainerLiveStatus Status(
        bool enabled,
        string? httpError,
        CharInfo? snapshot,
        bool fresh)
    {
        if (!enabled)
        {
            return new(
                RetainerLiveState.Idle,
                "Listener stopped",
                "Enable serving to bind the loopback listener; snapshot collection continues.");
        }

        if (!string.IsNullOrEmpty(httpError))
        {
            return new(
                RetainerLiveState.Failed,
                "Listener failed to bind",
                httpError);
        }

        if (snapshot is null)
        {
            return new(
                RetainerLiveState.NeedsSetup,
                "Waiting for the first retainer snapshot",
                "Log in and wait for a readable retainer table; GET /retainers returns 503 until then.");
        }

        if (!fresh)
        {
            return new(
                RetainerLiveState.Blocked,
                "Serving the last-known snapshot",
                "The game is in transition or logged out; the last good payload remains available.");
        }

        return new(
            RetainerLiveState.Ready,
            "Serving the current live snapshot",
            $"{snapshot.Retainers.Count} retainers · {ActiveVentures(snapshot)} active ventures in the published payload.");
    }

    public static int ActiveVentures(CharInfo snapshot)
        => snapshot.Retainers.Count(retainer => retainer.HasVenture);

    public static string LiveCount(CharInfo? snapshot)
        => snapshot is null
            ? "No snapshot"
            : $"{snapshot.Retainers.Count} retainers · {ActiveVentures(snapshot)} active ventures";

    public static string LiveSubtext(bool enabled, int port)
        => enabled
            ? $"127.0.0.1:{port} · GET /retainers"
            : "Listener off · snapshots still update";
}
