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
/// <para>
/// <see cref="Fetchable"/> separates the two kinds of place that end up in one list. A retainer, the saddlebag,
/// the armoury chest and the glamour dresser all hold units you can go and get; a market-board listing does not -
/// it is reported purely so the player is told where the stock went (2026-09-05, t_c69287be). Everything defaults
/// to <c>true</c>, so a place is only ever unreachable when its producer says so explicitly.
/// </para>
/// </summary>
/// <param name="Where">Short place name, written to read after "from".</param>
/// <param name="Quantity">Units this place is holding.</param>
/// <param name="Fetchable">
/// Whether those units can actually be brought into the bags. <c>false</c> only for a market-board listing:
/// it is named for information, never counted as stock you have, and never a place a retrieval can be satisfied
/// from. Consumers that pick "where do I go to get this" must prefer fetchable places (see
/// <c>DispatchPlan.PlacesFor</c>) - sorting by quantity alone points the player at the board (card t_05e6722b).
/// </param>
/// <param name="Retainer">
/// The retainer this place belongs to, as a bare name, when one is known (card t_35be7be5). Set by the producer -
/// never parsed back out of <see cref="Where"/>, which is a display string and may be a fallback wording. It is
/// what lets <see cref="BlockedListings"/> group "pull these off sale" advice by retainer, so one summoning-bell
/// visit covers one retainer. <c>null</c> when the place is not a retainer's, or when the retainer names could not
/// be read (the adapter then falls back to one unnamed entry).
/// </param>
public sealed record StoredElsewhere(string Where, int Quantity, bool Fetchable = true, string? Retainer = null)
{
    /// <summary>"107 on retainer Cid" / "3 in the saddlebag" - for the refusal and retrieve lines.</summary>
    public string Phrase => Quantity + (Where.Contains("retainer", StringComparison.OrdinalIgnoreCase) ? " on " : " in ") + Where;

    /// <summary>The grouping key for per-retainer advice: the retainer's name when known, else the place name itself.</summary>
    public string Owner => string.IsNullOrWhiteSpace(Retainer) ? Where : Retainer!;
}
