using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Round 4 snarl lane (task tasks-20261015-crucible-r4-fix-snarl-aggro-01). Titles start with "r4-snarl:" and name the gap id.
///     rot-14: Snarl at the pull so the familiar tanks (dossier S7 / S8), on the pieces where the familiar survives the largest hit it could take.
///     5.1-05: Challenge back after the cover Snarl on the Flauros Piece (Erratic Blaster).
///     5.3-02: Provision (49215 / 49216) swaps who tanks on the Boogyman Piece.
///     The three options are set through <see cref="Opt"/> (reflection) so this file compiles before the fields exist: the failing-first run
///     fails at runtime on the missing option, not at build time.
/// </summary>
internal static partial class Program
{
    private const string R4SnarlPullOpt = "CrucibleSnarlAtPull";
    private const string R4SnarlBackOpt = "CrucibleChallengeBack";
    private const string R4SnarlSwapOpt = "CrucibleProvisionSwap";

    private static void R4SnarlLane20261015()
    {
        Console.WriteLine("-- r4-snarl: Snarl at pull, Challenge back after a cover Snarl, Provision aggro swap --");
        var shipped = BstSettings.Defaults();
        var on = shipped with { CrucibleAggro = CrucibleAggroMode.On };
        bool Has(string field) => typeof(BstSettings).GetField(field) is not null;
        BstSettings Off(BstSettings c, string field) => Opt(c, field, false);
        BstDecision D(BstState s, BstSettings c) => Decide(s, c);
        bool Is(BstDecision d, uint action, string reason) => d.ActionId == action && d.Reason == reason;
        string Show(BstDecision d) => $"{d.ActionId}/{d.Reason} sh={d.Shadow} [{d.Declines}]";

        // A fight that has just started: the character holds the enemy, both duty actions ready, a full 3,492 HP familiar out and settled.
        BstState Fight(int board, int battle, uint nameId = 0) => CrucibleState() with
        {
            CrucibleBoard = board, CrucibleBattle = battle, TargetNameId = nameId, InCombat = true, HasHostileTarget = true, TargetDistance = 3f,
            ReadySnarl = true, ReadyChallenge = true, ReadyParting = false, EnemyTargetsPlayer = true, EnemyTargetsPet = false,
            TargetHpPercent = 100f, HighestEnemyHpPercent = 100f, PetHp = 3492f, PetHpPercent = 100f, PlayerHp = 4060f, PlayerHpPercent = 100f,
            SinceHornPress = 30f, SinceSummon = 20f, SinceSnarl = float.MaxValue, CanWeave = true,
            CrucibleDegreeKnown = true, CrucibleDegreeLevel = CrucibleDegree.Standard,
        };
        BstState At(BstState s, int degree) => s with { CrucibleDegreeKnown = true, CrucibleDegreeLevel = degree };
        BstState Unread(BstState s) => s with { CrucibleDegreeKnown = false, CrucibleDegreeLevel = 0 };
        // The familiar holds the enemy.
        BstState OnPet(BstState s) => s with { EnemyTargetsPet = true, EnemyTargetsPlayer = false };

        // ================================================================ the options
        Check("r4-snarl: rot-14 the pull Snarl is its own option (BstSettings.CrucibleSnarlAtPull), On in the shipped settings",
            Has(R4SnarlPullOpt) && (bool)(typeof(BstSettings).GetField(R4SnarlPullOpt)?.GetValue(shipped) ?? false), $"field present {Has(R4SnarlPullOpt)}");
        Check("r4-snarl: 5.1-05 the Challenge back is its own option (BstSettings.CrucibleChallengeBack), On in the shipped settings",
            Has(R4SnarlBackOpt) && (bool)(typeof(BstSettings).GetField(R4SnarlBackOpt)?.GetValue(shipped) ?? false), $"field present {Has(R4SnarlBackOpt)}");
        Check("r4-snarl: 5.3-02 the Provision swap is its own option (BstSettings.CrucibleProvisionSwap), On in the shipped settings",
            Has(R4SnarlSwapOpt) && (bool)(typeof(BstSettings).GetField(R4SnarlSwapOpt)?.GetValue(shipped) ?? false), $"field present {Has(R4SnarlSwapOpt)}");
        var cfgSrc = RepoFile("Combos", "PvE", "BST", "BST_Config.cs");
        var botSrc = RepoFile("Combos", "PvE", "BST", "BST.cs");
        foreach (var (key, field, gap) in new[] { ("BST_CrucibleSnarlAtPull", R4SnarlPullOpt, "rot-14"), ("BST_CrucibleChallengeBack", R4SnarlBackOpt, "5.1-05"), ("BST_CrucibleProvisionSwap", R4SnarlSwapOpt, "5.3-02") })
        {
            var literal = System.Text.RegularExpressions.Regex.Match(cfgSrc, key + @"\s*=\s*new\(\s*""" + key + @"""\s*,\s*true\s*\)");
            Check($"r4-snarl: {gap} {key} is a config option that defaults On in BST_Config.cs and is wired into the settings (BST.cs)",
                literal.Success && botSrc.Contains($"{field} = {key}", StringComparison.Ordinal), $"literal {literal.Success}, wiring {botSrc.Contains($"{field} = {key}", StringComparison.Ordinal)}");
            Check($"r4-snarl: {gap} {key} has a settings-window checkbox and a description string",
                cfgSrc.Contains($"DrawAdditionalBoolChoice({key}", StringComparison.Ordinal));
        }

        // ================================================================ rot-14 Snarl at pull
        // Third Board, battle 1 (Cavalier Piece): 38 Standard-period encounters, no hit on a familiar above 396, none on the character above 615.
        var pull = Fight(3, 1);
        var d = D(pull, on);
        Check("r4-snarl: rot-14 pull: fresh fight, the enemy on the character at full HP, a full familiar: Snarl (aggro:snarl-pull)",
            Is(d, BST.Snarl, "aggro:snarl-pull"), Show(d));
        Check("r4-snarl: rot-14 pull: the option unticked: no Snarl (reversible from the settings window)",
            !Is(D(pull, Off(on, R4SnarlPullOpt)), BST.Snarl, "aggro:snarl-pull") && D(pull, Off(on, R4SnarlPullOpt)).ActionId != BST.Snarl, Show(D(pull, Off(on, R4SnarlPullOpt))));
        var shadow = D(pull, shipped with { CrucibleAggro = CrucibleAggroMode.Shadow });
        Check("r4-snarl: rot-14 pull: shadow mode: the Snarl is logged (sh=aggro:snarl-pull), not pressed",
            shadow.ActionId != BST.Snarl && shadow.Shadow == "aggro:snarl-pull", Show(shadow));
        Check("r4-snarl: rot-14 pull: no weave window: nothing is held for it, an attack goes out (the Snarl never costs a GCD or holds the fight)",
            D(pull with { CanWeave = false, GcdReady = true }, on) is { } nw && nw.ActionId != BST.Snarl && nw.ActionId != 0 && !nw.Reason.StartsWith("crucible:hold", StringComparison.Ordinal), Show(D(pull with { CanWeave = false, GcdReady = true }, on)));
        Check("r4-snarl: rot-14 pull: familiar at 85%: no Snarl (the pull is taken by a fresh familiar only)",
            D(pull with { PetHp = 2968f, PetHpPercent = 85f }, on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: the enemy already down to 70% (not the pull): no Snarl",
            D(pull with { TargetHpPercent = 70f }, on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: Snarl on its recast: no Snarl", D(pull with { ReadySnarl = false }, on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: the familiar already holds the enemy: no Snarl", D(OnPet(pull), on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: the familiar was only just blown in (settling): no Snarl",
            D(pull with { SinceHornPress = 1.0f }, on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: a cleave-auto boss (Siren, 14583): no Snarl (the cone autos hit the familiar and the Challenge rule would take it straight back)",
            D(pull with { TargetNameId = 14583 }, on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: a registered tankbuster cast is up (Thunderbolt 48620, no recorded hit on a familiar): no pull Snarl, the tankbuster rules own it",
            D(pull with { TargetCastId = 48620, TargetCastRemaining = 4f }, on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: Directional Parry up: the parry rule decides (aggro:snarl-parry), not the pull",
            D(pull with { TargetHasParry = true }, on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: a piece the logs never measured (board 3 battle 3, Zu: 3 encounters): no Snarl",
            D(Fight(3, 3), on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: Final Sting is due (a wespe about to leave): no Snarl",
            D(pull with { ActiveSlot = 1, Slot1Beast = 10, PetObjectBeast = 10, TargetHpPercent = 100f, TargetVulnerabilityRemaining = 5f }, on).Reason != "aggro:snarl-pull");

        // The pet-safety gate per piece: the familiar must hold twice (x1.25 where the degree is read) the largest hit the logs recorded on whoever held the enemy.
        // Gargoyle Piece (4,5): 2,616 recorded on a familiar at Standard. Strix Piece (4,1): the character took 1,290 from Darkness, a familiar 1.8x that.
        // Durga Piece (5,6): Atomic Ray took 6,386 from the character.
        var garg = Fight(4, 5);
        Check("r4-snarl: rot-14 pull: Gargoyle, Standard read, a 3,492 HP familiar (2,616 x 1.25 = 3,270): Snarl",
            Is(D(garg, on), BST.Snarl, "aggro:snarl-pull"), Show(D(garg, on)));
        Check("r4-snarl: rot-14 pull: Gargoyle, Standard read, the familiar at 3,200 HP (under 3,270): no Snarl",
            D(garg with { PetHp = 3200f, PetHpPercent = 92f }, on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: Gargoyle, First Degree (hits 2x: 5,232 x 1.25): no Snarl at 3,492 HP",
            D(At(garg, CrucibleDegree.First), on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: Gargoyle, degree unread (2,616 x 2.0 margin = 5,232): no Snarl at 3,492 HP",
            D(Unread(garg), on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: Strix (4,1), Standard read, a 3,492 HP familiar (2,330 x 1.25 = 2,913): Snarl",
            Is(D(Fight(4, 1), on), BST.Snarl, "aggro:snarl-pull"), Show(D(Fight(4, 1), on)));
        Check("r4-snarl: rot-14 pull: Strix (4,1), First Degree: no Snarl (the Darkness that hit only the character at Standard would land on the familiar: 2,045 recorded at First)",
            D(At(Fight(4, 1), CrucibleDegree.First), on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: Durga (5,6), Standard read, a 3,492 HP familiar: no Snarl (Atomic Ray took 6,386 from the character)",
            D(Fight(5, 6), on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: Third Board battle 1 at First Degree (1,110 x 2 x 1.25 = 2,775): Snarl at a full 3,492 HP familiar; at 2,700 HP the familiar is still under 90%: no",
            Is(D(At(pull, CrucibleDegree.First), on), BST.Snarl, "aggro:snarl-pull") && D(At(pull with { PetHp = 2700f, PetHpPercent = 77f }, CrucibleDegree.First), on).Reason != "aggro:snarl-pull");
        Check("r4-snarl: rot-14 pull: no Snarl on a Flauros Piece (5,1) even with a familiar that could take the table's hit: the cover Snarl and the Challenge back own that fight",
            D(Fight(5, 1) with { PetHp = 20000f }, on).Reason != "aggro:snarl-pull");

        // The gate is the table: in every piece that has a ceiling, the Snarl fires exactly when the familiar's HP is at least ceiling x 1.25 (Standard read).
        var ceilings = typeof(BST_CrucibleData).GetField("FamiliarHitCeilings", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(null) as System.Collections.IDictionary;
        Check("r4-snarl: rot-14 pull: BST_CrucibleData.FamiliarHitCeilings exists and holds the measured pieces (at least 20)", ceilings is { Count: >= 20 }, $"{ceilings?.Count}");
        var wrongGate = new List<string>();
        if (ceilings is not null)
            foreach (System.Collections.DictionaryEntry e in ceilings)
            {
                var (b, bt) = ((int, int))e.Key;
                var ceiling = Convert.ToSingle(e.Value);
                var need = ceiling * 1.25f;
                if (b == 5 && bt == 1)
                    continue;
                foreach (var (hp, label) in new[] { (need + 1f, "just above"), (need - 1f, "just below") })
                {
                    var st = Fight(b, bt) with { PetHp = hp, PetHpPercent = 100f };
                    var fires = D(st, on).Reason == "aggro:snarl-pull";
                    if (fires != (label == "just above"))
                        wrongGate.Add($"({b},{bt}) ceiling {ceiling} hp {hp:0} {label}: fires={fires}");
                }
            }
        Check("r4-snarl: rot-14 pull: for every piece in the table the Snarl fires with a familiar just above ceiling x 1.25 HP and never just below it (the familiar survives the largest recorded hit on every piece where it fires)",
            ceilings is not null && wrongGate.Count == 0, string.Join(" | ", wrongGate.Take(6)));

        // The cost of the pull is the familiar's HP: it is Challenged back once it has used its share (the pet's HP is the run's, no regeneration).
        var tanking = OnPet(pull) with { PetHp = 1920f, PetHpPercent = 55f, SinceSnarl = 20f, ReadySnarl = false };
        Check("r4-snarl: rot-14 pull: the familiar tanking is at 55% and the character is at 100%: Challenge back (aggro:challenge-pull-budget)",
            Is(D(tanking, on), BST.Challenge, "aggro:challenge-pull-budget"), Show(D(tanking, on)));
        Check("r4-snarl: rot-14 pull: ... the familiar at 70%: it keeps tanking", D(tanking with { PetHp = 2444f, PetHpPercent = 70f }, on).ActionId != BST.Challenge);
        Check("r4-snarl: rot-14 pull: ... the character at 50%: it keeps tanking", D(tanking with { PlayerHp = 2030f, PlayerHpPercent = 50f }, on).Reason != "aggro:challenge-pull-budget");
        Check("r4-snarl: rot-14 pull: ... the option unticked: no budget Challenge (the old rules only)", D(tanking, Off(on, R4SnarlPullOpt)).Reason != "aggro:challenge-pull-budget");
        Check("r4-snarl: rot-14 pull: ... a piece with no ceiling in the table: no budget Challenge (unchanged there)", D(OnPet(Fight(3, 3)) with { PetHp = 1920f, PetHpPercent = 55f, SinceSnarl = 20f, ReadySnarl = false }, on).Reason != "aggro:challenge-pull-budget");
        Check("r4-snarl: rot-14 pull: ... Gargoyle at First Degree (the pull does not cover it): a tanking familiar at 55% is not Challenged by the budget rule (the old rules stand)",
            D(OnPet(At(Fight(4, 5), CrucibleDegree.First)) with { PetHp = 1920f, PetHpPercent = 55f, SinceSnarl = 20f, ReadySnarl = false }, on).Reason != "aggro:challenge-pull-budget");
        Check("r4-snarl: rot-14 pull: ... a registered tankbuster cast is up: no budget Challenge (a covering familiar stays through the cast)",
            D(tanking with { TargetCastId = 48620, TargetCastRemaining = 3f }, on).Reason != "aggro:challenge-pull-budget");
        // End to end through Decide, tick by tick: pull -> Snarl, familiar holding -> nothing, familiar spent -> Challenge, no second Snarl at once.
        var t0 = D(pull, on);
        var t1 = D(OnPet(pull) with { ReadySnarl = false, SinceSnarl = 0.6f, TargetHpPercent = 96f }, on);
        var t2 = D(OnPet(pull) with { ReadySnarl = false, SinceSnarl = 25f, PetHp = 1920f, PetHpPercent = 55f, TargetHpPercent = 60f }, on);
        var t3 = D(pull with { ReadyChallenge = false, SinceSnarl = 25f, TargetHpPercent = 60f, PetHp = 1920f, PetHpPercent = 55f }, on);
        Check("r4-snarl: rot-14 pull: a whole opening, tick by tick: Snarl, then neither, then the Challenge back, then no second Snarl",
            Is(t0, BST.Snarl, "aggro:snarl-pull") && t1.ActionId != BST.Snarl && t1.ActionId != BST.Challenge
            && Is(t2, BST.Challenge, "aggro:challenge-pull-budget") && t3.ActionId != BST.Snarl,
            $"{Show(t0)} || {Show(t1)} || {Show(t2)} || {Show(t3)}");
        Check("r4-snarl: rot-14 pull: score mode: no pull Snarl (score mode keeps the Snarl for the tankbuster dodge)",
            D(pull, on with { CrucibleScoreMode = true }).Reason != "aggro:snarl-pull");

        // ================================================================ 5.1-05 Erratic Blaster: Challenge back after the cover Snarl
        const uint blaster = 49188;
        var flauros = Fight(5, 1) with { PetHp = 3492f, PetHpPercent = 100f };
        var cast = flauros with { TargetCastId = blaster, TargetCastRemaining = 6.7f };
        Check("r4-snarl: 5.1-05 the cast starts, a full familiar: the cover Snarl goes in (aggro:snarl-tankbuster, unchanged)",
            Is(D(cast, on), BST.Snarl, "aggro:snarl-tankbuster"), Show(D(cast, on)));
        var after = OnPet(flauros) with { SinceSnarl = 9f, ReadySnarl = false, PetHp = 1750f, PetHpPercent = 50f, PlayerHp = 3500f, PlayerHpPercent = 86f };
        Check("r4-snarl: 5.1-05 the hit has landed (9 s after the cover Snarl, 8.0 s needed), no cast up, the character at 86%: Challenge back (aggro:challenge-back)",
            Is(D(after, on), BST.Challenge, "aggro:challenge-back"), Show(D(after, on)));
        Check("r4-snarl: 5.1-05 ... 5 s after the Snarl (the hit lands at 7.0 s): no Challenge yet (it would hand the hit back to the character)",
            D(after with { SinceSnarl = 5f }, on).ActionId != BST.Challenge);
        Check("r4-snarl: 5.1-05 ... 7.5 s after the Snarl (1.0 s of margin after the hit not yet taken): no Challenge yet", D(after with { SinceSnarl = 7.5f }, on).ActionId != BST.Challenge);
        Check("r4-snarl: 5.1-05 ... the next Erratic Blaster is already casting: the familiar keeps the cover",
            D(after with { TargetCastId = blaster, TargetCastRemaining = 5.5f }, on).ActionId != BST.Challenge);
        Check("r4-snarl: 5.1-05 ... the character at 50%: no Challenge (the low-character rules own that)", D(after with { PlayerHp = 2030f, PlayerHpPercent = 50f }, on).Reason != "aggro:challenge-back");
        Check("r4-snarl: 5.1-05 ... the option unticked: no Challenge back (the familiar keeps the enemy, as before)", D(after, Off(on, R4SnarlBackOpt)).Reason != "aggro:challenge-back");
        Check("r4-snarl: 5.1-05 ... Challenge on its recast: no Challenge", D(after with { ReadyChallenge = false }, on).ActionId != BST.Challenge);
        Check("r4-snarl: 5.1-05 ... the same state on the Boogyman Piece (5,3), no Provision cast: no Challenge back (the Challenge back is a Flauros rule)",
            D(after with { CrucibleBattle = 3 }, on).Reason != "aggro:challenge-back");
        Check("r4-snarl: 5.1-05 ... shadow mode: logged (sh=aggro:challenge-back), not pressed",
            D(after, on with { CrucibleAggro = CrucibleAggroMode.Shadow }) is { } sh && sh.ActionId != BST.Challenge && sh.Shadow == "aggro:challenge-back", Show(D(after, on with { CrucibleAggro = CrucibleAggroMode.Shadow })));
        // The cycle: cover Snarl in the cast, Challenge back, the next cast covered again (Snarl recast 15 s is long clear by then: 8 s + the cast gap).
        var next = flauros with { TargetCastId = blaster, TargetCastRemaining = 6.7f, SinceSnarl = 22f, ReadySnarl = true, ReadyChallenge = true, PetHp = 1750f + 0f, PetHpPercent = 50f };
        Check("r4-snarl: 5.1-05 the next cast, the familiar at 1,750 HP (under 1,752 x 1.25): no cover Snarl, the character takes it (unchanged)", D(next, on).Reason != "aggro:snarl-tankbuster");
        Check("r4-snarl: 5.1-05 the Challenge back makes no hold: the decision is an ability on a weave",
            D(after, on) is { } cb && cb.ActionId == BST.Challenge && !cb.Reason.StartsWith("crucible:hold", StringComparison.Ordinal));

        // ================================================================ 5.3-02 Provision swaps who tanks
        const uint provisionA = 49215, provisionB = 49216;
        var boog = Fight(5, 3);
        Check("r4-snarl: 5.3-02 Provision 49215 and 49216 are named in BST_CrucibleData (ProvisionCasts)",
            (typeof(BST_CrucibleData).GetField("ProvisionCasts", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(null) as IEnumerable<uint>)
                is { } pc && pc.Contains(provisionA) && pc.Contains(provisionB));
        var swapToPet = boog with { TargetCastId = provisionA, TargetCastRemaining = 2.0f };
        Check("r4-snarl: 5.3-02 Provision 49215 is cast, the character holds the enemy, a full familiar: Snarl (aggro:provision-swap)",
            Is(D(swapToPet, on), BST.Snarl, "aggro:provision-swap"), Show(D(swapToPet, on)));
        var swapToChar = OnPet(boog) with { TargetCastId = provisionB, TargetCastRemaining = 2.0f, PetHp = 2800f, PetHpPercent = 80f, PlayerHp = 3800f, PlayerHpPercent = 94f };
        Check("r4-snarl: 5.3-02 Provision 49216 is cast, the familiar holds the enemy, the character at 94%: Challenge (aggro:provision-swap)",
            Is(D(swapToChar, on), BST.Challenge, "aggro:provision-swap"), Show(D(swapToChar, on)));
        Check("r4-snarl: 5.3-02 ... the option unticked: neither goes out",
            D(swapToPet, Off(on, R4SnarlSwapOpt)).Reason != "aggro:provision-swap" && D(swapToChar, Off(on, R4SnarlSwapOpt)).Reason != "aggro:provision-swap");
        Check("r4-snarl: 5.3-02 ... the familiar at 40%: it is not given the enemy", D(swapToPet with { PetHp = 1400f, PetHpPercent = 40f }, on).Reason != "aggro:provision-swap");
        Check("r4-snarl: 5.3-02 ... the familiar at a full 2,000 HP (under 1,660 x 1.25 = 2,075): not given the enemy", D(swapToPet with { PetHp = 2000f, PetHpPercent = 100f }, on).Reason != "aggro:provision-swap");
        Check("r4-snarl: 5.3-02 ... the familiar at 2,100 HP (over 2,075): given the enemy", Is(D(swapToPet with { PetHp = 2100f, PetHpPercent = 100f }, on), BST.Snarl, "aggro:provision-swap"));
        Check("r4-snarl: 5.3-02 ... the character at 50% when the familiar holds: no Challenge, the familiar keeps it",
            D(swapToChar with { PlayerHp = 2030f, PlayerHpPercent = 50f }, on).Reason != "aggro:provision-swap");
        // No bounce inside the one cast: the swap just pressed has put the other action on its recast.
        var justSnarled = OnPet(boog) with { TargetCastId = provisionA, TargetCastRemaining = 1.2f, ReadySnarl = false, SinceSnarl = 0.8f };
        Check("r4-snarl: 5.3-02 ... the Snarl has just gone in and the cast is still up: no Challenge back inside the same cast", D(justSnarled, on).ActionId != BST.Challenge);
        var justChallenged = boog with { TargetCastId = provisionB, TargetCastRemaining = 1.2f, ReadyChallenge = false };
        Check("r4-snarl: 5.3-02 ... the Challenge has just gone in and the cast is still up: no Snarl back inside the same cast", D(justChallenged, on).ActionId != BST.Snarl);
        Check("r4-snarl: 5.3-02 ... the swap needs both actions ready (recast 15 s each): Snarl on its recast: nothing", D(swapToPet with { ReadySnarl = false }, on).Reason != "aggro:provision-swap");
        Check("r4-snarl: 5.3-02 ... no Provision cast, no swap (a Veil of Darkness cast 49209 is not one)", D(boog with { TargetCastId = 49209, TargetCastRemaining = 2f }, on).Reason != "aggro:provision-swap");
        Check("r4-snarl: 5.3-02 ... shadow mode: logged (sh=aggro:provision-swap), not pressed",
            D(swapToPet, on with { CrucibleAggro = CrucibleAggroMode.Shadow }) is { } sw && sw.ActionId != BST.Snarl && sw.Shadow == "aggro:provision-swap", Show(D(swapToPet, on with { CrucibleAggro = CrucibleAggroMode.Shadow })));
        // A whole fight's weapon swaps: the tank alternates at every cast, with the 15 s recast between.
        var seq = new List<string>();
        var st3 = swapToPet;
        foreach (var cast3 in new[] { provisionA, provisionB, provisionA, provisionB })
        {
            var tick = st3 with { TargetCastId = cast3, TargetCastRemaining = 2f, ReadySnarl = true, ReadyChallenge = true };
            var dd = D(tick, on);
            seq.Add(dd.ActionId == BST.Snarl ? "Snarl" : dd.ActionId == BST.Challenge ? "Challenge" : "-");
            st3 = dd.ActionId == BST.Snarl ? OnPet(tick) with { PetHp = 2800f, PetHpPercent = 80f }
                : dd.ActionId == BST.Challenge ? tick with { EnemyTargetsPet = false, EnemyTargetsPlayer = true } : tick;
        }
        Check("r4-snarl: 5.3-02 four Provision casts in a row alternate the tank: Snarl, Challenge, Snarl, Challenge", string.Join(",", seq) == "Snarl,Challenge,Snarl,Challenge", string.Join(",", seq));
    }
}
