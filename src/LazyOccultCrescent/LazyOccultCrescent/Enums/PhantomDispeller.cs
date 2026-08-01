namespace LazyOccultCrescent.Enums;

// North Horn's equivalent of South Horn's demiatma. Values are Item row ids,
// datamined 2026-08-01 (Item 50974-50976).
//
// There are three, not six - the zone does not use demiatma at all, so any
// North Horn event carrying a Demiatma value would be wrong by construction.
public enum PhantomDispeller
{
    Alpha = 50974, // Phantom Dispeller α
    Beta = 50975,  // Phantom Dispeller β
    Gamma = 50976, // Phantom Dispeller γ
}

public static class PhantomDispellerExtensions
{
    public static string ToFriendlyString(this PhantomDispeller dispeller)
    {
        return dispeller switch
        {
            PhantomDispeller.Alpha => "Phantom Dispeller α",
            PhantomDispeller.Beta => "Phantom Dispeller β",
            PhantomDispeller.Gamma => "Phantom Dispeller γ",
            _ => dispeller.ToString(),
        };
    }
}
