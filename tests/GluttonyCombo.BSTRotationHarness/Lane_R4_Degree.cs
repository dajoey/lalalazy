using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Round 4 degree lane (task tasks-20261015-crucible-r4-degree-guard-first-degree-01). Titles start with "r4-degree:".
///     Rows of the Degree Matrix: "Second Degree hit scale", "Third Degree hit scale" (the guard scales) and "Degree-specific casts"
///     (a cast id no table knows, at First / Second / Third).
/// </summary>
internal static partial class Program
{
    private static void R4DegreeLane20261015()
    {
        Console.WriteLine("-- r4-degree: guard scales at Second / Third Degree, unknown cast at every degree --");
        var cfg = BstSettings.Defaults();
        var on = cfg with { CrucibleAggro = CrucibleAggroMode.On };
        BstState At(BstState s, int degree) => s with { CrucibleDegreeKnown = true, CrucibleDegreeLevel = degree };

        // ---- Second Degree hit scale. Measured: the one recorded Second Degree fight (encounter 2286, 2026-10-05) put the Auto-attack c660 on the
        // character at 37.5 on average (n=13) against 15.2 at Standard = 2.47x. The guides say +84% (nettoge) for Second and +126% for Third.
        // Third Degree has no run: its figure follows the guides' step from Second (2.26 / 1.84 = 1.23) applied to the measured Second figure.
        const float measuredSecond = 2.47f, guideSecond = 1.84f, guideThird = 2.26f;
        var charSecond = CrucibleDegree.CharacterHitScale(true, CrucibleDegree.Second);
        var charThird = CrucibleDegree.CharacterHitScale(true, CrucibleDegree.Third);
        var charFirst = CrucibleDegree.CharacterHitScale(true, CrucibleDegree.First);
        var famFirst = CrucibleDegree.FamiliarHitScale(true, CrucibleDegree.First);
        var famSecond = CrucibleDegree.FamiliarHitScale(true, CrucibleDegree.Second);
        var famThird = CrucibleDegree.FamiliarHitScale(true, CrucibleDegree.Third);

        Check("r4-degree: second-degree-hit-scale: the character scale at Second is not below the measured 2.47x",
            charSecond >= measuredSecond, $"Second {charSecond}");
        Check("r4-degree: third-degree-hit-scale: the character scale at Third is not below Second",
            charThird >= charSecond, $"Third {charThird}, Second {charSecond}");
        Check("r4-degree: third-degree-hit-scale: the character scale at Third is not below the guides' +126% (2.26x)",
            charThird >= guideThird, $"Third {charThird}");
        Check("r4-degree: third-degree-hit-scale: Third follows the guides' step from the measured Second (2.47 x 2.26 / 1.84 = 3.03)",
            charThird >= measuredSecond * guideThird / guideSecond, $"Third {charThird}");
        Check("r4-degree: second-degree-hit-scale: the familiar scale keeps First's familiar-to-character ratio at Second",
            famSecond >= charSecond * famFirst / charFirst - 0.001f, $"familiar Second {famSecond}, character Second {charSecond}");
        Check("r4-degree: third-degree-hit-scale: the familiar scale keeps First's familiar-to-character ratio at Third",
            famThird >= charThird * famFirst / charFirst - 0.001f, $"familiar Third {famThird}, character Third {charThird}");
        Check("r4-degree: scales never fall as the degree rises (character and familiar, Standard to Third)",
            CrucibleDegree.CharacterHitScale(true, CrucibleDegree.Standard) <= charFirst && charFirst <= charSecond && charSecond <= charThird
            && CrucibleDegree.FamiliarHitScale(true, CrucibleDegree.Standard) <= famFirst && famFirst <= famSecond && famSecond <= famThird);
        Check("r4-degree: the cover margin at Second and Third is not below First's (the unmeasured degrees keep the wider margin)",
            CrucibleDegree.CoverMargin(true, CrucibleDegree.Second) >= CrucibleDegree.CoverMargin(true, CrucibleDegree.First)
            && CrucibleDegree.CoverMargin(true, CrucibleDegree.Third) >= CrucibleDegree.CoverMargin(true, CrucibleDegree.First));

        // ---- Standard play unchanged: the figures of a60f1ec77.
        Check("r4-degree: Standard figures unchanged (character 1.0, familiar 1.0, margin 1.25)",
            CrucibleDegree.CharacterHitScale(true, CrucibleDegree.Standard) == 1f && CrucibleDegree.FamiliarHitScale(true, CrucibleDegree.Standard) == 1f
            && CrucibleDegree.CoverMargin(true, CrucibleDegree.Standard) == 1.25f);
        Check("r4-degree: unread degree figures unchanged (character 1.0, familiar 1.0, margin 2.0)",
            CrucibleDegree.CharacterHitScale(false, CrucibleDegree.Third) == 1f && CrucibleDegree.FamiliarHitScale(false, CrucibleDegree.Third) == 1f
            && CrucibleDegree.CoverMargin(false, CrucibleDegree.First) == 2f && CrucibleDegree.CoverMargin(false, CrucibleDegree.Unknown) == 2f);
        Check("r4-degree: First Degree figures unchanged (character 1.75, familiar 2.0, margin 1.25)",
            charFirst == 1.75f && famFirst == 2f && CrucibleDegree.CoverMargin(true, CrucibleDegree.First) == 1.25f);

        // ---- Decisions at the changed scales. Rotten Stench (48690, PartyWide): recorded 773 on the character.
        var stench = CrucibleState() with
        {
            InCombat = true, CrucibleBoard = 4, CrucibleBattle = 3, TargetCastId = 48690, TargetCastRemaining = 3f,
            PlayerHp = 1500f, PlayerHpPercent = 37f, ReadyHealPotion = 46961, EnemyTargetsPet = true, EnemyTargetsPlayer = false,
        };
        Check("r4-degree: second-degree-hit-scale: Rotten Stench, 1,500 HP, First Degree (1.75 x 773 = 1,353): no guard",
            Decide(At(stench, CrucibleDegree.First), cfg).Reason != "crucible:raidwide-guard");
        Check("r4-degree: second-degree-hit-scale: Rotten Stench, 1,500 HP, Second Degree (2.5 x 773 = 1,933): the potion",
            Decide(At(stench, CrucibleDegree.Second), cfg) is { ActionId: 46961, Reason: "crucible:raidwide-guard" }, Decide(At(stench, CrucibleDegree.Second), cfg).Reason);
        var stench2200 = stench with { PlayerHp = 2200f, PlayerHpPercent = 54f };
        Check("r4-degree: third-degree-hit-scale: Rotten Stench, 2,200 HP, Second Degree (1,933): no guard",
            Decide(At(stench2200, CrucibleDegree.Second), cfg).Reason != "crucible:raidwide-guard");
        Check("r4-degree: third-degree-hit-scale: Rotten Stench, 2,200 HP, Third Degree (3.1 x 773 = 2,396): the potion",
            Decide(At(stench2200, CrucibleDegree.Third), cfg) is { ActionId: 46961, Reason: "crucible:raidwide-guard" }, Decide(At(stench2200, CrucibleDegree.Third), cfg).Reason);
        Check("r4-degree: Rotten Stench, 2,200 HP, Standard and unread: no guard (unchanged)",
            Decide(At(stench2200, CrucibleDegree.Standard), cfg).Reason != "crucible:raidwide-guard" && Decide(stench2200, cfg).Reason != "crucible:raidwide-guard");

        // Sweeping Evisceration (48717, Tankbuster, recorded 1,225 on the character) with a 1,500 HP familiar that cannot take it: the cover Snarl
        // only goes in when the character is already under the scaled hit (the alternative is the death).
        var evis = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 5, TargetCastId = 48717, TargetCastRemaining = 7.6f,
            ReadySnarl = true, ReadyParting = true, PetHp = 1500f, PetHpPercent = 43f, PlayerHp = 2800f, PlayerHpPercent = 69f,
            EnemyTargetsPlayer = true, EnemyTargetsPet = false,
        };
        bool Snarls(BstState s) => Decide(s, on) is { ActionId: BST.Snarl, Reason: "aggro:snarl-tankbuster" };
        Check("r4-degree: second-degree-hit-scale: Evisceration, the character at 2,800 HP (over 1.75 x 1,225 = 2,144), First Degree: no cover Snarl",
            !Snarls(At(evis, CrucibleDegree.First)));
        Check("r4-degree: second-degree-hit-scale: Evisceration, the character at 2,800 HP (under 2.5 x 1,225 = 3,063), Second Degree: the cover Snarl goes in",
            Snarls(At(evis, CrucibleDegree.Second)), $"{Decide(At(evis, CrucibleDegree.Second), on).ActionId}/{Decide(At(evis, CrucibleDegree.Second), on).Reason}");
        var evis3500 = evis with { PlayerHp = 3500f, PlayerHpPercent = 86f };
        Check("r4-degree: third-degree-hit-scale: Evisceration, the character at 3,500 HP, Second Degree (3,063): no cover Snarl",
            !Snarls(At(evis3500, CrucibleDegree.Second)));
        Check("r4-degree: third-degree-hit-scale: Evisceration, the character at 3,500 HP (under 3.1 x 1,225 = 3,798), Third Degree: the cover Snarl goes in",
            Snarls(At(evis3500, CrucibleDegree.Third)), $"{Decide(At(evis3500, CrucibleDegree.Third), on).ActionId}/{Decide(At(evis3500, CrucibleDegree.Third), on).Reason}");
        Check("r4-degree: Evisceration, the character at 2,800 HP, Standard and unread: no cover Snarl (unchanged)",
            !Snarls(At(evis, CrucibleDegree.Standard)) && !Snarls(evis));

