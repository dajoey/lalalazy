namespace LazyCrucible;

/// <summary>
///     The complete list of inputs LazyCrucible may ever send, checked before every send. PURE. Anything not listed is
///     refused, so walking, entering, commencing, starting a battle, resting, suspending, forfeiting, closing a
///     screen or choosing a path can never be sent by mistake.
///     <list type="bullet">
///         <item><c>XBMContentsItemShop</c> callback <c>[2, stockIndex]</c> — buy one stock entry [pub, recorded shop layout].</item>
///         <item><c>XBMPetParty</c> agent event kind 0 <c>[1, listIndex]</c> — pick one familiar (horns, feed target, campsite rest
///             pick) [obs, live-proven for horns].</item>
///         <item><c>XBMContentsTreasure</c> button click on a choice button (event param ≥ 2) [pub].</item>
///         <item><c>XBMContentsBooty</c> button click on node 46, "Take all" [pub].</item>
///         <item><c>SelectYesno</c> callback <c>[0]</c> (Yes) / <c>[1]</c> (No), only on the prompt this plugin's own input
///             opened (<see cref="PromptGuard"/>).</item>
///     </list>
///     Never: <c>XBMStageDetailList</c> / <c>XBMStageList</c> / <c>XBMStageMap</c> (board select, challenge = [8]),
///     <c>ContentsFinderConfirm</c> (commence), <c>XBMContentsMainHUD</c> (suspend, forfeit, board items),
///     <c>XBMPetParty</c> Rest / Return / close ([3], [-2], node 21), the shop's close button (node 40), <c>XBMResult</c>.
/// </summary>
internal static class ActuationGuard
{
    public const int BootyTakeAllNode = 46;
    public const int TreasureFirstParam = 2;

    public enum Input
    {
        Callback,
        AgentEvent,
        Button,
    }

    /// <summary> Whether this exact input may be sent. <paramref name="values"/> are the callback / event ints. </summary>
    public static bool Allowed(string addon, Input input, IReadOnlyList<int> values, uint node = 0, int eventParam = -1)
    {
        switch (addon)
        {
            case "XBMContentsItemShop":
                return input == Input.Callback && values.Count == 2 && values[0] == 2 && values[1] is >= 0 and < 32;
            case "XBMPetParty":
                return input == Input.AgentEvent && values.Count == 2 && values[0] == 1 && values[1] is >= 0 and < 32;
            case "XBMContentsTreasure":
                return input == Input.Button && eventParam is >= TreasureFirstParam and < TreasureFirstParam + 8;
            case "XBMContentsBooty":
                return input == Input.Button && node == BootyTakeAllNode;
            case "SelectYesno":
                return input == Input.Callback && values.Count == 1 && values[0] is 0 or 1;
            default:
                return false;
        }
    }
}

/// <summary>
///     One screen open, from the automation's side: armed when it opens, done after its decisions, stood down (for the
///     rest of this open) when the player changes something the automation did not send or a read-back fails. A close
///     clears it. PURE.
/// </summary>
internal sealed class ScreenLatch
{
    public bool Open { get; private set; }
    public bool Done { get; private set; }
    public bool StoodDown { get; private set; }
    public string Why { get; private set; } = "";
    public int Actions { get; private set; }

    /// <summary> Feed the screen's visibility each tick; returns true on the tick it opens. </summary>
    public bool Update(bool open)
    {
        if (!open)
        {
            Open = Done = StoodDown = false;
            Why = "";
            Actions = 0;
            return false;
        }
        if (Open)
            return false;
        Open = true;
        return true;
    }

    public bool MayAct => Open && !Done && !StoodDown;

    public void CountAction() => Actions++;

    public void Finish(string why = "")
    {
        Done = true;
        if (why.Length > 0)
            Why = why;
    }

    public void StandDown(string why)
    {
        StoodDown = true;
        Why = why;
    }
}

/// <summary>
///     The Beast Feed picker re-selects its previous cursor on its own as it reopens (<c>kind 0 [1, slot]</c> within 2 ms
///     of the open, recorded 09-21 00:45:15, 11:43:58, 11:44:02, 11:44:12, 12:36:01); the earliest recorded human pick came
///     1.72 s after an open. Events inside the window are not the player's. PURE.
/// </summary>
internal static class PickerTiming
{
    public const long OpenEchoMs = 1200;

    public static bool IsOpenEcho(long msSinceOpen) => msSinceOpen is >= 0 and < OpenEchoMs;
}
