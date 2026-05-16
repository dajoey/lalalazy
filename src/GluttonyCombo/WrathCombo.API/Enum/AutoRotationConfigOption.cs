#region

using WrathCombo.API.Attribute;

#endregion

namespace WrathCombo.API.Enum;

/// <summary>
///     IPC-settable AutoRotation configuration options.
/// </summary>
public enum AutoRotationConfigOption
{
    [ConfigValueType(typeof(bool))] InCombatOnly = 0,

    [ConfigValueType(typeof(DPSRotationMode))]
    DPSRotationMode = 1,

    [ConfigValueType(typeof(HealerRotationMode))]
    HealerRotationMode = 2,

    [ConfigValueType(typeof(bool))] FATEPriority = 3,

    [ConfigValueType(typeof(bool))] QuestPriority = 4,

    [ConfigValueType(typeof(int))] SingleTargetHPP = 5,

    [ConfigValueType(typeof(int))] AoETargetHPP = 6,

    [ConfigValueType(typeof(int))] SingleTargetRegenHPP = 7,

    [ConfigValueType(typeof(bool))] ManageKardia = 8,

    [ConfigValueType(typeof(bool))] AutoRez = 9,

    [ConfigValueType(typeof(bool))] AutoRezDPSJobs = 10,

    [ConfigValueType(typeof(bool))] AutoCleanse = 11,

    [ConfigValueType(typeof(bool))] IncludeNPCs = 12,

    [ConfigValueType(typeof(bool))] OnlyAttackInCombat = 13,

    [ConfigValueType(typeof(bool))] OrbwalkerIntegration = 14,

    [ConfigValueType(typeof(bool))] AutoRezOutOfParty = 15,

    [ConfigValueType(typeof(int))] DPSAoETargets = 16,

    [ConfigValueType(typeof(int))] SingleTargetExcogHPP = 17,

    [ConfigValueType(typeof(bool))] AutoRezDPSJobsHealersOnly = 18,

    [ConfigValueType(typeof(bool))] DPSAlwaysHardTarget = 19,

    [ConfigValueType(typeof(bool))] HealerAlwaysHardTarget = 20,

    /// <remarks>
    ///     <see cref="InCombatOnly" /> is what is being bypassed here.
    /// </remarks>
    [ConfigValueType(typeof(bool))] BypassQuest = 21,

    /// <remarks>
    ///     <see cref="InCombatOnly" /> is what is being bypassed here.
    /// </remarks>
    [ConfigValueType(typeof(bool))] BypassFATE = 22,

    [ConfigValueType(typeof(bool))] IgnoreRangeInBoss = 23,
}