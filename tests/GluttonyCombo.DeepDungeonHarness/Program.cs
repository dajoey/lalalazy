// DeepDungeonHarness (2026-10-18): offline harness for the real DeepDungeon potion gate and the
// PotD behemoth meteor-state predicate.
//
// WHAT IS REAL HERE: DeepDungeon.cs, DeepDungeon_Config.cs and DeepDungeon_Behemoth.cs are compiled
// into this console app UNCHANGED (see the csproj Compile Include links): the whole
// UseSustainingPotion gate, the Config defaults and the entire behemoth NameId/cast/HP predicate
// are the exact shipping code. Everything the decisions READ (target identity/cast/HP, player HP,
// statuses, presets, item readiness, config ints) comes from the harness fakes instead of the game.
// DeepDungeon_Helper.cs is NOT compiled; the harness supplies its consts/enum half (see Fakes.cs).
//
// THE CASES (PotD behemoth gap-close round, tasks-20261018-potd-gluttony-gap-close-01):
//   PRED-*    : BehemothMeteorWindow/BehemothMeteorHold against the Fight Baseline numbers —
//               Floor 80 Gudanna (NameId 5333, Ecliptic Meteor 7099, threshold 10%),
//               Floor 180 Dendainsonne (NameId 5461, Ecliptic Meteor 7166, threshold 15%).
//   POTION-*  : the heal-window close — during the meteor window the PoTD potion rolls regardless
//               of the own-HP slider (still gated on the preset, item readiness, no
//               Rehabilitation); outside the window today's HP-threshold behaviour is unchanged.
//               The window case is the FAILING-FIRST case: it fails against the pre-close source.
//
// THE CANARY: an assertion of the OPPOSITE behaviour, EXPECTED TO FAIL; the harness only exits 0
// when every real case passes AND the canary fails as expected (proof the harness can fail).
//
//   dotnet build tests/GluttonyCombo.DeepDungeonHarness -c Release
//   dotnet tests/GluttonyCombo.DeepDungeonHarness/bin/Release/net10.0-windows7.0/GluttonyCombo.DeepDungeonHarness.dll

using GluttonyCombo.Combos;
using GluttonyCombo.Combos.PvE.Content.DeepDungeons;
using static GluttonyCombo.DeepDungeonHarness.FakeGame;

namespace GluttonyCombo.DeepDungeonHarness;

internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        AddDalamudResolver();
        return Run();
    }

    // Kept out of Main: Main is JIT-compiled before the resolver below can be registered, and its
    // body references Dalamud types, so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo DeepDungeon harness: behemoth meteor window + sustaining potion, offline --");

        // ---- PRED: the meteor-window predicate (Floor 80 Gudanna: 5333 / 7099 / 10%) ----
        Reset();
        SetBehemoth(DeepDungeons.GudannaNameId, DeepDungeons.GudannaEclipticMeteor, 50f);
        Check("PRED-GUDANNA-CAST: Gudanna targeted with Ecliptic Meteor (7099) on the bar at 50% HP: window OPEN",
            DeepDungeons.BehemothMeteorWindow(), $"window={DeepDungeons.BehemothMeteorWindow()}");

        Reset();
        SetBehemoth(DeepDungeons.GudannaNameId, 0, 9.5f);
        Check("PRED-GUDANNA-THRESHOLD: Gudanna at 9.5% HP, no cast: window OPEN (at/below 10% the cast fires)",
            DeepDungeons.BehemothMeteorWindow(), $"window={DeepDungeons.BehemothMeteorWindow()}");

        Reset();
        SetBehemoth(DeepDungeons.GudannaNameId, 0, 10.5f);
        Check("PRED-GUDANNA-OUT: Gudanna at 10.5% HP, no cast: window CLOSED",
            !DeepDungeons.BehemothMeteorWindow(), $"window={DeepDungeons.BehemothMeteorWindow()}");

        Reset();
        SetBehemoth(DeepDungeons.GudannaNameId, 0, 14f);
        Check("PRED-GUDANNA-HOLD: Gudanna at 14% HP (band 10-17), no cast: HOLD on, window closed",
            DeepDungeons.BehemothMeteorHold() && !DeepDungeons.BehemothMeteorWindow(),
            $"hold={DeepDungeons.BehemothMeteorHold()}, window={DeepDungeons.BehemothMeteorWindow()}");

        Reset();
        SetBehemoth(DeepDungeons.GudannaNameId, 0, 50f);
        Check("PRED-GUDANNA-HOLD-OUT: Gudanna at 50% HP: no hold, no window",
            !DeepDungeons.BehemothMeteorHold() && !DeepDungeons.BehemothMeteorWindow(),
            $"hold={DeepDungeons.BehemothMeteorHold()}, window={DeepDungeons.BehemothMeteorWindow()}");

        // ---- PRED: Floor 180 Dendainsonne (5461 / 7166 / 15%) ----
        Reset();
        SetBehemoth(DeepDungeons.DendainsonneNameId, DeepDungeons.DendainsonneEclipticMeteor, 50f);
        Check("PRED-DENDAI-CAST: Dendainsonne targeted with Ecliptic Meteor (7166) on the bar at 50% HP: window OPEN",
            DeepDungeons.BehemothMeteorWindow(), $"window={DeepDungeons.BehemothMeteorWindow()}");

        Reset();
        SetBehemoth(DeepDungeons.DendainsonneNameId, 0, 15f);
        Check("PRED-DENDAI-THRESHOLD: Dendainsonne at exactly 15.0% HP, no cast: window OPEN (loops from here)",
            DeepDungeons.BehemothMeteorWindow(), $"window={DeepDungeons.BehemothMeteorWindow()}");

        Reset();
        SetBehemoth(DeepDungeons.DendainsonneNameId, 0, 15.5f);
        Check("PRED-DENDAI-OUT: Dendainsonne at 15.5% HP, no cast: window CLOSED",
            !DeepDungeons.BehemothMeteorWindow(), $"window={DeepDungeons.BehemothMeteorWindow()}");

        Reset();
        SetBehemoth(DeepDungeons.DendainsonneNameId, 0, 20f);
        Check("PRED-DENDAI-HOLD: Dendainsonne at 20% HP (band 15-22), no cast: HOLD on, window closed",
            DeepDungeons.BehemothMeteorHold() && !DeepDungeons.BehemothMeteorWindow(),
            $"hold={DeepDungeons.BehemothMeteorHold()}, window={DeepDungeons.BehemothMeteorWindow()}");

        Reset();
        SetBehemoth(DeepDungeons.DendainsonneNameId, 6497, 50f);
        Check("PRED-DENDAI-ATTACK: Dendainsonne mid-Attack (6497) at 50% HP: no window, no hold",
            !DeepDungeons.BehemothMeteorWindow() && !DeepDungeons.BehemothMeteorHold(),
            $"window={DeepDungeons.BehemothMeteorWindow()}, hold={DeepDungeons.BehemothMeteorHold()}");

        // ---- PRED: identity discipline — never by cast id alone, never for other bosses ----
        Reset();
        SetBehemoth(400, DeepDungeons.GudannaEclipticMeteor, 50f);
        Check("PRED-OTHER-BOSS: a non-behemoth target (NameId 400) casting action 7099: window CLOSED (cast id alone never matches)",
            !DeepDungeons.BehemothMeteorWindow(), $"window={DeepDungeons.BehemothMeteorWindow()}");

        Reset();
        TargetPresent = false;
        Check("PRED-NO-TARGET: no target: window and hold both CLOSED",
            !DeepDungeons.BehemothMeteorWindow() && !DeepDungeons.BehemothMeteorHold(),
            $"window={DeepDungeons.BehemothMeteorWindow()}, hold={DeepDungeons.BehemothMeteorHold()}");

        // ---- POTION: the heal-window close (FAILING FIRST against the pre-close source) ----
        Reset();
        SetBehemoth(DeepDungeons.DendainsonneNameId, DeepDungeons.DendainsonneEclipticMeteor, 50f);
        EnabledPresets.Add(Preset.PoTD_SustainingPotion);
        ReadyItems.Add(DeepDungeons.PoTDSustainingPotion);
        PlayerHPPercent = 90f;
        var potionOk = DeepDungeons.UseSustainingPotion(out var potionId);
        Check("POTION-WINDOW-90: Dendainsonne mid-meteor, player at 90% HP (slider 50): potion ROLLS regardless of the slider",
            potionOk && potionId == DeepDungeons.PoTDSustainingPotion,
            $"rolled={potionOk}, potionId={potionId}");

        Reset();
        SetBehemoth(DeepDungeons.DendainsonneNameId, 0, 50f);
        EnabledPresets.Add(Preset.PoTD_SustainingPotion);
        ReadyItems.Add(DeepDungeons.PoTDSustainingPotion);
        PlayerHPPercent = 90f;
        Check("POTION-NO-WINDOW-90: same state but window closed (no cast, 50% HP): potion does NOT roll above the slider",
            !DeepDungeons.UseSustainingPotion(out _), "rolled=true");

        Reset();
        SetBehemoth(DeepDungeons.GudannaNameId, DeepDungeons.GudannaEclipticMeteor, 50f);
        EnabledPresets.Add(Preset.PoTD_SustainingPotion);
        ReadyItems.Add(DeepDungeons.PoTDSustainingPotion);
        PlayerHPPercent = 90f;
        Check("POTION-WINDOW-GUDANNA: Gudanna mid-meteor at player 90% HP: potion rolls (Floor 80's single 20s meteor counts)",
            DeepDungeons.UseSustainingPotion(out _), "rolled=false");

        Reset();
        EnabledPresets.Add(Preset.PoTD_SustainingPotion);
        ReadyItems.Add(DeepDungeons.PoTDSustainingPotion);
        PlayerHPPercent = 50f;
        Check("POTION-THRESHOLD-UNCHANGED: no behemoth, player at exactly the default 50% slider: potion rolls (today's behaviour preserved)",
            DeepDungeons.UseSustainingPotion(out _), "rolled=false");

        Reset();
        SetBehemoth(DeepDungeons.DendainsonneNameId, DeepDungeons.DendainsonneEclipticMeteor, 50f);
        EnabledPresets.Add(Preset.PoTD_SustainingPotion);
        ReadyItems.Add(DeepDungeons.PoTDSustainingPotion);
        PlayerHPPercent = 90f;
        Statuses.Add(DeepDungeons.Buffs.Rehabilitation);
        Check("POTION-WINDOW-REHAB: mid-meteor but Rehabilitation (648) on the player: potion does NOT roll (the gate holds inside the window)",
            !DeepDungeons.UseSustainingPotion(out _), "rolled=true");

        Reset();
        ReadyItems.Add(DeepDungeons.PoTDSustainingPotion);
        PlayerHPPercent = 50f;
        Check("POTION-PRESET-OFF: PoTD_SustainingPotion preset disabled at 50% HP: potion does NOT roll (the preset gate holds)",
            !DeepDungeons.UseSustainingPotion(out _), "rolled=true");

        Reset();
        EnabledPresets.Add(Preset.PoTD_SustainingPotion);
        PlayerHPPercent = 50f;
        Check("POTION-ITEM-NOT-READY: preset on, 50% HP, potion on cooldown: potion does NOT roll (the item gate holds)",
            !DeepDungeons.UseSustainingPotion(out _), "rolled=true");

        // ---- the CANARY: deliberately asserts the opposite; must FAIL ----
        Reset();
        SetBehemoth(DeepDungeons.DendainsonneNameId, DeepDungeons.DendainsonneEclipticMeteor, 50f);
        EnabledPresets.Add(Preset.PoTD_SustainingPotion);
        ReadyItems.Add(DeepDungeons.PoTDSustainingPotion);
        PlayerHPPercent = 90f;
        CheckCanary("CANARY (expected to FAIL): mid-meteor at 90% HP, asserting the potion does NOT roll",
            !DeepDungeons.UseSustainingPotion(out _), "the opposite assertion held");

        Console.WriteLine($"-- {_pass} passed, {_fail} failed (canary failures are expected and counted as passes) --");
        if (_fail == 0)
            Console.WriteLine("OK");
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(string desc, bool ok, string detail = "")
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine($"PASS {desc} [{detail}]");
        }
        else
        {
            _fail++;
            Console.WriteLine($"FAIL {desc} [{detail}]");
        }
    }

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
    // the Dalamud dev folder the way the plugin build does (same pattern as the RotationHarnesses).
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
