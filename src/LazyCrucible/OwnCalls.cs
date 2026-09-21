namespace LazyCrucible;

/// <summary>
///     Greater than 0 while one of LazyCrucible's own inputs is on the stack (a familiar toggle, a shop purchase, a
///     button click, a Yes). The agent probe and the callback recorder read it so the plugin never mistakes its own
///     input for the player's edit (which makes every writer stand down for that screen).
/// </summary>
internal static class OwnCalls
{
    public static int Depth;
}