        // ---- Degree-specific casts (UNOBSERVED row): a cast id no table knows, at First, Second and Third. The tables are degree-blind, so the
        // plugin treats it like any unknown cast: no hold, no stall, and the generic low-HP lines (panic heal in combat, board heal out of it) fire.
        const uint unknownCast = 9_999_001;
        Check("r4-degree: the unknown cast id is in no table (heavy cast, tankbuster)",
            BST_CrucibleData.HeavyCast(unknownCast) is null && !BST_CrucibleData.Tankbusters.Contains(unknownCast));
        var healthy = CrucibleState() with
        {
            InCombat = true, GcdReady = true, CanWeave = true, TargetCastId = unknownCast, TargetCastRemaining = 3f,
            PlayerHp = 4000f, PlayerHpPercent = 98f, ReadyHealPotion = 46961, HasHostileTarget = true, TargetDistance = 3f,
        };
        var baseline = Decide(healthy, cfg);
        foreach (var degree in new[] { CrucibleDegree.First, CrucibleDegree.Second, CrucibleDegree.Third })
        {
            var name = degree switch { CrucibleDegree.First => "First", CrucibleDegree.Second => "Second", _ => "Third" };
            var d = Decide(At(healthy, degree), cfg);
            Check($"r4-degree: unknown-degree-cast: {name} Degree, healthy character: not a hold, an action is chosen",
                d.ActionId != 0 && !d.Reason.StartsWith("crucible:hold", StringComparison.Ordinal), $"{d.ActionId}/{d.Reason}");
            Check($"r4-degree: unknown-degree-cast: {name} Degree, healthy character: the same decision as with the degree unread (degree-blind)",
                d.ActionId == baseline.ActionId && d.Reason == baseline.Reason, $"{d.ActionId}/{d.Reason} vs {baseline.ActionId}/{baseline.Reason}");
            var stalled = 0;
            for (var tick = 0; tick < 40; tick++)
            {
                var step = Decide(At(healthy with { TargetCastRemaining = 3f - 0.1f * tick }, degree), cfg);
                if (step.ActionId == 0 || step.Reason.StartsWith("crucible:hold", StringComparison.Ordinal))
                    stalled++;
            }
            Check($"r4-degree: unknown-degree-cast: {name} Degree, a 4 s cast counted down in 0.1 s ticks: no tick holds or returns nothing", stalled == 0, $"{stalled} of 40");
            var panic = Decide(At(healthy with { PlayerHp = 800f, PlayerHpPercent = 20f }, degree), cfg);
            Check($"r4-degree: unknown-degree-cast: {name} Degree, 20% HP in combat: the panic heal", panic is { ActionId: 46961, Reason: "crucible:panic-heal" }, $"{panic.ActionId}/{panic.Reason}");
            var board = Decide(At(healthy with { InCombat = false, TargetCastId = 0, TargetCastRemaining = 0f, PlayerHp = 2400f, PlayerHpPercent = 59f }, degree), cfg);
            Check($"r4-degree: unknown-degree-cast: {name} Degree, 59% HP out of combat: the board heal", board is { ActionId: 46961, Reason: "crucible:board-heal" }, $"{board.ActionId}/{board.Reason}");
        }
    }
}
