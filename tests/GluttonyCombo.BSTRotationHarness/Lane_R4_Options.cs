using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Round 4 options lane (task tasks-20261015-crucible-r4-fix-options-pack-window-01). Titles start with "r4-options:".
///     The dossier reactions that were built but switched off on a fresh install: the add-pack release and Parting Blow (CruciblePackWindow),
///     the pre-pull horn and Borrow (CruciblePrepullHorns) and the cycle exit (CrucibleCycleForDamage); the pack battle list missing board 5
///     battle 6; and the pre-pull Beastskin on the Gargoyle (board 4 battle 5), which no fight need arms.
/// </summary>
internal static partial class Program
{
    /// <summary> The pre-round-4 defaults of the three options: the cases written for the options Off keep running under exactly those settings. </summary>
    private static BstSettings OptionsOff(BstSettings c) => c with { CruciblePackWindow = false, CrucibleCycleForDamage = false, CruciblePrepullHorns = false };

    private static void R4OptionsLane20261015()
    {
        Console.WriteLine("-- r4-options: pack window, pre-pull horn and cycle exit on by default; (5,6) pack; Beastskin pre-pull --");
        var shipped = BstSettings.Defaults();
        var off = OptionsOff(shipped);
        bool IsHorn(uint id) => id is BST.FirstBattlehorn or BST.SecondBattlehorn or BST.ThirdBattlehorn;

        // L30, board / battle given, the active familiar `beast` with its One with Nature unspent, two healthy ready horns behind it.
        BstState Wave(int board, int battle, int beast, int enemies = 1) => CrucibleState() with
        {
            CrucibleBoard = board, CrucibleBattle = battle, ActiveSlot = 1, Slot1Beast = beast, PetObjectBeast = beast, Slot2Beast = 34, Slot3Beast = 26,
            OneWithNature = true, ReadyTempered = true, SinceSummon = 5f, SinceHornPress = 6f, ReadyHorn2 = true, ReadyHorn3 = true,
            TargetHpPercent = 90f, HighestEnemyHpPercent = 90f, EnemyCount = enemies, HasHostileTarget = true,
        };
        // The same wave once the release has gone off and the pack is still standing.
        BstState Spent(BstState s) => s with { OneWithNature = false, ReadyTempered = false, TemperedRecastRemaining = 50f, SinceTempered = 3f, SinceSummon = 14f };
        bool Held(BstDecision d) => d.Declines.Contains("crucible:hold-release-window");
        bool Attacks(BstDecision d) => d.ActionId != 0 && d.ActionId != BST_CrucibleLogic.Hold && !d.Reason.StartsWith("crucible:hold", StringComparison.Ordinal);

        // One pack wave end to end with the settings the plugin ships: no pack yet -> the release waits and the fight is attacked,
        // the pack arrives -> the release goes on it at once, the release spent -> the familiar leaves with Parting Blow.
        void PackWave(string id, int board, int battle, int beast, int pack, string piece)
        {
            var quiet = Decide(Wave(board, battle, beast), shipped);
            Check($"r4-options: {id} {piece}: shipped settings, no pack yet: the AoE release is held for the pack",
                Held(quiet) && quiet.ActionId != BST.TemperedRelease, $"{quiet.Reason} [{quiet.Declines}]");
            Check($"r4-options: {id} {piece}: ... and the held release does not stop the fight: an attack is pressed on that tick",
                Attacks(quiet), $"{quiet.ActionId} {quiet.Reason}");
            var burst = Decide(Wave(board, battle, beast, pack), shipped);
            Check($"r4-options: {id} {piece}: shipped settings, {pack} enemies up: the release goes on the pack",
                burst is { ActionId: BST.TemperedRelease, Reason: "crucible:pack-release" }, $"{burst.Reason} [{burst.Declines}]");
            var parting = Decide(Spent(Wave(board, battle, beast, pack)), shipped);
            Check($"r4-options: {id} {piece}: shipped settings, release gone off on {pack} enemies: Parting Blow the pack",
                parting is { ActionId: BST.PartingBlow, Reason: "crucible:pack-parting" }, $"{parting.Reason} [{parting.Declines}]");
            // The option still turns the behaviour off: the release is spent as before and the window that would have held it is only logged.
            Check($"r4-options: {id} {piece}: with the option unticked the release is spent as before and the window is logged (how to turn it off)",
                Decide(Wave(board, battle, beast), off) is { ActionId: BST.TemperedRelease, Reason: "own:tempered", Shadow: "crucible:pack-hold-off" });
        }

        // ---- the options are on in what the plugin ships ----
        Check("r4-options: rot-24 Crucible options: pack window, pre-pull horn and cycle exit are on in the shipped settings",
            shipped is { CruciblePackWindow: true, CruciblePrepullHorns: true, CrucibleCycleForDamage: true },
            $"pack {shipped.CruciblePackWindow} prepull {shipped.CruciblePrepullHorns} cycle {shipped.CrucibleCycleForDamage}");
        var cfgSrc = RepoFile("Combos", "PvE", "BST", "BST_Config.cs");
        Check("r4-options: rot-24 Crucible options: the settings window's own defaults are on too (BST_Config.cs literals; the harness default must equal them)",
            cfgSrc.Contains("BST_CruciblePackWindow\", true", StringComparison.Ordinal)
            && cfgSrc.Contains("BST_CrucibleCycleForDamage\", true", StringComparison.Ordinal)
            && cfgSrc.Contains("BST_CruciblePrepullHorns\", true", StringComparison.Ordinal));
        // A saved configuration that still holds the old default needs the one-time step (the default flip never reaches it).
        var mig = RepoFile("Core", "ConfigMigration.cs");
        var wiring = RepoFile("GluttonyCombo.cs");
        var cfgClass = RepoFile("Core", "Configuration.cs");
        Check("r4-options: rot-24 Crucible options: a config-version step switches a saved old default on (v11 in ConfigMigration, wired in GluttonyCombo.cs, Configuration.Version 11)",
            mig.Contains("version < 11", StringComparison.Ordinal) && mig.Contains("CurrentVersion = 11", StringComparison.Ordinal)
            && cfgClass.Contains("Version { get; set; } = 11", StringComparison.Ordinal)
            && new[] { "BST_CruciblePackWindow", "BST_CrucibleCycleForDamage", "BST_CruciblePrepullHorns" }.All(k => wiring.Contains(k, StringComparison.Ordinal)));
        Check("r4-options: rot-24 Crucible options: Simple Mode runs the shipped settings, so the flip reaches the one-button rotation",
            RepoFile("Combos", "PvE", "BST", "BST.cs").Contains("WithCrucible(BST_RotationLogic.BstSettings.Defaults(aoe))", StringComparison.Ordinal));

        // ---- the add-pack battles (PackBattles; (5,6) added) ----
        Check("r4-options: 5.6-08 Rooks: board 5 battle 6 is a pack battle (Summon 49269 / Aether Charge 49271)", BST_CrucibleData.PackBattles.Contains((5, 6)));
        Check("r4-options: 5.6-08 Rooks: the other twelve pack battles are unchanged",
            new[] { (1, 0), (1, 4), (1, 5), (2, 2), (2, 4), (3, 1), (3, 6), (4, 0), (4, 8), (5, 4), (5, 5), (5, 9) }.All(BST_CrucibleData.PackBattles.Contains)
            && BST_CrucibleData.PackBattles.Count == 13, $"{BST_CrucibleData.PackBattles.Count}");

        PackWave("1.5-04", 1, 5, 34, 3, "Burning Ward wisps");
        PackWave("2.4-04", 2, 4, 26, 4, "Zombie Pieces");
        PackWave("3.1-01b", 3, 1, 34, 4, "Bone Bishop summons");
        PackWave("3.6-09a", 3, 6, 34, 4, "Summon 48567 sirens");
        PackWave("5.4-03", 5, 4, 34, 3, "Red Gremlins");
        PackWave("5.6-08", 5, 6, 34, 3, "Rooks");
        // The tail of the wave: a Parting Blow with no hold in front of it (the release already spent), pressed on the pack only.
        foreach (var (id, board, battle, pack, piece) in new[] { ("5.5-09", 5, 5, 3, "Barbmoles"), ("5.9-05", 5, 9, 3, "Sphinx riddle adds") })
        {
            var parting = Decide(Spent(Wave(board, battle, 34, pack)), shipped);
            Check($"r4-options: {id} {piece}: shipped settings, release gone off on {pack} enemies: Parting Blow the pack",
                parting is { ActionId: BST.PartingBlow, Reason: "crucible:pack-parting" }, $"{parting.Reason} [{parting.Declines}]");
            Check($"r4-options: {id} {piece}: ... with the option unticked nothing is pressed and the open window is logged",
                Decide(Spent(Wave(board, battle, 34, pack)), off) is { Shadow: "crucible:pack-parting-off" } o && o.ActionId != BST.PartingBlow);
            Check($"r4-options: {id} {piece}: ... never next to a protected egg or morpho, and never on a lone target",
                Decide(Spent(Wave(board, battle, 34, pack)) with { ProtectedNearTarget = true }, shipped).Reason != "crucible:pack-parting"
                && Decide(Spent(Wave(board, battle, 34, 1)), shipped).Reason != "crucible:pack-parting");
        }
        Check("r4-options: 5.5-09 Barbmoles: with the shipped settings the Morpho case still holds the AoE (no release next to a protected enemy)",
            Decide(Wave(5, 5, 34, 4) with { ProtectedNearTarget = true }, shipped).ActionId != BST.TemperedRelease);
        Check("r4-options: rot-20 pack Parting Blow (synthetic, no run): on the shipped settings a pack battle ends the wave with Parting Blow, not an idle exit-hold",
            Decide(Spent(Wave(3, 1, 34, 4)), shipped) is { ActionId: BST.PartingBlow, Reason: "crucible:pack-parting" });
        Check("r4-options: lv-07 Parting Blow as the exit: the pack exit is not gated on the AoE-preset option (AoePartingBlow stays off)",
            !shipped.AoePartingBlow && Decide(Spent(Wave(3, 6, 34, 4)), shipped).ActionId == BST.PartingBlow);

        // ---- the holds this creates are release holds, bounded, and never an idle fight ----
        // Sweep the whole 30 s window with every pack size: the tick is always an attack, the release waits only while the window is shut,
        // and the first tick of an open window releases at once. The longest the release can be held is the 30 s window (WindowHoldSeconds).
        var worstAttackGap = 0;
        var longestHeld = 0f;
        for (var sinceSummon = 1f; sinceSummon <= 35f; sinceSummon += 0.5f)
            foreach (var pack in new[] { 1, 2 })
            {
                var d = Decide(Wave(3, 6, 34, pack) with { SinceSummon = sinceSummon, SinceHornPress = sinceSummon + 1f }, shipped);
                if (!Attacks(d))
                    worstAttackGap++;
                if (Held(d))
                    longestHeld = Math.Max(longestHeld, sinceSummon);
            }
        Check("r4-options: 3.6-09a pack hold: over the whole 30 s window, with 1 and 2 enemies up, every tick presses an attack (no tick is a hold)", worstAttackGap == 0, $"{worstAttackGap} ticks without an attack");
        Check("r4-options: 3.6-09a pack hold: the release is held for at most the 30 s window after the summon, then spent as before", longestHeld < 30f && longestHeld > 0f
            && Decide(Wave(3, 6, 34) with { SinceSummon = 30.5f }, shipped) is { ActionId: BST.TemperedRelease, Reason: "own:tempered" }, $"longest held at {longestHeld} s");
        Check("r4-options: 3.6-09a pack hold: the pack arriving ends the hold on that tick, at any time inside the window",
            new[] { 1f, 10f, 20f, 29f }.All(t => Decide(Wave(3, 6, 34, 3) with { SinceSummon = t, SinceHornPress = t + 1f }, shipped) is { ActionId: BST.TemperedRelease, Reason: "crucible:pack-release" }));
        Check("r4-options: 3.6-09a pack hold: its stated release conditions fail the hold at once (familiar under the swap line, target not attackable)",
            !Held(Decide(Wave(3, 6, 34) with { PetHpPercent = 40f }, shipped)) && !Held(Decide(Wave(3, 6, 34) with { TargetDoNotAttack = true }, shipped)));

        // ---- the pre-pull horn and Borrow ----
        var pre = CrucibleState() with
        {
            InCombat = false, ActiveSlot = 0, PetObjectPresent = false, SinceHornPress = 30f, HasHostileTarget = true,
            ReadyHorn1 = true, ReadyHorn2 = true, ReadyHorn3 = true,
            Slot1Beast = 1, Slot2Beast = 7, Slot3Beast = 4, CrucibleNeeds = CrucibleNeeds.Dispel,
        };
        var horn = Decide(pre, shipped);
        Check("r4-options: lv-13 pre-pull Borrow: shipped settings, out of combat, the fight needs a dispel, pugil on horn 3: its horn is blown before the pull",
            horn is { ActionId: BST.ThirdBattlehorn, Reason: "crucible:prepull-wavekin-slot3" }, $"{horn.Reason} [{horn.Declines}]");
        var out4 = pre with { ActiveSlot = 3, PetObjectPresent = true, PetObjectBeast = 4, OneWithNature = true, ReadyBorrow = true, SinceHornPress = 3f };
        Check("r4-options: lv-13 pre-pull Borrow: ... and with the pugil out the Borrow is pressed before the pull",
            Decide(out4, shipped) is { ActionId: BST.Borrow, Reason: "crucible:prepull-borrow-wavekin" });
        Check("r4-options: lv-13 pre-pull Borrow: with the option unticked no horn is blown out of combat (how to turn it off)",
            Decide(pre, off) is { } n && !IsHorn(n.ActionId) && n.Declines.Contains("crucible:no-horns-out-of-combat"));

        // ---- 4.5 Gargoyle: Beastskin before the pull, no fight need ----
        var gargoyle = pre with { CrucibleBoard = 4, CrucibleBattle = 5, CrucibleNeeds = CrucibleNeeds.None };
        var skinHorn = Decide(gargoyle, shipped);
        Check("r4-options: 4.5-panel-beastskin Gargoyle: shipped settings, out of combat, no ability need, Cu Sith (Beastkin) on horn 1: its horn is blown before the pull",
            skinHorn is { ActionId: BST.FirstBattlehorn, Reason: "crucible:prepull-beastkin-slot1" }, $"{skinHorn.Reason} [{skinHorn.Declines}]");
        var cuSith = gargoyle with { ActiveSlot = 1, PetObjectPresent = true, PetObjectBeast = 1, OneWithNature = true, ReadyBorrow = true, SinceHornPress = 3f };
        Check("r4-options: 4.5-panel-beastskin Gargoyle: ... and with the Cu Sith out the Borrow is pressed, so Beastskin is ready when the fight starts",
            Decide(cuSith, shipped) is { ActionId: BST.Borrow, Reason: "crucible:prepull-borrow-beastkin" }, $"{Decide(cuSith, shipped).Reason}");
        Check("r4-options: 4.5-panel-beastskin Gargoyle: Beastskin already held: nothing more is borrowed",
            Decide(cuSith with { OneWithNature = false, KinshipHeld = true, KinshipSlot = 1, BeastModeResolved = BST.Beastskin }, shipped).Reason != "crucible:prepull-borrow-beastkin");
        Check("r4-options: 4.5-panel-beastskin Gargoyle: no Beastkin on a horn: nothing is blown for it",
            Decide(gargoyle with { Slot1Beast = 7, Slot2Beast = 7, Slot3Beast = 7 }, shipped).Reason != "crucible:prepull-beastkin-slot1");
        Check("r4-options: 4.5-panel-beastskin Gargoyle: the Beastskin setting off: no horn or Borrow is spent on it",
            Decide(gargoyle, shipped with { UseBeastskin = false }).Reason.StartsWith("crucible:prepull-beastkin", StringComparison.Ordinal) is false
            && Decide(cuSith, shipped with { UseBeastskin = false }).Reason != "crucible:prepull-borrow-beastkin");
        Check("r4-options: 4.5-panel-beastskin Gargoyle: any other no-need battle (board 4 battle 4) is left alone",
            Decide(gargoyle with { CrucibleBattle = 4 }, shipped).Reason.StartsWith("crucible:prepull", StringComparison.Ordinal) is false);
        Check("r4-options: 4.5-panel-beastskin Gargoyle: with the pre-pull option unticked the Gargoyle gets no horn out of combat",
            Decide(gargoyle, off) is { } g && !IsHorn(g.ActionId));
        Check("r4-options: 4.5-panel-beastskin Gargoyle: an answer the fight needs still goes first (a dispel need beats Beastskin)",
            Decide(gargoyle with { CrucibleNeeds = CrucibleNeeds.Dispel }, shipped).Reason == "crucible:prepull-wavekin-slot3");
        Check("r4-options: 4.5-panel-beastskin Gargoyle: in the fight the Cu Sith's release goes to Borrow for Beastskin, then the skin is pressed",
            Decide(Wave(4, 5, 1) with { ReadyBorrow = true }, shipped) is { ActionId: BST.Borrow, Reason: "crucible:borrow-beastkin" });

        // ---- the cycle exit ----
        var steady = CrucibleState() with { TargetHpPercent = 60f, HighestEnemyHpPercent = 60f };
        Check("r4-options: rot-24 cycle exit: shipped settings, a healthy fight, spare horns: the familiar leaves with Parting Blow (exit:partingblow)",
            Decide(steady, shipped) is { ActionId: BST.PartingBlow, Reason: "exit:partingblow" }, $"{Decide(steady, shipped).Reason} [{Decide(steady, shipped).Declines}]");
        Check("r4-options: rot-24 cycle exit: ... but not with only critical familiars behind it (the resummon guard) and not when the round is ending",
            Decide(steady with { Slot2PetHp = 20f, Slot3PetHp = 20f }, shipped).ActionId != BST.PartingBlow
            && Decide(steady with { TargetHpPercent = 8f, HighestEnemyHpPercent = 8f }, shipped).ActionId != BST.PartingBlow);
        Check("r4-options: rot-24 cycle exit: with the option unticked a familiar leaves only when its HP says so (exit-hold; how to turn it off)",
            Decide(steady, off) is { ActionId: not BST.PartingBlow } hold && hold.Declines.Contains("crucible:exit-hold"));
    }
}
