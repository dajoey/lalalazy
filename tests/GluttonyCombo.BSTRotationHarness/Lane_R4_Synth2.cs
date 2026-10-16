using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Round 4 synthetic lane: the First Master's Board mechanics Joey's recorded runs never showed (task
///     tasks-20261015-crucible-r4-synthetic-m1-01). Titles start with "r4-synth2:" and carry the dossier gap
///     id verbatim. Rows: 4.0-10 Salivous Snap cover, 4.1-05 Ultimate Focus dispel / Darkness heal-through,
///     4.2-01 Morbol pack element, 4.2-02 Extremely Bad Breath cleanse, 4.2-05 Tremblor skins, 4.4-01 Ice Dragon
///     element, 4.4-10 Blizzard, Confound 48740, Bug walk + Object 199, Noxious Nova 48738, Grenade damage
///     stacking + boss, Pyrobolus: Toxic Fumes 48796, Snoll: Ice Spikes 48793 / Hypothermal Combustion 48794.
///     Every case runs the real <see cref="BST_RotationLogic.Decide"/> (or the row's own rule) and asserts the
///     cross-checked dossier reaction, plus its negative twin (the same state without the trigger does not
///     react). The movement halves of 4.2-02 (rotate with the cone) and 4.9 Pyrobolus (dodge the cone) are
///     BossMod Reborn / AutoDuty work and stay EXTERNAL.
/// </summary>
internal static partial class Program
{
    private static void R4Synth2Lane20261015()
    {
        Console.WriteLine("-- r4-synth2: First Master's Board mechanics the logs never showed --");
        var cfg = BstSettings.Defaults();
        var on = cfg with { CrucibleAggro = CrucibleAggroMode.On };
        List<int> Allowed(params BST_CrucibleLogic.TargetCandidate[] c) => BST_CrucibleLogic.AllowedTargets(c);
        string D(BstDecision d) => $"{d.ActionId}/{d.Reason} [{d.Declines}]";
        List<string> Why(int board, int battle, int row, CrucibleNeeds covered)
        {
            var why = new List<string>();
            BST_CrucibleAdvisor.Score(board, battle, row, covered, why);
            return why;
        }

        // ---- 4.0-10 Salivous Snap 48822 (Borgny, battle 0): "Keep a healthy familiar on enmity through it (Snarl),
        // or mitigate and heal through". The cast IS registered and measured (875 on the character, 1,077 on a
        // familiar); the cover arms when the familiar can take it: hit 1,077 x 1 x 2.0 unread-degree margin = 2,154.
        var salivous = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 0, TargetCastId = 48822, TargetCastRemaining = 6.7f,
            ReadySnarl = true, PetHp = 3492f, PetHpPercent = 100f, PlayerHp = 5240f, PlayerHpPercent = 93f,
        };
        var salivousWeak = salivous with { PetHp = 2100f, PetHpPercent = 70f };
        var salivousNoCast = salivous with { TargetCastId = 0, TargetCastRemaining = 0f };
        Check("r4-synth2: 4.0-10 Salivous Snap, a healthy (3,492 HP) familiar on enmity: Snarl covers the cast",
            Decide(salivous, on).Reason == "aggro:snarl-tankbuster", D(Decide(salivous, on)));
        Check("r4-synth2: 4.0-10 Salivous Snap, a 2,100 HP familiar (under 2,154): no cover Snarl is pressed",
            Decide(salivousWeak, on).Reason != "aggro:snarl-tankbuster", D(Decide(salivousWeak, on)));
        Check("r4-synth2: 4.0-10 Salivous Snap, no cast up: no cover Snarl (negative twin)",
            Decide(salivousNoCast, on).Reason != "aggro:snarl-tankbuster", D(Decide(salivousNoCast, on)));
        Check("r4-synth2: 4.0-10 Salivous Snap, data: 48822 is a registered tankbuster with a measured familiar hit (1,077)",
            BST_CrucibleData.Tankbusters.Contains(48822u)
            && BST_CrucibleData.HeavyCast(48822) is { Kind: CrucibleHitKind.Tankbuster, MaxOnFamiliar: 1077 },
            $"{BST_CrucibleData.Tankbusters.Contains(48822u)} / {BST_CrucibleData.HeavyCast(48822)?.MaxOnFamiliar.ToString() ?? "no row"}");

        // ---- 4.1-05 Ultimate Focus into On the Properties of Darkness (Strix Piece, battle 1): "Dispel with Wavekin
        // Quelling Wave or vulture; else heal through". Darkness 48669 is in Tankbusters but has no measured familiar
        // hit (MaxOnFamiliar 0): never Snarl-covered, so the heal-through is the potion at the tankbuster HP line.
        var wave = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 1, TargetHasDispellableBuff = true,
            KinshipHeld = true, BeastModeResolved = BST.QuellingWave, ReadyBeastMode = true, GcdReady = true,
        };
        var vulture = wave with
        {
            BeastModeResolved = 0, ReadyBeastMode = false, GcdReady = false,
            OneWithNature = true, ReadyTempered = true, Slot1Beast = 11, PetObjectBeast = 11, SinceSummon = 20f,
        };
        var waveNoBuff = wave with { TargetHasDispellableBuff = false };
        var waveUnarmed = wave with { KinshipHeld = false, BeastModeResolved = 0, ReadyBeastMode = false };
        Check("r4-synth2: 4.1-05 Ultimate Focus up, Quelling Wave armed: dispel it",
            Decide(wave, on).Reason == "crucible:dispel-quellingwave", D(Decide(wave, on)));
        Check("r4-synth2: 4.1-05 Ultimate Focus up, the vulture out with its One with Nature: Bloodcurdling Caw dispels it",
            Decide(vulture, on).Reason == "crucible:dispel-caw", D(Decide(vulture, on)));
        Check("r4-synth2: 4.1-05 no dispellable buff on the Strix: no dispel press (negative twin)",
            Decide(waveNoBuff, on).Reason != "crucible:dispel-quellingwave"
            && Decide(waveNoBuff, on).ActionId != BST.QuellingWave, D(Decide(waveNoBuff, on)));
        Check("r4-synth2: 4.1-05 buff up but no dispel armed: the decision declines crucible:dispel-unavailable",
            Decide(waveUnarmed, on).Declines.Contains("crucible:dispel-unavailable"), D(Decide(waveUnarmed, on)));
        var darkness = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 1, TargetCastId = 48669, TargetCastRemaining = 7.7f,
            PlayerHp = 1700f, PlayerHpPercent = 30f, ReadyHealPotion = 46961,
        };
        var darknessCover = darkness with { ReadySnarl = true, PetHp = 3492f, PetHpPercent = 100f };
        Check("r4-synth2: 4.1-05 Darkness cast with no cover armed and a 30% character: heal through it (raidwide guard)",
            Decide(darkness, on).Reason == "crucible:raidwide-guard", D(Decide(darkness, on)));
        Check("r4-synth2: 4.1-05 Darkness cast with a healthy familiar and Snarl ready: never a tankbuster cover (no measured familiar hit)",
            Decide(darknessCover, on).Reason != "aggro:snarl-tankbuster", D(Decide(darknessCover, on)));
        Check("r4-synth2: 4.1-05 data: 14596 needs Dispel, 5020 is panel-dispellable, Darkness has MaxOnFamiliar 0 and is registered",
            BST_CrucibleData.Enemy(14596) is { } strix && (strix.Needs & CrucibleNeeds.Dispel) != 0
            && BST_CrucibleData.PanelDispellableBuffs.Contains(5020u)
            && BST_CrucibleData.Tankbusters.Contains(48669u)
            && BST_CrucibleData.HeavyCast(48669) is { Kind: CrucibleHitKind.Tankbuster, MaxOnFamiliar: 0 },
            $"{BST_CrucibleData.Enemy(14596)?.Needs.ToString() ?? "?"} / 5020 {BST_CrucibleData.PanelDispellableBuffs.Contains(5020u)} / {BST_CrucibleData.HeavyCast(48669)?.MaxOnFamiliar.ToString() ?? "no row"}");
        Check("r4-synth2: 4.1-05 data: need-first orders interrupt, then dispel, then cleanse (NeedFirst.Kinds)",
            BST_CrucibleNeedFirst.Kinds.SequenceEqual([CrucibleNeeds.Interrupt, CrucibleNeeds.Dispel, CrucibleNeeds.Cleanse]),
            string.Join(",", BST_CrucibleNeedFirst.Kinds));

        // ---- 4.2-01 Morbol pack familiar element: "Slashing autos for the pack; Ice only if fighting the Carrion
        // Broth". The pack (14599 Morbol, 14600 seedling, 14601 ochu) is slashing-weak x3; only 14602 (Broth) is
        // ice-weak. The picker scores slashing 3 weakness hits vs ice 1 on board 4 battle 2.
        Check("r4-synth2: 4.2-01 Morbol pack element: the slashing familiar (row 16) outranks the ice one (row 21)",
            BST_CrucibleAdvisor.Score(4, 2, 16, CrucibleNeeds.Cleanse) > BST_CrucibleAdvisor.Score(4, 2, 21, CrucibleNeeds.Cleanse),
            $"{BST_CrucibleAdvisor.Score(4, 2, 16, CrucibleNeeds.Cleanse)} vs {BST_CrucibleAdvisor.Score(4, 2, 21, CrucibleNeeds.Cleanse)}");
        Check("r4-synth2: 4.2-01 Morbol pack element: the slashing pick's why list reads slashing x3 (the whole pack)",
            Why(4, 2, 16, CrucibleNeeds.Cleanse).Contains("slashing x3") && !Why(4, 2, 16, CrucibleNeeds.Cleanse).Contains("ice"),
            string.Join(", ", Why(4, 2, 16, CrucibleNeeds.Cleanse)));
        Check("r4-synth2: 4.2-01 Morbol pack element: the ice pick scores one weakness hit only (the Carrion Broth), never slashing",
            Why(4, 2, 21, CrucibleNeeds.Cleanse).Contains("ice") && !Why(4, 2, 21, CrucibleNeeds.Cleanse).Contains("slashing"),
            string.Join(", ", Why(4, 2, 21, CrucibleNeeds.Cleanse)));
        Check("r4-synth2: 4.2-01 data: 14599/14600/14601 are slashing-weak, 14602 (Broth) is the only ice-weak row",
            BST_CrucibleData.Enemy(14599) is { } morbol && morbol.Weakness == CrucibleWeakness.Slashing
            && BST_CrucibleData.Enemy(14600) is { } seedling && seedling.Weakness == CrucibleWeakness.Slashing
            && BST_CrucibleData.Enemy(14601) is { } ochu && ochu.Weakness == CrucibleWeakness.Slashing
            && BST_CrucibleData.Enemy(14602) is { } broth && broth.Weakness == CrucibleWeakness.Ice,
            $"{BST_CrucibleData.Enemy(14599)?.Weakness}/{BST_CrucibleData.Enemy(14600)?.Weakness}/{BST_CrucibleData.Enemy(14601)?.Weakness}/{BST_CrucibleData.Enemy(14602)?.Weakness}");

        // ---- 4.2-02 Extremely Bad Breath (cleanse half, Morbol battle 2): "Cleanse any debuff with bat Ultrasonics
        // or borrowed Ashkin Scouring Ash". The boss row 14599 needs Cleanse; 5382 is panel-cleansable.
        var ash = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 2, PlayerHasCleansableDebuff = true,
            KinshipHeld = true, BeastModeResolved = BST.ScouringAsh, ReadyBeastMode = true,
        };
        var ashBat = ash with
        {
            KinshipHeld = false, BeastModeResolved = 0, ReadyBeastMode = false,
            OneWithNature = true, ReadyTempered = true, Slot1Beast = 19, PetObjectBeast = 19, SinceSummon = 20f,
        };
        var ashNone = ash with { PlayerHasCleansableDebuff = false };
        Check("r4-synth2: 4.2-02 Extremely Bad Breath debuff, Scouring Ash held: cleanse it",
            Decide(ash, on).Reason == "crucible:cleanse-scouringash", D(Decide(ash, on)));
        Check("r4-synth2: 4.2-02 Extremely Bad Breath debuff, the bat out with its One with Nature: Ultrasonics cleanses it",
            Decide(ashBat, on).Reason == "crucible:cleanse-ultrasonics", D(Decide(ashBat, on)));
        Check("r4-synth2: 4.2-02 no cleansable debuff up: no cleanse press (negative twin)",
            !Decide(ashNone, on).Reason.StartsWith("crucible:cleanse"), D(Decide(ashNone, on)));
        Check("r4-synth2: 4.2-02 data: the Morbol row needs Cleanse and 5382 is a panel-cleansable debuff",
            BST_CrucibleData.Enemy(14599) is { } mb && (mb.Needs & CrucibleNeeds.Cleanse) != 0
            && BST_CrucibleData.PanelCleansableDebuffs.Contains(5382u),
            $"{BST_CrucibleData.Enemy(14599)?.Needs.ToString() ?? "?"} / 5382 {BST_CrucibleData.PanelCleansableDebuffs.Contains(5382u)}");

        // ---- 4.2-05 Tremblor 50758 (Morbol battle 2, 10 y knockback): "Mitigate with Scaleskin; cannot be taken by
        // the pet". The knockback is registered, so the shipped Vileskin press waits for the cast; Scaleskin answers
        // when the character is targeted (shipped off; the dossier names it, so the case turns it on).
        var tremblor = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 2, TargetCastId = 50758, TargetCastRemaining = 5f,
            ReadySnarl = true, PetHp = 3492f, PetHpPercent = 100f, PlayerHp = 5240f, PlayerHpPercent = 93f,
        };
        Check("r4-synth2: 4.2-05 Tremblor cast up with Snarl ready and a healthy familiar: no tankbuster cover (the pet cannot take it)",
            Decide(tremblor, on).Reason != "aggro:snarl-tankbuster", D(Decide(tremblor, on)));
        var scaleskin = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 2, TargetCastId = 50758, TargetCastRemaining = 5f,
            KinshipHeld = true, BeastModeResolved = BST.Scaleskin, ReadyBeastMode = true, PlayerTargetedByEnemy = true,
        };
        var skinOn = on with { UseScaleskin = true };
        Check("r4-synth2: 4.2-05 Tremblor, Scaleskin held and the character targeted (UseScaleskin on): press it",
            Decide(scaleskin, skinOn).Reason == "beastmode:scaleskin", D(Decide(scaleskin, skinOn)));
        Check("r4-synth2: 4.2-05 Tremblor, Scaleskin held with the shipped config (UseScaleskin false): no scaleskin press",
            Decide(scaleskin, on).ActionId != BST.Scaleskin, D(Decide(scaleskin, on)));
        var vileskin = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 2, KinshipHeld = true, BeastModeResolved = BST.Vileskin,
            ReadyBeastMode = true, KnockbackCastRemaining = 2f,
        };
        var vileskinIdle = vileskin with { KnockbackCastRemaining = 0f };
        Check("r4-synth2: 4.2-05 Tremblor in flight (2.0 s left), Vileskin held: knockback press",
            Decide(vileskin, on).Reason == "beastmode:vileskin-knockback", D(Decide(vileskin, on)));
        Check("r4-synth2: 4.2-05 no knockback cast in flight: Vileskin waits (vileskin:held-for-knockback), no press",
            Decide(vileskinIdle, on).Declines.Contains("vileskin:held-for-knockback")
            && Decide(vileskinIdle, on).ActionId != BST.Vileskin, D(Decide(vileskinIdle, on)));
        Check("r4-synth2: 4.2-05 data: 50758 is a registered knockback of battle (4,2) and is not a tankbuster",
            BST.CrucibleKnockbacks.IsKnockbackCast(50758u) && BST.CrucibleKnockbacks.BattleHasKnockback(4, 2)
            && !BST_CrucibleData.Tankbusters.Contains(50758u),
            $"{BST.CrucibleKnockbacks.IsKnockbackCast(50758u)} / {BST.CrucibleKnockbacks.BattleHasKnockback(4, 2)}");

        // ---- 4.4-01 Ice Dragon battle 4 familiar element: "Fire familiar; kill the sprite first with magic". Both
        // rows (14606 dragon, 14607 sprite) are fire-weak; the fire pick (row 2) is magical, so the star-defense
        // clause (which needs Weakness == None) cannot add anything here - the sheet weakness alone decides.
        Check("r4-synth2: 4.4-01 Ice Dragon element: the fire familiar (row 2) outranks the slashing (16) and ice (21) picks",
            BST_CrucibleAdvisor.Score(4, 4, 2, CrucibleNeeds.None) > BST_CrucibleAdvisor.Score(4, 4, 16, CrucibleNeeds.None)
            && BST_CrucibleAdvisor.Score(4, 4, 2, CrucibleNeeds.None) > BST_CrucibleAdvisor.Score(4, 4, 21, CrucibleNeeds.None),
            $"{BST_CrucibleAdvisor.Score(4, 4, 2, CrucibleNeeds.None)} vs {BST_CrucibleAdvisor.Score(4, 4, 16, CrucibleNeeds.None)}/{BST_CrucibleAdvisor.Score(4, 4, 21, CrucibleNeeds.None)}");
        Check("r4-synth2: 4.4-01 Ice Dragon element: the fire pick's why list reads fire x2; the slashing pick scores no weakness hit",
            Why(4, 4, 2, CrucibleNeeds.None).Contains("fire x2") && !Why(4, 4, 16, CrucibleNeeds.None).Contains("fire"),
            $"fire: {string.Join(", ", Why(4, 4, 2, CrucibleNeeds.None))} | slashing: {string.Join(", ", Why(4, 4, 16, CrucibleNeeds.None))}");
        Check("r4-synth2: 4.4-01 kill the sprite first: with dragon and sprite up, only the sprite is targetable",
            Allowed(new(14606, 100f, false), new(14607, 100f, false)).SequenceEqual([1]),
            string.Join(",", Allowed(new(14606, 100f, false), new(14607, 100f, false))));
        Check("r4-synth2: 4.4-01 kill the sprite first: sprite gone, the dragon is targetable (negative twin)",
            Allowed(new(14606, 100f, false)).SequenceEqual([0]),
            string.Join(",", Allowed(new(14606, 100f, false))));
        Check("r4-synth2: 4.4-01 data: both rows fire-weak, the fire pick is magical, the sprite is a panel-burst caster",
            BST_CrucibleData.Enemy(14606) is { } dragon && dragon.Weakness == CrucibleWeakness.Fire
            && BST_CrucibleData.Enemy(14607) is { } sprite && sprite.Weakness == CrucibleWeakness.Fire
            && BST_CrucibleData.BeastProfiles[2].AutoMagic && BST_CrucibleData.PanelBurstCasters.ContainsKey(14607u),
            $"{BST_CrucibleData.Enemy(14606)?.Weakness}/{BST_CrucibleData.Enemy(14607)?.Weakness} magic={BST_CrucibleData.BeastProfiles[2].AutoMagic}");

        // ---- 4.4-10 Blizzard (the ice sprite): "While Snarled the pet takes it; the sprites die first anyway". The
        // recorded hit on a familiar is 48 (PanelBurstCasters), so an armed Snarl on a healthy familiar covers it;
        // the Snarl is deliberately NOT the default for hard hits (research m6), so the un-armed twin does not react.
        var blizzard = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 4, TargetNameId = 14607,
            ReadySnarl = true, PetHp = 3492f, PetHpPercent = 100f, PlayerHp = 5240f, PlayerHpPercent = 93f,
        };
        var blizzardNoCaster = blizzard with { TargetNameId = 0 };
        var blizzardUnarmed = blizzard with { ReadySnarl = false };
        Check("r4-synth2: 4.4-10 Blizzard, the sprite targeting the character and Snarl armed: the pet takes it (aggro:snarl-panel-burst)",
            Decide(blizzard, on).Reason == "aggro:snarl-panel-burst", D(Decide(blizzard, on)));
        Check("r4-synth2: 4.4-10 no panel-burst caster is the target: no cover Snarl (negative twin)",
            Decide(blizzardNoCaster, on).Reason != "aggro:snarl-panel-burst", D(Decide(blizzardNoCaster, on)));
        Check("r4-synth2: 4.4-10 Snarl not armed: no cover is chased (the m6 divergence - covering a hard hit is not the default)",
            Decide(blizzardUnarmed, on).Reason != "aggro:snarl-panel-burst", D(Decide(blizzardUnarmed, on)));
        Check("r4-synth2: 4.4-10 data: the sprite (14607) is a Blizzard panel-burst caster with a 48 recorded familiar hit",
            BST_CrucibleData.PanelBurstCasters.TryGetValue(14607u, out var spriteBlizzard)
            && spriteBlizzard.HitId == 48621u && spriteBlizzard.MaxOnFamiliar == 48,
            $"{BST_CrucibleData.PanelBurstCasters.TryGetValue(14607u, out var s2b)} {s2b.HitId}/{s2b.MaxOnFamiliar}");

        // ---- Confound 48740 (the Administrator, battle 6): interrupt machinery live - Soul Crush presses inside
        // 3.5 y while the cast is interruptible; each blocked shape declines with its own reason.
        var confound = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 6, TargetInterruptible = true,
            KinshipHeld = true, BeastModeResolved = BST.SoulCrush, ReadyBeastMode = true, TargetDistance = 3f,
        };
        var confoundFar = confound with { TargetDistance = 6f };
        var confoundNotReady = confound with { ReadyBeastMode = false };
        Check("r4-synth2: Confound 48740 interruptible, Soul Crush held and ready, in reach: press it",
            Decide(confound, on).Reason == "beastmode:soulcrush-interrupt", D(Decide(confound, on)));
        Check("r4-synth2: Confound 48740 not interruptible: no interrupt press (negative twin)",
            Decide(confound with { TargetInterruptible = false }, on).Reason != "beastmode:soulcrush-interrupt"
            && Decide(confound with { TargetInterruptible = false }, on).ActionId != BST.SoulCrush,
            D(Decide(confound with { TargetInterruptible = false }, on)));
        Check("r4-synth2: Confound 48740 interruptible but out of reach (6 y): declines crucible:interrupt-out-of-range",
            Decide(confoundFar, on).Declines.Contains("crucible:interrupt-out-of-range")
            && Decide(confoundFar, on).ActionId != BST.SoulCrush, D(Decide(confoundFar, on)));
        Check("r4-synth2: Confound 48740 interruptible but Soul Crush not ready: declines crucible:interrupt-soulcrush-not-ready",
            Decide(confoundNotReady, on).Declines.Contains("crucible:interrupt-soulcrush-not-ready")
            && Decide(confoundNotReady, on).ActionId != BST.SoulCrush, D(Decide(confoundNotReady, on)));
        Check("r4-synth2: Confound data: 48740 is a panel-interruptible action and the Administrator row needs Interrupt",
            BST_CrucibleData.PanelInterruptibleActions.Contains(48740u)
            && BST_CrucibleData.Enemy(14610) is { } admin && (admin.Needs & CrucibleNeeds.Interrupt) != 0,
            $"48740 {BST_CrucibleData.PanelInterruptibleActions.Contains(48740u)} / {BST_CrucibleData.Enemy(14610)?.Needs.ToString() ?? "?"}");

        // ---- Bug walk + Object 199 (the Administrator's fight): kill-bugs-first is live - bug 14611 is in
        // PriorityAdds, so with the Administrator and the bug up only the bug is targetable.
        Check("r4-synth2: Bug walk + Object 199: with the Administrator and the bug up, only the bug is targetable",
            Allowed(new(14610, 100f, false), new(14611, 100f, false)).SequenceEqual([1]),
            string.Join(",", Allowed(new(14610, 100f, false), new(14611, 100f, false))));
        Check("r4-synth2: Bug walk + Object 199: bug gone, the Administrator is targetable (negative twin)",
            Allowed(new(14610, 100f, false)).SequenceEqual([0]),
            string.Join(",", Allowed(new(14610, 100f, false))));
        Check("r4-synth2: Bug walk + Object 199 data: the bug (14611) is a priority add, the Administrator (14610) is not",
            BST_CrucibleData.PriorityAdds.Contains(14611u) && !BST_CrucibleData.PriorityAdds.Contains(14610u),
            $"{BST_CrucibleData.PriorityAdds.Contains(14611u)}/{BST_CrucibleData.PriorityAdds.Contains(14610u)}");

        // ---- Noxious Nova 48738 (cobra 14616, battle 6): 8.0 s room-wide + Poison (5140, cleansable), NOT
        // interruptible: the cleanse press is the answer; no Soul Crush press exists for the cast.
        var nova = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 6, PlayerHasCleansableDebuff = true,
            KinshipHeld = true, BeastModeResolved = BST.ScouringAsh, ReadyBeastMode = true,
        };
        var novaNone = nova with { PlayerHasCleansableDebuff = false };
        var novaInterrupt = nova with { BeastModeResolved = BST.SoulCrush, TargetInterruptible = false };
        Check("r4-synth2: Noxious Nova 48738 poison on the character, Scouring Ash held: cleanse it",
            Decide(nova, on).Reason == "crucible:cleanse-scouringash", D(Decide(nova, on)));
        Check("r4-synth2: Noxious Nova 48738 no poison up: no cleanse press (negative twin)",
            !Decide(novaNone, on).Reason.StartsWith("crucible:cleanse"), D(Decide(novaNone, on)));
        Check("r4-synth2: Noxious Nova 48738 with Soul Crush held and ready: still no interrupt press (the cast is not interruptible)",
            Decide(novaInterrupt, on).ActionId != BST.SoulCrush, D(Decide(novaInterrupt, on)));
        Check("r4-synth2: Noxious Nova data: the cobra row needs Cleanse, 5140 is panel-cleansable, 48738 is not a panel-interruptible action",
            BST_CrucibleData.Enemy(14616) is { } cobra && (cobra.Needs & CrucibleNeeds.Cleanse) != 0
            && BST_CrucibleData.PanelCleansableDebuffs.Contains(5140u)
            && !BST_CrucibleData.PanelInterruptibleActions.Contains(48738u),
            $"{BST_CrucibleData.Enemy(14616)?.Needs.ToString() ?? "?"} / 5140 {BST_CrucibleData.PanelCleansableDebuffs.Contains(5140u)} / 48738 int={BST_CrucibleData.PanelInterruptibleActions.Contains(48738u)}");

        // ---- Grenade damage stacking + boss immunity (battle 9): "Burst Grenade down at once with your biggest
        // hits". The grenade (14624) is a priority add; the boss is not targetable while it lives, and its
        // Self-destruct cast (48792) keys the burst: the held Tempered Release goes first.
        Check("r4-synth2: Grenade damage stacking + boss: with the boss and the grenade up, only the grenade is targetable",
            Allowed(new(14623, 100f, false), new(14624, 100f, false)).SequenceEqual([1]),
            string.Join(",", Allowed(new(14623, 100f, false), new(14624, 100f, false))));
        var grenade = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 9, TargetCastId = 48792, TargetCastRemaining = 7.5f,
            OneWithNature = true, ReadyTempered = true,
        };
        var grenadeNoCast = grenade with { TargetCastId = 0, TargetCastRemaining = 0f };
        Check("r4-synth2: Grenade damage stacking + boss: the Self-destruct cast up with the release held: burst (Tempered Release) goes first",
            Decide(grenade, on).Reason == "crucible:burst-tempered", D(Decide(grenade, on)));
        Check("r4-synth2: Grenade damage stacking + boss: grenade gone, the boss is targetable (negative twin)",
            Allowed(new(14623, 100f, false)).SequenceEqual([0]),
            string.Join(",", Allowed(new(14623, 100f, false))));
        Check("r4-synth2: Grenade damage stacking + boss: no Self-destruct cast up, no burst press (negative twin)",
            !Decide(grenadeNoCast, on).Reason.StartsWith("crucible:burst"), D(Decide(grenadeNoCast, on)));
        Check("r4-synth2: Grenade data: 14624 is a priority add and 48792 is a Self-destruct cast",
            BST_CrucibleData.PriorityAdds.Contains(14624u) && BST_CrucibleTactics.SelfDestructCasts.Contains(48792u),
            $"{BST_CrucibleData.PriorityAdds.Contains(14624u)}/{BST_CrucibleTactics.SelfDestructCasts.Contains(48792u)}");

        // ---- Pyrobolus: Toxic Fumes 48796 (battle 9): cleanse poison (5141, cleansable); the cone-dodging half is
        // movement (BossMod/AutoDuty) and stays EXTERNAL.
        var fumes = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 9, PlayerHasCleansableDebuff = true,
            KinshipHeld = true, BeastModeResolved = BST.ScouringAsh, ReadyBeastMode = true,
        };
        var fumesNone = fumes with { PlayerHasCleansableDebuff = false };
        Check("r4-synth2: Pyrobolus: Toxic Fumes 48796 poison on the character, Scouring Ash held: cleanse it",
            Decide(fumes, on).Reason == "crucible:cleanse-scouringash", D(Decide(fumes, on)));
        Check("r4-synth2: Pyrobolus: Toxic Fumes 48796 no poison up: no cleanse press (negative twin)",
            !Decide(fumesNone, on).Reason.StartsWith("crucible:cleanse"), D(Decide(fumesNone, on)));
        Check("r4-synth2: Pyrobolus data: 14627 needs Cleanse and 5141 is a panel-cleansable debuff",
            BST_CrucibleData.Enemy(14627) is { } pyro && (pyro.Needs & CrucibleNeeds.Cleanse) != 0
            && BST_CrucibleData.PanelCleansableDebuffs.Contains(5141u),
            $"{BST_CrucibleData.Enemy(14627)?.Needs.ToString() ?? "?"} / 5141 {BST_CrucibleData.PanelCleansableDebuffs.Contains(5141u)}");

        // ---- Snoll: Ice Spikes 48793 / Hypothermal Combustion 48794 (battle 9): hold every press while the Snoll
        // holds Ice Spikes (2528); with the Quelling Wave armed, dispel the stance instead (the dispel row is panel).
        var snoll = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 9, TargetInStance = true,
        };
        var snollWave = snoll with
        {
            KinshipHeld = true, BeastModeResolved = BST.QuellingWave, ReadyBeastMode = true, GcdReady = true,
        };
        var snollNoStance = snoll with { TargetInStance = false };
        Check("r4-synth2: Snoll: Ice Spikes 48793, Hypothermal Combustion 48794: nothing is pressed while Ice Spikes stands (hold-stance)",
            Decide(snoll, on).Reason == "crucible:hold-stance", D(Decide(snoll, on)));
        Check("r4-synth2: Snoll: Ice Spikes 48793, Hypothermal Combustion 48794: Quelling Wave armed - dispel the stance instead of holding",
            Decide(snollWave, on).Reason == "crucible:dispel-quellingwave", D(Decide(snollWave, on)));
        Check("r4-synth2: Snoll: Ice Spikes 48793, Hypothermal Combustion 48794: no stance up, no hold (negative twin)",
            !Decide(snollNoStance, on).Reason.StartsWith("crucible:hold"), D(Decide(snollNoStance, on)));
        Check("r4-synth2: Snoll data: 2528 is a stance status, the (4,9) dispel row is panel, 14626 needs Dispel, (4,9) is not a quelling-stance battle",
            BST_CrucibleData.StanceStatuses.Contains(2528u)
            && BST_CrucibleTactics.DispelRows.Any(r => r.Board == 4 && r.Battle == 9 && r.StatusId == 2528 && r.Basis == "panel")
            && BST_CrucibleData.Enemy(14626) is { } snollRow && (snollRow.Needs & CrucibleNeeds.Dispel) != 0
            && !BST_CrucibleTactics.QuellingStanceBattles.Contains((4, 9)),
            $"{BST_CrucibleData.StanceStatuses.Contains(2528u)} / {BST_CrucibleData.Enemy(14626)?.Needs.ToString() ?? "?"}");
    }
}
