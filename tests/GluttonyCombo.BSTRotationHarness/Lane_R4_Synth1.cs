using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Round 4 synthetic lane (task tasks-20261015-crucible-r4-synthetic-b1-b2-b3-01). Titles start with "r4-synth1:" and name the gap id.
///     The 14 rows are class UNOBSERVED on the round-3 gap pages (Boards 1-3): the mechanic never appeared in Joey's recorded runs, so each case
///     below builds the state from the dossier row and asserts the cross-checked dossier's reaction through the real <see cref="Decide"/> (or the
///     rule's own function), plus its negative twin. Rows: 1.1-04 (Windburn cleanse), 2.0-03 (Sand Breath Blind cleanse), 2.2-05 (Eyes on Me:
///     no reaction exists, the ahriman is killed on sight), 2.6-04 (Void Paralyze cleanse), 3.0-12b (Woken Thanatos race), 3.1-02 (Soul Douse
///     kill-the-caster), 3.2-06b (Tsunami Vileskin), 3.3-02 (Caustic Vomit interrupt / familiar cover), 3.3-07b (Flying Frenzy cover),
///     3.7-01 (flowertender first), 3.7-02 (the order is static while the Guardia is Covered), 3.7-04 (room-wide panic heal), 3.7-07 (unranked
///     wave stays targetable), 3.7-08 (tail order + lightning / ice picks). The 3.3-02 familiar-cover and 3.3-07b cover cases failed first at the
///     integration head (no measured familiar hit for 48489 / 50465): the fixes are the EstimatedTankbusterHits[48489] stand-in entry and the
///     measured 50465 HeavyCasts row, both in this lane's fix commits.
/// </summary>
internal static partial class Program
{
    private static void R4Synth1Lane20261015()
    {
        Console.WriteLine("-- r4-synth1: Boards 1-3 UNOBSERVED rows (cleanse x3, Eyes on Me, Thanatos, Soul Douse, Tsunami, Caustic Vomit, Flying Frenzy, cactuar pack x4) --");
        var cfg = BstSettings.Defaults() with { CrucibleAggro = CrucibleAggroMode.On };
        BstDecision D(BstState s) => Decide(s, cfg);
        bool Is(BstDecision d, uint action, string reason) => d.ActionId == action && d.Reason == reason;
        string Show(BstDecision d) => $"{d.ActionId}/{d.Reason} [{d.Declines}]";
        List<int> Allowed(params BST_CrucibleLogic.TargetCandidate[] c) => BST_CrucibleLogic.AllowedTargets(c);
        string Ids(IEnumerable<int> l) => string.Join(",", l);
        BST_CrucibleLogic.TargetCandidate T(uint id, float hp = 100f, uint cast = 0, bool interruptible = false, bool petrified = false) =>
            new(id, hp, false, CastInterruptible: interruptible, CastId: cast, Petrified: petrified);

        // Mid-fight shape shared by the battle states: the familiar settled at full HP (3,492), the character healthy,
        // both ready, nothing in flight — the third2 lane's Fight() shape, which keeps every aggro branch quiet.
        BstState Fight(int board, int battle, uint nameId) => CrucibleState() with
        {
            CrucibleBoard = board, CrucibleBattle = battle, TargetNameId = nameId, InCombat = true, HasHostileTarget = true,
            TargetDistance = 3f, EnemyCount = 2, ReadySnarl = true, ReadyChallenge = true, ReadyParting = false,
            EnemyTargetsPlayer = true, EnemyTargetsPet = false, TargetHpPercent = 60f, HighestEnemyHpPercent = 100f,
            PetHp = 3492f, PetHpPercent = 100f, PlayerHp = 4060f, PlayerHpPercent = 100f,
            SinceHornPress = 30f, SinceSummon = 20f, SinceSnarl = float.MaxValue, CanWeave = true, GcdReady = true,
        };
        BstState Cleansed(BstState s) => s with { CrucibleNeeds = CrucibleNeeds.Cleanse, PlayerHasCleansableDebuff = true };
        BstState AshHeld(BstState s) => s with { KinshipHeld = true, BeastModeResolved = BST.ScouringAsh, ReadyBeastMode = true };
        BstState BatOut(BstState s) => s with
        {
            Slot1Beast = BST_CrucibleData.BatRow, PetObjectBeast = BST_CrucibleData.BatRow,
            OneWithNature = true, ReadyTempered = true,
        };
        BstState CrushHeld(BstState s) => s with { KinshipHeld = true, BeastModeResolved = BST.SoulCrush, ReadyBeastMode = true };

        // ================================================================ 1.1-04 Ancient Aero / Windburn cleanse
        // The bishop's Ancient Aero (46870, 5.7 s) can put Windburn (4880) on the character; the dossier answer is the
        // cleanse (Scouring Ash, or the bat's Ultrasonics). 0 recorded Windburn adds in any 1.1 window.
        Check("r4-synth1: 1.1-04 Windburn 4880 is in the panel's cleansable debuffs and the 1.1 bishop calls for a cleanse",
            BST_CrucibleData.PanelCleansableDebuffs.Contains(4880u)
            && BST_CrucibleData.Enemies.Any(e => e.Board == 1 && e.Battle == 1 && (e.Needs & CrucibleNeeds.Cleanse) != 0),
            string.Join(",", BST_CrucibleData.Enemies.Where(e => e.Board == 1 && e.Battle == 1 && (e.Needs & CrucibleNeeds.Cleanse) != 0).Select(e => e.Name)));
        var ash11 = D(AshHeld(Cleansed(Fight(1, 1, 14532))));
        Check("r4-synth1: 1.1-04 Windburn up, Scouring Ash held: the cleanse press (crucible:cleanse-scouringash)",
            Is(ash11, BST.ScouringAsh, "crucible:cleanse-scouringash"), Show(ash11));
        var noBurn11 = D(AshHeld(Fight(1, 1, 14532) with { CrucibleNeeds = CrucibleNeeds.Cleanse, PlayerHasCleansableDebuff = false }));
        Check("r4-synth1: 1.1-04 no Windburn: no cleanse press (the ash waits)",
            !Is(noBurn11, BST.ScouringAsh, "crucible:cleanse-scouringash"), Show(noBurn11));
        var bat11 = D(Cleansed(BatOut(Fight(1, 1, 14532))));
        Check("r4-synth1: 1.1-04 Windburn up, the bat out with One with Nature: Ultrasonics (crucible:cleanse-ultrasonics)",
            Is(bat11, BST.TemperedRelease, "crucible:cleanse-ultrasonics"), Show(bat11));
        var batClean11 = D(BatOut(Fight(1, 1, 14532) with { CrucibleNeeds = CrucibleNeeds.Cleanse, PlayerHasCleansableDebuff = false }));
        Check("r4-synth1: 1.1-04 no Windburn, the bat out: its One with Nature is held for the cleanse, not spent (own:held-for-cleanse)",
            batClean11.Declines.Contains("own:held-for-cleanse") && batClean11.Reason != "crucible:cleanse-ultrasonics", Show(batClean11));

        // ================================================================ 2.0-03 Sand Breath / Blind cleanse
        // Sand Breath (48226/48227) can blind the character (68 casts, 0 applications recorded); the dossier answer is the
        // cleanse if hit. Chewchum 14562 carries the battle's Cleanse need.
        Check("r4-synth1: 2.0-03 Chewchum 14562 carries the 2.0 Cleanse need",
            BST_CrucibleData.Enemies.Any(e => e.NameId == 14562 && (e.Needs & CrucibleNeeds.Cleanse) != 0),
            BST_CrucibleData.Enemies.FirstOrDefault(e => e.NameId == 14562).Needs.ToString());
        var ash20 = D(AshHeld(Cleansed(Fight(2, 0, 14562))));
        Check("r4-synth1: 2.0-03 Blind up, Scouring Ash held: the cleanse press (crucible:cleanse-scouringash)",
            Is(ash20, BST.ScouringAsh, "crucible:cleanse-scouringash"), Show(ash20));
        var noBlind20 = D(AshHeld(Fight(2, 0, 14562) with { CrucibleNeeds = CrucibleNeeds.Cleanse, PlayerHasCleansableDebuff = false }));
        Check("r4-synth1: 2.0-03 no Blind: no cleanse press", !Is(noBlind20, BST.ScouringAsh, "crucible:cleanse-scouringash"), Show(noBlind20));
        var bat20 = D(Cleansed(BatOut(Fight(2, 0, 14562))));
        Check("r4-synth1: 2.0-03 Blind up, the bat out with One with Nature: Ultrasonics (crucible:cleanse-ultrasonics)",
            Is(bat20, BST.TemperedRelease, "crucible:cleanse-ultrasonics"), Show(bat20));
        var batClean20 = D(BatOut(Fight(2, 0, 14562) with { CrucibleNeeds = CrucibleNeeds.Cleanse, PlayerHasCleansableDebuff = false }));
        Check("r4-synth1: 2.0-03 no Blind, the bat out: the One with Nature is held for the cleanse (own:held-for-cleanse)",
            batClean20.Declines.Contains("own:held-for-cleanse"), Show(batClean20));

        // ================================================================ 2.2-05 Eyes on Me (no reaction exists)
        // The ahriman's Eyes on Me (48152/48153) is unavoidable self-destruct damage; the dossier records NO reaction — the
        // ahriman is a priority add killed on sight and nothing else changes. 0 recorded casts.
        Check("r4-synth1: 2.2-05 Eyes on Me ids are in no reaction table (not Self-destruct, not kill-the-caster, not interruptible) and the ahriman is a priority add",
            !BST_CrucibleData.SelfDestructCasts.Contains(48152u) && !BST_CrucibleData.SelfDestructCasts.Contains(48153u)
            && !BST_CrucibleData.KillTheCaster.Contains(48152u) && !BST_CrucibleData.PanelInterruptibleActions.Contains(48152u)
            && BST_CrucibleData.PriorityAdds.Contains(14548u),
            "priority=" + BST_CrucibleData.PriorityAdds.Contains(14548u));
        var eyes = D(Fight(2, 2, 14548) with { TargetCastId = 48152, TargetCastRemaining = 3f, TargetInterruptible = false, CrucibleNeeds = CrucibleNeeds.None });
        Check("r4-synth1: 2.2-05 the ahriman casting Eyes on Me: the rotation keeps attacking (no hold, no defensive press)",
            eyes.ActionId != BST_CrucibleLogic.Hold && eyes.ActionId != 0
            && eyes.Reason != "crucible:hold-invulnerable" && !eyes.Declines.Contains("selfdestruct"),
            Show(eyes));
        var eyesAim = Allowed(T(14548, 90f, 48152), T(14546, 100f));
        Check("r4-synth1: 2.2-05 the ahriman in a wave: it is the only targetable (priority add, killed on sight)",
            eyesAim.SequenceEqual(new[] { 0 }), Ids(eyesAim));
        var eyesAimIdle = Allowed(T(14548, 90f), T(14546, 100f));
        Check("r4-synth1: 2.2-05 the same wave with the cast not up: the same answer (the priority, not the cast, aims it)",
            eyesAimIdle.SequenceEqual(new[] { 0 }), Ids(eyesAimIdle));

        // ================================================================ 2.6-04 Void Paralyze cleanse
        // Void Paralyze (48247) can paralyze the character (1 recorded cast, 0 applications); the dossier answer is the
        // cleanse if applied. The demon piece 14557 carries the battle's Cleanse need.
        Check("r4-synth1: 2.6-04 the demon piece 14557 carries the 2.6 Cleanse need and 48247 is a registered tankbuster",
            BST_CrucibleData.Enemies.Any(e => e.NameId == 14557 && (e.Needs & CrucibleNeeds.Cleanse) != 0)
            && BST_CrucibleData.Tankbusters.Contains(48247u),
            BST_CrucibleData.Enemies.FirstOrDefault(e => e.NameId == 14557).Needs.ToString());
        var ash26 = D(AshHeld(Cleansed(Fight(2, 6, 14557))));
        Check("r4-synth1: 2.6-04 Paralysis up, Scouring Ash held: the cleanse press (crucible:cleanse-scouringash)",
            Is(ash26, BST.ScouringAsh, "crucible:cleanse-scouringash"), Show(ash26));
        var noPara26 = D(AshHeld(Fight(2, 6, 14557) with { CrucibleNeeds = CrucibleNeeds.Cleanse, PlayerHasCleansableDebuff = false }));
        Check("r4-synth1: 2.6-04 no Paralysis: no cleanse press", !Is(noPara26, BST.ScouringAsh, "crucible:cleanse-scouringash"), Show(noPara26));
        var bat26 = D(Cleansed(BatOut(Fight(2, 6, 14557))));
        Check("r4-synth1: 2.6-04 Paralysis up, the bat out with One with Nature: Ultrasonics (crucible:cleanse-ultrasonics)",
            Is(bat26, BST.TemperedRelease, "crucible:cleanse-ultrasonics"), Show(bat26));

        // ================================================================ 3.0-12b Woken Thanatos race
        // A woken Thanatos Piece (14595) casts Infernal Pain (50540, 8 s); the dossier answer is to kill it inside the
        // cast, with Lightning (its weakness). 0 recorded casts. The plugin's shape: priority-add targeting (Thanatos
        // before the guardia that covers it) and lightning-weighted familiar picks.
        Check("r4-synth1: 3.0-12b Thanatos 14595 is a priority add, weak to Lightning, ordered before the guardia; Guttler is weak to nothing",
            BST_CrucibleData.PriorityAdds.Contains(14595u)
            && BST_CrucibleData.Enemies.First(e => e.NameId == 14595).Weakness == CrucibleWeakness.Lightning
            && BST_CrucibleData.Enemies.First(e => e.NameId == 14592).Weakness == CrucibleWeakness.None
            && Enumerable.Range(0, BST_CrucibleData.PriorityAddOrder.Length)
                .Any(w => BST_CrucibleData.PriorityAddTier(14595, w) == 0 && BST_CrucibleData.PriorityAddTier(14591, w) == 1),
            BST_CrucibleData.Enemies.First(e => e.NameId == 14595).Weakness.ToString());
        var wake = Allowed(T(14592, 100f), T(14595, 100f, 50540), T(14591, 100f));
        Check("r4-synth1: 3.0-12b Guttler, a woken Thanatos and the guardia: only the Thanatos is targetable (killed on sight, before its covering guardia)",
            wake.SequenceEqual(new[] { 1 }), Ids(wake));
        var wakeGone = Allowed(T(14592, 100f), T(14591, 100f));
        Check("r4-synth1: 3.0-12b the Thanatos dead: the covering guardia becomes the target (its tier)",
            wakeGone.SequenceEqual(new[] { 1 }), Ids(wakeGone));
        var why = new List<string>();
        var bolt = BST_CrucibleAdvisor.Score(3, 0, 13, CrucibleNeeds.None, why);   // geshunpest: lightning auto-attacks
        var noBolt = BST_CrucibleAdvisor.Score(3, 0, 17, CrucibleNeeds.None);      // slime: same kin, no element
        Check("r4-synth1: 3.0-12b a lightning familiar outscores its same-kin non-lightning sibling for battle 3.0, and the why says lightning",
            bolt > noBolt && why.Contains("lightning"), $"{bolt} vs {noBolt} [{string.Join("|", why)}]");

        // ================================================================ 3.1-02 Soul Douse kill-the-caster
        // The bone bishop's Soul Douse (50693, 5 s): the dossier answer is to kill the casting bishop inside the cast.
        // The rule shipped 2026-10-01; every recorded cast is from the day before.
        Check("r4-synth1: 3.1-02 Soul Douse 50693 is in KillTheCaster", BST_CrucibleData.KillTheCaster.Contains(50693u), "");
        var douse = Allowed(T(14564, 100f), T(14567, 100f, 50693));
        Check("r4-synth1: 3.1-02 the cavalier and a casting bishop: only the bishop is targetable (kill the caster first)",
            douse.SequenceEqual(new[] { 1 }), Ids(douse));
        var douseBeatPetrified = Allowed(T(14567, 100f, 50693), T(14564, 100f, petrified: true));
        Check("r4-synth1: 3.1-02 the casting bishop even beats a petrified enemy (the petrified does not act, the caster does)",
            douseBeatPetrified.SequenceEqual(new[] { 0 }), Ids(douseBeatPetrified));
        var douseIdle = Allowed(T(14567, 100f), T(14564, 100f, petrified: true));
        Check("r4-synth1: 3.1-02 the bishop not casting: the petrified cavalier wins instead (the killer rule keys on the live cast)",
            douseIdle.SequenceEqual(new[] { 1 }), Ids(douseIdle));

        // ================================================================ 3.2-06b Tsunami Vileskin
        // The Ymir's Tsunami (48480/48481) knocks back; against the wall the dossier answer is Vileskin inside 3 s of the
        // wall (the Shield Charge positioning half is movement: BossMod/AutoDuty, not this plugin). 0 vileskin-knockback
        // decisions in the logs — the decision post-dates the captures.
        Check("r4-synth1: 3.2-06b Tsunami 48481 is the registered knockback of battle 3.2",
            BST.CrucibleKnockbacks.BattleHasKnockback(3, 2)
            && BST.CrucibleKnockbacks.Casts.Any(c => c.CastId == 48481 && c.Board == 3 && c.Battle == 2),
            "");
        var skin = D(Fight(3, 2, 14569) with
        {
            KinshipHeld = true, BeastModeResolved = BST.Vileskin, ReadyBeastMode = true, KnockbackCastRemaining = 2f,
        });
        Check("r4-synth1: 3.2-06b the wall 2 s away with Vileskin held: the skin is pressed (beastmode:vileskin-knockback)",
            Is(skin, BST.Vileskin, "beastmode:vileskin-knockback"), Show(skin));
        var skinEarly = D(Fight(3, 2, 14569) with
        {
            KinshipHeld = true, BeastModeResolved = BST.Vileskin, ReadyBeastMode = true, KnockbackCastRemaining = 5f,
        });
        Check("r4-synth1: 3.2-06b the wall 5 s away: the skin is held (vileskin:held-for-knockback), not spent early",
            !Is(skinEarly, BST.Vileskin, "beastmode:vileskin-knockback") && skinEarly.Declines.Contains("vileskin:held-for-knockback"),
            Show(skinEarly));

        // ================================================================ 3.3-02 Caustic Vomit interrupt / familiar cover
        // The pullet's Caustic Vomit (48489, 13 s, Heavy): the dossier answer is interrupt with Soul Crush, else let the
        // familiar take it. 6 casts, 0 completions — Soul Crush was never held. 48489 is registered; the cover gate had no
        // measured familiar hit, so the first two checks here failed at the integration head and the stand-in estimate
        // (Dreadwash 910 x 1.65 = 1502) is this lane's fix.
        BST_CrucibleData.EstimatedTankbusterHits.TryGetValue(48489u, out var vomitEst);
        Check("r4-synth1: 3.3-02 Caustic Vomit 48489 is a registered, panel-interruptible tankbuster with a documented cover estimate, and the pullet calls for an interrupt",
            BST_CrucibleData.Tankbusters.Contains(48489u) && BST_CrucibleData.PanelInterruptibleActions.Contains(48489u)
            && BST_CrucibleData.Enemies.First(e => e.NameId == 14574).Needs.HasFlag(CrucibleNeeds.Interrupt)
            && vomitEst.EstimatedFamiliarHit == 1502,
            vomitEst.Evidence);
        var vomitCoverS = Fight(3, 3, 14574) with
        {
            TargetCastId = 48489, TargetCastRemaining = 2f, TargetInterruptible = false, CrucibleNeeds = CrucibleNeeds.Interrupt,
        };
        var vomitCover = D(vomitCoverS);
        Check("r4-synth1: 3.3-02 no Soul Crush, Snarl ready, the familiar can take the estimate: Snarl so the familiar covers it (aggro:snarl-tankbuster)",
            Is(vomitCover, BST.Snarl, "aggro:snarl-tankbuster"), Show(vomitCover));
        Check("r4-synth1: 3.3-02 a familiar below the estimate's gate does not cover (no Snarl pressed into a hit it cannot take)",
            D(vomitCoverS with { PetHp = 1500f, PetHpPercent = 43f }).Reason != "aggro:snarl-tankbuster",
            Show(D(vomitCoverS with { PetHp = 1500f, PetHpPercent = 43f })));
        var vomitCrush = D(CrushHeld(Fight(3, 3, 14574) with
        {
            TargetCastId = 48489, TargetCastRemaining = 2f, TargetInterruptible = true, CrucibleNeeds = CrucibleNeeds.Interrupt,
            ReadySnarl = false,
        }));
        Check("r4-synth1: 3.3-02 Soul Crush held, the cast interruptible: the interrupt press (beastmode:soulcrush-interrupt)",
            Is(vomitCrush, BST.SoulCrush, "beastmode:soulcrush-interrupt"), Show(vomitCrush));
        var vomitRearm = D(Fight(3, 3, 14574) with
        {
            TargetCastId = 48489, TargetCastRemaining = 2f, TargetInterruptible = true, CrucibleNeeds = CrucibleNeeds.Interrupt,
            ReadySnarl = false, Slot2Beast = 7,
        });
        Check("r4-synth1: 3.3-02 the cast live, nothing held, the Soulkin on horn 2: its horn is blown to arm the interrupt (crucible:answer-interrupt-slot2)",
            Is(vomitRearm, BST.SecondBattlehorn, "crucible:answer-interrupt-slot2"), Show(vomitRearm));
        var vomitAim = BST_CrucibleLogic.AllowedTargets(new[] { T(14574, 100f, 48489, interruptible: true), T(14573, 100f) }, interruptArmed: true);
        Check("r4-synth1: 3.3-02 the armed interrupt aims the casting pullet first, before the cockerel",
            vomitAim.SequenceEqual(new[] { 0 }), Ids(vomitAim));
        var vomitBoth = D(Fight(3, 3, 14574) with
        {
            TargetCastId = 48489, TargetCastRemaining = 2f, TargetInterruptible = true, CrucibleNeeds = CrucibleNeeds.Interrupt,
        });
        Check("r4-synth1: 3.3-02 both answers ready (48489 is not in InterruptFirstCasts): the Snarl goes first, the 13 s window still holds the interrupt",
            Is(vomitBoth, BST.Snarl, "aggro:snarl-tankbuster"), Show(vomitBoth));

        // ================================================================ 3.3-07b Flying Frenzy cover
        // The Zu's Flying Frenzy (50465 cast, 48490 damage, 8 s marker on the enmity holder): the dossier answer is to
        // mitigate the buster — hand it to the familiar. 10 marker casts; 7 of 9 recorded hits on familiars. The cover
        // gate had no measured row, so this check failed first and the measured HeavyCasts row (852 / 869) is this lane's
        // fix. The "place it off the eggs" half is movement (BossMod/AutoDuty), not this plugin.
        Check("r4-synth1: 3.3-07b Flying Frenzy 50465 is a registered tankbuster with a measured row (8.0 s cast, 852 on the character, 869 on a familiar)",
            BST_CrucibleData.Tankbusters.Contains(50465u)
            && BST_CrucibleData.HeavyCast(50465) is { Kind: CrucibleHitKind.Tankbuster, CastSeconds: 8f, MaxOnCharacter: 852, MaxOnFamiliar: 869 },
            BST_CrucibleData.HeavyCast(50465)?.ToString());
        var frenzyS = Fight(3, 3, 14572) with { TargetCastId = 50465, TargetCastRemaining = 2f, CrucibleNeeds = CrucibleNeeds.None };
        var frenzy = D(frenzyS);
        Check("r4-synth1: 3.3-07b Flying Frenzy up, the character holds enmity, Snarl ready: the familiar covers it (aggro:snarl-tankbuster)",
            Is(frenzy, BST.Snarl, "aggro:snarl-tankbuster"), Show(frenzy));
        Check("r4-synth1: 3.3-07b a familiar below the measured hit's gate does not cover",
            D(frenzyS with { PetHp = 900f, PetHpPercent = 26f }).Reason != "aggro:snarl-tankbuster",
            Show(D(frenzyS with { PetHp = 900f, PetHpPercent = 26f })));
        Check("r4-synth1: 3.3-07b no cover Snarl inside the last half second (too late to be worth the press)",
            D(frenzyS with { TargetCastRemaining = 0.3f }).Reason != "aggro:snarl-tankbuster",
            Show(D(frenzyS with { TargetCastRemaining = 0.3f })));

        // ================================================================ 3.7-04 Seeding Needles room-wide heal
        // Seeding Needles (48589, 5 s, room-wide): the dossier answer is keep HP up. 0 recorded runs of battle 3.7. The
        // cast is registered nowhere (no HeavyCast row), so the survival answer is the generic panic line at 25% or less.
        Check("r4-synth1: 3.7-04 Seeding Needles 48589 is registered nowhere: the raidwide guard does not own it, the panic line does",
            BST_CrucibleData.HeavyCast(48589) is null && !BST_CrucibleData.Tankbusters.Contains(48589u), "");
        var needlesS = Fight(3, 7, 14587) with
        {
            TargetCastId = 48589, TargetCastRemaining = 2f, CrucibleNeeds = CrucibleNeeds.None,
            PlayerHpPercent = 22f, PlayerHp = 893f, ReadyHealPotion = 46959, ReadySnarl = false,
        };
        Check("r4-synth1: 3.7-04 the room-wide live at 22% HP: the potion (crucible:panic-heal)",
            Is(D(needlesS), 46959, "crucible:panic-heal"), Show(D(needlesS)));
        Check("r4-synth1: 3.7-04 the same cast at 60% HP: no panic heal",
            D(needlesS with { PlayerHpPercent = 60f, PlayerHp = 2436f }).Reason != "crucible:panic-heal",
            Show(D(needlesS with { PlayerHpPercent = 60f, PlayerHp = 2436f })));

        // ================================================================ 3.7-01 / 3.7-02 / 3.7-07 / 3.7-08 the cactuar pack
        // The pack: Campeador 14587, Sabotender 14588, Flowertender 14589, Soldado 14590, Guardia 14591. The documented
        // order is flowertender (its Healing Water) before the guardia; the rest are unranked. Weaknesses: 14587-14590
        // Lightning, 14591 Ice. 0 recorded runs of battle 3.7.
        Check("r4-synth1: 3.7-08 the pack's panel weaknesses: 14587-14590 Lightning, 14591 Ice",
            BST_CrucibleData.Enemies.Where(e => e.Board == 3 && e.Battle == 7).All(e =>
                (e.NameId == 14591) == (e.Weakness == CrucibleWeakness.Ice)
                && (e.NameId is >= 14587 and <= 14590) == (e.Weakness == CrucibleWeakness.Lightning)),
            string.Join("|", BST_CrucibleData.Enemies.Where(e => e.Board == 3 && e.Battle == 7).Select(e => $"{e.NameId}:{e.Weakness}")));
        Check("r4-synth1: 3.7-01 the cactuar order wave is flowertender tier 0, guardia tier 1, and the other three are in no order list",
            Enumerable.Range(0, BST_CrucibleData.PriorityAddOrder.Length)
                .Any(w => BST_CrucibleData.PriorityAddTier(14589, w) == 0 && BST_CrucibleData.PriorityAddTier(14591, w) == 1)
            && !BST_CrucibleData.PriorityAdds.Contains(14587u) && !BST_CrucibleData.PriorityAdds.Contains(14588u)
            && !BST_CrucibleData.PriorityAdds.Contains(14590u),
            "");
        var flower = Allowed(T(14587, 100f), T(14588, 100f), T(14589, 100f), T(14590, 100f), T(14591, 100f));
        Check("r4-synth1: 3.7-01 the full pack wave: only the flowertender is targetable (Healing Water dies first)",
            flower.SequenceEqual(new[] { 2 }), Ids(flower));
        var covered = Allowed(T(14589, 30f), T(14591, 100f));
        Check("r4-synth1: 3.7-02 the guardia Covered (2413) before the flowertender dies: the order is static, the flowertender still dies first",
            covered.SequenceEqual(new[] { 0 }) && BST_CrucibleData.Enemies.Any(e => e.NameId == 14591),
            Ids(covered));
        var flowerGone = Allowed(T(14587, 100f), T(14588, 100f), T(14590, 100f), T(14591, 100f));
        Check("r4-synth1: 3.7-02 the flowertender dead: the guardia becomes the target (tier 1)",
            flowerGone.SequenceEqual(new[] { 3 }), Ids(flowerGone));
        var tail = Allowed(T(14587, 100f), T(14588, 100f), T(14590, 100f));
        Check("r4-synth1: 3.7-07 the tail (Campeador, Sabotender, Soldado) alone: the whole wave stays targetable (no order owns them)",
            tail.SequenceEqual(new[] { 0, 1, 2 }), Ids(tail));
        var rankedAmong = Allowed(T(14589, 100f), T(14587, 100f), T(14588, 100f), T(14590, 100f));
        Check("r4-synth1: 3.7-07 a ranked add among unranked ones narrows the wave to itself",
            rankedAmong.SequenceEqual(new[] { 0 }), Ids(rankedAmong));
        var boltPack = BST_CrucibleAdvisor.Score(3, 7, 13, CrucibleNeeds.None);    // geshunpest: lightning
        var noBoltPack = BST_CrucibleAdvisor.Score(3, 7, 17, CrucibleNeeds.None);  // slime: no element
        var ice = BST_CrucibleAdvisor.Score(3, 7, 20, CrucibleNeeds.None);         // flying trap: ice
        var noIce = BST_CrucibleAdvisor.Score(3, 7, 22, CrucibleNeeds.None);       // sabotender: same kin, piercing
        Check("r4-synth1: 3.7-08 the picks follow the panel: lightning beats its element-less sibling, ice beats its same-kin sibling",
            boltPack > noBoltPack && ice > noIce, $"{boltPack}/{noBoltPack} {ice}/{noIce}");
    }
}
