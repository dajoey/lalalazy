using Lalalazy.Crucible;
using LazyCrucible.Policy;

namespace LazyCrucible.Harness;

/// <summary>
///     AutoDuty need-coverage correction (0.1.9.4 fix round, 2026-10-03). Live 2026-10-02 evening: AutoDuty drove the
///     boards all session, picked its own leveling familiars at every battlehorn screen and roster rebuild, and the
///     stand-down meant nothing re-fielded the answers the fights call for — the Strix dispel landed 9 of 20 times
///     (every unanswered episode had no dispeller on the horn; the two with the vulture were answered), and the
///     healer fight often had no Soulkin in the roster at all. These cases replay that evening's shapes: the
///     correction is MINIMAL (the driver's picks are kept except where a need is uncovered), the delta and the miss
///     are named for the run log, and a covered fight is never touched.
/// </summary>
internal static class AutoDutyNeedFixCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    // The live 2026-10-02 shapes. Roster of 15:21 (has the vulture 11 and a Karlabos 48, no Soulkin), roster of
    // 14:42 (Wavekin 31 but no Soulkin), and a driver horn set that answers nothing.
    private static readonly int[] RosterWithVulture = [6, 11, 33, 42, 43, 44, 45, 46, 47, 48, 49, 50];
    private static readonly int[] RosterNoSoulkin = [30, 31, 32, 34, 35, 36, 37, 38, 39, 42, 49, 50];

    private static Dictionary<int, int> Full(params int[] hurtRows)
    {
        var hp = new Dictionary<int, int>();
        for (var row = 1; row <= BST_Beasts.Count; row++)
            hp[row] = 100;
        foreach (var row in hurtRows)
            hp[row] = 0;
        return hp;
    }

    public static void Run()
    {
        Console.WriteLine("-- AutoDuty need-coverage correction --");

        // The live plugin installs the fight guide's tiered needs into the model at startup; the correction
        // must see exactly what the picker sees (Required + Useful-non-dispel), so install it here too.
        var guide = CrucibleGuide.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CrucibleGuide.json")));
        guide.InstallNeedModel();
        try
        {
            RunCases();
        }
        finally
        {
            CrucibleNeedModel.Extras = null;
        }
    }

    private static void RunCases()
    {

        // ---- horn: the driver's three answer nothing, the roster carries the answer (16:44 shape: rows 3.49.7 picked, vulture 11 sits in the roster)
        var hornFix = AutoDutyNeedFix.Horn(4, 1, [3, 49, 7], RosterWithVulture, Full());
        Check("Strix Piece: the driver's three answer nothing, the vulture is swapped in for one pick",
            hornFix.Rows.Count == 3 && hornFix.Rows.Contains(11) && hornFix.Rows.Contains(3) && hornFix.Rows.Contains(7),
            $"rows={string.Join(".", hornFix.Rows)} delta={hornFix.Delta}");
        Check("... the delta names the swap for the run log", hornFix.Delta == "+11-49", hornFix.Delta);
        Check("... nothing stays uncovered and no miss is claimed", hornFix.Miss.Count == 0, string.Join("; ", hornFix.Miss));

        // ---- horn: already covered (15:23 shape: the driver itself picked the vulture) — nothing changes
        var covered = AutoDutyNeedFix.Horn(4, 1, [33, 6, 11], RosterWithVulture, Full());
        Check("the driver's picks already cover the fight: untouched, empty delta",
            covered.Delta == "" && covered.Rows.SequenceEqual([33, 6, 11]), covered.Delta);

        // ---- horn: a Wavekin answers too — the vulture is preferred (the rotation's own answer order)
        var rostOnlyWavekin = new int[] { 3, 7, 49, 9 }; // 9 megalocrab (Wavekin), no vulture
        var wavekinFix = AutoDutyNeedFix.Horn(4, 1, [3, 49, 7], rostOnlyWavekin, Full());
        Check("no vulture in the roster: the Wavekin answers instead",
            wavekinFix.Rows.Count == 3 && wavekinFix.Rows.Contains(9), $"rows={string.Join(".", wavekinFix.Rows)}");

        // ---- horn: nothing in the roster answers — the pick stands, the miss is named
        var rostBlind = new int[] { 1, 2, 3, 5, 26, 30, 33, 38, 50 };
        var missFix = AutoDutyNeedFix.Horn(4, 1, [1, 2, 3], rostBlind, Full());
        Check("no roster member answers the dispel: the picks stand and the miss says so",
            missFix.Delta == "" && missFix.Miss.Count == 1 && missFix.Miss[0].Contains("dispel"), string.Join("; ", missFix.Miss));

        // ---- horn: the answerer is knocked out — miss, not a corpse on the horn
        var koFix = AutoDutyNeedFix.Horn(4, 1, [3, 49, 7], RosterWithVulture, Full(11, 48));
        Check("every roster answerer is knocked out: miss names it, nothing is swapped in",
            koFix.Delta == "" && koFix.Miss.Count == 1 && koFix.Miss[0].Contains("knocked out"), string.Join("; ", koFix.Miss));

        // ---- horn: the effective needs match the rotation's: a Required interrupt (3.2 Dreadwash, panel) counts
        var reqI = AutoDutyNeedFix.Horn(3, 2, [1, 2, 3], [1, 2, 3, 7], Full());
        Check("a fight whose interrupt the panel calls for: the Soulkin goes on the horn",
            reqI.Rows.Contains(7), $"rows={string.Join(".", reqI.Rows)}");

        // ---- horn: a Useful cleanse counts (the game's own flag decides live), a Useful dispel does not
        var usefulC = AutoDutyNeedFix.Horn(5, 3, [1, 2, 3], [1, 2, 3, 17], Full()); // 5.3: Blind cleanse is Useful
        Check("a Useful cleanse still earns a horn slot",
            usefulC.Rows.Contains(17), $"rows={string.Join(".", usefulC.Rows)}");
        var usefulD = AutoDutyNeedFix.Horn(3, 5, [1, 2, 3], [1, 2, 3, 9, 11], Full()); // 3.5: Might dispel is Useful, panel disagrees
        Check("a Useful dispel the panel disputes never earns a horn slot",
            usefulD.Delta == "" && usefulD.Miss.Count == 0, $"delta={usefulD.Delta}");

        // ---- horn: a smaller driver team gets the answerer as the extra pick, nothing removed
        var small = AutoDutyNeedFix.Horn(4, 1, [3, 49], RosterWithVulture, Full());
        Check("a two-pick driver team: the answerer is added, both picks kept",
            small.Rows.Count == 3 && small.Rows.Contains(11) && small.Rows.Contains(3) && small.Rows.Contains(49) && small.Delta == "+11",
            $"rows={string.Join(".", small.Rows)} delta={small.Delta}");

        // ---- roster: the board's needs have no answerer in the driver's roster (14:42 shape: no Soulkin for 4.8), full roster
        var captured = RosterNoSoulkin.Concat(new[] { 7, 11 }).ToList();
        var rosterFix = AutoDutyNeedFix.Roster(4, RosterNoSoulkin, captured, Full(), 12);
        Check("board 4 roster without a Soulkin: one is swapped in, the roster stays at the cap",
            rosterFix.Rows.Count == 12 && rosterFix.Rows.Contains(7), $"rows={string.Join(".", rosterFix.Rows)} delta={rosterFix.Delta}");
        Check("... the swapped-out rows are leveling picks that answer no board need",
            rosterFix.Delta.StartsWith("+7-") && rosterFix.Delta.Count(c => c == '-') == 1, rosterFix.Delta);

        // ---- roster: not full — the answerer is appended, nothing removed
        var shortRoster = RosterNoSoulkin.Take(9).ToList();
        var appendFix = AutoDutyNeedFix.Roster(4, shortRoster, shortRoster.Concat(new[] { 7 }).ToList(), Full(), 12);
        Check("roster below the cap: the Soulkin is appended, every driver pick kept",
            appendFix.Rows.Count == 10 && appendFix.Rows.Contains(7) && appendFix.Delta == "+7", appendFix.Delta);

        // ---- roster: the driver's roster already covers the board — untouched (board 4 needs dispel+interrupt+cleanse)
        var okRoster = new int[] { 6, 11, 7, 17, 42, 43, 44, 45, 47, 48, 49, 50 }; // vulture, coblyn, slime
        var okFix = AutoDutyNeedFix.Roster(4, okRoster, okRoster.Concat(new[] { 23 }).ToList(), Full(), 12);
        Check("roster already covering every board need: empty delta", okFix.Delta == "" && okFix.Rows.SequenceEqual(okRoster) && okFix.Miss.Count == 0, $"{okFix.Delta} [{string.Join("; ", okFix.Miss)}]");

        // ---- roster: the only captured answerer for a need is knocked out — miss, not added
        var koDriver = new int[] { 17, 30, 31, 32, 34, 35, 36, 37, 38, 39, 42, 49 }; // 17 covers cleanse, 31 dispel
        var koCaptured = koDriver.Concat(new[] { 7 }).ToList();
        var koRoster = AutoDutyNeedFix.Roster(4, koDriver, koCaptured, Full(7, 18, 23, 29, 47), 12);
        Check("every captured Soulkin is knocked out: the roster stands, the miss names it",
            koRoster.Delta == "" && koRoster.Rows.SequenceEqual(koDriver) && koRoster.Miss.Count == 1 && koRoster.Miss[0].Contains("knocked out"),
            $"{koRoster.Delta} [{string.Join("; ", koRoster.Miss)}]");
    }
}
