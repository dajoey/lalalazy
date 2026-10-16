using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Round 4 synthetic lane (task tasks-20261015-crucible-r4-synthetic-m2-bst-01). Titles start with "r4-synth3:"
///     and name the gap id. Nine UNOBSERVED gap rows — mechanics the recorded runs never showed, so only a constructed
///     case can put them on the board — run the real engine:
///     lv-04 (an empty slot with a ready healthy horn summons), sv-01a (Forward Guard: hold while the parry faces the
///     character, Snarl at pet 50%+, recall only with a spare horn), rot-26 (Seedsower shipped on, Scaleskin shipped off),
///     5.3-05 (the casting Oogle deepeye is targeted before anything else), 5.4-01 (the panel sheet the picker scores:
///     Blunt / Piercing), 5.4-02 (the Atomos summon wave dies in the guides' order), 5.6-05 (Vileskin waits for Thermobaric
///     Charge and goes in inside the 3 s lead), 5.8-01 (the chimera sheet: Water weakness and an armed Cleanse need),
///     5.8-06 (Toxic Tantrum's poison: Scouring Ash, or the bat's Ultrasonics without the kinship).
///     Every trigger also gets its negative twin: the same state without the trigger does not react.
/// </summary>
internal static partial class Program
{
    private static void R4Synth3Lane20261015()
    {
        Console.WriteLine("-- r4-synth3: the nine UNOBSERVED dossier rows the recorded runs never showed --");
        var cfg = BstSettings.Defaults();
        BST_CrucibleLogic.TargetCandidate T(uint id, float hp = 100f, uint cast = 0) =>
            new(id, hp, false, CastId: cast);
        List<int> Allowed(params BST_CrucibleLogic.TargetCandidate[] c) => BST_CrucibleLogic.AllowedTargets(c);
        string Show(IEnumerable<int> l) => string.Join(",", l);

        // ---- lv-04: never fight petless. The recorded runs sat at pet=0 for 12.4% of in-combat seconds and horn
        // readiness never appears on the CR line, so only a constructed case can show the summon decision.
        // Dossier: keep a familiar out (31% of damage).
        var petless = CrucibleState(50) with
        {
            ActiveSlot = 0, PetObjectPresent = false, ReadyHorn2 = true, ReadyHorn3 = false, Slot2PetHp = 100f,
        };
        Check("r4-synth3: lv-04 empty slot, second horn ready and healthy, standing still in combat: the rotation summons",
            Decide(petless, cfg) is { ActionId: BST.SecondBattlehorn, Reason: "summon:slot2" }, Decide(petless, cfg).Reason);
        Check("r4-synth3: lv-04 ... twin: the familiar already present: no summon press",
            !Decide(petless with { ActiveSlot = 1, PetObjectPresent = true }, cfg).Reason.StartsWith("summon:", StringComparison.Ordinal),
            Decide(petless with { ActiveSlot = 1, PetObjectPresent = true }, cfg).Reason);
        Check("r4-synth3: lv-04 ... twin: the same empty slot while moving defers the summon (crucible:summon-moving)",
            Decide(petless with { IsMoving = true }, cfg).Declines.Contains("crucible:summon-moving")
            && !Decide(petless with { IsMoving = true }, cfg).Reason.StartsWith("summon:", StringComparison.Ordinal),
            Decide(petless with { IsMoving = true }, cfg).Reason);

        // ---- sv-01a: Forward Guard / Directional Parry (1.1 Bone Knight). Dossier: Snarl or Challenge so the parry
        // faces the pet; never hit the front. The plugin holds while the parry faces the character, Snarls at pet 50%+
        // and recalls only with a spare horn.
        var parryHold = CrucibleState(50) with
        {
            CrucibleBoard = 1, CrucibleBattle = 1, TargetCastId = 46864, TargetCastRemaining = 2.5f,
            TargetHasParry = true, PetHpPercent = 45f, PetHp = 4500f,
            ReadyHorn2 = false, ReadyHorn3 = false, ReadySnarl = true,
        };
        Check("r4-synth3: sv-01a Forward Guard up and facing the character, pet 45% (under the Snarl line), no spare horn: the rotation holds (never a frontal hit)",
            Decide(parryHold, cfg) is { ActionId: BST_CrucibleLogic.Hold, Reason: "crucible:hold-parry" }, Decide(parryHold, cfg).Reason);
        Check("r4-synth3: sv-01a ... twin: pet 80% (50%+): Snarl turns the guard to the pet instead of holding",
            Decide(parryHold with { PetHpPercent = 80f, PetHp = 8000f }, cfg) is { ActionId: BST.Snarl, Reason: "aggro:snarl-parry" },
            Decide(parryHold with { PetHpPercent = 80f, PetHp = 8000f }, cfg).Reason);
        Check("r4-synth3: sv-01a ... twin: the parry faces the pet already (the enemy is on the familiar): no hold, the rotation attacks",
            Decide(parryHold with { EnemyTargetsPet = true }, cfg).ActionId != BST_CrucibleLogic.Hold,
            Decide(parryHold with { EnemyTargetsPet = true }, cfg).Reason);
        Check("r4-synth3: sv-01a ... with a spare horn held: the recall fires instead of the hold (crucible:petsave-guard)",
            Decide(parryHold with { PetHpPercent = 80f, PetHp = 8000f, ReadyHorn2 = true, ReadySnarl = false, Slot2PetHp = 100f }, cfg)
                is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-guard" },
            Decide(parryHold with { PetHpPercent = 80f, PetHp = 8000f, ReadyHorn2 = true, ReadySnarl = false, Slot2PetHp = 100f }, cfg).Reason);

        // ---- rot-26: the shipped options are the dossier's O10 row: Seedsower on, Scaleskin off.
        Check("r4-synth3: rot-26 the shipped options are the dossier's O10 row: Seedsower on, Scaleskin off",
            cfg.UseSeedsower && !cfg.UseScaleskin, $"Seedsower={cfg.UseSeedsower} Scaleskin={cfg.UseScaleskin}");
        var seedsower = CrucibleState(50) with
        {
            KinshipHeld = true, ReadyBeastMode = true, BeastModeResolved = BST.Seedsower, EnemiesWithin6y = 2, ReadyParting = false,
        };
        Check("r4-synth3: rot-26 Seedsower held, enemies around the character: the AoE press goes out",
            Decide(seedsower, cfg) is { ActionId: BST.Seedsower, Reason: "beastmode:seedsower" }, Decide(seedsower, cfg).Reason);
        Check("r4-synth3: rot-26 ... twin: nobody in reach: no Seedsower press",
            Decide(seedsower with { EnemiesWithin6y = 0 }, cfg).ActionId != BST.Seedsower,
            Decide(seedsower with { EnemiesWithin6y = 0 }, cfg).Reason);
        Check("r4-synth3: rot-26 ... twin: an egg / morpho beside the target blocks the AoE (crucible:seedsower-protected-near)",
            Decide(seedsower with { ProtectedNearTarget = true }, cfg).Declines.Contains("crucible:seedsower-protected-near"),
            Decide(seedsower with { ProtectedNearTarget = true }, cfg).Reason);
        var scaleskin = CrucibleState(50) with
        {
            KinshipHeld = true, ReadyBeastMode = true, BeastModeResolved = BST.Scaleskin, PlayerTargetedByEnemy = true, ReadyParting = false,
        };
        Check("r4-synth3: rot-26 Scaleskin held, the character targeted: the shipped default presses nothing (the option is off)",
            Decide(scaleskin, cfg).ActionId != BST.Scaleskin, Decide(scaleskin, cfg).Reason);
        Check("r4-synth3: rot-26 ... with the option on: the barrier press goes out",
            Decide(scaleskin, cfg with { UseScaleskin = true }) is { ActionId: BST.Scaleskin, Reason: "beastmode:scaleskin" },
            Decide(scaleskin, cfg with { UseScaleskin = true }).Reason);
        Check("r4-synth3: rot-26 ... twin: nobody targets the character: no barrier even with the option on",
            Decide(scaleskin with { PlayerTargetedByEnemy = false }, cfg with { UseScaleskin = true }).ActionId != BST.Scaleskin,
            Decide(scaleskin with { PlayerTargetedByEnemy = false }, cfg with { UseScaleskin = true }).Reason);

        // ---- 5.3-05: Oogle (deepeye, 49214). Kill the caster before anything else — before the wave's own priority.
        const uint boogyman = 14638, bomb = 14640, deepeye = 14639;
        Check("r4-synth3: 5.3-05 Oogle 49214 is a kill-the-caster cast and the deepeye is the boogyman wave's middle tier",
            BST_CrucibleData.KillTheCaster.Contains(49214u)
            && BST_CrucibleData.PriorityAddTier(deepeye, BST_CrucibleData.PriorityAddWave(deepeye)) == 1);
        Check("r4-synth3: 5.3-05 the deepeye casting Oogle is the target before the bomb and the boss (kill the caster)",
            Allowed(T(bomb, 90f), T(deepeye, 70f, 49214), T(boogyman)).SequenceEqual(new[] { 1 }),
            Show(Allowed(T(bomb, 90f), T(deepeye, 70f, 49214), T(boogyman))));
        Check("r4-synth3: 5.3-05 ... twin: the same deepeye not casting: the bomb (the wave's first tier) is the target",
            Allowed(T(bomb, 90f), T(deepeye, 70f), T(boogyman)).SequenceEqual(new[] { 0 }),
            Show(Allowed(T(bomb, 90f), T(deepeye, 70f), T(boogyman))));

        // ---- 5.4-01: the 5.4 panel sheet. Atomos 14642 is Blunt, the gremlin trio and the vodoriga are Piercing;
        // the picker scores both halves, and a familiar matching neither scores below both.
        var panel54 = BST_CrucibleData.Enemies.Where(e => e.Board == 5 && e.Battle == 4).ToList();
        var atomos54 = panel54.First(e => e.Sub == 0);
        Check("r4-synth3: 5.4-01 the 5.4 panel sheet: Atomos (14642) is Blunt, the gremlin trio and the vodoriga are Piercing",
            atomos54.NameId == 14642u && atomos54.Weakness == CrucibleWeakness.Blunt
            && panel54.Where(e => e.Sub != 0 && e.Weakness == CrucibleWeakness.Piercing).Select(e => e.NameId)
                .SequenceEqual(new uint[] { 14643, 14644, 14648 }),
            $"{atomos54.NameId}/{atomos54.Weakness} piercing: {string.Join(",", panel54.Where(e => e.Sub != 0 && e.Weakness == CrucibleWeakness.Piercing).Select(e => e.NameId))}");
        var bluntWhy = new List<string>();
        var bluntScore = BST_CrucibleAdvisor.Score(5, 4, 26, CrucibleNeeds.None, bluntWhy);
        var iceScore = BST_CrucibleAdvisor.Score(5, 4, 20, CrucibleNeeds.None);
        Check("r4-synth3: 5.4-01 a Blunt familiar outscores an Ice familiar on the 5.4 sheet (the picker reads the panel)",
            bluntScore > iceScore && bluntWhy.Any(w => w.Contains("blunt")), $"blunt={bluntScore} ice={iceScore} [{string.Join(",", bluntWhy)}]");
        var pierceWhy = new List<string>();
        var pierceScore = BST_CrucibleAdvisor.Score(5, 4, 10, CrucibleNeeds.None, pierceWhy);
        Check("r4-synth3: 5.4-01 ... and a Piercing familiar outscores the same Ice familiar (both halves of the sheet)",
            pierceScore > iceScore && pierceWhy.Any(w => w.Contains("piercing")), $"pierce={pierceScore} ice={iceScore} [{string.Join(",", pierceWhy)}]");

        // ---- 5.4-02: the Atomos summon wave dies in the guides' order: bavarois; pudding / flan / dahak; gremlins.
        const uint atomos = 14642, bavarois = 14646, flan = 14647, gremlin = 14644;
        var waveIdx = BST_CrucibleData.PriorityAddWave(bavarois);
        Check("r4-synth3: 5.4-02 the Atomos summons order is bavarois; pudding / flan / dahak; gremlins and vodoriga",
            waveIdx >= 0
            && BST_CrucibleData.PriorityAddTier(bavarois, waveIdx) == 0
            && BST_CrucibleData.PriorityAddTier(flan, waveIdx) == 1
            && BST_CrucibleData.PriorityAddTier(gremlin, waveIdx) == 2);
        Check("r4-synth3: 5.4-02 the summon wave up: only the bavarois (the first tier) stays targetable — kill the waves in order",
            Allowed(T(atomos), T(bavarois, 90f), T(flan, 80f), T(gremlin, 70f)).SequenceEqual(new[] { 1 }),
            Show(Allowed(T(atomos), T(bavarois, 90f), T(flan, 80f), T(gremlin, 70f))));
        Check("r4-synth3: 5.4-02 ... twin: the bavarois dead: the middle tier (flan) is the target, the gremlin waits",
            Allowed(T(atomos), T(flan, 80f), T(gremlin, 70f)).SequenceEqual(new[] { 1 }),
            Show(Allowed(T(atomos), T(flan, 80f), T(gremlin, 70f))));
        Check("r4-synth3: 5.4-02 ... twin: the wave dead, only the Atomos: the boss is the target",
            Allowed(T(atomos)).SequenceEqual(new[] { 0 }), Show(Allowed(T(atomos))));

        // ---- 5.6-05: Thermobaric Charge (49274, Vodoriga origin). Vileskin waits for the knockback and goes in
        // inside the 3 s lead — the field data never showed the wait side.
        Check("r4-synth3: 5.6-05 Thermobaric Charge 49274 is a registered knockback cast and (5,6) is a knockback battle",
            BST.CrucibleKnockbacks.IsKnockbackCast(49274) && BST.CrucibleKnockbacks.BattleHasKnockback(5, 6)
            && BST_CrucibleData.KnockbackBattles.Contains((5, 6)));
        var thermobaric = CrucibleState(50) with
        {
            CrucibleBoard = 5, CrucibleBattle = 6, KinshipHeld = true, ReadyBeastMode = true, BeastModeResolved = BST.Vileskin,
            ReadyParting = false, KnockbackCastRemaining = 2f,
        };
        Check("r4-synth3: 5.6-05 Thermobaric Charge, 2 s left (inside the 3 s lead): Vileskin covers the knockback",
            Decide(thermobaric, cfg) is { ActionId: BST.Vileskin, Reason: "beastmode:vileskin-knockback" }, Decide(thermobaric, cfg).Reason);
        Check("r4-synth3: 5.6-05 ... twin: 6 s left (outside the lead): the skin is held for the cast, nothing pressed",
            Decide(thermobaric with { KnockbackCastRemaining = 6f }, cfg).ActionId != BST.Vileskin
            && Decide(thermobaric with { KnockbackCastRemaining = 6f }, cfg).Declines.Contains("vileskin:held-for-knockback"),
            Decide(thermobaric with { KnockbackCastRemaining = 6f }, cfg).Reason);
        Check("r4-synth3: 5.6-05 ... twin: no knockback cast in flight: no Vileskin",
            Decide(thermobaric with { KnockbackCastRemaining = 0f }, cfg).ActionId != BST.Vileskin,
            Decide(thermobaric with { KnockbackCastRemaining = 0f }, cfg).Reason);

        // ---- 5.8-01: the chimera sheet (14663). Water weakness and an armed Cleanse need for the picker.
        var chimera = BST_CrucibleData.Enemies.First(e => e.Board == 5 && e.Battle == 8 && e.Sub == 0);
        Check("r4-synth3: 5.8-01 the 5.8 panel sheet: the chimera (14663) is Water-weak and its Cleanse need is armed",
            chimera.NameId == 14663u && chimera.Weakness == CrucibleWeakness.Water && (chimera.Needs & CrucibleNeeds.Cleanse) != 0,
            $"{chimera.NameId}/{chimera.Weakness}/needs={chimera.Needs}");
        var waterWhy = new List<string>();
        var waterScore = BST_CrucibleAdvisor.Score(5, 8, 15, CrucibleNeeds.None, waterWhy);
        var chimeraIceScore = BST_CrucibleAdvisor.Score(5, 8, 20, CrucibleNeeds.None);
        Check("r4-synth3: 5.8-01 a Water familiar outscores an Ice familiar on the chimera sheet",
            waterScore > chimeraIceScore && waterWhy.Any(w => w.Contains("water")),
            $"water={waterScore} ice={chimeraIceScore} [{string.Join(",", waterWhy)}]");
        var batWhy = new List<string>();
        BST_CrucibleAdvisor.Score(5, 8, BST_CrucibleData.BatRow, CrucibleNeeds.None, batWhy);
        Check("r4-synth3: 5.8-01 ... the Cleanse need is armed for the picker: the bat's Ultrasonics scores",
            batWhy.Contains("Ultrasonics"), $"[{string.Join(",", batWhy)}]");
        var batPlainWhy = new List<string>();
        BST_CrucibleAdvisor.Score(5, 9, BST_CrucibleData.BatRow, CrucibleNeeds.None, batPlainWhy);
        Check("r4-synth3: 5.8-01 ... twin: no Cleanse need on 5.9: no Ultrasonics entry",
            !batPlainWhy.Contains("Ultrasonics"), $"[{string.Join(",", batPlainWhy)}]");

        // ---- 5.8-06: Toxic Tantrum's poison. Cleanse it with Scouring Ash; with the bat out a spendable release
        // answers without the kinship — the familiar takes it and is swapped.
        var tantrum = CrucibleState(50) with
        {
            CrucibleBoard = 5, CrucibleBattle = 8, PlayerHasCleansableDebuff = true,
            KinshipHeld = true, ReadyBeastMode = true, BeastModeResolved = BST.ScouringAsh,
        };
        Check("r4-synth3: 5.8-06 Toxic Tantrum's poison on the character, Scouring Ash held: the cleanse goes out",
            Decide(tantrum, cfg) is { ActionId: BST.ScouringAsh, Reason: "crucible:cleanse-scouringash" }, Decide(tantrum, cfg).Reason);
        Check("r4-synth3: 5.8-06 ... twin: no cleansable debuff: no cleanse press",
            Decide(tantrum with { PlayerHasCleansableDebuff = false }, cfg).Reason != "crucible:cleanse-scouringash",
            Decide(tantrum with { PlayerHasCleansableDebuff = false }, cfg).Reason);
        var batCover = tantrum with
        {
            KinshipHeld = false, BeastModeResolved = 0, ReadyBeastMode = false,
            Slot1Beast = BST_CrucibleData.BatRow, PetObjectBeast = BST_CrucibleData.BatRow,
            OneWithNature = true, ReadyTempered = true, SinceSummon = 5f,
        };
        Check("r4-synth3: 5.8-06 ... the bat out with a spendable release: Ultrasonics answers without the kinship",
            Decide(batCover, cfg) is { ActionId: BST.TemperedRelease, Reason: "crucible:cleanse-ultrasonics" }, Decide(batCover, cfg).Reason);
    }
}
