namespace GluttonyCombo.Combos.PvE;

// Split out of BST_Gauge.cs (t_02fe2681) so the pure rotation logic in BST_RotationLogic.cs -
// and this file itself - compile Dalamud-free into tests/GluttonyCombo.BSTRotationHarness.
// BST_Gauge.cs (the vendored ClientStructs gauge overlay, which DOES need Dalamud/ClientStructs
// to read the live gauge) references these two enums from the same namespace.

/// <summary> Affinity of the most recent instinctual skill (PR #1947 BeastmasterAffinity). </summary>
public enum BeastmasterAffinity : byte
{
    None = 0,
    Volant = 1,
    Rampant = 2,
    Durant = 3,
    Eldritch = 4,
    Sunstrider = 5,
    Moonstalker = 6,
    // Value 7 is not an affinity: both upstream WrathCombo (mrbeastmaster, read
    // 2026-09-11) and this fork read byte 0x0C == 7 as the Wavering Heart combo lockout.
    WaveringHeart = 7,
}

/// <summary> A familiar's kin type; matches Kinship statuses 4602 Beast .. 4609 Ash (PR #1947). </summary>
public enum BeastmasterKinType : byte
{
    None = 0,
    Beastkin = 1,
    Vilekin = 2,
    Cloudkin = 3,
    Seedkin = 4,
    Wavekin = 5,
    Scalekin = 6,
    Soulkin = 7,
    Ashkin = 8,
}
