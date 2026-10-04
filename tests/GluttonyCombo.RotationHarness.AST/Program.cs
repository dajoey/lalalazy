// RotationHarness AST (2026-10-04, SC11 round 6): offline cases for the real Astrologian rotation.
//
// WHAT IS REAL HERE: the Astrologian job source itself. AST.cs and AST_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the card/Lord pool
// gates, the Divination gate, the opener plumbing and the action/buff tables are the exact shipping code.
// Everything the decisions READ (cooldowns, statuses, gauge bytes, target, presets, settings, GCD count)
// comes from the harness fakes instead of the game.
//
// THE CASES (round 6, improvements AST-1 and AST-3 from the ranked list):
//   AST-1  — off-boss with the Divination HP option at 100 ("Non bosses" mode), a DPS card held, card
//            pooling on and Divination ready but unable to fire (HP gate can never pass): the held card
//            must be PLAYED (Play I) instead of rotting while AstralDraw stays blocked.
//   AST-3  — without the opener preset, the FIRST Divination is held until 3 GCDs are used; with the
//            opener preset the old behaviour (standing still is enough) is unchanged.
//   CHAR   — a characterization case of today's Divination behaviour that must keep holding.
//
// THE CANARY: the CHAR state run through an assertion of the OPPOSITE behaviour. It is EXPECTED TO FAIL;
// the harness only exits 0 when the canary fails as expected (proof the test can actually fail).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.AST -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.AST\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.AST.dll

