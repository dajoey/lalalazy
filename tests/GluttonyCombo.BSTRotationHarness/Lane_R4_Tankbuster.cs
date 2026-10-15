using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Round 4 tankbuster lane (task tasks-20261015-crucible-r4-fix-tankbuster-registry-01). Titles start with "r4-tankbuster:".
///     Rows: 2.1-02 Deadly Hold 48138, 2.5-04 100-tonze Slash 48204, 2.6-06 Void Fire III 48222, 3.1-08a Crushing Blade 48471,
///     3.1-09 Blizzard 48621, 4.4-07 Cauterize 48704 knockback, 4.5 Fivefold Fallout 48724, 5.13 Straight Punch 49435,
///     5.5-06 Mangling Fang 49254, 2.0-07 Goblin Rush 48517. The Desolation/Outcrop row is closed without code on the fix page.
/// </summary>
internal static partial class Program
{
    private static void R4TankbusterLane20261015()
    {
        Console.WriteLine("-- r4-tankbuster: register the missing tankbusters, arm the familiar cover on unmeasured ones --");
        var cfg = BstSettings.Defaults();
        var on = cfg with { CrucibleAggro = CrucibleAggroMode.On };
        BstState At(BstState s, int degree) => s with { CrucibleDegreeKnown = true, CrucibleDegreeLevel = degree };

        // ---- 2.1-02 Deadly Hold 48138 (Second Board battle 1): measured 206 on the character, 227 on a familiar (Diremite).
        // The cast IS registered but FamiliarCanTakeTankbuster answers false without a measured MaxOnFamiliar row, so
        // tankbusterSetUp never arms. Cover at Standard, First Degree and unread degree once the row exists.
        var hold = CrucibleState() with
        {
            CrucibleBoard = 2, CrucibleBattle = 1, TargetCastId = 48138, TargetCastRemaining = 3f,
            ReadySnarl = true, PetHp = 3492f, PetHpPercent = 100f, PlayerHp = 5240f, PlayerHpPercent = 93f,
        };
        Check("r4-tankbuster: 2.1-02 Deadly Hold, a 3,492 HP familiar at Standard (227 x 1.25 = 284): Snarl covers it",
            Decide(hold, on).Reason == "aggro:snarl-tankbuster", Decide(hold, on).Reason);
        Check("r4-tankbuster: 2.1-02 Deadly Hold, First Degree (227 x 2.0 x 1.25 = 568), a 600 HP familiar: cover",
            Decide(At(hold with { PetHp = 600f, PetHpPercent = 18f }, CrucibleDegree.First), on).Reason == "aggro:snarl-tankbuster",
            Decide(At(hold with { PetHp = 600f, PetHpPercent = 18f }, CrucibleDegree.First), on).Reason);
        Check("r4-tankbuster: 2.1-02 Deadly Hold, degree unread (margin 2.0 -> 227 x 2.0 = 454), a 460 HP familiar: cover",
            Decide(hold with { PetHp = 460f, PetHpPercent = 14f }, on).Reason == "aggro:snarl-tankbuster",
            Decide(hold with { PetHp = 460f, PetHpPercent = 14f }, on).Reason);
    }
}
