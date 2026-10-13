namespace LazyFateAutomation;

/// <summary>
///     The BossMod Reborn autorotation preset the FATE grind creates and activates. No Dalamud references:
///     the offline harness compiles this file and asserts its contents.
///
///     BossMod only moves, dodges, picks FATE mobs and level-syncs here; Gluttony Combo owns the rotation.
///     So the preset holds ONLY BossMod.Autorotation.MiscAI.* modules and never a job rotation (xan.*,
///     VeynWAR, ...) or a xan role-AI module, which would cast beside Gluttony Combo. Before this preset the
///     grind created and activated one named "Gluttony" that carried 21 job rotations and 5 role-AI modules;
///     that preset is left as it is in the user's BossMod database and is no longer used.
///
///     Modules (internal names from BMR origin/main BossMod/Autorotation/MiscAI/*.cs):
///     - AutoTarget: FATE mob priority. FateGrind adds transient Retarget / FATE / MaxTargets on top.
///     - FateUtils: Sync = Enable (level sync to the FATE).
///     - NormalMovement: Destination = Pathfind. FateGrind sets a transient Destination = None to stop
///       BossMod walking the character out of the FATE ring, and clears it again.
///     BMR's AddTransientStrategy IPC only applies to a module the preset already contains, so every module
///     FateGrind drives must be listed here (the harness cross-checks FateGrind.cs).
///
///     Shape: BMR BossMod/Autorotation/Preset.cs JsonPresetConverter (Name + Modules: { type: [ {Track, Option} ] }).
/// </summary>
internal static class FateBossModPreset {
    /// <summary>The plugin's own preset name, so a user preset (e.g. one named "Gluttony") is never touched.</summary>
    public const string Name = "LazyFateAutomation";

    /// <summary>The only module namespace this preset may contain.</summary>
    public const string MiscAiPrefix = "BossMod.Autorotation.MiscAI.";

    /// <summary>Passed to BMR's Presets.Create(json, overwrite: true) once per grind run.</summary>
    public const string Json = """
        {
          "Name": "LazyFateAutomation",
          "Modules": {
            "BossMod.Autorotation.MiscAI.AutoTarget": [
              { "Track": "FATE", "Option": "Enabled" },
              { "Track": "Retarget", "Option": "Always" }
            ],
            "BossMod.Autorotation.MiscAI.FateUtils": [
              { "Track": "Sync", "Option": "Enable" }
            ],
            "BossMod.Autorotation.MiscAI.NormalMovement": [
              { "Track": "Destination", "Option": "Pathfind" }
            ]
          }
        }
        """;
}
