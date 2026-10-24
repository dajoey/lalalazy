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
using GluttonyCombo.AutoRotation;
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

        // ==== RAIDWIDE (2026-10-19 report): the timed AoE HoT heal fires once per raidwide ====
        // The autorot timed lock (AutoRotationController, not compiled here) arms at raidwide
        // bar-detect and owns the scheduling; the combo's early RaidwideAspectedHelios feature must
        // stand down while the lock holds the timing (one cast, completing after the bar), and the
        // whole Aspected Helios/Helios Conjunction family must stand down while a committed timed
        // cast's own HoT is live (plain Helios is the substitute).

        SetRaidwideHealState();
        TimedRegenGate.Arm(TimeSpan.FromSeconds(15));   // the lock scheduled the cast off the live bar
        uint gotRwArmed = InvokeRaidwideHeal();
        Check("RAIDWIDE-ARMED: raidwide incoming + timed lock armed (bar in hand): " +
              $"Invoke(Helios) does NOT return the Aspected HoT ({AST.HeliosConjuction}) — the early " +
              "combo feature stands down; the lock's post-bar cast is the only one",
            gotRwArmed is not (AST.AspectedHelios or AST.HeliosConjuction), $"returned {gotRwArmed}");

        SetRaidwideHealState();
        uint gotRwFree = InvokeRaidwideHeal();
        Check("RAIDWIDE control: same state, no arm, no committed cast: " +
              $"Invoke(Helios) returns the Aspected HoT ({AST.HeliosConjuction}) — the raidwide feature still fires",
            gotRwFree == AST.HeliosConjuction, $"returned {gotRwFree}");

        SetRaidwideHealState(raidwideIncoming: false, partyHP: 40f);
        TimedRegenGate.MarkCast(TimeSpan.FromSeconds(17));  // an early committed cast; its HoT is live
        uint gotRwOwned = InvokeRaidwideHeal();
        Check("RAIDWIDE-OWNED: raidwide resolved + party hurt + a committed cast's HoT live: " +
              $"Invoke(Helios) returns plain Helios ({AST.Helios}) — the same HoT is not recast while it ticks",
            gotRwOwned == AST.Helios, $"returned {gotRwOwned}");

        SetRaidwideHealState(raidwideIncoming: false, partyHP: 40f);
        uint gotRwOwnedFree = InvokeRaidwideHeal();
        Check("RAIDWIDE control: same hurt party, no committed cast: " +
              $"Invoke(Helios) returns the Aspected HoT ({AST.HeliosConjuction}) — the HoT heal is available again",
            gotRwOwnedFree == AST.HeliosConjuction, $"returned {gotRwOwnedFree}");

        // ==== CLEANSE (2026-10-24 FATE report): Esuna only when the debuff is worth the cast, and once ====
        // Recorded scenario (CT 2026-10-24 13:29:56.507): solo FATE grind, the heal combo chose
        // Esuna (7568) 21 times in 3 minutes; every decision saw the mob's own Combust III
        // (status 1881), and the WHM twin-cast it at 0% HP on 10-20. Nothing gated severity,
        // danger, or a cast already in flight. The candidate here is the party member (the
        // friendliness fix lives in the stack/retarget code, outside this harness); the worth
        // decision under test is the shared CleanseGate the branch must consult.

        SetCleanseState([18u]); // Poison: moderate DoT, healer healthy at the recorded 69.8%
        uint gotCleanse = InvokeHeal();
        Check("CLEANSE control: party member carries Poison (moderate) + healer at 69.8%: " +
              $"Invoke(Benefic II) returns Esuna (7568)",
            gotCleanse == 7568, $"returned {gotCleanse}");

        SetCleanseState([64u]); // Vulnerability Up: falls off on its own, never worth the GCD
        uint gotTrivial = InvokeHeal();
        Check("CLEANSE-TRIVIAL: party member carries only Vulnerability Up (trivial): " +
              $"Invoke(Benefic II) does NOT return Esuna — attacking continues",
            gotTrivial != 7568, $"returned {gotTrivial}");

        SetCleanseState([18u]);
        CleanseGate.NoteSent(FakeCleanseAlly.Instance.GameObjectId, DateTime.Now); // the first cast is in flight
        uint gotInFlight = InvokeHeal();
        Check("CLEANSE-INFLIGHT: the same debuff already has a cleanse resolving (status list still lags): " +
              $"Invoke(Benefic II) does NOT return Esuna again — one debuff, one cast",
            gotInFlight != 7568, $"returned {gotInFlight}");

        SetCleanseState([18u], playerHp: 20f);
        uint gotHealFirst = InvokeHeal();
        Check("CLEANSE-HEALFIRST: Poison (moderate) + healer at 20% HP (danger): " +
              $"Invoke(Benefic II) does NOT return Esuna — heal first",
            gotHealFirst != 7568, $"returned {gotHealFirst}");

        SetCleanseState([14u]); // Heavy: movement lock, healthy healer
        uint gotHeavy = InvokeHeal();
        Check("CLEANSE control: party member Heavied (movement lock) + healer at 69.8%: " +
              $"Invoke(Benefic II) returns Esuna — cleansing it IS the dodge",
            gotHeavy == 7568, $"returned {gotHeavy}");

        SetCleanseState([14u], playerHp: 20f);
        uint gotHeavyDanger = InvokeHeal();
        Check("CLEANSE-HEAVY-DANGER: Heavy + healer at 20% HP (danger): " +
              $"Invoke(Benefic II) does NOT return Esuna — heal first even for a movement lock",
            gotHeavyDanger != 7568, $"returned {gotHeavyDanger}");

        SetCleanseState([64u], playerHp: 10f, doom: true);
        uint gotDoom = InvokeHeal();
        Check("CLEANSE-DOOM: cleansable doom + healer at 10% HP: " +
              $"Invoke(Benefic II) returns Esuna — doom kills outright, always worth the cast",
            gotDoom == 7568, $"returned {gotDoom}");

        // Gate units: the severity table and the in-flight window, direct.
        SetCleanseState([]);
        Check("CLEANSE-GATE: SeverityOf table pins (14 Heavy=High, 64 Vuln Up=Trivial, 18 Poison=Moderate, unlisted=Moderate)",
            CleanseGate.SeverityOf(14) == CleanseSeverity.High &&
            CleanseGate.SeverityOf(64) == CleanseSeverity.Trivial &&
            CleanseGate.SeverityOf(18) == CleanseSeverity.Moderate &&
            CleanseGate.SeverityOf(999999) == CleanseSeverity.Moderate,
            $"got {CleanseGate.SeverityOf(14)}/{CleanseGate.SeverityOf(64)}/{CleanseGate.SeverityOf(18)}/{CleanseGate.SeverityOf(999999)}");

        Check("CLEANSE-GATE: nothing dispellable on the candidate -> never cast",
            !CleanseGate.MayCleanse(1, [], false, 100f, 100f, DateTime.Now), "returned true");

        Check("CLEANSE-GATE: dead candidate -> never cast",
            !CleanseGate.MayCleanse(1, [18u], false, 0f, 100f, DateTime.Now), "returned true");

        var sentAt = DateTime.Now;
        CleanseGate.NoteSent(7, sentAt);
        Check("CLEANSE-GATE: in-flight window suppresses the same target, expires after it",
            CleanseGate.IsInFlight(7, sentAt.AddSeconds(CleanseGate.InFlightWindowSeconds - 0.5)) &&
            !CleanseGate.IsInFlight(7, sentAt.AddSeconds(CleanseGate.InFlightWindowSeconds + 0.5)) &&
            !CleanseGate.IsInFlight(8, sentAt), "window verdict wrong");

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

    /// <summary>The cleanse-case state: ST heal combo + its Esuna sub-preset, retargeting on,
    /// a party member carrying the given dispellable statuses, healer HP case-set.</summary>
    private static void SetCleanseState(uint[] statusIds, float playerHp = 69.8f, bool doom = false)
    {
        FakeGame.Reset();
        TimedRegenGate.Reset();
        CleanseGate.Reset();

        FakeGame.EnabledPresets.Add(Preset.AST_ST_Heals);
        FakeGame.EnabledPresets.Add(Preset.AST_ST_Heals_Esuna);
        FakeGame.RetargetingOn = true;
        FakeGame.CleanseTarget = FakeCleanseAlly.Instance;
        FakeGame.CleanseTargetStatusIds = statusIds;
        FakeGame.CleanseTargetDoom = doom;
        FakeGame.PlayerHPPercent = playerHp;
    }

    private static uint InvokeHeal() => new AST.AST_ST_Heals().RunInvoke(AST.Benefic2);

    /// <summary>
    ///     The raidwide heal state: AST_AoE_Heals with the Aspected sub-preset on and
    ///     AST_Raidwide_AspectedHelios on, level 100 (Helios Conjunction via the OriginalHook map),
    ///     in combat, weave window open, standing still, no Aspected HoT on the player, raidwide
    ///     detection and party average HP case-set. The TimedRegenGate state starts empty.
    /// </summary>
    private static void SetRaidwideHealState(bool raidwideIncoming = true, float partyHP = 100f)
    {
        FakeGame.Reset();
        TimedRegenGate.Reset();

        foreach (var p in new[]
                 {
                     Preset.AST_AoE_Heals, Preset.AST_AoE_Heals_Aspected, Preset.AST_Raidwide_AspectedHelios,
                 })
            FakeGame.EnabledPresets.Add(p);

        FakeGame.InCombat = true;
        FakeGame.PartyInCombatFlag = true;
        FakeGame.InBossEncounter = true;
        FakeGame.CanWeave = true;
        FakeGame.IsMovingFlag = false;
        FakeGame.TimeStoodStillSeconds = 5f;
        FakeGame.GroupDamageIncoming = raidwideIncoming;
        FakeGame.PartyAvgHP = partyHP;
        FakeGame.NumberOfGcdsUsed = 0;

        // level-100 hook: Aspected Helios -> Helios Conjunction
        FakeGame.HookOverrides[AST.AspectedHelios] = AST.HeliosConjuction;
    }

    private static uint InvokeRaidwideHeal() => new AST.AST_AoE_Heals().RunInvoke(AST.Helios);

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
