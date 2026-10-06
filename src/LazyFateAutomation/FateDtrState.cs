namespace LazyFateAutomation;

/// <summary>
///     Pure decision core for the "Lazy Fate Automation" server info bar (DTR) entry — the
///     FATE bot's on/off switch in the game bar, which Umbra's DtrBar widget renders as a
///     button. Kept free of Dalamud so the offline harness compiles it as-is; the
///     Dalamud-facing wrapper is FateDtr.cs.
///
///     Click behavior mirrors the existing switches: clicking a running bot hard-stops it
///     (the window's Stop button), and starting needs a confirming second click the way the
///     Lazy Hub switch asks for one, so a stray click in a toolbar cannot start the
///     automation. Starting also requires a logged-in character (the hub refuses with
///     "Log in first."). The text mirrors the sibling entries' "icon: On/Off" shape.
/// </summary>
internal static class FateDtrLogic {
    /// <summary>How long an armed start stays armed before the entry falls back to Off.</summary>
    public const int ConfirmSeconds = 10;

    public enum ClickAction {
        /// <summary>Nothing to do (e.g. no logged-in character).</summary>
        None,
        /// <summary>Arm the start: show the confirm prompt and wait for a second click.</summary>
        ArmStart,
        /// <summary>The armed start was confirmed: start the bot.</summary>
        StartNow,
        /// <summary>Stop the bot (hard stop, like the window's Stop button).</summary>
        Stop,
        /// <summary>Arm the soft stop: finish the current FATE, stop once out of combat</summary>
        /// <summary>(the window's Ctrl+click, the hub's "Stop when safe").</summary>
        StopWhenSafe,
    }

    /// <summary>
    ///     What a click on the entry does in the given state. Ctrl+click on a running bot
    ///     arms the soft stop instead, exactly like the window's Start/Stop button.
    /// </summary>
    public static ClickAction DecideClick(bool running, bool confirmPending, bool playerAvailable, bool ctrlHeld = false) {
        if (running)
            return ctrlHeld ? ClickAction.StopWhenSafe : ClickAction.Stop;
        if (!playerAvailable)
            return ClickAction.None;
        return confirmPending ? ClickAction.StartNow : ClickAction.ArmStart;
    }

    /// <summary>
    ///     Whether an armed start is still live. The wrapper passes this before every
    ///     decision so an armed start nobody confirmed expires on its own.
    /// </summary>
    public static bool IsConfirmLive(bool confirmPending, long armedAtMs, long nowMs, int confirmSeconds = ConfirmSeconds)
        => confirmPending && nowMs - armedAtMs < confirmSeconds * 1000L;

    /// <summary>
    ///     The text after the entry title: ": Off", ": Start? (click again)" while a start is
    ///     armed, or ": On" — with "(stopping)" while a soft stop is pending and the plugin's
    ///     own state otherwise (e.g. "Paused (in instance)").
    /// </summary>
    public static string EntryText(bool running, bool stopWhenSafePending, bool confirmPending, string? currentState) {
        if (!running)
            return confirmPending ? ": Start? (click again)" : ": Off";
        if (stopWhenSafePending)
            return ": On (stopping)";
        var state = currentState?.Trim() ?? "";
        if (state.Length == 0 || state.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            return ": On";
        return $": On ({state})";
    }

    /// <summary>Unsheathed sword while the bot runs, sheathed while stopped — the sibling entries' icon pair.</summary>
    public static bool SwordUnsheathed(bool running) => running;
}
