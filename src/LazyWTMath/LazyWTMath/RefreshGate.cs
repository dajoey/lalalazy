namespace WondrousTailsSolver;

/// <summary>
/// Fork-only. The WeeklyBingo update callback fires every frame, but the overlay text is a pure
/// function of the board mask and the placed-sticker count. The controller asks
/// <see cref="ShouldRefresh"/> first and calls <see cref="Commit"/> only after the text was
/// actually updated, so a pass that bailed out early never suppresses the next real refresh.
/// </summary>
internal sealed class RefreshGate
{
    private int lastBoardMask = -1;
    private int lastStickers = -1;

    /// <summary>True when the inputs differ from the last committed ones (always true after construction or <see cref="Reset"/>).</summary>
    public bool ShouldRefresh(int boardMask, int stickers)
        => boardMask != lastBoardMask || stickers != lastStickers;

    /// <summary>Records the inputs the overlay text was just rendered from.</summary>
    public void Commit(int boardMask, int stickers)
    {
        lastBoardMask = boardMask;
        lastStickers = stickers;
    }

    /// <summary>Forgets the committed inputs, so the next frame does a full pass.</summary>
    public void Reset()
    {
        lastBoardMask = -1;
        lastStickers = -1;
    }
}
