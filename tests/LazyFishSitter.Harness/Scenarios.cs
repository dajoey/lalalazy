using LazyFishSitter.Core;

namespace LazyFishSitter.Harness;

/// <summary>
/// A scripted fishing session: a list of (time, snapshot) frames the policy is replayed over.
/// Frames are generated at a fixed cadence so timing guards (StandConfirm, StateSettle,
/// HookTransientWindow, MinSendSpacing) are exercised for real rather than mocked away.
/// </summary>
internal sealed class Session
{
    public const double FrameSeconds = 0.25;
    private static readonly DateTime Epoch = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly List<(DateTime At, FishingSnapshot Snap)> _frames = new();
    private DateTime _clock = Epoch;

    /// <summary>Current character/fishing state; Hold() samples it into frames.</summary>
    public bool Fishing;
    public bool HandlerAvailable = true;
    public FishState State = FishState.None;
    public bool ChangingPosition;
    public bool CanFish;
    public bool Seated;
    /// <summary>Seated per Character.Mode (EmoteLoop/InPositionLoop). Split out so a trace can
    /// model "the game says seated but Mode does not", which is the open question from v0.1.1.0.</summary>
    public bool ModeSeatedOverride;
    public string ModeName = "Normal";
    public byte ModeValue;

    public IReadOnlyList<(DateTime At, FishingSnapshot Snap)> Frames => _frames;

    private FishingSnapshot Snap() => new(
        Fishing, HandlerAvailable, State, ChangingPosition, CanFish,
        ModeName, ModeValue, 0, 0,
        Seated ? "SittingOnGround" : "Normal",
        ModeSeatedOverride || (Seated && ModeName == "EmoteLoop"),
        Seated);

    /// <summary>Hold the current state for <paramref name="seconds"/>, emitting frames.</summary>
    public Session Hold(double seconds)
    {
        var n = Math.Max(1, (int)Math.Round(seconds / FrameSeconds));
        for (var i = 0; i < n; i++)
        {
            _frames.Add((_clock, Snap()));
            _clock = _clock.AddSeconds(FrameSeconds);
        }
        return this;
    }

    public Session Set(Action<Session> mutate) { mutate(this); return this; }

    /// <summary>Walk up to a hole: handler live, CanFish true, rod not out yet.</summary>
    public Session ArriveAtHole(double seconds = 2)
        => Set(s => { s.CanFish = true; s.State = FishState.None; }).Hold(seconds);

    /// <summary>The standby beat: rod out, nothing in the water. This is where /sit is accepted.</summary>
    public Session PoleReady(double seconds)
        => Set(s => { s.Fishing = true; s.State = FishState.PoleReady; }).Hold(seconds);

    /// <summary>One full cast with no bite: cast out, line in water, pull pole in, back to standby.</summary>
    public Session CastNoBite(double lineSeconds = 12)
        => Set(s => { s.Fishing = true; s.State = FishState.CastingOut; }).Hold(1)
          .Set(s => s.State = FishState.LineInWater).Hold(lineSeconds)
          .Set(s => s.State = FishState.PullingPoleIn).Hold(1.5)
          .Set(s => s.State = FishState.PoleReady).Hold(1.5);

    /// <summary>
    /// One full cast that catches a fish. If <paramref name="gameStandsYou"/>, the character reads
    /// as standing for a moment around the hook and the game re-seats them afterwards - Joey's
    /// model of the real behaviour, and the case that must NOT produce a send.
    /// </summary>
    public Session CastAndCatch(double lineSeconds = 8, bool gameStandsYou = true)
    {
        Set(s => { s.Fishing = true; s.State = FishState.CastingOut; }).Hold(1);
        Set(s => s.State = FishState.LineInWater).Hold(lineSeconds);
        Set(s => s.State = FishState.Bite).Hold(0.5);
        if (gameStandsYou) Set(s => s.Seated = false);
        Set(s => s.State = FishState.Hooking).Hold(3);
        Set(s => s.State = FishState.ReleasingCatch).Hold(1);
        // The game puts you back down by itself - this is the whole premise of v0.1.3.0.
        if (gameStandsYou) Set(s => s.Seated = true);
        Set(s => s.State = FishState.PullingPoleIn).Hold(1.5);
        Set(s => s.State = FishState.PoleReady).Hold(2);
        return this;
    }

    /// <summary>Leave the hole for long enough that the trip ends.</summary>
    public Session LeaveHole(double seconds = 30)
        => Set(s => { s.Fishing = false; s.CanFish = false; s.State = FishState.None; }).Hold(seconds);
}

/// <summary>Result of replaying a session: what the policy sent, and when.</summary>
internal sealed record Replay(List<(DateTime At, string Cmd, FishingSnapshot Snap)> Sends, List<string> Logs)
{
    public int Count => Sends.Count;
}

internal static class Runner
{
    /// <summary>
    /// Replay a session through the real policy. <paramref name="sitTakes"/> models the game
    /// accepting the /sit: when true, ChangingPosition flips true 0.5 s after a send and the
    /// character reads seated 1 s after that (what should happen at the standby beat). When
    /// false the emote is swallowed - what actually happened to v0.1.1.0 mid-cast.
    /// </summary>
    public static Replay Run(Session session, bool sitTakes = true, bool enabled = true,
                             string? blockReason = null, string sitCommand = "/sit")
    {
        var policy = new SitPolicy();
        var sends = new List<(DateTime, string, FishingSnapshot)>();
        var logs = new List<string>();
        DateTime? sentAt = null;

        foreach (var (at, baseSnap) in session.Frames)
        {
            var snap = baseSnap;

            // Model the game's reaction to a send we already made.
            if (sitTakes && sentAt is { } t)
            {
                var dt = (at - t).TotalSeconds;
                if (dt is >= 0.5 and < 1.5) snap = snap with { ChangingPosition = true };
                if (dt >= 1.5) snap = snap with { GameSeated = true, GamePosture = "SittingOnGround" };
            }

            var step = policy.Step(snap, at, new PolicyContext(enabled, sitCommand, blockReason));
            logs.AddRange(step.Logs);
            if (step.SendCommand is { } cmd)
            {
                sends.Add((at, cmd, snap));
                sentAt = at;
            }
        }

        return new Replay(sends.Select(s => (s.Item1, s.Item2, s.Item3)).ToList(), logs);
    }
}
