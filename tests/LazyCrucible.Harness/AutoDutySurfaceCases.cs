using Lalalazy.Crucible;
using LazyCrucible.Policy;

namespace LazyCrucible.Harness;

/// <summary>
///     Working WITH AutoDuty (replaces the need-coverage corrections, which reached the fight in 0 of 40 AutoDuty-driven fights:
///     AutoDuty re-asserted its own picks every time). LazyCrucible stands by on exactly the surfaces AutoDuty drives and acts as
///     usual on the ones it leaves alone. AutoDuty 0.0.0.382 (Managers/Crucible/CrucibleMenus.cs) drives the run roster, the shop and
///     its feed picker, treasure, spoils and the campsite whatever its toggles say (a toggle that is off makes it CLOSE that screen:
///     ItemShop.Close plus Yes on its prompt, Treasure.Close, Booty.Close, PetParty.RestReturn); only "Fight picks" off leaves a screen,
///     the battlehorns, alone. The active profile's Fight picks is read over AutoDuty's IPC (<c>AutoDuty.GetConfig</c>), with its
///     config file as the fallback. When AutoDuty picks the team, one telemetry field per fight names the needs that team cannot answer.
///     The fixture is the shape of AutoDuty 0.0.0.382's AutoDutyConfigV2.json, trimmed, with made-up character ids.
/// </summary>
internal static class AutoDutySurfaceCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    private const ulong MainCid = 18014400000000123;
    private const ulong NumericCid = 18014400000000456;
    private const ulong AllOffCid = 18014400000000789;
    private const ulong UnknownCid = 18014400000000999;

    private const CrucibleSurface AlwaysAutoDuty =
        CrucibleSurface.Roster | CrucibleSurface.Shop | CrucibleSurface.Feed | CrucibleSurface.Treasure | CrucibleSurface.Spoils | CrucibleSurface.Camp;

    public static void Run()
    {
        Console.WriteLine("-- working with AutoDuty: surfaces, its config, the uncovered needs --");

        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "autoduty_config.json"));

        // ---- reading AutoDuty's config file (the fallback when its IPC is missing)
        var migrated = AutoDutyCrucible.Parse(json, UnknownCid);
        Check("AutoDuty config: a character with no profile of its own reads the default profile (Leveling, Carry_Minus_2, First, fight picks on)",
            migrated is { Profile: "Migrated", TeamMode: "Leveling", LevelingMode: "Carry_Minus_2", Degree: "First", FightPicks: true },
            migrated?.ToString());
        var main = AutoDutyCrucible.Parse(json, MainCid);
        Check("AutoDuty config: a character listed in a profile's CIDs reads that profile (fight picks off)",
            main is { Profile: "Main", TeamMode: "Recommended", FightPicks: false }, main?.ToString());
        var numeric = AutoDutyCrucible.Parse(json, NumericCid);
        Check("AutoDuty config: enum values written as numbers read as names (TeamMode 2 = Custom)",
            numeric is { TeamMode: "Custom", Degree: "Third", FightPicks: true }, numeric?.ToString());
        var allOff = AutoDutyCrucible.Parse(json, AllOffCid);
        Check("AutoDuty config: a profile with every menu toggle off reads fight picks off", allOff is { Profile: "AllOff", FightPicks: false }, allOff?.ToString());
        var bomCrlf = AutoDutyCrucible.Parse("﻿" + json.Replace("\n", "\r\n"), MainCid);
        Check("AutoDuty config: the file as AutoDuty writes it (UTF-8 byte order mark, CRLF) reads the same", bomCrlf == main, bomCrlf?.ToString());
        Check("AutoDuty config: an empty, truncated or foreign file reads as nothing (no banner, no error)",
            AutoDutyCrucible.Parse("", 1) is null && AutoDutyCrucible.Parse("{\"profileData\": [", 1) is null
            && AutoDutyCrucible.Parse("{\"Language\":\"en-US\"}", 1) is null && AutoDutyCrucible.Parse("[1,2]", 1) is null);
        var bare = AutoDutyCrucible.Parse("{\"profileData\":[{\"Name\":\"Other\",\"CIDs\":[],\"Config\":{\"Meta\":{}}}],\"DefaultConfigName\":\"Gone\"}", 1);
        Check("AutoDuty config: no matching profile is AutoDuty's bare defaults (Recommended, fight picks on)",
            bare is { TeamMode: "Recommended", FightPicks: true }, bare?.ToString());

        // ---- reading the ACTIVE profile over AutoDuty's IPC (AutoDuty.GetConfig: string path -> value.ToString())
        var asked = new List<string>();
        Func<string, string?> Ipc(Dictionary<string, string> answers) => path =>
        {
            asked.Add(path);
            return answers.TryGetValue(path, out var v) ? v : "";
        };
        var live = AutoDutyCrucible.FromIpc(Ipc(new() { ["Meta.Crucible.FightPicks"] = "False", ["Meta.Crucible.TeamMode"] = "Leveling" }));
        Check("AutoDuty IPC: the active profile's Fight picks and team mode are read (\"False\" = off)",
            live is { FightPicks: false, TeamMode: "Leveling", Profile: "(active)" }, live?.ToString());
        Check("AutoDuty IPC: only the two known paths are asked, Fight picks first (an unknown path is an error in AutoDuty's log)",
            asked.SequenceEqual(["Meta.Crucible.FightPicks", "Meta.Crucible.TeamMode"]), string.Join(",", asked));
        asked.Clear();
        var on = AutoDutyCrucible.FromIpc(Ipc(new() { ["Meta.Crucible.FightPicks"] = "True", ["Meta.Crucible.TeamMode"] = "Recommended" }));
        Check("AutoDuty IPC: \"True\" = fight picks on", on is { FightPicks: true, TeamMode: "Recommended" }, on?.ToString());
        asked.Clear();
        var none = AutoDutyCrucible.FromIpc(Ipc([]));
        Check("AutoDuty IPC: an answer that is not True/False (unknown path, old AutoDuty) reads as nothing, so the file is read instead, and nothing more is asked",
            none is null && asked.SequenceEqual(["Meta.Crucible.FightPicks"]), string.Join(",", asked));

        // ---- which surfaces LazyCrucible stands by on
        Check("unreadable AutoDuty config: every surface stands by while AutoDuty runs (the safe side, as before)",
            AutoDutyCrucible.Driven(null) == CrucibleSurface.All);
        Check("AutoDuty with fight picks on: every surface stands by",
            AutoDutyCrucible.Driven(migrated) == CrucibleSurface.All, AutoDutyCrucible.Driven(migrated).ToString());
        var mainDriven = AutoDutyCrucible.Driven(main);
        Check("AutoDuty with fight picks off: LazyCrucible picks the battlehorns while AutoDuty runs",
            (mainDriven & CrucibleSurface.Horns) == 0, mainDriven.ToString());
        Check("AutoDuty with shop and treasure off still closes those screens: the shop, the feed picker and treasure stand by",
            (mainDriven & (CrucibleSurface.Shop | CrucibleSurface.Feed | CrucibleSurface.Treasure)) == (CrucibleSurface.Shop | CrucibleSurface.Feed | CrucibleSurface.Treasure),
            mainDriven.ToString());
        var allOffDriven = AutoDutyCrucible.Driven(allOff);
        Check("AutoDuty with every menu toggle off: the roster, shop, feed picker, treasure, spoils and campsite still stand by (it closes them, it returns from camp), only the battlehorns are LazyCrucible's",
            allOffDriven == AlwaysAutoDuty, allOffDriven.ToString());
        Check("AutoDuty builds the run roster in every team mode (Recommended, Leveling, Custom): the roster always stands by",
            (mainDriven & CrucibleSurface.Roster) != 0 && (AutoDutyCrucible.Driven(migrated) & CrucibleSurface.Roster) != 0
            && (AutoDutyCrucible.Driven(numeric) & CrucibleSurface.Roster) != 0);

        Check("formation pass: the horn write stands down while AutoDuty runs with fight picks on",
            AutoDutyCrucible.Yields(true, AutoDutyCrucible.Driven(migrated), CrucibleSurface.Horns));
        Check("formation pass: the horn write is LazyCrucible's while AutoDuty runs with fight picks off",
            !AutoDutyCrucible.Yields(true, mainDriven, CrucibleSurface.Horns));
        Check("formation pass: the roster write stands down while AutoDuty runs, fight picks or not",
            AutoDutyCrucible.Yields(true, mainDriven, CrucibleSurface.Roster) && AutoDutyCrucible.Yields(true, AutoDutyCrucible.Driven(migrated), CrucibleSurface.Roster));
        Check("formation pass: without an AutoDuty run nothing stands down",
            !AutoDutyCrucible.Yields(false, CrucibleSurface.All, CrucibleSurface.Horns) && !AutoDutyCrucible.Yields(false, CrucibleSurface.All, CrucibleSurface.Roster));

        // ---- the one quiet status line
        Check("status line: every surface standing by",
            AutoDutyCrucible.StandingBy(CrucibleSurface.All)
            == "AutoDuty is choosing familiars for this run; LazyCrucible is standing by on the run roster, the battlehorns, the shop and feeding, treasure, spoils and the campsite.",
            AutoDutyCrucible.StandingBy(CrucibleSurface.All));
        Check("status line: fight picks off names the battlehorns as LazyCrucible's",
            AutoDutyCrucible.StandingBy(mainDriven)
            == "AutoDuty is choosing familiars for this run; LazyCrucible is standing by on the run roster, the shop and feeding, treasure, spoils and the campsite, and handles the battlehorns.",
            AutoDutyCrucible.StandingBy(mainDriven));
        Check("status line: nothing driven, nothing said", AutoDutyCrucible.StandingBy(CrucibleSurface.None) == "");

        // ---- the per-fight grade of the team AutoDuty chose (PS|..|uncovered=)
        var guide = CrucibleGuide.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CrucibleGuide.json")));
        guide.InstallNeedModel();
        int[] trio = [19, 8, 22]; // the Leveling trio of 102 of 122 fights since 10-06: bat, diremite, sabotender
        Check("uncovered: the Leveling trio at the Strix Piece cannot dispel",
            AutoDutyCrucible.Uncovered(4, 1, trio) == "D", AutoDutyCrucible.Uncovered(4, 1, trio));
        Check("uncovered: the Leveling trio in the Biloko pack cannot interrupt Natural Nurture (the disputed Grab and Grow dispel is not counted)",
            AutoDutyCrucible.Uncovered(4, 8, trio) == "I", AutoDutyCrucible.Uncovered(4, 8, trio));
        Check("uncovered: Bone Knight + Bone Bishop with the trio: the bat cleanses, interrupt and dispel stay open (interrupt, dispel, cleanse order)",
            AutoDutyCrucible.Uncovered(1, 1, trio) == "I,D", AutoDutyCrucible.Uncovered(1, 1, trio));
        Check("uncovered: a Soulkin on the team answers the interrupt", AutoDutyCrucible.Uncovered(4, 8, [19, 7, 22]) == "");
        Check("uncovered: a knocked-out answerer answers nothing",
            AutoDutyCrucible.Uncovered(4, 8, [19, 7, 22], new Dictionary<int, int> { [7] = 0 }) == "I");
        Check("uncovered: a fight with no ability needs grades empty", AutoDutyCrucible.Uncovered(4, 5, trio) == "");
        CrucibleNeedModel.Extras = null;
        CrucibleNeedModel.MitigationExtras = null;
    }
}
