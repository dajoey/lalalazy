using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Round 4, Third Board battle 2 (task tasks-20261015-crucible-r4-fix-third-board-battle-2-01). Titles start with "r4-third2:" and name the gap id.
///     3.2-03b  Dreadwash (48485): Soul Crush first, else Snarl so the familiar covers it (it follows the enmity holder: 33 character, 31 familiar hits in 64 casts).
///     3.2-08   Water (48626): the Sahagin's instant single-target spell goes to the familiar (Snarl while it is on the character).
///     sv-02 / kd-03  Paralyzing Spikes (50532, buff 5434 on the Sahagin): nothing hits the Sahagin from the last second of the cast, a Shield Charge never lands
///              beside it, and a held Quelling Wave (not reflected) is the damage while the stance is up.
///     3.2-01   Ymir shell: closed without code (characterisation cases only): the long holds were builds up to 1.0.4.251, the bind window ships on.
///     The new BstState field is set by name (<see cref="R4T2Set"/>), so this file compiles before the field exists and the failing-first run fails at runtime.
/// </summary>
internal static partial class Program
{
    private static BstState R4T2Set(BstState s, string field, object value)
    {
        object boxed = s;
        typeof(BstState).GetField(field)?.SetValue(boxed, value);
        return (BstState)boxed;
    }