using Dalamud.Game.ClientState.JobGauge.Enums;
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
    // references Dalamud types (ASTGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: AST_ST_DPS, offline --");

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

        // ---- CHAR: characterization of today's Divination behaviour (must keep passing) ----
        SetBossFightState();
        FakeGame.NumberOfGcdsUsed = 10; // WaitGCDs => NumberOfGcdsUsed >= 10
        uint gotChar = InvokeSt();
        Check("CHAR: boss encounter + 10 GCDs used + DPS card held + Divination ready + standing still: " +
              $"Invoke(FallMalefic) returns Divination ({AST.Divination})",
            gotChar == AST.Divination, $"returned {gotChar}");

        // ---- AST-1: the pool must release when Divination can never fire (off-boss, HP option 100) ----
        SetBossFightState();
        FakeGame.InBossEncounter = false;          // trash: divHPThreshold = AST_ST_DPS_DivinationOption
        FakeGame.IntValues["AST_ST_DPS_DivinationOption"] = 100; // his live setting, "Non bosses" mode
        FakeGame.NumberOfGcdsUsed = 4;
        uint got1 = InvokeSt();
        Check("AST-1: NOT a boss encounter + Divination HP option 100 + DPS card held + card pooling on + " +
              $"no Divination buff (Divination cannot fire): Invoke(FallMalefic) returns Play I ({AST.Play1})",
            got1 == AST.Play1, $"returned {got1}");

        // ---- AST-1 paired: boss pooling behaviour must NOT change ----
        SetBossFightState();
        var divCd = FakeGame.Cooldown(AST.Divination);
        divCd.IsCooldown = true;                   // Divination down: pool holds until it is back
        divCd.CooldownRemaining = 60f;
        FakeGame.NumberOfGcdsUsed = 4;
        uint got1b = InvokeSt();
        Check("AST-1 (paired, unchanged): boss encounter + Divination on cooldown + DPS card held + " +
              $"card pooling on: Invoke(FallMalefic) still returns Malefic ({AST.Malefic}) — the pool " +
              "waits for Divination exactly as today",
            got1b == AST.Malefic, $"returned {got1b}");

        // ---- AST-3: first Divination held until 3 GCDs when the opener preset is off ----
        SetBossFightState();
        FakeAstCards.SetCard(0, CardType.None);    // cards played out; AstralDraw on cooldown below
        var drawCd = FakeGame.Cooldown(AST.AstralDraw);
        drawCd.IsCooldown = true;
        drawCd.CooldownRemaining = 20f;
        FakeGame.NumberOfGcdsUsed = 1;
        uint got3 = InvokeSt();
        Check("AST-3: opener preset OFF + 1 GCD used + standing still + Divination ready + no cards: " +
              $"Invoke(FallMalefic) returns Malefic ({AST.Malefic}) — the first Divination is held to 3 GCDs",
            got3 == AST.Malefic, $"returned {got3}");

        // ---- AST-3: at 3 GCDs the hold releases ----
        SetBossFightState();
        FakeAstCards.SetCard(0, CardType.None);
        drawCd = FakeGame.Cooldown(AST.AstralDraw);
        drawCd.IsCooldown = true;
        drawCd.CooldownRemaining = 20f;
        FakeGame.NumberOfGcdsUsed = 3;
        uint got3b = InvokeSt();
        Check("AST-3 at 3 GCDs: opener preset OFF + 3 GCDs used + standing still + Divination ready: " +
              $"Invoke(FallMalefic) returns Divination ({AST.Divination})",
            got3b == AST.Divination, $"returned {got3b}");

        // ---- AST-3 paired: opener users keep today's behaviour ----
        SetBossFightState();
        FakeAstCards.SetCard(0, CardType.None);
        drawCd = FakeGame.Cooldown(AST.AstralDraw);
        drawCd.IsCooldown = true;
        drawCd.CooldownRemaining = 20f;
        FakeGame.EnabledPresets.Add(Preset.AST_ST_DPS_Opener);
        FakeGame.NumberOfGcdsUsed = 1;
        uint got3c = InvokeSt();
        Check("AST-3 (paired, unchanged): opener preset ON + 1 GCD used + standing still + Divination " +
              $"ready: Invoke(FallMalefic) returns Divination ({AST.Divination}) — opener timing owns the pull",
            got3c == AST.Divination, $"returned {got3c}");

        // ---- the CANARY: deliberately asserts the opposite; must FAIL ----
        SetBossFightState();
        FakeGame.NumberOfGcdsUsed = 10;
        uint gotCanary = InvokeSt();
        CheckCanary($"CANARY (expected to FAIL): identical CHAR state, asserting Invoke(FallMalefic) " +
                    $"does NOT return Divination", gotCanary != AST.Divination, $"returned {gotCanary}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The shared state: Joey's live AST_ST_DPS configuration (every sub-option on except Opener and
    ///     LightSpeedHold), level 100, in combat, weave window open, standing still (StandStill true), a
    ///     Balance held and the crown empty, Divination ready, Lightspeed and EarthlyStar spent (so the
    ///     burst/star branches cannot preempt the decision under test), in a boss encounter.
    /// </summary>
    private static void SetBossFightState()
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
        // NOT enabled (his live setup): AST_ST_DPS_Opener, AST_DPS_LightSpeedHold, AST_Cards_QuickTargetCards.

        FakeGame.InBossEncounter = true;
        FakeGame.InCombat = true;
        FakeGame.CanWeave = true;
        FakeGame.IsMovingFlag = false;
        FakeGame.TimeStoodStillSeconds = 5f;       // StandStill => stood still >= 3s
        FakeGame.TargetHPPercent = 100f;
        FakeGame.NumberOfGcdsUsed = 0;

        FakeAstCards.SetCard(0, CardType.Balance); // DPS card held
        FakeAstCards.SetCard(1, CardType.None);
        FakeAstCards.SetCard(2, CardType.None);
        FakeAstCards.SetCrown(CardType.None);      // no Lord

        // Divination ready (off cooldown); Lightspeed and EarthlyStar spent so their branches stay quiet.
        var lightspeed = FakeGame.Cooldown(AST.Lightspeed);
        lightspeed.IsCooldown = true;
        lightspeed.CooldownRemaining = 30f;
        var star = FakeGame.Cooldown(AST.EarthlyStar);
        star.IsCooldown = true;
        star.CooldownRemaining = 40f;
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
