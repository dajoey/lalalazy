using System.Collections.Generic;

namespace Lalalazy.Crucible;

/// <summary> A kind of damage a fight calls to be blunted (a wall of magic, a poison, a physical tankbuster). </summary>
[System.Flags]
public enum CrucibleMitigation : byte
{
    None = 0,
    Magic = 1 << 0,
    Poison = 1 << 1,
    Physical = 1 << 2,
}

/// <summary> One mitigation a fight calls for: the kind, what for, and who says so. Never tiered: it only ranks below every ability need. </summary>
public readonly record struct CrucibleMitigationNeed(CrucibleMitigation Kind, string What, IReadOnlyList<string> Src);

/// <summary> One mitigation need of a selection: covered by <see cref="Row"/>, 0 when no pick answers it. </summary>
public readonly record struct CrucibleMitigationStatus(CrucibleMitigationNeed Need, int Row)
{
    public bool Covered => Row != 0;
}

/// <summary>
///     Which familiars blunt which damage. PURE shared SOURCE. From the 7.56 ability and Tempered Release text (notebook Game Data
///     7.56), not from the guides' summaries:
///     <list type="bullet">
///         <item>magic: any Scalekin (Borrow -> Scaleskin, a barrier of 8% max HP against magic damage); drake's Smoldering Scales
///         (party magic vulnerability -20%, fire -15%); pugil's Water Wall (party magic vulnerability -20%).</item>
///         <item>poison: coblyn's Vulcanize (party poison resistance, poison duration -80%; also physical vulnerability -20%).</item>
///         <item>physical: any Beastkin (Borrow -> Beastskin, physical damage taken -25%); coblyn's Vulcanize and adamantoise's
///         Harden Shell (party physical vulnerability -20%).</item>
///     </list>
///     A poison WEAKNESS (Lauda, Guttler: Vulnerable bit 5) is not mitigation: a familiar that inflicts poison already scores it as
///     crowd control in <see cref="BST_CrucibleAdvisor.Score"/>.
/// </summary>
internal static class BST_CrucibleMitigation
{
    public const int PugilRow = 4;
    public const int CoblynRow = 7;
    public const int AdamantoiseRow = 25;
    public const int DrakeRow = 35;

    /// <summary> Kinds in report order. </summary>
    public static readonly CrucibleMitigation[] Kinds = [CrucibleMitigation.Magic, CrucibleMitigation.Poison, CrucibleMitigation.Physical];

    /// <summary> The mitigation kinds a familiar answers (its Kinship borrow or its Tempered Release). </summary>
    public static CrucibleMitigation Answers(int row)
    {
        if (BST_Beasts.ByRow(row) is not { } beast)
            return CrucibleMitigation.None;
        var m = CrucibleMitigation.None;
        if (beast.Kin == BeastmasterKinType.Scalekin || row is DrakeRow or PugilRow)
            m |= CrucibleMitigation.Magic;
        if (row == CoblynRow)
            m |= CrucibleMitigation.Poison | CrucibleMitigation.Physical;
        if (beast.Kin == BeastmasterKinType.Beastkin || row == AdamantoiseRow)
            m |= CrucibleMitigation.Physical;
        return m;
    }

    /// <summary> The skill that answers a kind for this familiar (its own Tempered Release before a Kinship borrow). </summary>
    public static string AnswerName(int row, CrucibleMitigation kind) => kind switch
    {
        CrucibleMitigation.Magic => row == DrakeRow ? "Smoldering Scales" : row == PugilRow ? "Water Wall" : "Scaleskin",
        CrucibleMitigation.Poison => "Vulcanize",
        CrucibleMitigation.Physical => row == CoblynRow ? "Vulcanize" : row == AdamantoiseRow ? "Harden Shell" : "Beastskin",
        _ => kind.ToString(),
    };

    /// <summary> The kind as one word (guide file, log fields). </summary>
    public static string Word(CrucibleMitigation kind) => kind switch
    {
        CrucibleMitigation.Magic => "magic",
        CrucibleMitigation.Poison => "poison",
        CrucibleMitigation.Physical => "physical",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary> Parse a guide word; None when unknown. </summary>
    public static CrucibleMitigation FromWord(string word) => word switch
    {
        "magic" => CrucibleMitigation.Magic,
        "poison" => CrucibleMitigation.Poison,
        "physical" => CrucibleMitigation.Physical,
        _ => CrucibleMitigation.None,
    };
}
