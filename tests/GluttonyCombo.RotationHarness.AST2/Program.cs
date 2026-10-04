// RotationHarness AST2 (2026-10-04, SC11 round 7): offline cases for the real Astrologian rotation.
//
// WHAT IS REAL HERE: the Astrologian job source itself. AST.cs and AST_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the card/Lord pool
// gates, the Divination gate, the opener plumbing and the action/buff tables are the exact shipping code.
// Everything the decisions READ (cooldowns, statuses, gauge bytes, target, presets, settings, GCD count,
// party-burst flag, the DoT's remaining time on the target) comes from the harness fakes instead of the
// game. This project is a round-7 copy of the round-6 RotationHarness.AST project (both live side by
// side until the harnesses are consolidated); its cases are the round-7 ones:
//   CHAR   — characterization of today's Combust refresh behaviour: at/below the configured remaining
//            threshold the ST rotation returns Combust, above it Malefic (the no-early-refresh rule).
//   AST-2  — opt-in early Combust refresh while the party is bursting: with the option ON and
//            Bursting.PartyIsBursting true, a Combust with 8s left (threshold 4) is refreshed early so
//            the DoT covers the ~20s buff window; option OFF, or not bursting, keeps today's behaviour.
//            Paired cases pin both unchanged sides: option OFF while bursting, and option ON while not
//            bursting, both keep returning Malefic above the threshold.
//
// THE CANARY: the CHAR refresh state run through an assertion of the OPPOSITE behaviour. It is EXPECTED
// TO FAIL; the harness only exits 0 when the canary fails as expected (proof the test can actually fail).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.AST2 -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.AST2\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.AST2.dll

using Dalamud.Game.ClientState.JobGauge.Enums;
using Dalamud.Game.ClientState.JobGauge.Types;
using GluttonyCombo.Combos.PvE;
using GluttonyCombo.Combos;

