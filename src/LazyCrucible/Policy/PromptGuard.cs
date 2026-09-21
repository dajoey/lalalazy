namespace LazyCrucible;

/// <summary>
///     Decides whether a Yes/No prompt is the one LazyCrucible's own input opened, before Yes is pressed. PURE.
///     Templates are the client's Addon sheet rows (English text from the 7.56 sheet; the live layer reads the same
///     rows from the running client, so the fragments follow the client language). A prompt passes only when it
///     contains the expected template's fixed text in order AND the names the plugin chose (item, familiar), and matches
///     none of the refused templates (forfeit, suspend, commence, leave/close without taking, discard, sell, rest).
/// </summary>
internal static class PromptGuard
{
    public enum Kind
    {
        None,
        /// <summary> Addon#17641 "Purchase &lt;item&gt;?" </summary>
        Purchase,
        /// <summary> Addon#17653 "Purchase &lt;feed&gt; and feed it to your &lt;familiar&gt;?" </summary>
        Feed,
        /// <summary> Addon#17626 "Choose &lt;item&gt;?" </summary>
        Treasure,
        /// <summary> Addon#17674 "You will receive: ・ N Territory Tokens ・ &lt;item&gt; ... Proceed?" </summary>
        TakeAll,
    }

    /// <summary> Addon sheet rows per kind (the live layer loads their text from the client). </summary>
    public static readonly IReadOnlyDictionary<Kind, uint> Rows = new Dictionary<Kind, uint>
    {
        [Kind.Purchase] = 17641,
        [Kind.Feed] = 17653,
        [Kind.Treasure] = 17626,
        [Kind.TakeAll] = 17674,
    };

    /// <summary>
    ///     Prompts Yes is never pressed on: 17618 forfeit, 17619 suspend, 17787 challenge the board, 17789 commence with
    ///     an empty horn, 17788 discard changes, 17633 close the coffer empty-handed, 17632/17647/17684/17610 discard,
    ///     17648 leave the shop, 17656 leave the campsite, 17660/17662 rest, 17663 sell, 17676 leave loot behind,
    ///     17894/17895 flee, 17697 revive, 17927 remove all familiars, 17918-17920 team presets.
    /// </summary>
    public static readonly uint[] RefusedRows =
        [17618, 17619, 17787, 17789, 17788, 17633, 17632, 17647, 17684, 17610, 17648, 17656, 17660, 17662, 17663, 17676, 17894, 17895, 17697, 17927, 17918, 17919, 17920];

    /// <summary> English fixed text per kind (7.56 Addon sheet, names removed), used when the client sheet cannot be read. </summary>
    public static readonly IReadOnlyDictionary<Kind, string[]> EnglishFragments = new Dictionary<Kind, string[]>
    {
        [Kind.Purchase] = ["Purchase ", "?"],
        [Kind.Feed] = ["Purchase ", " and feed it to your ", "?"],
        [Kind.Treasure] = ["Choose ", "?"],
        [Kind.TakeAll] = ["You will receive:", "Proceed?"],
    };

    /// <summary> English fixed text of the refused prompts (distinctive parts). </summary>
    public static readonly string[][] EnglishRefused =
    [
        ["Forfeit and exit"], ["Suspend progress"], ["Proceed to challenge this board?"], ["Commence battle anyway?"],
        ["Discard changes"], ["Close the coffer"], ["Discard "], ["Conclude purchasing"], ["Leave the campsite"],
        ["Rest and recover"], ["Sell "], ["Leave it behind?"], ["to flee from battle?"], ["to revive your "],
        ["Remove all familiars"], ["team composition?"],
    ];

    /// <summary> The fixed text of the prompt kind to expect (client fragments when known). </summary>
    public static Func<Kind, IReadOnlyList<string>> Expected { get; set; } = k => EnglishFragments.TryGetValue(k, out var f) ? f : [];

    /// <summary> The refused prompts' fixed text (client fragments when known). </summary>
    public static Func<IReadOnlyList<IReadOnlyList<string>>> Refused { get; set; } = () => EnglishRefused;

    /// <summary> Whether <paramref name="text"/> contains <paramref name="fragments"/> in order (ignoring case). </summary>
    public static bool ContainsInOrder(string text, IReadOnlyList<string> fragments)
    {
        var at = 0;
        foreach (var f in fragments)
        {
            if (f.Length == 0)
                continue;
            var i = text.IndexOf(f, at, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                return false;
            at = i + f.Length;
        }
        return true;
    }

    /// <summary> Yes may be pressed only when this returns ok; <c>why</c> says what failed. </summary>
    public static (bool Ok, string Why) Check(Kind kind, string text, params string[] names)
    {
        if (kind == Kind.None)
            return (false, "no prompt expected");
        if (string.IsNullOrWhiteSpace(text))
            return (false, "empty prompt");
        foreach (var refused in Refused())
        {
            var fixedText = refused.Where(f => f.Trim().Length > 2).ToList();
            if (fixedText.Count > 0 && ContainsInOrder(text, fixedText))
                return (false, "refused prompt");
        }
        var expected = Expected(kind);
        if (expected.Count == 0 || !ContainsInOrder(text, expected))
            return (false, "not the expected prompt");
        foreach (var n in names)
            if (!string.IsNullOrEmpty(n) && text.IndexOf(n, StringComparison.OrdinalIgnoreCase) < 0)
                return (false, $"'{n}' not in the prompt");
        return (true, "");
    }
}

/// <summary>
///     Treasure pick actuation. The choice buttons are plain buttons whose click event param starts at 2 [pub]; which
///     param belongs to which choice (AtkValue order) was never recorded, so the treasure pick ships as a suggestion
///     until the 0.1.0.0 recorder's <c>XR|</c> line of one manual pick (param) and the HUD after it (item) ground the
///     mapping. The input path below is complete and still refuses Yes unless the prompt names the chosen item.
/// </summary>
internal static class TreasureActuation
{
    public static readonly bool Grounded = false;
}
