// RotationHarness SGE (2026-10-04, SC11 round 7): offline cases for the real Sage rotation.
//
// WHAT IS REAL HERE: the Sage job source itself. SGE.cs and SGE_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the Eukrasian
// Dosis refresh gate, the Phlegma charge rules, the movement fillers, the heal priority logic, the
// opener step tables and the action/buff tables are the exact shipping code. Everything the
// decisions READ (cooldowns, charges, statuses, gauge bytes, target, presets, settings, the
// party-burst flag, the DoT's remaining time on the target) comes from the harness fakes instead
// of the game. Cases for this round:
//   CHAR   — characterization of today's behaviour on the two decisions the round-7 rows modify:
//            (a) the Eukrasian Dosis refresh: at/below the configured remaining threshold the ST
//                rotation returns Eukrasia (the DoT is refreshed); above it, Dosis.
//            (b) the Phlegma charge rule with Burst off and pool 0: the first charge fires
//                immediately (today's "dump on cooldown" behaviour the SGE-1 row modifies).
//   SGE-1  — opt-in Phlegma party-burst mode (added in its own commit with the change).
//   SGE-2  — opt-in early Eukrasian Dosis refresh while the party is bursting (own commit).
//   OOC    — the 1.0.4.278 out-of-combat wiring: every damage-path weave (Addersgall overcap
//            protect, Rhizomata, the Eukrasia chains) and the raidwide answer stand down while
//            not in combat; each case pairs an in-combat control with the same state ooc.
//
// THE CANARY: the Phlegma characterization state run through an assertion of the OPPOSITE
// behaviour. It is EXPECTED TO FAIL; the harness only exits 0 when the canary fails as expected
// (proof the test can actually fail).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.SGE -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.SGE\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.GluttonyCombo.RotationHarness.SGE.dll

using Dalamud.Game.ClientState.JobGauge.Types;
using GluttonyCombo.Combos.PvE;
using SgeJob = GluttonyCombo.Combos.PvE.SGE;
using GluttonyCombo.Combos;

namespace GluttonyCombo.RotationHarness.SGE;

internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        AddDalamudResolver();
        return Run();
    }

    // Kept out of Main: Main is JIT-compiled before the resolver below can be registered, and its body
    // references Dalamud types (SGEGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: SGE_ST_Advanced_DPS decisions, offline --");

        // Evidence: the gauge fake is a REAL SGEGauge over harness memory; show the probed layout.
        _ = FakeGauges.Get<SGEGauge>();
        Console.WriteLine("gauge probe: SGEGauge over harness memory, probed props = " + string.Join(", ",
            FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(SGEGauge))
                .Select(kv => $"{kv.Key.Prop}@{kv.Value}")));

        // ---- CHAR: at/below the threshold the DoT is refreshed (today's behaviour, must keep passing) ----
        SetStDpsState(3f, phlegmaCharges: 1);
        uint gotDotLow = InvokeSt();
        Check("CHAR: boss encounter + EDosis uptime on + threshold 4s + DoT 3s left: " +
              $"Invoke(Dosis3) returns Eukrasia ({SgeJob.Eukrasia}) — the DoT is refreshed at the threshold",
            gotDotLow == SgeJob.Eukrasia, $"returned {gotDotLow}");

        // ---- CHAR: above the threshold there is no early refresh today ----
        SetStDpsState(8f, phlegmaCharges: 1, phlegmaUnavailable: true);
        uint gotDotHigh = InvokeSt();
        Check("CHAR: same state but DoT 8s left (threshold 4s): " +
              $"Invoke(Dosis3) returns Dosis ({SgeJob.Dosis3}) — today the DoT is NOT refreshed early",
            gotDotHigh == SgeJob.Dosis3, $"returned {gotDotHigh}");

        // ---- CHAR: with Burst off and pool 0, the first Phlegma charge fires immediately ----
        SetStDpsState(30f, phlegmaCharges: 1);
        uint gotPhlegma = InvokeSt();
        Check("CHAR: DoT 30s left + Phlegma 1 of 2 charges + Burst off + pool 0: " +
              $"Invoke(Dosis3) returns Phlegma ({SgeJob.Phlegma3}) — today the first charge is dumped at once",
            gotPhlegma == SgeJob.Phlegma3, $"returned {gotPhlegma}");

        // ---- SGE-1: opt-in Phlegma party-burst mode (config SGE_ST_Adv_DPS_Phlegma_PartyBurst, default off) ----
        // While ON, outside the caller's own burst rule Phlegma is only spent while the party is
        // bursting; capped charges still dump (the cap rule returns before the gate).
        SetStDpsState(30f, phlegmaCharges: 1);
        FakeGame.BoolValues["SGE_ST_DPS_Phlegma_PartyBurst"] = true;
        FakeGame.PartyIsBurstingFlag = true;
        uint gotSge1Burst = InvokeSt();
        Check("SGE-1: option ON + party bursting + Phlegma 1 of 2 charges: " +
              $"Invoke(Dosis3) returns Phlegma ({SgeJob.Phlegma3}) - the mode still spends Phlegma in burst",
            gotSge1Burst == SgeJob.Phlegma3, $"returned {gotSge1Burst}");

        SetStDpsState(30f, phlegmaCharges: 1);
        FakeGame.BoolValues["SGE_ST_DPS_Phlegma_PartyBurst"] = true;
        FakeGame.PartyIsBurstingFlag = false;
        uint gotSge1Hold = InvokeSt();
        Check("SGE-1: option ON + NOT bursting + Phlegma 1 of 2 charges: " +
              $"Invoke(Dosis3) returns Dosis ({SgeJob.Dosis3}) - Phlegma is held for the burst window",
            gotSge1Hold == SgeJob.Dosis3, $"returned {gotSge1Hold}");

        SetStDpsState(30f, phlegmaCharges: 2);
        FakeGame.BoolValues["SGE_ST_DPS_Phlegma_PartyBurst"] = true;
        FakeGame.PartyIsBurstingFlag = false;
        uint gotSge1Capped = InvokeSt();
        Check("SGE-1: option ON + NOT bursting + Phlegma 2 of 2 charges (capped): " +
              $"Invoke(Dosis3) returns Phlegma ({SgeJob.Phlegma3}) - capped charges still dump",
            gotSge1Capped == SgeJob.Phlegma3, $"returned {gotSge1Capped}");

        SetStDpsState(30f, phlegmaCharges: 1);
        FakeGame.BoolValues["SGE_ST_DPS_Phlegma_PartyBurst"] = false;
        FakeGame.PartyIsBurstingFlag = false;
        uint gotSge1Off = InvokeSt();
        Check("SGE-1: option OFF (default) + NOT bursting + Phlegma 1 of 2 charges: " +
              $"Invoke(Dosis3) returns Phlegma ({SgeJob.Phlegma3}) - default-off keeps today's behaviour",
            gotSge1Off == SgeJob.Phlegma3, $"returned {gotSge1Off}");

        // ---- SGE-2: opt-in early Eukrasian Dosis refresh while the party is bursting ----
        // (config SGE_ST_Adv_DPS_EukrasianDosisUptime_BurstRefresh, default off; window 20s)
        SetStDpsState(8f, phlegmaCharges: 1, phlegmaUnavailable: true);
        FakeGame.BoolValues["SGE_ST_DPS_EukrasianDosisUptime_BurstRefresh"] = true;
        FakeGame.PartyIsBurstingFlag = true;
        uint gotSge2Burst = InvokeSt();
        Check("SGE-2: option ON + party bursting + DoT 8s left (threshold 4s): " +
              $"Invoke(Dosis3) returns Eukrasia ({SgeJob.Eukrasia}) - the DoT is refreshed early to cover the burst window",
            gotSge2Burst == SgeJob.Eukrasia, $"returned {gotSge2Burst}");

        SetStDpsState(8f, phlegmaCharges: 1, phlegmaUnavailable: true);
        FakeGame.BoolValues["SGE_ST_DPS_EukrasianDosisUptime_BurstRefresh"] = false;
        FakeGame.PartyIsBurstingFlag = true;
        uint gotSge2Off = InvokeSt();
        Check("SGE-2: option OFF (default) + party bursting + DoT 8s left: " +
              $"Invoke(Dosis3) returns Dosis ({SgeJob.Dosis3}) - default-off keeps today's behaviour",
            gotSge2Off == SgeJob.Dosis3, $"returned {gotSge2Off}");

        SetStDpsState(8f, phlegmaCharges: 1, phlegmaUnavailable: true);
        FakeGame.BoolValues["SGE_ST_DPS_EukrasianDosisUptime_BurstRefresh"] = true;
        FakeGame.PartyIsBurstingFlag = false;
        uint gotSge2NoBurst = InvokeSt();
        Check("SGE-2: option ON + NOT bursting + DoT 8s left: " +
              $"Invoke(Dosis3) returns Dosis ({SgeJob.Dosis3}) - the widened window only applies in burst",
            gotSge2NoBurst == SgeJob.Dosis3, $"returned {gotSge2NoBurst}");

        // ==== OOC (1.0.4.278 wiring): out of combat the damage paths do not react ====
        // The 2026-10-04 report's out-of-combat fires, re-derived per cast from the harvested
        // logs (action_events for the player's own character, distinct timestamps, no in-combat
        // sample within 12s before / 2s after): Druochole x2 - the Addersgall overcap
        // protection named in the report - plus Eukrasia x4 and Eukrasian Dosis III x2 (the
        // Eukrasia chain). Every one of the player's own raidwide-shield casts sat inside a
        // combat window (22 Eukrasian Prognosis II casts, 0 out of combat). Each case below
        // pairs an in-combat control (the weave fires) with the same state out of combat.

        // ---- OOC-1: advanced ST - the Addersgall overcap protection waits for combat ----
        SetStDpsState(30f, phlegmaCharges: 1, phlegmaUnavailable: true);
        FakeGame.CanWeave = true;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 3);
        FakeGame.EnabledPresets.Add(Preset.SGE_ST_Adv_DPS_AddersgallProtect);
        uint gotOoc1Control = InvokeSt();
        Check("OOC-1 control: in combat + weave window + Addersgall 3 of 3 + protect preset on (threshold 3): " +
              $"Invoke(Dosis3) returns Druochole ({SgeJob.Druochole}) - the overcap protect fires",
            gotOoc1Control == SgeJob.Druochole, $"returned {gotOoc1Control}");

        SetStDpsState(30f, phlegmaCharges: 1, phlegmaUnavailable: true);
        FakeGame.CanWeave = true;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 3);
        FakeGame.EnabledPresets.Add(Preset.SGE_ST_Adv_DPS_AddersgallProtect);
        FakeGame.InCombat = false;
        FakeGame.PartyInCombatFlag = false;
        uint gotOoc1 = InvokeSt();
        Check("OOC-1: identical state but OUT OF COMBAT: " +
              $"Invoke(Dosis3) returns Dosis ({SgeJob.Dosis3}) - no overcap dump, no self-target",
            gotOoc1 == SgeJob.Dosis3, $"returned {gotOoc1}");

        // ---- OOC-2: advanced ST - Rhizomata (the gauge top-up) waits for combat ----
        SetStDpsState(30f, phlegmaCharges: 1, phlegmaUnavailable: true);
        FakeGame.CanWeave = true;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 0);
        FakeGame.EnabledPresets.Add(Preset.SGE_ST_Adv_DPS_Rhizo);
        uint gotOoc2Control = InvokeSt();
        Check("OOC-2 control: in combat + weave window + Addersgall 0 + Rhizo preset on (threshold 1): " +
              $"Invoke(Dosis3) returns Rhizomata ({SgeJob.Rhizomata})",
            gotOoc2Control == SgeJob.Rhizomata, $"returned {gotOoc2Control}");

        SetStDpsState(30f, phlegmaCharges: 1, phlegmaUnavailable: true);
        FakeGame.CanWeave = true;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 0);
        FakeGame.EnabledPresets.Add(Preset.SGE_ST_Adv_DPS_Rhizo);
        FakeGame.InCombat = false;
        FakeGame.PartyInCombatFlag = false;
        uint gotOoc2 = InvokeSt();
        Check("OOC-2: identical state but OUT OF COMBAT: " +
              $"Invoke(Dosis3) returns Dosis ({SgeJob.Dosis3}) - no Rhizomata top-up",
            gotOoc2 == SgeJob.Dosis3, $"returned {gotOoc2}");

        // ---- OOC-3: simple ST - the ungated simple weave block waits for combat ----
        FakeGame.Reset();
        FakeGame.EnabledPresets.Add(Preset.SGE_ST_Simple_DPS);
        FakeGame.CanWeave = true;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 3);
        FakeGame.HookOverrides[SgeJob.Dosis] = SgeJob.Dosis3;
        uint gotOoc3Control = InvokeSimpleSt();
        Check("OOC-3 control: in combat + weave window + Addersgall 3 (simple mode presses the protect on its own): " +
              $"Invoke(Dosis3) returns Druochole ({SgeJob.Druochole})",
            gotOoc3Control == SgeJob.Druochole, $"returned {gotOoc3Control}");

        FakeGame.Reset();
        FakeGame.EnabledPresets.Add(Preset.SGE_ST_Simple_DPS);
        FakeGame.CanWeave = true;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 3);
        FakeGame.HookOverrides[SgeJob.Dosis] = SgeJob.Dosis3;
        FakeGame.InCombat = false;
        FakeGame.PartyInCombatFlag = false;
        uint gotOoc3 = InvokeSimpleSt();
        Check("OOC-3: identical state but OUT OF COMBAT: " +
              $"Invoke(Dosis3) returns Dosis ({SgeJob.Dosis3})",
            gotOoc3 == SgeJob.Dosis3, $"returned {gotOoc3}");

        // ---- OOC-4: advanced AoE - SGE_AoE_Adv_DPS_AddersgallProtect (the preset of the report) ----
        FakeGame.Reset();
        FakeGame.EnabledPresets.Add(Preset.SGE_AoE_Advanced_DPS);
        FakeGame.EnabledPresets.Add(Preset.SGE_AoE_Adv_DPS_AddersgallProtect);
        FakeGame.CanWeave = true;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 3);
        FakeGame.HasBattleTarget = false;
        uint gotOoc4Control = InvokeAoeAdv();
        Check("OOC-4 control: in combat + weave window + Addersgall 3 + AoE protect preset on: " +
              $"Invoke(Dyskrasia2) returns Druochole ({SgeJob.Druochole})",
            gotOoc4Control == SgeJob.Druochole, $"returned {gotOoc4Control}");

        FakeGame.Reset();
        FakeGame.EnabledPresets.Add(Preset.SGE_AoE_Advanced_DPS);
        FakeGame.EnabledPresets.Add(Preset.SGE_AoE_Adv_DPS_AddersgallProtect);
        FakeGame.CanWeave = true;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 3);
        FakeGame.HasBattleTarget = false;
        FakeGame.InCombat = false;
        FakeGame.PartyInCombatFlag = false;
        uint gotOoc4 = InvokeAoeAdv();
        Check("OOC-4: identical state but OUT OF COMBAT: " +
              $"Invoke(Dyskrasia2) returns the plain AoE GCD ({SgeJob.Dyskrasia}) - no overcap dump",
            gotOoc4 == SgeJob.Dyskrasia, $"returned {gotOoc4}");

        // ---- OOC-5: UseRaidwide - incoming-damage detection is combat-only at the boundary ----
        // (the shipped Action.cs gate pins this rule in GroupDamageIncoming/RaidwideCasting; the
        // harness fake delegates to the same OutOfCombatGate.MayDetectIncomingDamage so the
        // rotation's raidwide answer is exercised on both sides of the combat line)
        SetStDpsState(30f, phlegmaCharges: 1, phlegmaUnavailable: true);
        FakeGame.CanWeave = true;
        FakeGame.GroupDamageIncoming = true;
        GluttonyCombo.AutoRotation.AutoRotationController.RaidwideMitOnCooldown = false;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 3);
        FakeGame.EnabledPresets.Add(Preset.SGE_Raidwide_Kerachole);
        uint gotOoc5Control = InvokeSt();
        Check("OOC-5 control: in combat + a raidwide cast up + Kerachole raidwide preset on: " +
              $"Invoke(Dosis3) returns Kerachole ({SgeJob.Kerachole}) - the mit answers the cast",
            gotOoc5Control == SgeJob.Kerachole, $"returned {gotOoc5Control}");

        SetStDpsState(30f, phlegmaCharges: 1, phlegmaUnavailable: true);
        FakeGame.CanWeave = true;
        FakeGame.GroupDamageIncoming = true;
        GluttonyCombo.AutoRotation.AutoRotationController.RaidwideMitOnCooldown = false;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 3);
        FakeGame.EnabledPresets.Add(Preset.SGE_Raidwide_Kerachole);
        FakeGame.InCombat = false;
        FakeGame.PartyInCombatFlag = false;
        uint gotOoc5 = InvokeSt();
        Check("OOC-5: identical state (raidwide cast up) but OUT OF COMBAT: " +
              $"Invoke(Dosis3) returns Dosis ({SgeJob.Dosis3}) - no hostile cast counts ooc",
            gotOoc5 == SgeJob.Dosis3, $"returned {gotOoc5}");

        // ---- OOC-6: simple ST - the Eukrasia chain does not open out of combat ----
        FakeGame.Reset();
        FakeGame.EnabledPresets.Add(Preset.SGE_ST_Simple_DPS);
        FakeGame.CanWeave = false;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 1);
        FakeGame.HookOverrides[SgeJob.Dosis] = SgeJob.Dosis3;
        FakeGame.TargetCanApplyStatus = true;
        FakeGame.DottableEnemyPresent = true;
        uint gotOoc6Control = InvokeSimpleSt();
        Check("OOC-6 control: party in combat + a dottable enemy + DoT below the refresh window: " +
              $"Invoke(Dosis3) returns Eukrasia ({SgeJob.Eukrasia}) - the chain opens",
            gotOoc6Control == SgeJob.Eukrasia, $"returned {gotOoc6Control}");

        FakeGame.Reset();
        FakeGame.EnabledPresets.Add(Preset.SGE_ST_Simple_DPS);
        FakeGame.CanWeave = false;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 1);
        FakeGame.HookOverrides[SgeJob.Dosis] = SgeJob.Dosis3;
        FakeGame.TargetCanApplyStatus = true;
        FakeGame.DottableEnemyPresent = true;
        FakeGame.InCombat = false;
        FakeGame.PartyInCombatFlag = false;
        uint gotOoc6 = InvokeSimpleSt();
        Check("OOC-6: identical state but OUT OF COMBAT: " +
              $"Invoke(Dosis3) returns Dosis ({SgeJob.Dosis3}) - no Eukrasia chain at field mobs",
            gotOoc6 == SgeJob.Dosis3, $"returned {gotOoc6}");

        // ---- OOC-7: simple AoE - the Eukrasian Dyskrasia chain does not open out of combat ----
        FakeGame.Reset();
        FakeGame.EnabledPresets.Add(Preset.SGE_AoE_Simple_DPS);
        FakeGame.CanWeave = true;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 1);
        FakeGame.EnemiesInRangeCount = 4;
        FakeGame.HasBattleTarget = false;
        uint gotOoc7Control = InvokeAoeSimple();
        Check("OOC-7 control: party in combat + 4 enemies in range without the DoTs: " +
              $"Invoke(Dyskrasia2) returns Eukrasia ({SgeJob.Eukrasia}) - the AoE chain opens",
            gotOoc7Control == SgeJob.Eukrasia, $"returned {gotOoc7Control}");

        FakeGame.Reset();
        FakeGame.EnabledPresets.Add(Preset.SGE_AoE_Simple_DPS);
        FakeGame.CanWeave = true;
        FakeGauges.SetByte<SGEGauge>("Addersgall", 1);
        FakeGame.EnemiesInRangeCount = 4;
        FakeGame.HasBattleTarget = false;
        FakeGame.InCombat = false;
        FakeGame.PartyInCombatFlag = false;
        uint gotOoc7 = InvokeAoeSimple();
        Check("OOC-7: identical state but OUT OF COMBAT: " +
              $"Invoke(Dyskrasia2) returns the plain AoE GCD ({SgeJob.Dyskrasia}) - no Eukrasia chain",
            gotOoc7 == SgeJob.Dyskrasia, $"returned {gotOoc7}");

        // ---- the CANARY: deliberately asserts the opposite; must FAIL ----
        SetStDpsState(30f, phlegmaCharges: 1);
        uint gotCanary = InvokeSt();
        CheckCanary($"CANARY (expected to FAIL): identical CHAR state, asserting Invoke(Dosis3) " +
                    $"does NOT return Phlegma", gotCanary != SgeJob.Phlegma3, $"returned {gotCanary}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The shared state: the live SGE_ST_Advanced_DPS configuration (EDosis and Phlegma sub-options
    ///     on; Kardia, Opener, Psyche, Lucid, Rhizo, Soteria, AddersgallProtect, Movement off), level
    ///     100, in combat with the party, on a boss target, standing still, weave window closed (the
    ///     oGCD block is not under test), Eukrasia ready — so the GCD block's EDosis-then-Phlegma
    ///     decisions are what <see cref="InvokeSt"/> returns. The Eukrasian Dosis debuff's remaining
    ///     time on the target and the Phlegma charge count are set per case.
    /// </summary>
    private static void SetStDpsState(float dotRemainingSeconds, uint phlegmaCharges, bool phlegmaUnavailable = false)
    {
        FakeGame.Reset();

        foreach (var p in new[]
                 {
                     Preset.SGE_ST_Advanced_DPS,
                     Preset.SGE_ST_Adv_DPS_EDosis,
                     Preset.SGE_ST_Adv_DPS_Phlegma,
                 })
            FakeGame.EnabledPresets.Add(p);
        // NOT enabled: SGE_ST_Adv_DPS_Kardia, _Opener, _Psyche, _Lucid, _Rhizo, _Soteria,
        // _AddersgallProtect, _Movement (the oGCD block is skipped anyway: CanWeave false).

        FakeGame.InCombat = true;              // the GCD block's gate
        FakeGame.PartyInCombatFlag = true;     // UseEDosis's non-simple gate
        FakeGame.CanWeave = false;             // skip the oGCD block and UseRaidwide's weave half
        FakeGame.HasBattleTarget = true;
        FakeGame.TargetCanApplyStatus = true;  // ShouldRefreshEDosis's gate
        FakeGame.TargetHPPercent = 100f;
        FakeGame.InBossEncounter = true;       // EDosisHpThreshold -> BossOption (default 0)
        FakeGame.TargetIsBoss = true;
        FakeGame.IsMovingFlag = false;
        FakeGame.TimeStoodStillSeconds = 5f;

        // A level-100 Sage's action bar: Dosis/Phlegma/Toxikon hook to their third ranks.
        FakeGame.HookOverrides[SgeJob.Dosis] = SgeJob.Dosis3;
        FakeGame.HookOverrides[SgeJob.Phlegma] = SgeJob.Phlegma3;
        FakeGame.HookOverrides[SgeJob.Toxikon] = SgeJob.Toxikon2;

        // Joey's live values for the two settings the round-7 rows modify: EDosis uptime threshold
        // 4.0s (shipped default 5.0) and Phlegma Burst off with pool 0 (Burst default is on).
        FakeGame.FloatValues["SGE_ST_DPS_EukrasianDosisUptime_Threshold"] = 4f;
        FakeGame.BoolValues["SGE_ST_DPS_Phlegma_Burst"] = false;
        FakeGame.IntValues["SGE_ST_DPS_Phlegma"] = 0;

        FakeGauges.SetByte<SGEGauge>("Addersgall", 1);  // below every addersgall gate, not empty
        FakeGauges.SetByte<SGEGauge>("Addersting", 0);

        // Phlegma III: two charges, the case's count remaining, off cooldown and in range unless the
        // case needs the Phlegma gate closed (then the fall-through Dosis is the expected result).
        var phlegma = FakeGame.Cooldown(SgeJob.Phlegma3);
        phlegma.MaxCharges = 2;
        phlegma.RemainingCharges = phlegmaCharges;
        phlegma.IsCooldown = false;
        if (phlegmaUnavailable)
            FakeGame.OutOfRangeActions.Add(SgeJob.Phlegma3);

        // The Eukrasian Dosis III debuff on the current target, with the case's remaining time.
        SgeJob.EukrasianDosisList.TryGetValue(SgeJob.Dosis3, out var dotDebuff);
        FakeGame.TargetStatuses[dotDebuff] = new FakeTargetStatus(dotDebuff, dotRemainingSeconds);
    }

    private static uint InvokeSt() => new SgeJob.SGE_ST_Advanced_DPS().RunInvoke(SgeJob.Dosis3);

    private static uint InvokeSimpleSt() => new SgeJob.SGE_ST_Simple_DPS().RunInvoke(SgeJob.Dosis3);

    private static uint InvokeAoeAdv() => new SgeJob.SGE_AoE_Advanced_DPS().RunInvoke(SgeJob.Dyskrasia2);

    private static uint InvokeAoeSimple() => new SgeJob.SGE_AoE_Simple_DPS().RunInvoke(SgeJob.Dyskrasia2);

    private static void Check(string desc, bool ok, string detail = "")
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine($"PASS {desc}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"FAIL {desc} [{detail}]");
        }
    }

    /// <summary>A canary asserts the OPPOSITE of the real behaviour and is required to fail.</summary>
    private static void CheckCanary(string desc, bool oppositeAssertion, string detail = "")
    {
        if (oppositeAssertion)
        {
            // the canary's wrong assertion unexpectedly held: the harness cannot detect regressions
            _fail++;
            Console.WriteLine($"FAIL {desc} — canary PASSED UNEXPECTEDLY, the harness is not actually " +
                              $"exercising this decision [{detail}]");
        }
        else
        {
            _pass++;
            Console.WriteLine($"FAIL {desc} — canary failed as expected [{detail}]");
        }
    }

    // The packaging keep-lists leave Dalamud's own assemblies out of the output folder; resolve them from
    // the Dalamud dev folder the way the plugin build does (same pattern as the VPR spike).
    private static void AddDalamudResolver()
    {
        var dir = Environment.GetEnvironmentVariable("DALAMUD_HOME")
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                      "XIVLauncher", "addon", "Hooks", "dev");
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var file = Path.Combine(dir, name.Name + ".dll");
            return File.Exists(file) ? ctx.LoadFromAssemblyPath(file) : null;
        };
    }
}
