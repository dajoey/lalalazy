// RotationHarness SCH (2026-10-04, SC11 round 7): offline cases for the real Scholar rotation.
//
// WHAT IS REAL HERE: the Scholar job source itself. SCH.cs and SCH_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the NeedsDoT DoT
// checker, the raidwide gates, the fairy summon logic, the opener plumbing and the action/buff tables
// are the exact shipping code. Everything the decisions READ (cooldowns, statuses, gauge bytes, target,
// presets, settings, GCD count, pet presence) comes from the harness fakes instead of the game.
//
// THE CASES (round 7, improvement SCH-2 from the ranked list, plus characterizations of today):
//   CHAR   — Biolysis 3.0 s remaining (== the uptime threshold 3.0): the normal refresh fires.
//   CHAR   — Biolysis 8.0 s remaining (> the uptime threshold): today the filler Broil IV is returned,
//            NOT an early refresh. This is the exact state SCH-2 will flip.
//   SCH-2  — opt-in burst refresh ON + the party bursting + Biolysis 8 s left: returns Biolysis.
//   SCH-2 (paired, unchanged) — option OFF, identical state: today's Broil IV result.
//   SCH-2 (guard) — option ON but the party NOT bursting, identical state: today's Broil IV result.
//
// THE CANARY: the CHAR state run through an assertion of the OPPOSITE behaviour. It is EXPECTED TO FAIL;
// the harness only exits 0 when the canary fails as expected (proof the test can actually fail).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.SCH -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.SCH\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.SCH.dll

using Dalamud.Game.ClientState.JobGauge.Types;
using GluttonyCombo.Combos.PvE;
using GluttonyCombo.Combos;

