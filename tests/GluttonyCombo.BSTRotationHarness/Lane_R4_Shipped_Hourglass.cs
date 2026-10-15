using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

internal static partial class Program
{
    /// <summary>
    ///     The Final Hourglass needle hold (a0c56e308, 1.0.4.307; the build before it is 587d75b65). Breaking the King Ahriman piece's Final Hourglass (board 5 battle 13) makes the next
    ///     needle tick fire Death, a 23 y cone along the needle's heading (Doom 5421, 6 s, not cleansable, the character died every time it landed). The rotation broke it as a priority add
    ///     whatever the needle did. The fix holds the weaponskills while the hourglass is at 75 percent or less and a tick the break can precede points the cone at the character.
    ///     Two recorded roulettes: 2026-10-14 21:08:25 to 21:08:32 ET (the break fired east, at the character) and 21:09:38 to 21:09:47 ET (the character was 91 degrees off the real heading and
    ///     was not hit). The needle fields are set by name (<see cref="R4ShippedSet"/>), so this file runs on the build before the fix.
    /// </summary>
    private static void R4ShippedHourglass()
    {
        Console.WriteLine("-- r4-shipped: Final Hourglass needle hold --");
        var cfg = BstSettings.Defaults();
        const uint hourglass = 14689;
        const float east = 1.5708f, west = -1.5708f, north = -3.1416f;
        // Roulette 1: the needle at (119.98,-420.00), the character at (134.05,-420.30) beside the hourglass at (133,-420). Needle turns: 27.614 -> -1.5709, 29.177 -> -3.1416, 30.740 -> +1.5708.
        var bearing1 = MathF.Atan2(134.05f - 119.98f, -420.30f - -420.00f);
        // Roulette 2: the needle at (120,-420), the character at (105.94,-419.66) beside the hourglass at (107,-420). Needle turns: 44.156 -> -1.5708, 45.713 -> -3.1416.
        var bearing2 = MathF.Atan2(105.94f - 120f, -419.66f - -420f);
        BstState Press(double at, float hourglassHp, float heading, double lastTurnAt, float bearing, float distance = 14.1f, float age = 6f)
        {
            var s = CrucibleState(50) with
            {
                CrucibleBoard = 5, CrucibleBattle = -1, EnemyCount = 1, HasHostileTarget = true, TargetNameId = hourglass,
                TargetHpPercent = hourglassHp, HighestEnemyHpPercent = hourglassHp, PlayerHpPercent = 99f, PetHpPercent = 100f, GcdReady = true, CanWeave = false,
            };
            s = R4ShippedSet(s, "NeedleKnown", true);
            s = R4ShippedSet(s, "NeedleHeading", heading);
            s = R4ShippedSet(s, "NeedleSinceTurn", (float)(at - lastTurnAt));
            s = R4ShippedSet(s, "NeedleToPlayerBearing", bearing);
            s = R4ShippedSet(s, "NeedleToPlayerDistance", distance);
            return R4ShippedSet(s, "HourglassAge", age);
        }
        BstDecision Decision(BstState s) => Decide(s, cfg);
        bool Held(BstState s) => Decision(s) is { ActionId: BST_CrucibleLogic.Hold, Reason: "crucible:hold-hourglass-needle" };
        bool Attacks(BstState s) => Decision(s).ActionId is BST.SmashAxe or BST.AxebladeBite or BST.Shieldsplitter;
        string Show(BstState s) => $"{Decision(s).ActionId} {Decision(s).Reason} [{Decision(s).Declines}]";

        Check("r4-shipped: hourglass roulette 1 geometry: the character lies east of the needle (the Death heading was +1.5708)", MathF.Abs(bearing1 - east) < 0.05f, $"{bearing1:F3}");
        Check("r4-shipped: hourglass roulette 2 geometry: the character lies west of the needle (the real Death heading was -3.1416)", MathF.Abs(bearing2 - west) < 0.05f, $"{bearing2:F3}");

        // ---- roulette 1 (hit): fails on the build before the fix, which attacked the priority add whatever the needle did
        var kill = Press(30.40, 1f, north, 29.177, bearing1);
        Check("r4-shipped: hourglass roulette 1, 21:08:30.40 ET, hourglass at 1%, the next tick fires east at the character: the weaponskill is held", Held(kill), Show(kill));
        Check("r4-shipped: hourglass ... the held press is the Hold action, never an attack", Decision(kill).ActionId == BST_CrucibleLogic.Hold && !Attacks(kill), Show(kill));
        // ---- roulette 2 (the side that would have hit it)
        var r2 = Press(44.73, 20f, west, 44.156, bearing2);
        Check("r4-shipped: hourglass roulette 2, 21:09:44.73 ET, hourglass low, the needle points at the character: held", Held(r2), Show(r2));
        // ---- roulette (miss): the needle points away, the rotation attacks (passes before and after)
        var safe1 = Press(27.88, 28f, west, 27.614, bearing1);
        Check("r4-shipped: hourglass roulette 1, 21:08:27.88 ET, hourglass 28%, no tick in reach fires at the character: the rotation attacks", !Held(safe1) && Attacks(safe1), Show(safe1));
        var safe2 = Press(45.90, 20f, north, 45.713, bearing2);
        Check("r4-shipped: hourglass roulette 2, 21:09:45.90 ET, the needle now points away and the next two ticks stay away: attacked", !Held(safe2) && Attacks(safe2), Show(safe2));

        // ---- the guard's edges (these pass before and after: the hold only ever acts inside them)
        Check("r4-shipped: hourglass at 100% or 76% HP: attacked whatever the needle does (one hit cannot break it)", !Held(Press(25.25, 100f, east, 24.5, bearing1)) && !Held(Press(25.25, 76f, east, 24.5, bearing1)));
        Check("r4-shipped: hourglass needle not read: attacked as before", !Held(R4ShippedSet(kill, "NeedleKnown", false)));
        Check("r4-shipped: hourglass another target (a Hapalit): attacked as before", !Held(kill with { TargetNameId = 14691 }));
        Check("r4-shipped: hourglass the character 30 y from the needle (past the cone): attacked", !Held(R4ShippedSet(kill, "NeedleToPlayerDistance", 30f)));
        Check("r4-shipped: hourglass the guard gives up after 12 s on the hourglass (the cast that follows is worse than a Doom risk): attacked",
            Attacks(R4ShippedSet(kill, "HourglassAge", 12.5f)));

        // ---- one full needle cycle with the character in one place, for each of the four sides the needle can point from: the hold clears within one turn of the needle.
        // The longest run of held 0.1 s steps is the longest hold in seconds this rule can produce in the recorded geometry (it must stay far below the Standard bar's 15 s).
        var longest = 0.0;
        var holdsTotal = 0;
        var releasesTotal = 0;
        foreach (var side in new[] { east, west, north, 0f })
        {
            var run = 0.0;
            for (var at = 0.0; at < 6.3; at += 0.1)
            {
                var turns = (int)(at / 1.5605);
                var heading = east - turns * 1.5708f;
                if (heading < -3.1417f) heading += 6.2832f;
                var sinceTurn = at - turns * 1.5605;
                if (Held(Press(at, 10f, heading, at - sinceTurn, side)))
                {
                    holdsTotal++;
                    run += 0.1;
                    longest = Math.Max(longest, run);
                }
                else
                {
                    releasesTotal++;
                    run = 0;
                }
            }
        }
        Check("r4-shipped: hourglass over one full needle cycle (6.3 s) from every side the press is held for part of it and released for the rest", holdsTotal > 10 && releasesTotal > 10, $"held {holdsTotal}, released {releasesTotal}");
        Check("r4-shipped: hourglass the longest unbroken hold in that cycle is under 6.3 s, far under the Standard bar's 15 s", longest > 0 && longest < 6.3, $"longest {longest:F1} s");
        Console.WriteLine($"   r4-shipped: hourglass longest unbroken hold over the four sides: {longest:F1} s");
    }
}
