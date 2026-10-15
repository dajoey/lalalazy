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
        // tankbusterSetUp never arms. Cover at Standard, First Degree and unread degree; no cover under the gate.
        var hold = CrucibleState() with
        {
            CrucibleBoard = 2, CrucibleBattle = 1, TargetCastId = 48138, TargetCastRemaining = 3f,
            ReadySnarl = true, PetHp = 3492f, PetHpPercent = 100f, PlayerHp = 5240f, PlayerHpPercent = 93f,
        };
        Check("r4-tankbuster: 2.1-02 Deadly Hold, a 3,492 HP familiar at Standard (227 x 1.25 = 284): Snarl covers it",
            Decide(hold, on).Reason == "aggro:snarl-tankbuster", Decide(hold, on).Reason);
        Check("r4-tankbuster: 2.1-02 Deadly Hold, First Degree (227 x 2.0 x 1.25 = 568), a 600 HP familiar: cover",
            Decide(At(hold with { PetHp = 600f, PetHpPercent = 80f }, CrucibleDegree.First), on).Reason == "aggro:snarl-tankbuster",
            Decide(At(hold with { PetHp = 600f, PetHpPercent = 80f }, CrucibleDegree.First), on).Reason);
        Check("r4-tankbuster: 2.1-02 Deadly Hold, degree unread (margin 2.0 -> 227 x 2.0 = 454), a 460 HP familiar: cover",
            Decide(hold with { PetHp = 460f, PetHpPercent = 80f }, on).Reason == "aggro:snarl-tankbuster",
            Decide(hold with { PetHp = 460f, PetHpPercent = 80f }, on).Reason);
        Check("r4-tankbuster: 2.1-02 Deadly Hold, a 270 HP familiar at Standard (under 284): no cover",
            Decide(hold with { PetHp = 270f, PetHpPercent = 80f }, on).Reason != "aggro:snarl-tankbuster",
            Decide(hold with { PetHp = 270f, PetHpPercent = 80f }, on).Reason);
        Check("r4-tankbuster: 2.1-02 Deadly Hold, First Degree, a 500 HP familiar (under 568): no cover",
            Decide(At(hold with { PetHp = 500f, PetHpPercent = 80f }, CrucibleDegree.First), on).Reason != "aggro:snarl-tankbuster",
            Decide(At(hold with { PetHp = 500f, PetHpPercent = 80f }, CrucibleDegree.First), on).Reason);
        Check("r4-tankbuster: 2.1-02 Deadly Hold, degree unread, a 400 HP familiar (under 454): no cover",
            Decide(hold with { PetHp = 400f, PetHpPercent = 80f }, on).Reason != "aggro:snarl-tankbuster",
            Decide(hold with { PetHp = 400f, PetHpPercent = 80f }, on).Reason);

        // ---- 2.5-04 100-tonze Slash 48204 (Second Board battle 5, the tablitaur pair): measured 549 on the character,
        // 614 on a familiar (Drake). Registered (Board 1-3 id) with no measured row, so the cover never arms.
        var tonze = CrucibleState() with
        {
            CrucibleBoard = 2, CrucibleBattle = 5, TargetCastId = 48204, TargetCastRemaining = 5f,
            ReadySnarl = true, PetHp = 3492f, PetHpPercent = 100f, PlayerHp = 5240f, PlayerHpPercent = 93f,
        };
        Check("r4-tankbuster: 2.5-04 100-tonze Slash, a 3,492 HP familiar at Standard (614 x 1.25 = 768): Snarl covers it",
            Decide(tonze, on).Reason == "aggro:snarl-tankbuster", Decide(tonze, on).Reason);
        Check("r4-tankbuster: 2.5-04 100-tonze Slash, First Degree (614 x 2.0 x 1.25 = 1535), a 1,600 HP familiar: cover",
            Decide(At(tonze with { PetHp = 1600f, PetHpPercent = 80f }, CrucibleDegree.First), on).Reason == "aggro:snarl-tankbuster",
            Decide(At(tonze with { PetHp = 1600f, PetHpPercent = 80f }, CrucibleDegree.First), on).Reason);
        Check("r4-tankbuster: 2.5-04 100-tonze Slash, degree unread (614 x 2.0 = 1228), a 1,300 HP familiar: cover",
            Decide(tonze with { PetHp = 1300f, PetHpPercent = 80f }, on).Reason == "aggro:snarl-tankbuster",
            Decide(tonze with { PetHp = 1300f, PetHpPercent = 80f }, on).Reason);

        Check("r4-tankbuster: 2.5-04 100-tonze Slash, a 700 HP familiar at Standard (under 768): no cover",
            Decide(tonze with { PetHp = 700f, PetHpPercent = 80f }, on).Reason != "aggro:snarl-tankbuster",
            Decide(tonze with { PetHp = 700f, PetHpPercent = 80f }, on).Reason);
        Check("r4-tankbuster: 2.5-04 100-tonze Slash, First Degree, a 1,500 HP familiar (under 1535): no cover",
            Decide(At(tonze with { PetHp = 1500f, PetHpPercent = 80f }, CrucibleDegree.First), on).Reason != "aggro:snarl-tankbuster",
            Decide(At(tonze with { PetHp = 1500f, PetHpPercent = 80f }, CrucibleDegree.First), on).Reason);
        Check("r4-tankbuster: 2.5-04 100-tonze Slash, degree unread, a 1,200 HP familiar (under 1228): no cover",
            Decide(tonze with { PetHp = 1200f, PetHpPercent = 80f }, on).Reason != "aggro:snarl-tankbuster",
            Decide(tonze with { PetHp = 1200f, PetHpPercent = 80f }, on).Reason);

        // ---- 3.1-08a Crushing Blade 48471 (Third Board battle 1, the cavalier; also the 15 y knockback cast): measured
        // 455 on the character in 88 of 88 hits, never once on a familiar. Registered with no measured row, so the cover
        // never arms (the logs saw 50 castbars and 0 snarl decisions). The cover arms on the documented estimate.
        var blade = CrucibleState() with
        {
            CrucibleBoard = 3, CrucibleBattle = 1, TargetCastId = 48471, TargetCastRemaining = 3f,
            ReadySnarl = true, PetHp = 3492f, PetHpPercent = 100f, PlayerHp = 5240f, PlayerHpPercent = 93f,
        };
        Check("r4-tankbuster: 3.1-08a Crushing Blade, a 3,492 HP familiar at Standard (estimate 751 x 1.25 = 939): Snarl covers it",
            Decide(blade, on).Reason == "aggro:snarl-tankbuster", Decide(blade, on).Reason);
        Check("r4-tankbuster: 3.1-08a Crushing Blade, First Degree (751 x 2.0 x 1.25 = 1878), a 2,000 HP familiar: cover",
            Decide(At(blade with { PetHp = 2000f, PetHpPercent = 80f }, CrucibleDegree.First), on).Reason == "aggro:snarl-tankbuster",
            Decide(At(blade with { PetHp = 2000f, PetHpPercent = 80f }, CrucibleDegree.First), on).Reason);
        Check("r4-tankbuster: 3.1-08a Crushing Blade, degree unread (751 x 2.0 = 1502), a 1,600 HP familiar: cover",
            Decide(blade with { PetHp = 1600f, PetHpPercent = 80f }, on).Reason == "aggro:snarl-tankbuster",
            Decide(blade with { PetHp = 1600f, PetHpPercent = 80f }, on).Reason);

        Check("r4-tankbuster: 3.1-08a Crushing Blade, a 900 HP familiar at Standard (under 939): no cover",
            Decide(blade with { PetHp = 900f, PetHpPercent = 80f }, on).Reason != "aggro:snarl-tankbuster",
            Decide(blade with { PetHp = 900f, PetHpPercent = 80f }, on).Reason);
        Check("r4-tankbuster: 3.1-08a Crushing Blade, First Degree, an 1,800 HP familiar (under 1878): no cover",
            Decide(At(blade with { PetHp = 1800f, PetHpPercent = 80f }, CrucibleDegree.First), on).Reason != "aggro:snarl-tankbuster",
            Decide(At(blade with { PetHp = 1800f, PetHpPercent = 80f }, CrucibleDegree.First), on).Reason);
        Check("r4-tankbuster: 3.1-08a Crushing Blade, degree unread, a 1,400 HP familiar (under 1502): no cover",
            Decide(blade with { PetHp = 1400f, PetHpPercent = 80f }, on).Reason != "aggro:snarl-tankbuster",
            Decide(blade with { PetHp = 1400f, PetHpPercent = 80f }, on).Reason);
        Check("r4-tankbuster: 3.1-08a Crushing Blade, the character under the measured 455 but a 900 HP familiar: still no cover (the estimate never takes the death-cover)",
            Decide(blade with { PetHp = 900f, PetHpPercent = 80f, PlayerHp = 400f, PlayerHpPercent = 9f }, on).Reason != "aggro:snarl-tankbuster",
            Decide(blade with { PetHp = 900f, PetHpPercent = 80f, PlayerHp = 400f, PlayerHpPercent = 9f }, on).Reason);

        // ---- 3.1-09 Blizzard 48621 (Third Board bone bishop 14567, plus the sheet-pinned casters of the same cast: ice
        // sprite 14607, kupocaster 14679) and Straight Punch 49435 (Hapalit 14691): single-target hits that follow enmity
        // with NO castbar, so no tankbuster key ever fires. The cover keys on the caster.
        var bishop = CrucibleState() with
        {
            CrucibleBoard = 3, CrucibleBattle = 1, TargetNameId = 14567, ReadySnarl = true,
            PetHp = 3492f, PetHpPercent = 80f, PlayerHp = 5240f, PlayerHpPercent = 93f,
        };
        Check("r4-tankbuster: 3.1-09 Blizzard, the bone bishop on the character, a 3,492 HP familiar (48 x 1.25 = 60): Snarl covers it",
            Decide(bishop, on).Reason == "aggro:snarl-panel-burst", Decide(bishop, on).Reason);
        Check("r4-tankbuster: 3.1-09 Blizzard, the ice sprite (same cast, First Master's 4.4): Snarl covers it",
            Decide(bishop with { CrucibleBoard = 4, CrucibleBattle = 4, TargetNameId = 14607 }, on).Reason == "aggro:snarl-panel-burst",
            Decide(bishop with { CrucibleBoard = 4, CrucibleBattle = 4, TargetNameId = 14607 }, on).Reason);
        Check("r4-tankbuster: 3.1-09 Blizzard, the kupocaster (same cast, Second Master's 5.11): Snarl covers it",
            Decide(bishop with { CrucibleBoard = 5, CrucibleBattle = 11, TargetNameId = 14679 }, on).Reason == "aggro:snarl-panel-burst",
            Decide(bishop with { CrucibleBoard = 5, CrucibleBattle = 11, TargetNameId = 14679 }, on).Reason);

        var punch = CrucibleState() with
        {
            CrucibleBoard = 5, CrucibleBattle = 13, TargetNameId = 14691, ReadySnarl = true,
            PetHp = 1200f, PetHpPercent = 80f, PlayerHp = 5240f, PlayerHpPercent = 93f,
        };


        Check("r4-tankbuster: 3.1-09 Straight Punch, Hapalit on the character, a 1,200 HP familiar at Standard (909 x 1.25 = 1136): Snarl covers it",
            Decide(At(punch, CrucibleDegree.Standard), on).Reason == "aggro:snarl-panel-burst",
            Decide(At(punch, CrucibleDegree.Standard), on).Reason);
        Check("r4-tankbuster: 3.1-09 Straight Punch, First Degree (909 x 2.0 x 1.25 = 2273), a 2,400 HP familiar: cover",
            Decide(At(punch with { PetHp = 2400f }, CrucibleDegree.First), on).Reason == "aggro:snarl-panel-burst",
            Decide(At(punch with { PetHp = 2400f }, CrucibleDegree.First), on).Reason);
        Check("r4-tankbuster: 3.1-09 Straight Punch, degree unread (909 x 2.0 = 1818), a 1,900 HP familiar: cover",
            Decide(punch with { PetHp = 1900f }, on).Reason == "aggro:snarl-panel-burst",
            Decide(punch with { PetHp = 1900f }, on).Reason);

        Check("r4-tankbuster: 3.1-09 Blizzard, a 40 percent familiar (under the cleave settle bar): no panel-burst cover",
            Decide(bishop with { PetHp = 3492f, PetHpPercent = 40f }, on).Reason != "aggro:snarl-panel-burst",
            Decide(bishop with { PetHp = 3492f, PetHpPercent = 40f }, on).Reason);
        Check("r4-tankbuster: 3.1-09 Straight Punch, a 1,100 HP familiar at Standard (under 1136): no cover",
            Decide(At(punch with { PetHp = 1100f }, CrucibleDegree.Standard), on).Reason != "aggro:snarl-panel-burst",
            Decide(At(punch with { PetHp = 1100f }, CrucibleDegree.Standard), on).Reason);
        Check("r4-tankbuster: 3.1-09 Straight Punch, First Degree, a 2,200 HP familiar (under 2273): no cover",
            Decide(At(punch with { PetHp = 2200f }, CrucibleDegree.First), on).Reason != "aggro:snarl-panel-burst",
            Decide(At(punch with { PetHp = 2200f }, CrucibleDegree.First), on).Reason);
        Check("r4-tankbuster: 3.1-09 a manticore (not a panel-burst caster) on the character: no panel-burst cover",
            Decide(bishop with { TargetNameId = 14566 }, on).Reason != "aggro:snarl-panel-burst",
            Decide(bishop with { TargetNameId = 14566 }, on).Reason);

        // ---- 5.5-06 Mangling Fang 49254 (Second Master's battle 5, Drake Piece): the row had MaxOnFamiliar 0 (no familiar
        // hit in its original window); 2026-10-15 re-derivation measured Damselfly 691. The cover arms on the measured hit.
        var fang = CrucibleState() with
        {
            CrucibleBoard = 5, CrucibleBattle = 5, TargetCastId = 49254, TargetCastRemaining = 3f,
            ReadySnarl = true, PetHp = 900f, PetHpPercent = 80f, PlayerHp = 5240f, PlayerHpPercent = 93f,
        };
        Check("r4-tankbuster: 5.5-06 Mangling Fang, a 900 HP familiar at Standard (691 x 1.25 = 864): Snarl covers it",
            Decide(At(fang, CrucibleDegree.Standard), on).Reason == "aggro:snarl-tankbuster",
            Decide(At(fang, CrucibleDegree.Standard), on).Reason);
    }
}
