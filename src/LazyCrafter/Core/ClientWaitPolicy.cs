namespace LazyCrafter.Core;

/// <summary>
/// The wait-and-resume behaviour Joey picked on Helm thread <c>t-joey-1788710417021</c> (card t_ee6f7bf5), as
/// data and sentences rather than phase-machine plumbing, so the harness can prove the behaviour itself:
/// before each craft, if the client cannot accept a command, HOLD - say "waiting - close the market board to
/// continue", re-check, resume on its own the moment the window is gone, and after five minutes stop cleanly
/// with the cart intact. Exactly that: not stop-immediately, not configurable, not a different cap.
/// </summary>
public static class ClientWaitPolicy
{
    /// <summary>The cap he was sold: five minutes. Changing this changes a promise - the chat line says "5 minutes".</summary>
    public static readonly TimeSpan WaitCap = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The condition flags that mean "the game is not taking commands", by NAME. Names, not <c>ConditionFlag</c>
    /// literals, because Core must stay Dalamud-free (the harness compiles it standalone and asserts this list);
    /// <c>Adapters/ClientReadiness</c> resolves each name against the live enum once at startup and skips any
    /// that no longer parse - an upstream rename degrades to "window not named", never a crash.
    /// <para>
    /// The first nine are <b>Artisan's own refusal set, verbatim</b> (<c>PreCrafting.Occupied()</c> in Artisan's
    /// source): the game raises one of them when it answers Artisan's craft request with the exact error Joey's
    /// 11:58 run logged five times - "Unable to execute command while occupied". They are the empirical
    /// definition of "the client will bounce our craft". The remaining five (trade window, two cutscene flags,
    /// two zone-change flags) extend the same idea to states where a craft command cannot be issued either.
    /// </para>
    /// </summary>
    public static readonly string[] BlockingConditionNames =
    [
        "Occupied",                    // Artisan refusal set -
        "Occupied30",                  //   Artisan's PreCrafting.Occupied() checks exactly these nine;
        "Occupied33",                  //   the game raises them when it refuses a craft command.
        "Occupied38",                  //
        "Occupied39",                  //
        "OccupiedInEvent",             //
        "OccupiedInQuestEvent",        //
        "OccupiedInCutSceneEvent",     //
        "OccupiedSummoningBell",       // -
        "TradeOpen",
        "WatchingCutscene",
        "WatchingCutscene78",
        "BetweenAreas",
        "BetweenAreas51",
    ];

    /// <summary>
    /// Conditions that must NEVER be treated as blocking, because they are what a WORKING craft looks like.
    /// <c>Crafting</c>/<c>PreparingToCraft</c>/<c>ExecutingCraftingAction</c> are true while Artisan is
    /// legitimately crafting (ECommons' broader <c>IsOccupied()</c> includes them; Artisan's craft gate
    /// deliberately does not) - gating on them would deadlock the dispatcher against its own craft.
    /// Pinned by a harness check: this array and <see cref="BlockingConditionNames"/> must stay disjoint.
    /// </summary>
    public static readonly string[] CraftingConditionNames =
    [
        "Crafting",
        "PreparingToCraft",
        "ExecutingCraftingAction",
        "NormalConditions",
    ];

    /// <summary>True when the hold has reached the cap. Called with the phase clock's elapsed time.</summary>
    public static bool TimedOut(TimeSpan elapsed) => elapsed >= WaitCap;

    /// <summary>
    /// The one chat line on entering the hold (and when a DIFFERENT window takes over mid-hold). Prints the
    /// label <c>ClientReadiness.BusyBecause()</c> produced - already human wording ("the market board").
    /// </summary>
    public static string WaitLine(string? busyBecause) =>
        $"waiting - close {Label(busyBecause)} to continue";

    /// <summary>The persistent UI status line while held, with the hold's age so it never reads as a hang.</summary>
    public static string WaitStatus(string? busyBecause, TimeSpan held) =>
        $"waiting - {Label(busyBecause)} ({held:m\\:ss})";

    /// <summary>Said once when the window is gone and the cart carries on by itself - the whole point of the option.</summary>
    public static string ResumedLine(string? wasBlocking) =>
        $"{Label(wasBlocking)} was closed - resuming the cart.";

    /// <summary>Status after the hold ends in a resume.</summary>
    public static string ResumedStatus() => "resuming the cart";

    /// <summary>The blocked-run reason recorded for a 5-minute timeout: truthful, and says Resume still works.</summary>
    public static string TimeoutReason(string? busyBecause, TimeSpan cap) =>
        $"{Label(busyBecause)} blocked crafting for {(int)cap.TotalMinutes} minutes - close it and press Resume (or /lcraft resume) to continue the same cart";

    /// <summary>The red chat line for the same timeout.</summary>
    public static string TimeoutLine(string? busyBecause, TimeSpan cap) =>
        $"stopped - {TimeoutReason(busyBecause, cap)}.";

    private static string Label(string? busyBecause) =>
        string.IsNullOrWhiteSpace(busyBecause) ? CraftDiagnosis.UnknownWindow : busyBecause;
}

/// <summary>
/// "What time is it?" as an interface, so the hold's wall-clock cap can be faked in the harness (card
/// t_ee6f7bf5). The dispatcher itself uses <c>DateTime.UtcNow</c> directly; only the test rig substitutes this.
/// Kept in Core, Dalamud-free, alongside the policy that consumes the concept of time.
/// </summary>
public interface ITimeSource
{
    DateTime UtcNow { get; }
}
