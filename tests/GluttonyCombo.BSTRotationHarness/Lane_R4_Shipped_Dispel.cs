using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

internal static partial class Program
{
    /// <summary>
    ///     The buff dispel fix (692fd8227, 2026-10-03 19:46 ET, first tag 1.0.4.271, in production 1.0.4.301; the build before it is 692fd8227^). Before it the Regen on the Abaddon
    ///     (status 989, Rehabilitation) was a dispel target and 15 Quelling Waves went at it (19:34:28 to 19:35:19) while the Drake's Blaze Spikes (5465) waited; the game does not take 989
    ///     off. After it 989 is "disproven" and out of the set, and a carrier the panel flags (5465) is aimed at before one it does not. Every case reads the build's own status tables
    ///     (<see cref="BST_CrucibleData.DispellableBuffs"/>, <see cref="BST_CrucibleData.PanelFlagsDispellable"/>), so which target the dispel aims at is decided by the real table, not by flags
    ///     set by hand.
    /// </summary>
    private static void R4ShippedDispel()
    {
        Console.WriteLine("-- r4-shipped: dispel aim --");
        var cfg = BstSettings.Defaults();
        const uint abaddon = 14655, drake = 14651;
        const uint blazeSpikes = 5465, regen = 989;

        // The real table, one id at a time.
        var drakeBuff = R4ShippedCarries(blazeSpikes);
        var abaddonBuff = R4ShippedCarries(regen);
        Check("r4-shipped: dispel Blaze Spikes (5465) on the Drake is a dispellable buff and the panel flags it (known to come off)", drakeBuff is { Carries: true, Proven: true });
        Check("r4-shipped: dispel Regen (989) on the Abaddon is not a dispellable buff: the game does not take it off (15 Quelling Waves, none removed it)", abaddonBuff is { Carries: false, Proven: false },
            $"carries={abaddonBuff.Carries} proven={abaddonBuff.Proven}");

        // The aim. Both carriers up, the Abaddon free to be targeted (the Morphos eaten), the Wave held and ready: the table says which of them is a carrier.
        List<int> Aim(params BST_CrucibleLogic.TargetCandidate[] c) => BST_CrucibleLogic.AllowedTargets(c, false, true);
        BST_CrucibleLogic.TargetCandidate Cand(uint nameId, (bool Carries, bool Proven) buff, bool avoid = false) =>
            R4ShippedCandidate(nameId, 100f, avoid: avoid, dispellable: buff.Carries, proven: buff.Proven);
        Check("r4-shipped: dispel the Drake in Blaze Spikes (5465) and the Abaddon in Regen (989) both up: the dispel is aimed at the Drake only",
            Aim(Cand(abaddon, abaddonBuff), Cand(drake, drakeBuff, avoid: true)).SequenceEqual(new[] { 1 }),
            string.Join(",", Aim(Cand(abaddon, abaddonBuff), Cand(drake, drakeBuff, avoid: true))));
        Check("r4-shipped: dispel ... the same with the Abaddon listed second", Aim(Cand(drake, drakeBuff, avoid: true), Cand(abaddon, abaddonBuff)).SequenceEqual(new[] { 0 }),
            string.Join(",", Aim(Cand(drake, drakeBuff, avoid: true), Cand(abaddon, abaddonBuff))));

        // The decision. Wave Kinship held with Quelling Wave ready (the Abaddon fight, 5.5), the target flag derived from the table.
        var live = CrucibleState(50) with { SinceSummon = 12f, ReadyParting = false, SinceHornPress = 30f };
        var fight = live with
        {
            CrucibleBoard = 5, CrucibleBattle = 5, CrucibleNeeds = BST_CrucibleLogic.FightNeeds(5, 5), EnemyCount = 2,
            Slot1Beast = 1, Slot2Beast = 8, Slot3Beast = 4, ActiveSlot = 1, PetObjectBeast = 1, GcdReady = true,
            KinshipHeld = true, BeastModeResolved = BST.QuellingWave, ReadyBeastMode = true, HasHostileTarget = true,
        };
        BstState Target(BstState s, uint nameId, (bool Carries, bool Proven) buff) => s with
        {
            TargetNameId = nameId, TargetHasDispellableBuff = buff.Carries, EnemyHasDispellableBuff = buff.Carries,
        };
        var atAbaddon = Decide(Target(fight, abaddon, abaddonBuff), cfg);
        Check("r4-shipped: dispel the Abaddon targeted with only Regen (989) on it, Quelling Wave held and ready: no dispel goes at it (the 19:34 run sent 15)",
            atAbaddon.ActionId != BST.QuellingWave && atAbaddon.Reason is not ("crucible:dispel-quellingwave" or "crucible:dispel-caw"), $"{atAbaddon.ActionId} {atAbaddon.Reason} [{atAbaddon.Declines}]");
        var atDrake = Decide(Target(fight, drake, drakeBuff), cfg);
        Check("r4-shipped: dispel the Drake targeted with Blaze Spikes (5465), Quelling Wave held and ready: the Quelling Wave goes at it",
            atDrake is { ActionId: BST.QuellingWave, Reason: "crucible:dispel-quellingwave" }, $"{atDrake.ActionId} {atDrake.Reason} [{atDrake.Declines}]");

        // The research row stays, with the reason it is not tried.
        Check("r4-shipped: dispel the research row for 989 stays and says 'disproven'; the Drake's 5465 row is 'panel'",
            BST_CrucibleData.DispelRows.Any(r => r.StatusId == regen && r.Basis == "disproven") && BST_CrucibleData.DispelRows.Any(r => r.StatusId == blazeSpikes && r.Basis == "panel"));
    }
}
