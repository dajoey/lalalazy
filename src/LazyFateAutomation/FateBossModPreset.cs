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
///     - StayCloseToTarget: range = 2.6 (melee reach). This is what walks the character to the target: NormalMovement
///       only pathfinds to goal zones, AutoTarget only picks the target, and the job rotations that used to add the
///       approach goal are gone. FateGrind sets the role range as a transient each FATE (StayCloseRange); the base
///       value is melee so a failed update never leaves a melee job standing out of reach.
///       Track/options per BMR MiscAI/StayCloseToTarget.cs (identical at origin/main be8ff771c and tag 7.5.6.34).
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

    /// <summary>StayCloseToTarget range for tanks and melee (ClassJob.Role 1 / 2): AutoDuty's melee default.</summary>
    public const float MeleeRange = 2.6f;

    /// <summary>StayCloseToTarget range for every other role (ranged, casters, healers): AutoDuty's ranged default.</summary>
    public const float RangedRange = 10f;

    /// <summary>
    ///     Move-to-target range in yalms for a ClassJob.Role, the same split AutoDuty uses (BMRoleChecks: Role 1 tank /
    ///     2 melee -> MaxDistanceToTargetRoleMelee 2.6, everything else -> MaxDistanceToTargetRoleRanged 10), so a
    ///     ranged job or caster is never pulled into melee.
    /// </summary>
    public static float StayCloseRangeYalms(byte classJobRole) => classJobRole is 1 or 2 ? MeleeRange : RangedRange;

    /// <summary>The same range as BMR's StayCloseToTarget option name ("2.6", "10": InvariantCulture, 0.1 steps).</summary>
    public static string StayCloseRange(byte classJobRole)
        => MathF.Round(StayCloseRangeYalms(classJobRole), 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

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
            ],
            "BossMod.Autorotation.MiscAI.StayCloseToTarget": [
              { "Track": "range", "Option": "2.6" }
            ]
          }
        }
        """;
}
/// <summary>
///     Release rule for the plugin's BossMod preset (Dalamud-free; FateBossModControl is the live half). BossMod has one
///     active preset for everyone: AutoDuty sets its own on duty entry and the player may pick one by hand, so the grind
///     clears the active preset only when it is still the one the grind set. Names compare exactly, as BMR's do.
/// </summary>
internal static class FateBossModRelease {
    public static bool ShouldClearActive(string? activePreset) => string.Equals(activePreset, FateBossModPreset.Name, StringComparison.Ordinal);
}

/// <summary>
///     Per-run memory of BossMod IPC endpoints that failed. An endpoint BMR does not register (IpcNotReadyError) is
///     reported once and skipped for the rest of the run; any other failure is reported once and the call keeps
///     being made. Reset at the start of each grind run, so an updated BossMod is tried again.
/// </summary>
internal sealed class BossModEndpointLatch {
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    /// <summary>False once the endpoint is known to be missing this run.</summary>
    public bool ShouldCall(string endpoint) => !_missing.Contains(endpoint);

    /// <summary>Records a failed call; true only for the first failure of that endpoint this run (log it then).</summary>
    public bool ReportFailure(string endpoint, bool endpointMissing) {
        if (endpointMissing)
            _missing.Add(endpoint);
        return _reported.Add(endpoint);
    }

    public void Reset() {
        _missing.Clear();
        _reported.Clear();
    }
}
