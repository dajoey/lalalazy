using Lalalazy.Crucible;

namespace LazyCrucible.Harness;

/// <summary>
///     Item values the tooltip parse misses (community picks checked against the 7.56 item tooltips): Beastly Knife (crit +8% and a
///     25% chance per crit of a 3% Damage Up stack, up to 8) parsed to nothing; Hero's Crown's Excellence (most attacks nullified and
///     +10% damage for 9 s after an instinctual combo) was unvalued; Rapid Recast (Astral Mantle 25%, Astral Band and Wristlet 15%,
///     Gold Hairpin 10%) was unvalued. Reraise is worth a little less at Third Degree only (one source calls revive potions a trap,
///     so the cut is modest).
/// </summary>
internal static class ItemValueCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    private const int HeroCrown = 27, AstralMantle = 35, AstralBand = 36, AstralWristlet = 37, FlameAxe = 38, BeastlyKnife = 55, GoldHairpin = 64;
    private const int G1Reraiser = 98, G2Reraiser = 99;

    private static int Value(int row, RunContext ctx, List<string>? why = null) => ItemValue.Of(row, ctx, Inventory.Empty, null, why);

    public static void Run()
    {
        Console.WriteLine("-- item values the tooltip parse misses --");
        var plain = new RunContext { Board = 4 };
        var fireAhead = new RunContext { Board = 4, WeaknessAhead = new Dictionary<CrucibleWeakness, int> { [CrucibleWeakness.Fire] = 3 } };

        var knifeWhy = new List<string>();
        var knife = Value(BeastlyKnife, plain, knifeWhy);
        Check("Beastly Knife is worth taking on its own and sits with the elemental axes (above an axe with no weak fight ahead, below one with three)",
            knife >= ItemValue.Worthwhile && knife > Value(FlameAxe, plain) && knife < Value(FlameAxe, fireAhead),
            $"knife {knife} ({string.Join(", ", knifeWhy)}), axe {Value(FlameAxe, plain)} / {Value(FlameAxe, fireAhead)}");

        var crownWhy = new List<string>();
        var crown = Value(HeroCrown, plain, crownWhy);
        Check("Hero's Crown counts its Excellence proc (attacks nullified for 9 s after an instinctual combo) on top of HP and damage",
            crownWhy.Any(w => w.Contains("Excellence")) && crown >= 30 + 18 / 3 + 10, $"{crown} ({string.Join(", ", crownWhy)})");

        int mantle = Value(AstralMantle, plain), band = Value(AstralBand, plain), wristlet = Value(AstralWristlet, plain), hairpin = Value(GoldHairpin, plain);
        Check("Rapid Recast is valued: Astral Mantle > Astral Band = Astral Wristlet > Gold Hairpin > 0, and the bands are worth taking on their own",
            mantle > band && band == wristlet && band > hairpin && hairpin > 0 && band >= ItemValue.Worthwhile,
            $"mantle {mantle}, band {band}, wristlet {wristlet}, hairpin {hairpin}");

        var standard = new RunContext { Board = 4, Degree = CrucibleDegree.Standard };
        var third = new RunContext { Board = 4, Degree = CrucibleDegree.Third };
        Check("Reraise is worth less at Third Degree only (a modest cut: still worth carrying)",
            Value(G1Reraiser, third) < Value(G1Reraiser, standard) && Value(G2Reraiser, third) < Value(G2Reraiser, standard)
            && Value(G1Reraiser, plain) == Value(G1Reraiser, standard) && Value(G2Reraiser, third) >= 50 && Value(G1Reraiser, third) >= ItemValue.Worthwhile,
            $"G1 {Value(G1Reraiser, standard)} -> {Value(G1Reraiser, third)}, G2 {Value(G2Reraiser, standard)} -> {Value(G2Reraiser, third)}");

        // r4-familiar lv-10 (row closed as designed, no code change): the held Blessed Horn (XBMItem row 96,
        // Role Revive) keeps its revival value against the Worthwhile floor 12, and more when a needed familiar
        // is down. Nothing in the run sells or discards it: the shop never sells and the discard prompts are
        // refused (pinned by the actuation allow-list refusals in PolicyCases.Guard).
        var hornPlain = Value(96, plain);
        var hornNeeded = ItemValue.Of(96, new RunContext { Board = 1, HornDemand = new Dictionary<int, double> { [18] = 2 } }, Inventory.Empty, [new FamiliarState(18, 0, HpPercent: 0, 0, 3, [])]);
        Check("r4-familiar: lv-10 the held Blessed Horn keeps its revive value (18 plain, 45 with a needed familiar down, floor 12): never worthless",
            CrucibleItems.Get(96).Role == ItemRole.Revive && hornPlain == 18 && hornNeeded == 45,
            $"plain {hornPlain}, needed familiar down {hornNeeded}");
    }
}
