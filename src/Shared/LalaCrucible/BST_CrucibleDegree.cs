namespace Lalalazy.Crucible;

/// <summary>
///     The Crucible board's difficulty degree and what it does to the recorded damage numbers. Pure, shared SOURCE (GluttonyCombo +
///     LazyCrucible + both harnesses). Degrees are 0 Standard, 1 First, 2 Second, 3 Third; <see cref="Unknown"/> (-1) is "this plugin has not
///     seen the degree set since it loaded" and always means the conservative reading.
///     <para>
///         Where it comes from: the board layout (AgentXBMStageDetailList) receives <c>2, degree</c> as its two event values whenever the degree
///         is set (the layout's own callback; AutoDuty fires the same one). LazyCrucible latches it (<see cref="FromStageDetailEvent"/>) and
///         publishes it over Dalamud IPC as <see cref="IpcName"/>; GluttonyCombo reads it there. Seen so far: 42 events, all <c>1</c> (First
///         Degree, the AutoDuty loop); a Standard value is the first thing the <c>dg=</c> field of the <c>CR|</c> line will show.
///     </para>
///     <para>
///         The scales are measured, not guide numbers (ffxivdb <c>action_events</c>, First Master's Board, 2026-10-02/03 Standard against
///         2026-10-05/06 First Degree, target the character or a familiar): the same ability hit about 1.55-1.75 times harder on the character
///         (Sweeping Evisceration max 1,254 -> 2,152, Darkness 1,290 -> 1,997, Grim Fate 194 -> 325, Rotten Stench 943 -> 1,579) and about
///         1.5-2.0 times harder on a familiar (Evisceration max 2,616 -> 3,892 and average 1,695 -> 3,346, Grim Fate 186 -> 369).
///     </para>
/// </summary>
internal static class CrucibleDegree
{
    public const int Unknown = -1;
    public const int Standard = 0;
    public const int First = 1;
    public const int Second = 2;
    public const int Third = 3;

    /// <summary> The EzIPC / Dalamud IPC function LazyCrucible provides: the latched degree (0-3) or -1. </summary>
    public const string IpcName = "LazyCrucible.Degree";

    /// <summary> The layout event that sets the degree: two values, <c>2</c> then the degree. </summary>
    public const int SetDegreeEventCode = 2;

    /// <summary> A reading of the board layout's event: the degree it sets, or <see cref="Unknown"/> when the event is anything else. </summary>
    public static int FromStageDetailEvent(ulong eventKind, uint valueCount, int? first, int? second) =>
        eventKind == 0 && valueCount == 2 && first == SetDegreeEventCode && second is { } d && IsDegree(d) ? d : Unknown;

    public static bool IsDegree(int degree) => degree is >= Standard and <= Third;

    /// <summary> The log letter: 0-3, or x when unknown. </summary>
    public static char Letter(bool known, int degree) => known && IsDegree(degree) ? (char)('0' + degree) : 'x';

    /// <summary>
    ///     How much harder a hit on the CHARACTER lands than the recorded one (recorded at Standard). The guard lines compare the
    ///     character's HP to a recorded largest hit, so the cheap side of the error is the larger scale: Second and Third are unmeasured and
    ///     take the First figure (they hit at least that hard). Unknown reads as the recorded value (1.0), as before the degree was read.
    /// </summary>
    public static float CharacterHitScale(bool known, int degree) =>
        !known ? 1f : degree switch { Standard => 1f, First or Second or Third => 1.75f, _ => 1f };

    /// <summary> The same for a hit on a FAMILIAR. </summary>
    public static float FamiliarHitScale(bool known, int degree) =>
        !known ? 1f : degree switch { Standard => 1f, First or Second or Third => 2f, _ => 1f };

    /// <summary>
    ///     How many times the scaled recorded hit a familiar has to be able to hold before a cover Snarl goes in. Where the degree is
    ///     measured (Standard, First) the scale already carries the harder hits and the margin is the ordinary 1.25; Second, Third and an
    ///     unread degree keep the 2.0 that 1.0.4.287 shipped.
    /// </summary>
    public static float CoverMargin(bool known, int degree) => known && degree is Standard or First ? 1.25f : 2f;
}