namespace GluttonyCombo.RotationHarness.AST2;

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
    // references Dalamud types (ASTGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: AST_ST_DPS Combust decisions, offline --");

        // Evidence: the gauge fake is a REAL ASTGauge over harness memory; show the probed layout.
        _ = FakeGauges.Get<ASTGauge>();
        _ = FakeAstCards.SlotMap;
        Console.WriteLine("gauge probe: ASTGauge over harness memory, DrawnCards nibbles = " +
                          string.Join(", ", FakeAstCards.SlotMap.OrderBy(kv => kv.Key)
                              .Select(kv => $"[{kv.Key}]@{kv.Value.Offset}+{kv.Value.Shift}")) +
                          ", crown nibble = " +
                          $"@{FakeAstCards.Crown.Offset}+{FakeAstCards.Crown.Shift}, probed props = " + string.Join(", ",
                              FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(ASTGauge))
                                  .Select(kv => $"{kv.Key.Prop}@{kv.Value}")));

        // ---- CHAR: at/below the threshold the DoT is refreshed (today's behaviour, must keep passing) ----
        SetCombustState(3f);
        uint gotChar = InvokeSt();
        Check("CHAR: boss encounter + Combust uptime on + threshold 4s + DoT 3s left: " +
              $"Invoke(FallMalefic) returns Combust ({AST.Combust})",
            gotChar == AST.Combust, $"returned {gotChar}");

        // ---- CHAR: above the threshold there is no early refresh today ----
        SetCombustState(8f);
        uint gotChar2 = InvokeSt();
        Check("CHAR: same state but DoT 8s left (threshold 4s): " +
              $"Invoke(FallMalefic) returns Malefic ({AST.Malefic}) — today the DoT is NOT refreshed early",
            gotChar2 == AST.Malefic, $"returned {gotChar2}");

        // ---- AST-2: opt-in early Combust refresh while the party is bursting ----
        SetCombustState(8f);
        FakeGame.BoolValues["AST_ST_DPS_CombustUptime_BurstRefresh"] = true; // the new opt-in option
        FakeGame.PartyIsBurstingFlag = true;
        uint got2 = InvokeSt();
        Check("AST-2: option ON + party bursting + threshold 4s + DoT 8s left: " +
              $"Invoke(FallMalefic) returns Combust ({AST.Combust}) — the DoT is refreshed early so it " +
              "covers the ~20s buff window",
            got2 == AST.Combust, $"returned {got2}");

        // ---- AST-2 paired (option OFF keeps today's behaviour) ----
        SetCombustState(8f);
        FakeGame.BoolValues["AST_ST_DPS_CombustUptime_BurstRefresh"] = false;
        FakeGame.PartyIsBurstingFlag = true;
        uint got2b = InvokeSt();
        Check("AST-2 (paired, unchanged): option OFF + party bursting + DoT 8s left (threshold 4s): " +
              $"Invoke(FallMalefic) returns Malefic ({AST.Malefic}) — off is exactly today's behaviour",
            got2b == AST.Malefic, $"returned {got2b}");

        // ---- AST-2 paired (no burst, no early refresh even with the option ON) ----
        SetCombustState(8f);
        FakeGame.BoolValues["AST_ST_DPS_CombustUptime_BurstRefresh"] = true;
        FakeGame.PartyIsBurstingFlag = false;
        uint got2c = InvokeSt();
        Check("AST-2 (paired, unchanged): option ON + party NOT bursting + DoT 8s left (threshold 4s): " +
              $"Invoke(FallMalefic) returns Malefic ({AST.Malefic}) — outside a burst window nothing changes",
            got2c == AST.Malefic, $"returned {got2c}");

        // ---- the CANARY: deliberately asserts the opposite; must FAIL ----
        SetCombustState(3f);
        uint gotCanary = InvokeSt();
        CheckCanary($"CANARY (expected to FAIL): identical CHAR state, asserting Invoke(FallMalefic) " +
                    $"does NOT return Combust", gotCanary != AST.Combust, $"returned {gotCanary}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The shared state: the live AST_ST_DPS configuration (every sub-option on except Opener and
    ///     LightSpeedHold), level 100, in combat, weave window open, standing still, no cards or Lord,
    ///     Divination/AstralDraw/Lightspeed/EarthlyStar all unavailable, in a boss encounter — so the
    ///     GCD block's Combust decision is what <see cref="InvokeSt"/> returns. The Combust DoT's
    ///     remaining time on the target is set per case.
    /// </summary>
    private static void SetCombustState(float dotRemainingSeconds)
    {
        FakeGame.Reset();

        foreach (var p in new[]
                 {
                     Preset.AST_ST_DPS,
                     Preset.AST_DPS_AutoPlay, Preset.AST_DPS_CardPool, Preset.AST_DPS_LordPool,
                     Preset.AST_DPS_LazyLord, Preset.AST_DPS_AutoDraw,
                     Preset.AST_ST_DPS_CombustUptime, Preset.AST_ST_DPS_Move_DoT,
                     Preset.AST_DPS_LightSpeed, Preset.AST_DPS_LightspeedBurst,
                     Preset.AST_DPS_Divination, Preset.AST_DPS_Oracle, Preset.AST_DPS_Lucid,
                     Preset.AST_ST_DPS_EarthlyStar, Preset.AST_ST_DPS_StellarDetonation,
                 })
            FakeGame.EnabledPresets.Add(p);
        // NOT enabled: AST_ST_DPS_Opener, AST_DPS_LightSpeedHold, AST_ST_Simple_DPS, QuickTargetCards.

        FakeGame.InBossEncounter = true;
        FakeGame.TargetIsBoss = true;
        FakeGame.InCombat = true;
        FakeGame.CanWeave = true;
        FakeGame.IsMovingFlag = false;
        FakeGame.TimeStoodStillSeconds = 5f;       // StandStill => stood still >= 3s
        FakeGame.TargetHPPercent = 100f;
        FakeGame.NumberOfGcdsUsed = 4;             // WaitGCDs false
        FakeGame.TargetCanApplyStatus = true;      // NeedsDoT's CanApplyStatus gate open
        FakeGame.FloatValues["AST_ST_DPS_CombustUptime_Threshold"] = 4f; // the live slider value

        FakeAstCards.SetCard(0, CardType.None);    // no cards: AutoPlay quiet, AstralDraw blocked below
        FakeAstCards.SetCard(1, CardType.None);
        FakeAstCards.SetCard(2, CardType.None);
        FakeAstCards.SetCrown(CardType.None);      // no Lord

        // Every earlier branch that StandStill/ready actions could satisfy is made unavailable, so the
        // Combust decision is reached: Divination and AstralDraw on cooldown, Lightspeed and EarthlyStar
        // spent (SetBossFightState's pattern from the round-6 harness).
        var div = FakeGame.Cooldown(AST.Divination);
        div.IsCooldown = true;
        div.CooldownRemaining = 60f;
        var draw = FakeGame.Cooldown(AST.AstralDraw);
        draw.IsCooldown = true;
        draw.CooldownRemaining = 20f;
        var lightspeed = FakeGame.Cooldown(AST.Lightspeed);
        lightspeed.IsCooldown = true;
        lightspeed.CooldownRemaining = 30f;
        var star = FakeGame.Cooldown(AST.EarthlyStar);
        star.IsCooldown = true;
        star.CooldownRemaining = 40f;

        // The DoT debuff on the current target, with the case's remaining time.
        AST.CombustList.TryGetValue(AST.Combust, out var combustDebuff);
        FakeGame.TargetStatuses[combustDebuff] = new FakeTargetStatus(combustDebuff, dotRemainingSeconds);
    }

    private static uint InvokeSt() => new AST.AST_ST_DPS().RunInvoke(AST.FallMalefic);

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
