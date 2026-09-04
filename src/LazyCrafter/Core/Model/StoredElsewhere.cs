namespace LazyCrafter.Core.Model;

/// <summary>
/// One place, other than the character's bags, that is holding some of an item (Plan §Phase 5 defect fix).
/// <para>
/// "Owned" is not "in the bags": a craft can only consume what is physically in the four bags plus the crystal
/// pouch. Everything else - a retainer, the chocobo saddlebag, the armoury chest, the glamour dresser, the free
/// company chest, another character - has to be fetched first, by hand, before a craft can start.
/// </para>
/// <see cref="Where"/> is a short place name written so it reads after "from": <c>retainer Cid</c>,
/// <c>the saddlebag</c>, <c>the armoury chest</c>. <see cref="Phrase"/> is the same fact written as a count.
/// </summary>
public sealed record StoredElsewhere(string Where, int Quantity)
{
    /// <summary>"107 on retainer Cid" / "3 in the saddlebag" - for the refusal and retrieve lines.</summary>
    public string Phrase => Quantity + (Where.Contains("retainer", StringComparison.OrdinalIgnoreCase) ? " on " : " in ") + Where;
}
