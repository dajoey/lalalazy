// RotationHarness RDM (SC11 round 7): offline harness for real Red Mage rotation decisions.
//
// WHAT IS REAL HERE: the Red Mage job source itself. RDM.cs and RDM_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke handler, the gauge
// properties, the action/buff tables and the opener tables are the exact shipping code. Everything
// the decisions READ (cooldowns, statuses, gauge bytes, target, presets, settings) comes from the
// harness fakes instead of the game.
//
// THIS COMMIT first carried only characterization cases that pass on the UNCHANGED source (plus the
// canary); the RDM-2 improvement commit added its failing-first cases here (RDM-2*).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.RDM -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.RDM\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.RDM.dll

using Dalamud.Game.ClientState.JobGauge.Types;
using GluttonyCombo.Combos;
using GluttonyCombo.Combos.PvE;
// The harness namespace ends in .RDM, so bare `RDM` would resolve to the namespace; alias the job class.
using Rdm = GluttonyCombo.Combos.PvE.RDM;

namespace GluttonyCombo.RotationHarness.RDM;

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
    // references Dalamud types (RDMGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: RDM_ST_DPS / RDM_AoE_DPS, offline --");

        // Evidence: the gauge fake is a REAL RDMGauge over harness memory; show the probed layout.
        var gauge = FakeGauges.Get<RDMGauge>();
        Console.WriteLine("gauge probe: RDMGauge over harness memory, byte offsets = " +
                          string.Join(", ", FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(RDMGauge))
                              .Select(kv => $"{kv.Key.Prop}=@{kv.Value}")));
        Console.WriteLine($"RDMGauge.BlackMana reads {gauge.BlackMana} (fresh, expected 0)");

        // ---- CH1 (characterization, current behaviour): Manafication fires inside the Embolden window ----
        // Weave window open, Manafication ready, Embolden on cooldown with 3s left (inside the
        // EmboldenCD <= 5 gate), no Embolden buff up. Today the hold gate passes, so Invoke(Jolt)
        // returns Manafication. True before the RDM-2 change (whatever the option state).
        SetManaficationState(emboldenSecondsLeft: 3f);
        uint got = new Rdm.RDM_ST_DPS().RunInvoke(Rdm.Jolt);
        Check("CH1 (unchanged behaviour): Manafication ready, Embolden 3s from ready: Invoke(Jolt) returns " +
              $"Manafication ({Rdm.Manafication})", got == Rdm.Manafication, $"returned {got}");

        // ---- the CANARY: deliberately asserts the opposite; must FAIL ----
        SetManaficationState(emboldenSecondsLeft: 3f);
        uint got2 = new Rdm.RDM_ST_DPS().RunInvoke(Rdm.Jolt);
        CheckCanary($"CANARY (expected to FAIL): identical to CH1, asserting Invoke(Jolt) " +
                    $"does NOT return Manafication", got2 != Rdm.Manafication, $"returned {got2}");

        // ---- CH2 (characterization, current behaviour): outside the Embolden window it holds ----
        // Same state but Embolden has 10s left on cooldown (outside the <= 5 window) and no Embolden
        // buff: today Manafication is held for Embolden, so Invoke(Jolt) falls through to the Jolt
        // filler. This is exactly the behaviour RDM-2's opt-in option will make switchable.
        SetManaficationState(emboldenSecondsLeft: 10f);
        uint got3 = new Rdm.RDM_ST_DPS().RunInvoke(Rdm.Jolt);
        Check("CH2 (unchanged behaviour): Manafication ready, Embolden 10s from ready: Invoke(Jolt) keeps " +
              $"the Jolt filler ({Rdm.Jolt}), Manafication is held", got3 == Rdm.Jolt, $"returned {got3}");

        // ---- CH3 (characterization, current behaviour): the AoE twin fires inside the window ----
        SetManaficationState(emboldenSecondsLeft: 3f, aoe: true);
        uint got4 = new Rdm.RDM_AoE_DPS().RunInvoke(Rdm.Scatter);
        Check("CH3 (unchanged behaviour): AoE, Manafication ready, Embolden 3s from ready: Invoke(Scatter) " +
              $"returns Manafication ({Rdm.Manafication})", got4 == Rdm.Manafication, $"returned {got4}");

        // ---- CH4 (characterization, current behaviour): the AoE twin also holds outside the window ----
        SetManaficationState(emboldenSecondsLeft: 10f, aoe: true);
        uint got5 = new Rdm.RDM_AoE_DPS().RunInvoke(Rdm.Scatter);
        Check("CH4 (unchanged behaviour): AoE, Manafication ready, Embolden 10s from ready: " +
              $"Invoke(Scatter) keeps the Scatter filler ({Rdm.Scatter})", got5 == Rdm.Scatter, $"returned {got5}");

        // ---- RDM-2 (round 7): opt-in Manafication on cooldown instead of held for Embolden ----
        // Balance guide (R5): "The 110s cooldown on Manafication often causes confusion on whether it
        // should be held for Embolden or used on cooldown, but in fights with unknown killtimes,
        // using Manafication on cooldown is often the better choice from a risk vs reward perspective."
        // State: Manafication ready, Embolden 10s from ready (outside the <= 5 window), no Embolden buff,
        // the new option ON. Invoke(Jolt) must return Manafication instead of holding it.
        // (Fails on the pre-RDM-2 source: the EmboldenCD <= 5 gate holds it.)
        SetManaficationState(emboldenSecondsLeft: 10f, onCooldown: true);
        uint got6 = new Rdm.RDM_ST_DPS().RunInvoke(Rdm.Jolt);
        Check($"RDM-2: Manafication ready, Embolden 10s away, option on: Invoke(Jolt) returns " +
              $"Manafication ({Rdm.Manafication}), not the Jolt filler",
            got6 == Rdm.Manafication, $"returned {got6}");

        // Paired unchanged behaviour: option OFF (the default), identical state - Manafication stays held
        // for Embolden exactly as today. True before and after the change.
        SetManaficationState(emboldenSecondsLeft: 10f);
        uint got7 = new Rdm.RDM_ST_DPS().RunInvoke(Rdm.Jolt);
        Check($"RDM-2-unchanged: option off, same state: Invoke(Jolt) keeps the Jolt filler ({Rdm.Jolt}), " +
              $"Manafication stays held for Embolden", got7 == Rdm.Jolt, $"returned {got7}");

        // Paired unchanged behaviour: option on, Embolden 3s away - inside the window Manafication fires
        // with or without the option; guards the gate rewrite against losing the window entirely.
        SetManaficationState(emboldenSecondsLeft: 3f, onCooldown: true);
        uint got8 = new Rdm.RDM_ST_DPS().RunInvoke(Rdm.Jolt);
        Check($"RDM-2-EmboldenWindow: option on, Embolden 3s away: Invoke(Jolt) still returns " +
              $"Manafication ({Rdm.Manafication})", got8 == Rdm.Manafication, $"returned {got8}");

        // The AoE twin of the same change.
        SetManaficationState(emboldenSecondsLeft: 10f, aoe: true, onCooldown: true);
        uint got9 = new Rdm.RDM_AoE_DPS().RunInvoke(Rdm.Scatter);
        Check($"RDM-2-AoE: AoE, Manafication ready, Embolden 10s away, option on: Invoke(Scatter) returns " +
              $"Manafication ({Rdm.Manafication}), not the Scatter filler",
            got9 == Rdm.Manafication, $"returned {got9}");

        SetManaficationState(emboldenSecondsLeft: 10f, aoe: true);
        uint got10 = new Rdm.RDM_AoE_DPS().RunInvoke(Rdm.Scatter);
        Check($"RDM-2-AoE-unchanged: AoE, option off, same state: Invoke(Scatter) keeps the Scatter filler " +
              $"({Rdm.Scatter}), Manafication stays held", got10 == Rdm.Scatter, $"returned {got10}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The Manafication decision state: weave window open, battle target present, Manafication
    ///     learned and ready, no Embolden buff, Embolden on cooldown with the given seconds left.
    ///     Single-target by default (RDM_ST_DPS invoked over Jolt); the AoE twin invokes RDM_AoE_DPS
    ///     over Scatter. All other oGCD sub-presets stay off so nothing outranks Manafication.
    ///     onCooldown switches on the RDM-2 opt-in option (RDM_ST/AoE_Manafication_OnCooldown).
    /// </summary>
    private static void SetManaficationState(float emboldenSecondsLeft, bool aoe = false, bool onCooldown = false)
    {
        FakeGame.Reset();
        FakeGame.CanWeave = true;
        FakeGame.EnabledPresets.Add(aoe ? Preset.RDM_AoE_Manafication : Preset.RDM_ST_Manafication);
        if (onCooldown)
            FakeGame.BoolValues[aoe ? "RDM_AoE_Manafication_OnCooldown" : "RDM_ST_Manafication_OnCooldown"] = true;

        var embolden = FakeGame.Cooldown(Rdm.Embolden);
        embolden.IsCooldown = true;
        embolden.CooldownRemaining = emboldenSecondsLeft;
        embolden.CooldownElapsed = Math.Max(0f, 120f - emboldenSecondsLeft);
    }

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
    // the Dalamud dev folder the way the plugin build does (same pattern as GluttonyCombo.HookTeardownHarness).
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