namespace GluttonyCombo.RotationHarness;

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
    // references Dalamud types (SCHGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: SCH_ST_ADV_DPS, offline --");

        // Evidence: the gauge fake is a REAL SCHGauge over harness memory; show the probed layout.
        _ = FakeGauges.Get<SCHGauge>();
        Console.WriteLine("gauge probe: SCHGauge over harness memory, probed props = " + string.Join(", ",
            FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(SCHGauge))
                .Select(kv => $"{kv.Key.Prop}@{kv.Value}")));

        // ---- CHAR: today's threshold refresh (must keep passing) ----
        SetStAdvDpsState();
        SetBiolysisRemaining(3.0f); // == SCH_ST_DPS_BioUptime_Threshold default (3.0)
        uint gotChar = InvokeSt();
        Check("CHAR: Biolysis 3.0 s left (== uptime threshold 3.0) + ADV Bio on: " +
              $"Invoke(Broil IV) returns Biolysis ({SCH.Biolysis})",
            gotChar == SCH.Biolysis, $"returned {gotChar}");

        // ---- CHAR: today's NO early refresh (this is the state SCH-2 will flip) ----
        SetStAdvDpsState();
        SetBiolysisRemaining(8.0f); // > threshold: nothing refreshes yet
        uint gotChar2 = InvokeSt();
        Check("CHAR: Biolysis 8.0 s left (> uptime threshold): " +
              $"Invoke(Broil IV) returns Broil IV ({SCH.Broil4}) — no early refresh today",
            gotChar2 == SCH.Broil4, $"returned {gotChar2}");

        // ---- SCH-2: opt-in early Biolysis refresh while the party is bursting ----
        SetStAdvDpsState();
        FakeGame.BoolValues["SCH_ST_ADV_DPS_Bio_BurstRefresh"] = true;
        FakeGame.PartyBursting = true;
        SetBiolysisRemaining(8.0f);
        uint got2 = InvokeSt();
        Check("SCH-2: burst refresh ON + party bursting + Biolysis 8.0 s left (> uptime threshold): " +
              $"Invoke(Broil IV) returns Biolysis ({SCH.Biolysis}) — the DoT is clipped into the burst window",
            got2 == SCH.Biolysis, $"returned {got2}");

        // ---- SCH-2 paired: option OFF keeps today's behaviour ----
        SetStAdvDpsState();
        FakeGame.BoolValues["SCH_ST_ADV_DPS_Bio_BurstRefresh"] = false;
        FakeGame.PartyBursting = true;
        SetBiolysisRemaining(8.0f);
        uint got2b = InvokeSt();
        Check("SCH-2 (paired, unchanged): burst refresh OFF + party bursting + Biolysis 8.0 s left: " +
              $"Invoke(Broil IV) returns Broil IV ({SCH.Broil4}) — off is exactly today",
            got2b == SCH.Broil4, $"returned {got2b}");

        // ---- SCH-2 guard: the option alone is not enough; the burst window must be live ----
        SetStAdvDpsState();
        FakeGame.BoolValues["SCH_ST_ADV_DPS_Bio_BurstRefresh"] = true;
        FakeGame.PartyBursting = false;
        SetBiolysisRemaining(8.0f);
        uint got2c = InvokeSt();
        Check("SCH-2 (guard): burst refresh ON but party NOT bursting + Biolysis 8.0 s left: " +
              $"Invoke(Broil IV) returns Broil IV ({SCH.Broil4}) — no burst window, no early refresh",
            got2c == SCH.Broil4, $"returned {got2c}");

        // ---- the CANARY: deliberately asserts the opposite; must FAIL ----
        SetStAdvDpsState();
        SetBiolysisRemaining(3.0f);
        uint gotCanary = InvokeSt();
        CheckCanary($"CANARY (expected to FAIL): identical CHAR state, asserting Invoke(Broil IV) " +
                    $"does NOT return Biolysis", gotCanary != SCH.Biolysis, $"returned {gotCanary}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The shared state: Joey's live SCH_ST_ADV_DPS configuration (Aetherflow, Baneful Impaction,
    ///     Chain Stratagem, Energy Drain, Bio, Ruin II movement, Fairy reminder, Lucid on; opener OFF),
    ///     level 100, in combat with the party, NOT weaving (the DoT decision is a GCD branch, so the
    ///     oGCD weave block — including the live-only Svc.Log.Debug line — stays closed offline), fairy
    ///     out, standing still, a living target at 100% HP that accepts the DoT, Bio hooked to Biolysis
    ///     and Ruin hooked to Broil IV (level-100 hook table), every cooldown ready.
    /// </summary>
    private static void SetStAdvDpsState()
    {
        FakeGame.Reset();

        foreach (var p in new[]
                 {
                     Preset.SCH_ST_ADV_DPS,
                     Preset.SCH_ST_ADV_DPS_Aetherflow, Preset.SCH_ST_ADV_DPS_BanefulImpact,
                     Preset.SCH_ST_ADV_DPS_ChainStrat, Preset.SCH_ST_ADV_DPS_EnergyDrain,
                     Preset.SCH_ST_ADV_DPS_Bio, Preset.SCH_ST_ADV_DPS_Ruin2Movement,
                     Preset.SCH_ST_ADV_DPS_FairyReminder, Preset.SCH_ST_ADV_DPS_Lucid,
                 })
            FakeGame.EnabledPresets.Add(p);
        // NOT enabled (his live setup): SCH_ST_ADV_DPS_Balance_Opener, SCH_ST_Simple_DPS.

        FakeGame.InCombat = true;
        FakeGame.PartyInCombatFlag = true;
        FakeGame.CanWeave = false;        // GCD branch: weave block (and the Svc.Log.Debug inside it) stays closed
        FakeGame.HasPetPresent = true;    // fairy out: NeedToSummon declines
        FakeGame.HasBattleTarget = true;
        FakeGame.TargetHPPercent = 100f;
        FakeGame.TargetCanApplyStatus = true;
        FakeGame.IsMovingFlag = false;    // Ruin2Movement branch stays closed
        FakeGame.NumberOfGcdsUsed = 10;   // past the Chain Stratagem 3-GCD gate

        // Level-100 action hooks: the Bio button is Biolysis, the Ruin button is Broil IV.
        FakeGame.HookOverrides[SCH.Bio] = SCH.Biolysis;
        FakeGame.HookOverrides[SCH.Ruin] = SCH.Broil4;
    }

    /// <summary>Puts the Biolysis debuff on the fake target with the given seconds remaining.</summary>
    private static void SetBiolysisRemaining(float seconds) =>
        FakeGame.TargetStatuses.Add(new(SCH.Debuffs.Biolysis, seconds));

    private static uint InvokeSt() => new SCH.SCH_ST_ADV_DPS().RunInvoke(SCH.Broil4);

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