    private static void R4Third2Lane20261015()
    {
        Console.WriteLine("-- r4-third2: Third Board battle 2 (Dreadwash, Water, Paralyzing Spikes, the shell) --");
        var on = BstSettings.Defaults() with { CrucibleAggro = CrucibleAggroMode.On };
        BstDecision D(BstState s, BstSettings c) => Decide(s, c);
        bool Is(BstDecision d, uint action, string reason) => d.ActionId == action && d.Reason == reason;
        string Show(BstDecision d) => $"{d.ActionId}/{d.Reason} sh={d.Shadow} [{d.Declines}]";

        const uint Ymir = 14569, ShellNpc = 14570, Sahagin = 14571;
        // The Sahagin is the target, mid-fight (not the pull), the familiar settled at full HP (3,492), the character healthy, both duty actions ready.
        BstState Fight(uint nameId = Sahagin) => CrucibleState() with
        {
            CrucibleBoard = 3, CrucibleBattle = 2, TargetNameId = nameId, InCombat = true, HasHostileTarget = true, TargetDistance = 3f, EnemyCount = 3,
            ReadySnarl = true, ReadyChallenge = true, ReadyParting = false, EnemyTargetsPlayer = true, EnemyTargetsPet = false,
            TargetHpPercent = 60f, HighestEnemyHpPercent = 100f, PetHp = 3492f, PetHpPercent = 100f, PlayerHp = 4060f, PlayerHpPercent = 100f,
            SinceHornPress = 30f, SinceSummon = 20f, SinceSnarl = float.MaxValue, CanWeave = true, GcdReady = true,
            CrucibleDegreeKnown = true, CrucibleDegreeLevel = CrucibleDegree.Standard,
        };
        BstState Dread(BstState s, float remaining = 6f) => s with
        {
            TargetCastId = 48485, TargetCastRemaining = remaining, TargetInterruptible = true, CrucibleNeeds = CrucibleNeeds.Interrupt,
        };
        BstState SoulCrushHeld(BstState s) => s with { KinshipHeld = true, BeastModeResolved = BST.SoulCrush, ReadyBeastMode = true };

        // ================================================================ 3.2-03b Dreadwash
        // 87 casts, 64 completed (cast start to effect 7.96-8.00 s); each completed cast hit ONE target: 33 on the character (up to 910), 31 on a familiar (up to 948).
        // Hysteria (296) on the character: 28 adds, every one inside a Dreadwash hit on the character (25 of them with nothing else in 1.5 s).
        Check("r4-third2: 3.2-03b Dreadwash 48485 is a registered tankbuster with a measured row (8.0 s cast, lands at the cast end, 910 / 948)",
            BST_CrucibleData.Tankbusters.Contains(48485)
            && BST_CrucibleData.HeavyCast(48485) is { Kind: CrucibleHitKind.Tankbuster, CastSeconds: 8f, HitDelay: 0f, MaxOnCharacter: 910, MaxOnFamiliar: 948 },
            BST_CrucibleData.HeavyCast(48485)?.ToString());
        var dread = D(Dread(Fight()), on);
        Check("r4-third2: 3.2-03b Dreadwash up, no Soul Crush held, Snarl ready: Snarl so the familiar covers it (aggro:snarl-tankbuster)",
            Is(dread, BST.Snarl, "aggro:snarl-tankbuster"), Show(dread));
        var crush = D(SoulCrushHeld(Dread(Fight())), on);
        Check("r4-third2: 3.2-03b Dreadwash up, Soul Crush held and in reach: the interrupt goes first, no Snarl", Is(crush, BST.SoulCrush, "beastmode:soulcrush-interrupt"), Show(crush));
        var far = D(SoulCrushHeld(Dread(Fight() with { TargetDistance = 8f })), on);
        Check("r4-third2: 3.2-03b Dreadwash up, Soul Crush held but the Sahagin is out of melee reach: Snarl (the interrupt cannot land)",
            Is(far, BST.Snarl, "aggro:snarl-tankbuster"), Show(far));
        Check("r4-third2: 3.2-03b Dreadwash up and the familiar already holds the Sahagin: no Snarl", D(Dread(Fight() with { EnemyTargetsPet = true, EnemyTargetsPlayer = false }), on).Reason != "aggro:snarl-tankbuster");
        Check("r4-third2: 3.2-03b Dreadwash up and Snarl on its recast: no Snarl", D(Dread(Fight() with { ReadySnarl = false }), on).Reason != "aggro:snarl-tankbuster");
        Check("r4-third2: 3.2-03b Dreadwash up, 0.3 s left (too late to take the aggro): no Snarl", D(Dread(Fight(), 0.3f), on).Reason != "aggro:snarl-tankbuster");
        var guard = D(Dread(Fight() with { ReadySnarl = false, PlayerHp = 1218f, PlayerHpPercent = 30f, ReadyHealPotion = 46962 }, 5f), on);
        Check("r4-third2: 3.2-03b Dreadwash up, no cover Snarl armed, the character at 30%: the potion guard (crucible:raidwide-guard)",
            Is(guard, 46962, "crucible:raidwide-guard"), Show(guard));
        var shadowDread = D(Dread(Fight()), BstSettings.Defaults() with { CrucibleAggro = CrucibleAggroMode.Shadow });
        Check("r4-third2: 3.2-03b aggro in shadow mode: the Snarl is logged, not pressed", shadowDread.ActionId != BST.Snarl && shadowDread.Shadow == "aggro:snarl-tankbuster", Show(shadowDread));

        // ================================================================ 3.2-08 Water
        // 923 hits (log_type 21) of 100-155: 463 on a familiar, 532 effect lines on the character (no cast bar: instant). A Snarl while the Sahagin is on the character.
        var water = D(Fight(), on);
        Check("r4-third2: 3.2-08 Water: the Sahagin on the character, a full familiar out: Snarl, the spell goes to the familiar (aggro:snarl-water)",
            Is(water, BST.Snarl, "aggro:snarl-water"), Show(water));
        Check("r4-third2: 3.2-08 Water: the Ymir is the target: no water Snarl", D(Fight(Ymir), on).Reason != "aggro:snarl-water");
        Check("r4-third2: 3.2-08 Water: the shell NPC is the target: no water Snarl", D(Fight(ShellNpc), on).Reason != "aggro:snarl-water");
        Check("r4-third2: 3.2-08 Water: the familiar at 65% (under the 70% line, above the 60% Challenge-back line): no Snarl, no flapping",
            D(Fight() with { PetHp = 2270f, PetHpPercent = 65f }, on).Reason != "aggro:snarl-water");
        var held = D(Fight() with { EnemyTargetsPet = true, EnemyTargetsPlayer = false, PetHp = 2000f, PetHpPercent = 57f }, on);
        Check("r4-third2: 3.2-08 Water: the familiar holds the Sahagin and is down to 57%: the character takes it back (aggro:challenge-pull-budget)",
            Is(held, BST.Challenge, "aggro:challenge-pull-budget"), Show(held));
        Check("r4-third2: 3.2-08 Water: Snarl on its recast: no Snarl", D(Fight() with { ReadySnarl = false }, on).Reason != "aggro:snarl-water");
        Check("r4-third2: 3.2-08 Water: 'Snarl at the pull' unticked (the option whose Challenge back at 60% balances it): no water Snarl",
            D(Fight(), Opt(on, "CrucibleSnarlAtPull", false)).Reason != "aggro:snarl-water");
        Check("r4-third2: 3.2-08 Water: a familiar that cannot hold the piece at this degree (Third Degree, 3,492 HP): no Snarl",
            D(Fight() with { CrucibleDegreeLevel = CrucibleDegree.Third }, on).Reason != "aggro:snarl-water");
        var sahaginStance = D(Fight() with { TargetInStance = true }, on);
        Check("r4-third2: 3.2-08 Water: the Sahagin in Paralyzing Spikes: Snarl still goes (an ability, not a hit on it)",
            Is(sahaginStance, BST.Snarl, "aggro:snarl-water"), Show(sahaginStance));

        // ================================================================ sv-02 / kd-03 Paralyzing Spikes
        // Cast 50532 is 3.0 s; the buff lands at its end. Recorded hits on the stanced Sahagin by the five "unanswered Paralyzing Spikes" deaths: a combo press
        // 1.8 s and an Axeblade Bite 0.0 s after the buff, two Shield Charge AoEs (6.3 s and 39.2 s in), one auto-attack.
        // No duty action ready: with a Snarl ready the aggro rules come first in the stance branch (the water Snarl above).
        BstState Spikes(float remaining) => Fight() with { ReadySnarl = false, ReadyChallenge = false, TargetCastId = 50532, TargetCastRemaining = remaining, TargetInStance = false };
        var landing = D(Spikes(0.6f), on);
        Check("r4-third2: sv-02 kd-03 the Sahagin is 0.6 s from finishing Paralyzing Spikes: nothing is pressed at it (crucible:hold-stance)",
            Is(landing, BST_CrucibleLogic.Hold, "crucible:hold-stance"), Show(landing));
        Check("r4-third2: sv-02 kd-03 the same cast with 2.5 s left (just started): the rotation goes on as usual",
            D(Spikes(2.5f), on).ActionId != BST_CrucibleLogic.Hold, Show(D(Spikes(2.5f), on)));
        Check("r4-third2: sv-02 kd-03 another Sahagin cast (Water II 48482) with 0.6 s left: no stance hold",
            D(Spikes(0.6f) with { TargetCastId = 48482 }, on).ActionId != BST_CrucibleLogic.Hold);
        var noFamiliar = D(Spikes(0.6f) with { ActiveSlot = 0, PetObjectPresent = false, PetHpPercent = 100f, PlayerHp = 1218f, PlayerHpPercent = 30f }, on);
        Check("r4-third2: sv-02 kd-03 no familiar and the character at 30%: the death-spiral exemption of the stance hold applies to the landing stance too",
            noFamiliar.ActionId != BST_CrucibleLogic.Hold, Show(noFamiliar));

        // A Shield Charge is an AoE (44893, log_type 22: the 2026-09-30 dash at the Ymir Shell hit the Ymir 38, the Shell 340 and the stanced Sahagin 505).
        BstState Dash(bool stanceNear) => R4T2Set(CrucibleState(50) with
        {
            CrucibleBoard = 3, CrucibleBattle = 2, TargetNameId = ShellNpc, InCombat = true, HasHostileTarget = true, TargetDistance = 10f, EnemyCount = 3,
            ReadyShieldCharge = true, ShieldChargeCharges = 2, ShieldChargeMax = 3, CanWeave = true, GcdReady = true, ReadyParting = false,
            EnemyTargetsPlayer = true, ReadySnarl = false, ReadyChallenge = false, TargetHpPercent = 60f,
            CrucibleDegreeKnown = true, CrucibleDegreeLevel = CrucibleDegree.Standard,
        }, "StanceNearTarget", stanceNear);
        var dashFree = D(Dash(false), on);
        Check("r4-third2: sv-02 kd-03 control: a gap-closing Shield Charge at the Ymir Shell with no stanced enemy near it goes out (shieldcharge:gapclose)",
            Is(dashFree, BST.ShieldCharge, "shieldcharge:gapclose"), Show(dashFree));
        var dashNear = D(Dash(true), on);
        Check("r4-third2: sv-02 kd-03 a Sahagin in Paralyzing Spikes within AoE reach of the target: no Shield Charge (crucible:shieldcharge-stance-near)",
            dashNear.ActionId != BST.ShieldCharge && dashNear.Declines.Contains("crucible:shieldcharge-stance-near"), Show(dashNear));
        var overcap = BstSettings.Defaults() with { CrucibleAggro = CrucibleAggroMode.On, UseShieldCharge = false, ShieldChargeOvercap = true };
        var overcapNear = D(Dash(true) with { TargetDistance = 3f, ShieldChargeCharges = 3 }, overcap);
        Check("r4-third2: sv-02 kd-03 the overcap dash obeys the same gate", overcapNear.ActionId != BST.ShieldCharge && overcapNear.Declines.Contains("crucible:shieldcharge-stance-near"), Show(overcapNear));
        var collector = RepoFile("Combos", "PvE", "BST", "BST_Crucible.cs");
        Check("r4-third2: sv-02 kd-03 the collector sets StanceNearTarget for an enemy in a stance beside the target (source wiring)",
            collector.Contains("StanceNearTarget = true", StringComparison.Ordinal) && collector.Contains("StanceStatuses.Contains", StringComparison.Ordinal));

        // Quelling Wave is not reflected (Icy Veins; no log has a Quelling Wave on a stanced Sahagin): the held wave is the damage while the stance is up.
        BstState Wave(BstState s) => s with { KinshipHeld = true, BeastModeResolved = BST.QuellingWave, ReadyBeastMode = true, TargetDistance = 8f };
        var stanced = Fight() with { TargetInStance = true, ReadySnarl = false, ReadyChallenge = false }; // no duty action ready: the aggro rules come first in the stance branch
        var wave = D(Wave(stanced), on);
        Check("r4-third2: sv-02 kd-03 the Sahagin in Paralyzing Spikes and a Quelling Wave held: the wave is pressed (crucible:stance-quellingwave)",
            Is(wave, BST.QuellingWave, "crucible:stance-quellingwave"), Show(wave));
        Check("r4-third2: sv-02 kd-03 no Quelling Wave held: the stance hold stands", Is(D(stanced, on), BST_CrucibleLogic.Hold, "crucible:hold-stance"), Show(D(stanced, on)));
        Check("r4-third2: sv-02 kd-03 the ranged-wave option unticked: the stance hold stands", D(Wave(stanced), on with { UseQuellingWaveRanged = false }).ActionId != BST.QuellingWave);
        Check("r4-third2: sv-02 kd-03 only on this battle: a stanced enemy elsewhere (1.1) is still held",
            D(Wave(stanced with { CrucibleBoard = 1, CrucibleBattle = 1, TargetNameId = 14531 }), on).ActionId != BST.QuellingWave);
        Check("r4-third2: sv-02 kd-03 the wave is a GCD spell: no GCD ready, no wave", D(Wave(stanced with { GcdReady = false }), on).ActionId != BST.QuellingWave);

        // ================================================================ 3.2-01 Ymir shell (closed without code)
        // Contestable hold-invulnerable spans on Third Board battle 2 (51 runs): 26, ten of them 15 s or more, every one of those on builds 1.0.4.251 and older;
        // the newest recorded build (1.0.4.291) has one, 4 s. The shell breaks when the Ymir Shell NPC (14570) dies (2198 left the Ymir within a second of 14570 at 0%).
        var shelled = Fight(Ymir) with { TargetInvulnerable = true, EnemyCount = 2 };
        var shellHold = D(shelled, on);
        Check("r4-third2: 3.2-01 characterisation: the shelled Ymir targeted is not hit (crucible:hold-invulnerable)", Is(shellHold, BST_CrucibleLogic.Hold, "crucible:hold-invulnerable"), Show(shellHold));
        // L30, the active familiar a ziz (21: stunning AoE release) with its One with Nature unspent, two healthy ready horns behind it; the window option set explicitly.
        var ymirTactic = CrucibleState() with
        {
            CrucibleBoard = 3, CrucibleBattle = 2, ActiveSlot = 1, Slot1Beast = 21, PetObjectBeast = 21, Slot2Beast = 34, Slot3Beast = 26,
            OneWithNature = true, ReadyTempered = true, SinceSummon = 5f, SinceHornPress = 6f, ReadyHorn2 = true, ReadyHorn3 = true,
            TargetHpPercent = 90f, HighestEnemyHpPercent = 90f, EnemyCount = 2, TargetNameId = Ymir,
        };
        var windowOn = on with { CruciblePackWindow = true };
        Check("r4-third2: 3.2-01 characterisation: the shell just broke, the Ymir targeted, a binding familiar's release ready: the bind goes on it (crucible:ymir-bind)",
            D(ymirTactic with { ShellJustBroke = true }, windowOn) is { ActionId: BST.TemperedRelease, Reason: "crucible:ymir-bind" });
        var cfgSrc = RepoFile("Combos", "PvE", "BST", "BST_Config.cs");
        Check("r4-third2: 3.2-01 characterisation: the pack / shell window ships on (BST_CruciblePackWindow defaults true in BST_Config.cs)",
            System.Text.RegularExpressions.Regex.IsMatch(cfgSrc, @"BST_CruciblePackWindow\s*=\s*new\(\s*""BST_CruciblePackWindow""\s*,\s*true\s*\)"));
    }
}
