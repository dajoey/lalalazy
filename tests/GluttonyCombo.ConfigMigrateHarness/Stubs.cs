// Stand-ins for the one game type AutoRotationConfig.cs needs, so the real settings class and
// the real migration ladder can compile with no Dalamud, no ECommons and no Lumina.
//
// Only ECommons' Job enum is stubbed. The real
// src/GluttonyCombo/ECommons/ECommons/ExcelServices/Enums/Job.cs cannot be compiled here: it
// carries a JobExtensions class that reaches into Lumina.Excel.Sheets.ClassJob. The numeric
// values below are copied from that file (verified 2026-09-05) and the harness asserts them, so
// a divergence fails the build rather than quietly testing a different enum than ships.

namespace ECommons.ExcelServices;

/// <summary>Job ids, mirroring ECommons' own <c>Job</c> enum.</summary>
public enum Job : byte
{
    /// <summary>Adventurer - the "no job" control.</summary>
    ADV = 0,

    /// <summary>Conjurer: WHM below level 30, and shares WHM's Raise.</summary>
    CNJ = 6,

    /// <summary>Paladin - a control: a job with no raise of its own.</summary>
    PLD = 19,

    WHM = 24,

    /// <summary>Summoner. Raises with SCH's Resurrection - the TRAP 1 collision.</summary>
    SMN = 27,

    /// <summary>Scholar. Raises with Resurrection - the TRAP 1 collision.</summary>
    SCH = 28,

    AST = 33,
    RDM = 35,
    BLU = 36,

    /// <summary>Gunbreaker - a second control.</summary>
    GNB = 37,

    SGE = 40,
}
