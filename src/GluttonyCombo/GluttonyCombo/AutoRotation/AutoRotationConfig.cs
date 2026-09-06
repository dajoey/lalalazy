using ECommons.ExcelServices;
using GluttonyCombo.API.Enum;
using Newtonsoft.Json;

namespace GluttonyCombo.AutoRotation;

public class AutoRotationConfig
{
    public bool Enabled;
    public bool InCombatOnly;
    public bool BypassQuest;
    public bool BypassFATE;
    public bool BypassBuffs;
    public int CombatDelay = 1;
    public bool EnableInInstance;
    public bool DisableAfterInstance;
    public DPSRotationMode DPSRotationMode;
    public HealerRotationMode HealerRotationMode;
    public HealerSettings HealerSettings = new();
    public DPSSettings DPSSettings = new();
    public int Throttler = 50;
    public bool OrbwalkerIntegration;
    public float QueueWindow = 0.3f;
    public bool PauseWhenNoTarget;
}

public class DPSSettings
{
    public bool FATEPriority = false;
    public bool QuestPriority = false;
    public bool TreasureHuntPriority = false;
    public int? DPSAoETargets = 3;
    public bool PreferNonCombat = false;
    public bool OnlyAttackInCombat = false;
    public bool DPSAlwaysHardTarget = false;
    public float MaxDistance = 30;
    public bool IgnoreRangeInBoss = true;
    public bool AoEIgnoreManual = false;
    public bool AoEOnlyWhenTargeting = false;
    public bool UnTargetAndDisableForPenalty = false;
    public bool AutoPositionals = false;
}

public class HealerSettings
{
    public int SingleTargetHPP = 70;
    public int AoETargetHPP = 80;
    public int SingleTargetRegenHPP = 60;
    public int SingleTargetExcogHPP = 50;
    public bool IncludeShields = false;
    public int? AoEHealTargetCount = 2;
    public int HealDelay = 1;
    public bool ManageKardia = false;
    public bool KardiaTanksOnly = false;
    public bool AutoRez = false;

    #region Auto-Rez "Require Swiftcast/Dualcast", per job

    /// <summary>
    ///     Require an instant cast before auto-rezzing, as WHM (and CNJ below level 30).
    /// </summary>
    /// <remarks>
    ///     These seven fields replaced a single global <c>AutoRezRequireSwift</c> in
    ///     v1.0.4.174. They are keyed by JOB and never by raise spell: SCH and SMN both raise
    ///     with <c>SCH.Resurrection</c>, so a switch on the spell would fuse two jobs into one
    ///     setting that could never be split again. CNJ and WHM share <c>WHM.Raise</c> and are
    ///     deliberately one field - the same job either side of level 30.
    ///     <para>
    ///         Resolve them with <see cref="RequireSwiftFor" />, never by hand.
    ///     </para>
    /// </remarks>
    public bool AutoRezRequireSwiftWHM = false;

    /// <summary>Require an instant cast before auto-rezzing, as SCH.</summary>
    public bool AutoRezRequireSwiftSCH = false;

    /// <summary>Require an instant cast before auto-rezzing, as AST.</summary>
    public bool AutoRezRequireSwiftAST = false;

    /// <summary>Require an instant cast before auto-rezzing, as SGE.</summary>
    public bool AutoRezRequireSwiftSGE = false;

    /// <summary>Require an instant cast before auto-rezzing, as SMN.</summary>
    public bool AutoRezRequireSwiftSMN = false;

    /// <summary>Require an instant cast before auto-rezzing, as BLU.</summary>
    public bool AutoRezRequireSwiftBLU = false;

    /// <summary>
    ///     Require an instant cast before auto-rezzing, as RDM. Defaults to <c>true</c>, unlike
    ///     its six siblings.
    /// </summary>
    /// <remarks>
    ///     Before v1.0.4.174 RDM's requirement was hardcoded ON - the rez branch only fired
    ///     Verraise once the cast time was already zero, and the old global toggle did not
    ///     reach it - so <c>true</c> is the value that means "nothing changed" for RDM, on
    ///     fresh and existing installs alike. Unticking it is the new capability: it lets
    ///     auto-rotation begin a ~10 second hard-cast Verraise when no Dualcast or Swiftcast is
    ///     available.
    /// </remarks>
    public bool AutoRezRequireSwiftRDM = true;

    /// <summary>
    ///     The pre-v1.0.4.174 single global toggle, read back off the saved JSON so the
    ///     migration can carry it onto the six jobs it used to govern.
    /// </summary>
    /// <remarks>
    ///     <see cref="NullValueHandling.Ignore" /> keeps the dead key out of every config
    ///     written from here on: the migration copies it once, sets this back to null, and the
    ///     property then stops being serialised entirely. A fresh install never had the key, so
    ///     this stays null and the per-job defaults above win untouched.
    /// </remarks>
    [JsonProperty("AutoRezRequireSwift", NullValueHandling = NullValueHandling.Ignore)]
    public bool? AutoRezRequireSwiftLegacy { get; set; }

    /// <summary>
    ///     Resolves the per-job "require an instant cast before rezzing" flag for
    ///     <paramref name="job" />.
    /// </summary>
    /// <remarks>
    ///     Jobs with no raise of their own answer <c>false</c>: they never reach the rez path,
    ///     and a bare <c>false</c> is the value that changes nothing if they ever do.
    /// </remarks>
    public bool RequireSwiftFor(Job job) => job switch
    {
        Job.CNJ or Job.WHM => AutoRezRequireSwiftWHM,
        Job.SCH => AutoRezRequireSwiftSCH,
        Job.AST => AutoRezRequireSwiftAST,
        Job.SGE => AutoRezRequireSwiftSGE,
        Job.SMN => AutoRezRequireSwiftSMN,
        Job.BLU => AutoRezRequireSwiftBLU,
        Job.RDM => AutoRezRequireSwiftRDM,
        _ => false,
    };

    #endregion

    public bool AutoRezDPSJobs = false;
    public bool AutoRezDPSJobsHealersOnly = false;
    public bool AutoRezOutOfParty = false;
    public bool AutoCleanse = false;
    public bool PreEmptiveHoT = false;
    public bool IncludeNPCs = false;
    public bool HealerAlwaysHardTarget = false;
    public bool HandleRaidwides = false;
    public bool HandleTankbusters = false;
    public bool TankbustersBeyondParty = false;

}
