using static LazySkywardTracker.InventoryScanner;

namespace LazySkywardTracker.UiHarness;

/// <summary>
/// One rendered state. <see cref="Setup"/> fakes the game state the window reads. <see cref="Wheel"/> scrolls the jobs
/// list with real mouse-wheel input before the capture. <see cref="TooltipRun"/> hovers the Nth visible row that has an
/// inventory projection (top to bottom) so the real tooltip is drawn.
/// </summary>
internal sealed record Scenario(string Name, string Shows, int Width, int Height, Action Setup)
{
    public int Wheel { get; init; }
    public int? TooltipRun { get; init; }
    public float? Scale { get; init; }
}

internal static class Scenarios
{
    // First-use size comes from TrackerWindow (Size 1180x760); its constraints are 420x360 up to 1600x1400.
    internal const int DefaultWidth = 1180, DefaultHeight = 760;
    internal const int MinWidth = 420, MinHeight = 360, MaxHeight = 1400;
    private const uint Goal = 500000;

    internal static readonly Scenario[] All =
    [
        new("blocked-logged-out-1180x760",
            "Blocked: logged out, no progress cached; amber Blocked pill, info box 'Log in', every row Missing progress",
            DefaultWidth, DefaultHeight, LoggedOut),
        new("empty-awaiting-progress-1180x760",
            "Empty: logged in but no progress report yet; 'press Refresh progress' info box and footer reason",
            DefaultWidth, DefaultHeight, AwaitingProgress),
        new("running-populated-1180x760",
            "Running: typical mid-collection state at first-use size; mixed done/partial/idle/missing rows, bag points projected, top of the list",
            DefaultWidth, DefaultHeight, Populated),
        new("running-populated-scrolled-1180x760",
            "Running: same data after scrolling the jobs card to its end; last rows and the card bottom edge above the footer",
            DefaultWidth, DefaultHeight, Populated) { Wheel = 60 },
        new("tooltip-partial-armorer-1180x760",
            "Running: hover on the Armorer progress bar; projection tooltip with the longest real item names and a 'Projected' line",
            DefaultWidth, DefaultHeight, Populated) { TooltipRun = 0 },
        new("tooltip-completes-culinarian-1180x760",
            "Running: list scrolled down and the Culinarian bar hovered; tooltip ends with the green 'Would complete this job' line",
            DefaultWidth, DefaultHeight, Populated) { Wheel = 60, TooltipRun = 0 },
        new("running-no-bags-1180x760",
            "Running without bag points: every job reported, nothing to turn in; 'No turn-in points in bags' notes and fallback footer line",
            DefaultWidth, DefaultHeight, RunningNoBags),
        new("ready-all-complete-1180x760",
            "Ready: all 11 jobs complete; full green bars, 'Goal met' rows, goal-met footer",
            DefaultWidth, DefaultHeight, AllComplete),
        new("running-populated-640x900",
            "Running at a medium width: stat cards stacked, rows already in their narrow layout, footer in its narrow height",
            640, 900, Populated),
        new("running-populated-narrow-420x760",
            "Narrowest allowed width (420) at the default height: stacked header, top bar on two lines, narrow rows clipped by the body",
            MinWidth, DefaultHeight, Populated),
        new("running-populated-narrow-tall-420x1400",
            "Narrowest width at the tallest allowed height: shows the whole stacked layout and all narrow rows",
            MinWidth, MaxHeight, Populated),
        new("blocked-logged-out-cached-narrow-420x760",
            "Blocked at the narrowest width with progress still cached after logging out, list scrolled to the 'Log in' info box above the stale narrow rows",
            MinWidth, DefaultHeight, LoggedOutCached) { Wheel = 5 },
        new("min-size-running-420x360",
            "Smallest allowed window (420x360), populated: how little of the body survives next to the top bar and footer",
            MinWidth, MinHeight, Populated),
        new("scale150-running-populated-1180x760",
            "Evidence only: the typical Running state with the global UI scale at 1.5",
            DefaultWidth, DefaultHeight, Populated) { Scale = 1.5f },
    ];

    /// <summary>Text the scenarios feed into the window (item names), for the glyph audit.</summary>
    internal static IEnumerable<string> DataStrings()
    {
        Populated();
        return InventoryScanner.Projections.Values.SelectMany(p => p.Items).Select(i => i.ItemName);
    }

    private static void Reset(bool loggedIn)
    {
        Plugin.ProgressCache.Clear();
        Plugin.Completed.Clear();
        InventoryScanner.Projections.Clear();
        Plugin.ClientState.IsLoggedIn = loggedIn;
    }

    private static void LoggedOut() => Reset(false);

    private static void AwaitingProgress() => Reset(true);

    private static void LoggedOutCached()
    {
        Populated();
        Plugin.ClientState.IsLoggedIn = false;
        InventoryScanner.Projections.Clear();
    }

    private static void Progress(uint id, uint current) => Plugin.ProgressCache[id] = (current, Goal);

    /// <summary>One bag stack as the scanner reports it (item name, stack size, points per item or batch, total).</summary>
    private static void Bag(uint id, string item, uint quantity, uint perItem, uint total)
    {
        if (!InventoryScanner.Projections.TryGetValue(id, out var projection))
            InventoryScanner.Projections[id] = projection = new InventoryProjection();
        projection.Items.Add(new ItemBreakdown(item, quantity, perItem, total));
        projection.TotalPoints += total;
    }

    /// <summary>
    /// Typical mid-collection state. Item names are the game's own Skybuilders items for each job; points per item
    /// follow the in-game turn-in tiers.
    /// </summary>
    private static void Populated()
    {
        Reset(true);
        Plugin.Completed.Add(2491);                 // Carpenter: completed flag set
        Progress(2494, Goal);                       // Blacksmith: progress report says complete
        Progress(2497, 301_250);                    // Armorer: partial, bags project more
        Progress(2500, 87_200);
        Progress(2503, 0);                          // Leatherworker: reported, nothing yet (Idle)
        Progress(2506, 249_990);
        Progress(2509, 18_000);
        Progress(2512, 480_500);                    // Culinarian: bags would complete it
        Progress(2515, 130_000);
        // Botanist (2518) has no report yet: Missing progress.
        Progress(2521, 305_000);

        Bag(2497, "Grade 4 Artisanal Skybuilders' Chocobo Weathervane", 3, 820, 2460);
        Bag(2497, "Grade 4 Skybuilders' Oven", 12, 25, 300);
        Bag(2512, "Grade 4 Artisanal Skybuilders' Tincture", 30, 820, 24600);
        Bag(2512, "Grade 4 Skybuilders' Growth Formula", 40, 25, 1000);
        Bag(2515, "Grade 2 Artisanal Skybuilders' Cloudstone", 120, 3, 36);
        Bag(2515, "Grade 2 Skybuilders' Umbral Flarestone", 85, 4, 32);
        Bag(2521, "Grade 3 Artisanal Skybuilders' Helicoprion", 9, 113, 1017);
        Bag(2521, "Grade 4 Artisanal Skybuilders' Meganeura", 2, 1078, 2156);
    }

    private static void RunningNoBags()
    {
        Populated();
        Progress(2518, 212_400);
        InventoryScanner.Projections.Clear();
    }

    private static void AllComplete()
    {
        Reset(true);
        foreach (var id in Plugin.AchievementIds)
        {
            Plugin.Completed.Add(id);
            Progress(id, Goal);
        }
    }
}
