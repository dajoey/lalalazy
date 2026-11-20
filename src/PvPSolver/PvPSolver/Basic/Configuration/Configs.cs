using Dalamud.Configuration;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.Logging;
using System.Collections.Concurrent;
using static RotationSolver.Basic.Configuration.ConfigTypes;

namespace RotationSolver.Basic.Configuration;

internal partial class Configs : IPluginConfiguration
{
	[JsonIgnore]
	public const string
		BasicTimer = "BasicTimer",
		BasicAutoSwitch = "BasicAutoSwitch",
		PvpSpecific = "PvpSpecific",
		BasicParams = "BasicParams",
		UiInformation = "UiInformation",
		UiWindows = "UiWindows",
		PvPSpecificControls = "PvPSpecificControls",
		AutoActionUsage = "AutoActionUsage",
		HealingActionCondition = "HealingActionCondition",
		TargetConfig = "TargetConfig",
		Extra = "Extra",
		Rotations = "Rotations",
		List = "List",
		List2 = "List2",
		List3 = "List3",
		Debug = "Debug";

	public const int CurrentVersion = 12;
	public int Version { get; set; } = CurrentVersion;

	public string LastSeenChangelog { get; set; } = "0.0.0.0";
	public bool TutorialDone { get; set; } = false;

	public List<ActionEventInfo> Events { get; private set; } = [];
	public SortedSet<Job> DisabledJobs { get; private set; } = [];

	public string[] RotationLibs { get; set; } = [];
	public List<TargetingType> TargetingTypes { get; set; } = [];


	#region PvP Specific


	#endregion

	#region PvP
	[JobConfig, UI("The HP for using Guard.",
		Filter = PvpSpecific)]
	[Range(0, 1, ConfigUnitType.Percent, 0.02f)]
	public float HealthForGuard { get; set; } = 0.15f;

	[ConditionBool, UI("Ignore TTK for PvP purposes.", Filter = PvpSpecific)]
	private static readonly bool _ignorePvPTTK = true;

	[ConditionBool, UI("Prioritize A tier tomeliths in Shatter.", Filter = PvpSpecific)]
	private static readonly bool _prioAtomelith = false;

	[ConditionBool, UI("Prioritize B tier tomeliths in Shatter.", Filter = PvpSpecific)]
	private static readonly bool _prioBtomelith = false;

	[JobConfig, UI("Ignore Invincibility for PvP purposes.", Filter = PvpSpecific)]
	private static readonly bool _ignorePvPInvincibility = false;

	[ConditionBool, UI("Auto turn off when dead in PvP.",
		 Filter = PvpSpecific)]
	private static readonly bool _autoOffWhenDeadPvP = true;

	[ConditionBool, UI("Auto turn off when PvP match ends.",
		 Filter = PvpSpecific)]
	private static readonly bool _autoOffPvPMatchEnd = true;

	[ConditionBool, UI("Auto turn on when PvP match starts.",
		 Filter = PvpSpecific)]
	private static readonly bool _autoOnPvPMatchStart = true;

	[ConditionBool, UI("Set PvP Solver to PvP specific state when enabled in PvP zone.",
		 Filter = PvpSpecific)]
	private static readonly bool _pvpStateControl = false;

	[ConditionBool, UI("Don't use any actions while in Guard.",
		 Filter = PvpSpecific)]
	private static readonly bool _pvpGuardControl = true;

	[ConditionBool, UI("Use Purify to remove Stun debuff",
		 Filter = PvpSpecific)]
	private static readonly bool _pvpPurifyStun = true;

	[ConditionBool, UI("Use Purify to remove Silence debuff",
		 Filter = PvpSpecific)]
	private static readonly bool _pvpPurifySilence = true;

	[ConditionBool, UI("Use Purify to remove Deep Freeze debuff",
		 Filter = PvpSpecific)]
	private static readonly bool _pvpPurifyDeepFreeze = true;

	[ConditionBool, UI("Use Purify to remove Miracle of Nature debuff",
		 Filter = PvpSpecific)]
	private static readonly bool _pvpPurifyMiracleOfNature = true;

	[ConditionBool, UI("Use Purify to remove Heavy debuff",
		 Filter = PvpSpecific)]
	private static readonly bool _pvpPurifyHeavy = false;

	[ConditionBool, UI("Use Purify to remove Bind debuff",
		 Filter = PvpSpecific)]
	private static readonly bool _pvpPurifyBind = false;

	[ConditionBool, UI("Lock out GCD cycle if you are below 50% HP and have over 2000 MP for heals (Experimental).",
		 Filter = PvpSpecific)]
	private static readonly bool _pvpGCDLockControl = true;

	[ConditionBool, UI("Allow Sprint when no target is set even if youre in range of hostiles (Experimental).",
		 Filter = PvpSpecific)]
	private static readonly bool _pvpAllowSprintWithoutTarget = true;

	#endregion

	[ConditionBool, UI("Intercept player input and queue it for PvP Solver to execute the action.",
	Filter = AutoActionUsage, Section = 5)]
	private static readonly bool _interceptAction3 = true;

	[ConditionBool, UI("Allow intercepting Spells.",
	Filter = AutoActionUsage, Section = 5, Parent = nameof(InterceptAction3))]
	private static readonly bool _interceptSpell3 = true;

	[ConditionBool, UI("Allow intercepting Weaponskills.",
	Filter = AutoActionUsage, Section = 5, Parent = nameof(InterceptAction3))]
	private static readonly bool _interceptWeaponskill3 = true;

	[ConditionBool, UI("Allow intercepting Abilities.",
	Filter = AutoActionUsage, Section = 5, Parent = nameof(InterceptAction3))]
	private static readonly bool _interceptAbility3 = true;

	[ConditionBool, UI("Allow intercepting actions in macros.",
	Filter = AutoActionUsage, Section = 5, Parent = nameof(InterceptAction3))]
	private static readonly bool _interceptMacro = false;

	[ConditionBool, UI("Allow intercepting actions that are currently on cooldown.",
	Filter = AutoActionUsage, Section = 5, Parent = nameof(InterceptAction3))]
	private static readonly bool _interceptCooldown = false;

	[ConditionBool, UI("Pass original player input to action manager. (Enabling this will cause clipping).",
	Filter = AutoActionUsage, Section = 5, Parent = nameof(InterceptAction3))]
	private static readonly bool _interceptPassing = true;

	[Range(1, 10, ConfigUnitType.Seconds)]
	public float InterceptActionTime { get; set; } = 5;

	/// <markdown file="Auto" name="What kind of AoE moves to use" section="Action Usage and Control">
	/// - Full: Use all available AoE actions.
	/// - Cleave: Use only single-target AoE actions.
	/// - Off: Do not use any AoE actions.
	/// </markdown>
	[UI("What kind of AoE moves to use.",
	Description = "Full: Use all available AoE actions.\nCleave: Use only single-target AoE actions.\nOff: Do not use any AoE actions.",
	Filter = AutoActionUsage, Section = 3)]
	public AoEType AoEType { get; set; } = AoEType.Full;

	[ConditionBool, UI("Ignore status application against mobs that are status capped.",
	Filter = AutoActionUsage, Section = 3)]
	private static readonly bool _statuscap2 = true;

	[ConditionBool, UI("Don't attack new mobs by AoE.", Description = "Never use any AoE action when this may attack mobs that are not hostile targets.",
		Filter = AutoActionUsage, Section = 3)]
	private static readonly bool _noNewHostiles = false;

	[ConditionBool, UI("Disable automatically during area transitions.",
	Description = "Automatically turn off combat state when moving between different areas.",
	Filter = BasicAutoSwitch)]
	private static readonly bool _autoOffBetweenArea = true;

	[ConditionBool, UI("Disable automatically during cutscenes.",
	Description = "Automatically turn off combat state during cutscenes.",
	Filter = BasicAutoSwitch)]
	private static readonly bool _autoOffCutScene = true;

	[ConditionBool, UI("Auto turn off when switching jobs",
	Description = "Automatically turn off combat state when you change your job/class.",
	Filter = BasicAutoSwitch)]
	private static readonly bool _autoOffSwitchClass = true;

	[ConditionBool, UI("Auto turn off when dead.",
	Description = "Automatically turn off combat state when your character dies.",
	Filter = BasicAutoSwitch)]
	private static readonly bool _autoOffWhenDead = true;

	[ConditionBool, UI("Enable changelog window popup on update",
	Description = "Show a popup window with the changelog when the plugin updates.",
	Filter = UiInformation)]
	private static readonly bool _changelogPopup = true;

	[ConditionBool, UI("Show plugin status in DTR bar.",
	Description = "Display the plugin's current status in the server information bar.",
	Filter = UiInformation)]
	private static readonly bool _showInfoOnDtr = true;

	[UI("DTR Behaviour", Filter = UiInformation, Parent = nameof(ShowInfoOnDtr))]
	public DTRType DTRType { get; set; } = DTRType.DTRNormal;

	[ConditionBool, UI("Display plugin status in toast popup",
	Description = "Show a toast notification with the combat state when changed.",
	Filter = UiInformation)]
	private static readonly bool _showInfoOnToast = false;

	[ConditionBool, UI("Lock movement when casting or performing certain actions.",
	Description = "Prevents your character from moving while casting or using specific actions.",
	Filter = Extra)]
	private static readonly bool _poslockCasting = false;

	[UI("", Action = ActionID.PassageOfArmsPvE, Parent = nameof(PoslockCasting))]
	public bool PosPassageOfArms { get; set; } = false;

	[UI("", Action = ActionID.FlamethrowerPvE, Parent = nameof(PoslockCasting))]
	public bool PosFlameThrower { get; set; } = false;

	[UI("", Action = ActionID.ImprovisationPvE, Parent = nameof(PoslockCasting))]
	public bool PosImprovisation { get; set; } = false;

	[ConditionBool, UI("Automatically use Anti-Knockback role actions (Arms Length, Surecast)",
	Description = "Enable to automatically use anti-knockback abilities when needed based on anti-knockback action list in List menu.",
	Filter = AutoActionUsage)]
	private static readonly bool _useKnockback = true;

	[Range(0, 30, ConfigUnitType.Yalms, 1f)]
	public float DistanceForMoving2 { get; set; } = 3f;

	[ConditionBool, UI("Allow the use of AOEs against priority-marked targets.",
	Description = "Enable to allow AoE actions to hit targets with priority markers.",
	Parent = nameof(ChooseAttackMark))]
	private static readonly bool _canAttackMarkAOE = true;

	[ConditionBool, UI("Teaching mode", Filter = UiInformation)]
	private static readonly bool _teachingMode = false;

	[ConditionBool, UI("Auto-target in teaching mode while in combat",
		Description = "When teaching mode is active, automatically switch your target to match the rotation's suggested action target. Useful for tanks and healers where the optimal target may differ from your current selection.",
		Parent = nameof(TeachingMode))]
	private static readonly bool _teachingModeAutoTarget = false;

	[ConditionBool, UI("Show target hint in Next Action window",
		Description = "When teaching mode is active, display the rotation's suggested target name below the Next Action icon. Shown in orange if you don't have that target selected, green if you do.",
		Parent = nameof(TeachingMode))]
	private static readonly bool _teachingModeShowTargetHint = false;

	[ConditionBool, UI("Simulate the effect of pressing abilities",
		Filter = UiInformation)]
	private static readonly bool _keyboardNoise = true;

	[ConditionBool, UI("Activate auto mode when countdown starts",
		Filter = BasicAutoSwitch, Section = 1)]
	private static readonly bool _startOnCountdown = true;

	[ConditionBool, UI("Start manual mode instead of auto mode when countdown starts",
			   Parent = nameof(StartOnCountdown))]
	private static readonly bool _countdownStartsManualMode = false;

	[ConditionBool, UI("Cancel auto mode if combat starts early during countdown",
		Filter = BasicAutoSwitch, Section = 1)]
	private static readonly bool _cancelStateOnCombatBeforeCountdown = false;

	[ConditionBool, UI("I understand that Auto On settings will turn PvP Solver's autorotation on, automatically.",
		Filter = BasicAutoSwitch, Section = 1)]
	private static readonly bool _AutoOnYes = false;

	[ConditionBool, UI("Auto turn on manual mode when attacked.",
			   Parent = nameof(AutoOnYes))]
	private static readonly bool _startOnAttackedBySomeone2 = false;

	[ConditionBool, UI("Auto turn on auto mode when party is in combat.",
			   Parent = nameof(AutoOnYes))]
	private static readonly bool _startOnPartyIsInCombat2 = false;

	/// <markdown file="Auto" name="Use healing abilities when playing a non-healer role" section="Healing Usage and Control">
	/// Allow usage of healing abilities when not playing as a healer (such as Vercure, Bloodbath, etc.)
	/// </markdown>
	[ConditionBool, UI("Use healing abilities when playing a non-healer role.",
		Filter = HealingActionCondition, Section = 1)]
	private static readonly bool _useHealWhenNotAHealer = true;

	[JobConfig, UI("Use interrupt abilities if possible.",
		Filter = AutoActionUsage, Section = 3,
		PvEFilter = JobFilterType.Interrupt,
		PvPFilter = JobFilterType.NoJob)]
	private static readonly bool _interruptibleMoreCheck = true;

	[ConditionBool, UI("Provoke anything not on the no provoke list.",
		Filter = AutoActionUsage, Section = 3)]
	private static readonly bool _provokeAnything = false;

	[ConditionBool, UI("Stop casting if the target dies.", Filter = Extra)]
	private static readonly bool _useStopCasting = false;

	/// <markdown file="Auto" name="Cleanse all dispellable debuffs" section="Action Usage and Control">
	/// Enabling this setting will force the usage of Esuna on all target that are affected by a
	/// cleansable debuff.
	/// </markdown>
	[ConditionBool, UI("Cleanse all dispellable debuffs regardless of healing.",
		Filter = AutoActionUsage, Section = 3,
		PvEFilter = JobFilterType.Dispel, PvPFilter = JobFilterType.NoJob)]
	private static readonly bool _dispelAll = false;

	[ConditionBool, UI("Debug Mode", Filter = Debug)]
	private static readonly bool _inDebug = false;

	[ConditionBool, UI("Make /pvpsolver Manual a toggle command.",
		Filter = BasicParams)]
	private static readonly bool _toggleManual = false;

	[ConditionBool, UI("Make /pvpsolver Auto a toggle command. (Normal behavior cycles between targeting settings)",
		Filter = BasicParams)]
	private static readonly bool _toggleAuto = false;

	[ConditionBool, UI("Only show these windows if there are enemies or in duty",
		Filter = UiWindows)]
	private static readonly bool _onlyShowWithHostileOrInDuty = false;

	[ConditionBool, UI("Show Control Window",
		Filter = UiWindows)]
	private static readonly bool _showControlWindow = false;

	[ConditionBool, UI("Lock Control Window",
		Filter = UiWindows)]
	private static readonly bool _isControlWindowLock = false;

	[ConditionBool, UI("Show Next Action Window", Filter = UiWindows)]
	private static readonly bool _showNextActionWindow = false;

	[ConditionBool, UI("Show Intercepted Action Window", Filter = UiWindows)]
	private static readonly bool _showInterceptedActionWindow = false;

	[ConditionBool, UI("No Inputs", Parent = nameof(ShowNextActionWindow))]
	private static readonly bool _isInfoWindowNoInputs = false;

	[ConditionBool, UI("No Move", Parent = nameof(ShowNextActionWindow))]
	private static readonly bool _isInfoWindowNoMove = false;

	[ConditionBool, UI("Show GCD Cooldown",
		Parent = nameof(ShowCooldownWindow))]
	private static readonly bool _showGCDCooldown = false;

	[ConditionBool, UI("Show Original Cooldown",
		Filter = UiInformation)]
	private static readonly bool _useOriginalCooldown = false;

	[ConditionBool, UI("Always Show Cooldowns", Filter = UiInformation)]
	private static readonly bool _showCooldownsAlways = false;

	[ConditionBool, UI("Show tooltips",
		Filter = UiInformation)]
	private static readonly bool _showTooltips = true;

	[ConditionBool, UI("Show Action Context Menu Enable/Disable toggle",
		Filter = UiWindows)]
	private static readonly bool _showContext = true;

	[ConditionBool, UI("Show random usage hints at top of configuration window",
		Description = "Displays rotating tips in the main panel; updates every 7 seconds.",
		Filter = UiInformation)]
	private static readonly bool _showHints = true;

	[ConditionBool, UI("Color disabled actions on hotbars",
		Description = "When enabled, actions you have disabled in PvP Solver will be tinted on in-game hotbars.",
		Filter = UiInformation)]
	private static readonly bool _reddenDisabledHotbarActions = false;

	[UI("Disabled actions hotbar tint color", Parent = nameof(ReddenDisabledHotbarActions), Filter = UiInformation)]
	public Vector4 HotbarDisabledTintColor { get; set; } = new(1f, 0f, 0f, 0.40f);

	[ConditionBool, UI("Display do action feedback on toast",
		Filter = UiInformation)]
	private static readonly bool _showToastsAboutDoAction = false;

	[ConditionBool, UI("Allow rotations that use this config to use abilities defined in the rotation as burst", Filter = AutoActionUsage, Section = 4)]
	private static readonly bool _autoBurst = true;

	/// <markdown file="Auto" name="Disable hostile actions if something is casting an action on the Gaze/Stop list" section="Action Usage and Control">
	/// This setting is linked with the <see cref="RotationSolver.Basic.Configuration.OtherConfiguration.HostileCastingStop">Gaze/Stop list.</see>
	/// </markdown>
	[ConditionBool, UI("Disable hostile actions if something is casting an action on the Gaze/Stop list (EXPERIMENTAL)", Filter = AutoActionUsage, Section = 4)]
	private static readonly bool _castingStop = false;

	[Range(0, 15, ConfigUnitType.Seconds)]
	public float CastingStopTime { get; set; } = 2.5f;

	[ConditionBool, UI("Disable for the entire duration (Enabling this will prevent your actions for the entire cast.)", Filter = AutoActionUsage, Section = 4, Parent = nameof(CastingStop))]
	private static readonly bool _castingStopCalculate = false;

	/// <markdown file="Auto" name="Automatic Healing Thresholds" section="Healing Usage and Control" isSubsection="1">
	/// When enabled, you can customize the healing thresholds for when healing will be cast occur on target(s).
	/// </markdown>
	[ConditionBool, UI("Automatic Healing Thresholds", Filter = HealingActionCondition, Section = 1, Order = 1)]
	private static readonly bool _autoHeal = true;

	/// <markdown file="Auto" name="Stop Healing Cast After Reaching Threshold" section="Healing Usage and Control" isSubsection="1">
	/// When enabled, you can customize the healing thresholds for when healing will be cast occur on target(s).
	/// </markdown>
	[ConditionBool, UI("Stop single target GCD healing after reaching threshold. (EXTREMELY Experimental)", Filter = HealingActionCondition, Section = 1, Order = 2, Description = "If you have another healer on the team, their healing might put the target player(s) above the healing threshold and you'll waste MP. This interrupts the cast if it happens.")]
	private static readonly bool _stopHealingAfterThresholdExperimental2 = false;

	/// <markdown file="Auto" name="Auto-use oGCD abilities" section="Action Usage and Control" isSubsection="1">
	/// Whether to use oGCD abilities or not at all.
	/// </markdown>
	[ConditionBool, UI("Auto-use oGCD abilities", Filter = AutoActionUsage)]
	private static readonly bool _useAbility = true;

	[ConditionBool, UI("Use defensive actions", Filter = AutoActionUsage, Description = "It is recommended to check this option if you can plan the heal and defense ability usage by yourself.")]
	private static readonly bool _useDefenseAbility = true;

	[ConditionBool, UI("Automatically use Single Target defensive actions", Filter = AutoActionUsage, Parent = nameof(UseDefenseAbility))]
	private static readonly bool _useSTDefense = true;

	[ConditionBool, UI("Automatically use AOE defensive actions", Filter = AutoActionUsage, Parent = nameof(UseDefenseAbility))]
	private static readonly bool _useAOEDefense = true;


	[Range(1, 8, ConfigUnitType.None, 0.05f)]
	public int AutoDefenseNumber { get; set; } = 2;

	[JobConfig, Range(0, 1, ConfigUnitType.Percent, 0.02f)]
	[UI("HP%% needed to use single/self targetted mitigation on Tanks", Parent = nameof(UseDefenseAbility),
		PvEFilter = JobFilterType.Tank)]
	private readonly float _healthForAutoDefense = 1;

	[ConditionBool, UI("Automatically activate tank stance", Parent = nameof(UseAbility),
		PvEFilter = JobFilterType.Tank)]
	private static readonly bool _autoTankStance = true;

	[ConditionBool, UI("Auto provoke when there is another tank in party", Description = "Automatically use provoke when an enemy is attacking a non-tank member of the party while there is more than one tank in party.",
		Parent = nameof(UseAbility), PvEFilter = JobFilterType.Tank)]
	private static readonly bool _autoProvokeForTank = true;

	/// <markdown file="Auto" name="Auto True North" section="Healing Usage and Control" subsection="RotationSolver.Basic.Configuration.Configs._useAbility">
	/// Whether to cast True North when playing as a melee DPS when you do not have the right
	/// positional on the enemy.
	/// </markdown>
	[ConditionBool, UI("Auto True North (Melee DPS)",
		Parent = nameof(UseAbility),
		PvEFilter = JobFilterType.Melee)]
	private static readonly bool _autoUseTrueNorth = true;

	[ConditionBool, UI("Use movement speed increase abilities when out of combat and in duty.", Parent = nameof(UseAbility))]
	private static readonly bool _autoSpeedOutOfCombat = true;

	[ConditionBool, UI("Use beneficial ground-targeted actions", Description = "1.    Self-Target Fallback:\r\nIf range is zero, always targets the player and returns all affectable targets at the player's position.\r\n2.    Preferred Positions (OnLocations):\r\n•    Tries to get predefined beneficial positions for the current territory.\r\n•    If none are found and the content is a trial or raid, uses fallback points (e.g., 0,0 or 100,100 point as those are the center of arenas most of the time).\r\n•    Picks the closest point to the player, applies a small random offset, and checks if it’s within effect range.\r\n•    If so, returns that as the target area.\r\n3.    Boss Positional Fallback:\r\n•    If the current target is a boss with positional requirements and within range, uses the boss’s position (or a point within range) as the target area.\r\n4.    Party Member Fallback:\r\n•    Gathers party members within range + effect range.\r\n•    Attempts to find a party member who is being attacked (tank or focus target).\r\n•    If found, calculates whether to stay at the player’s position or move closer to the tank, based on distances and effect range.\r\n•    If not found or not needed, defaults to the player’s position.", Filter = HealingActionCondition, Section = 3)]
	private static readonly bool _useGroundBeneficialAbility = true;

	/// <markdown file="Auto" name="Use beneficial ground-targeted actions when moving" section="Healing Usage and Control">
	/// Enable to allow the usage of ground AoE actions while moving, such as Earthly Star (AST), Sacred Soil (SCH), etc.
	/// </markdown>
	[JobConfig, UI("Use beneficial ground-targeted actions when moving.", Parent = nameof(UseGroundBeneficialAbility))]
	private static readonly bool _useGroundBeneficialAbilityWhenMoving = false;

	[JobConfig, UI("Use beneficial ground-targeted actions only on self, skipping other logic.", Parent = nameof(UseGroundBeneficialAbility))]
	private static readonly bool _useGroundBeneficialAbilityOnlySelf = false;

	[JobConfig, UI("Use beneficial ground-targeted actions on party Tank if present, skipping other logic.", Parent = nameof(UseGroundBeneficialAbility))]
	private static readonly bool _useTargetTankForGroundHeal = false;

	[ConditionBool, UI("Show Cooldown Window", Filter = UiWindows)]
	private static readonly bool _showCooldownWindow = false;

	[ConditionBool, UI("Show Action Timeline Window", Filter = UiWindows)]
	private static readonly bool _showActionTimelineWindow = false;

	[ConditionBool, UI("Only show timeline in combat", Parent = nameof(ShowActionTimelineWindow))]
	private static readonly bool _actionTimelineOnlyInCombat = true;

	[ConditionBool, UI("Only show timeline when PvP Solver is active", Parent = nameof(ShowActionTimelineWindow))]
	private static readonly bool _actionTimelineOnlyWhenActive = true;

	[ConditionBool, UI("Show oGCD actions in timeline", Parent = nameof(ShowActionTimelineWindow))]
	private static readonly bool _actionTimelineShowOGCD = true;

	[ConditionBool, UI("Show auto-attacks in timeline", Parent = nameof(ShowActionTimelineWindow))]
	private static readonly bool _actionTimelineShowAutoAttack = false;

	[ConditionBool, UI("Save timeline to JSON file after combat", Parent = nameof(ShowActionTimelineWindow))]
	private static readonly bool _actionTimelineSaveToFile = false;

	[ConditionBool, UI("Record AOE actions", Filter = List)]
	private static readonly bool _recordCastingArea = true;

	[ConditionBool, UI("Auto turn off PvP Solver when combat is over for more than:",
		Filter = BasicAutoSwitch)]
	private static readonly bool _autoOffAfterCombat = true;

	[ConditionBool, UI("Enable PvP Solver click counter in main menu",
		Filter = Extra)]
	private static readonly bool _enableClickingCount = true;

	[ConditionBool, UI("Hide all warnings",
		Filter = UiInformation)]
	private static readonly bool _hideWarning = false;

	/// <markdown file="Auto" name="Only heal self when not a Healer" section="Healing Usage and Control">
	/// When enabled and not a healer, skills that can target someone and heal will only target you.
	/// </markdown>
	[ConditionBool, UI("Only heal self when not a Healer",
		Filter = HealingActionCondition, Section = 1)]
	private static readonly bool _onlyHealSelfWhenNoHealer = false;

	[ConditionBool, UI("Only use healing abilities as a non-healer if there are no living healers in the party.",
	Description = "When enabled, non-healer jobs (such as DPS or tanks) will only use healing abilities if there are no healers in the party, or if all healers are incapacitated (at 0 HP). \r\nIf at least one healer is alive, non-healers will not use healing abilities.",
	Filter = HealingActionCondition, Section = 1)]
	private static readonly bool _onlyHealAsNonHealIfNoHealers = false;

	[ConditionBool, UI("Show toggled setting and new value in chat.",
		Filter = UiInformation)]
	private static readonly bool _ShowToggledSettingInChat = false;

	[ConditionBool, UI("Record knockback actions", Filter = List2)]
	private static readonly bool _recordKnockbackies = false;

	[ConditionBool, UI("Set Blue Mage Actions Automatically", Description = "When using a Blue Mage Rotation, PvP Solver can automatically set your spell book to the spells required by that rotation.", Filter = Extra)]
	private static readonly bool _setBluActions2 = false;

	#region Float
	[Range(0, 600, ConfigUnitType.Seconds)]
	public float AutoOffAfterCombatTime { get; set; } = 30;

	[Range(0, 90, ConfigUnitType.Degree, 0.02f)]
	public float AngleOfVisionCone { get; set; } = 45;

	/// <markdown file="Auto" name="Melee Range action using offset" section="Action Usage and Control">
	/// Additional buffer in yalms where ranged attacks will be used for melee classes. For example,
	/// if you are playing as Samurai and this setting is set to `1`, Enpi will only be used starting
	/// at 4 yalms.
	///
	/// This is because the default "overflow" for the melee range is 3 yalms, which means you can cast
	/// melee attacks 3 yalms outside the enemy's hitbox. This setting (the offset) takes max melee range and
	/// adds the value you set.
	///
	/// This setting exists to leave you time to approach and enter in melee range of the enemy without wasting
	/// a GCD.
	/// </markdown>
	[Range(0, 5, ConfigUnitType.Yalms, 0.02f)]
	public float MeleeRangeOffset { get; set; } = 1;

	[Range(0, 1, ConfigUnitType.Percent, 0.002f)]
	public float HealWhenNothingTodoBelow { get; set; } = 0.8f;

	[Range(0, 1, ConfigUnitType.Percent, 0.02f)]
	public float HealthTankRatio { get; set; } = 0.45f;

	[Range(0, 1, ConfigUnitType.Percent, 0.02f)]
	public float HealthHealerRatio { get; set; } = 0.4f;

	[Range(0, 1, ConfigUnitType.Percent, 0.02f)]
	public float HealthSelfRatio { get; set; } = 0.4f;

	[Range(0, 10, ConfigUnitType.Seconds, 0.002f)]
	public Vector2 EsunaDelay { get; set; } = new(0f, 0f);

	/// <markdown file="Extra" name="HP standard deviation for using AoE heal">
	/// Controls how much party members' HP must differ before using AoE healing instead
	/// of single-target heals. Lower values require party members to have more similar HP
	/// for AoE healing to trigger (more selective). Higher values allow AoE healing even
	/// when HP differences are larger (less selective). Adjust only if you want to fine-tune
	/// AoE heal behavior.
	/// </markdown>
	[Range(0, 0.5f, ConfigUnitType.Percent, 0.02f)]
	public float HealthDifference { get; set; } = 0.25f;

	[ConditionBool, UI("Heal party members when not in combat.",
		Filter = HealingActionCondition, Section = 3)]
	private static readonly bool _healOutOfCombat = false;

	[ConditionBool, UI("Heal party members with GCD if there is nothing to do in combat.",
		Filter = HealingActionCondition, Section = 3)]
	private static readonly bool _healWhenNothingTodo = true;

	[ConditionBool, UI("Prioritize Low HP tank for tankbusters.",
		Filter = HealingActionCondition, Section = 3)]
	private static readonly bool _priolowtank = false;

	/// <markdown file="Basic" name="The duration of special windows opened by /pvpsolver commands by default">
	/// The duration of special windows opened by /pvpsolver commands by default.
	/// (Found in Main => Macros)
	/// </markdown>
	[Range(1, 20, ConfigUnitType.Seconds, 1f)]
	public float SpecialDuration { get; set; } = 3;

	[Range(0, 3, ConfigUnitType.Seconds, 0.002f)]
	public Vector2 StopCastingDelay { get; set; } = new(0.5f, 1);

	[Range(0, 3, ConfigUnitType.Seconds, 0.002f)]
	public Vector2 InterruptDelay { get; set; } = new(0.5f, 1);

	[Range(0, 10, ConfigUnitType.Seconds, 0.05f)]
	public Vector2 ProvokeDelay { get; set; } = new(0.5f, 1);

	[Range(0, 10, ConfigUnitType.Seconds, 0.002f)]
	public Vector2 NotInCombatDelay { get; set; } = new(2, 3);

	/// <markdown file="Basic" name="Clicking actions random delay range">
	/// Delay between showing the clicking/pressing effect on the actions
	/// in your hotbars.
	/// </markdown>
	[Range(0.00f, 0.25f, ConfigUnitType.Seconds, 0.002f)]
	public Vector2 ClickingDelay { get; set; } = new(0.1f, 0.2f);

	[Range(0, 5, ConfigUnitType.Seconds, 0.05f)]
	public Vector2 HealWhenNothingTodoDelay { get; set; } = new(0.5f, 1);

	/// <markdown file="Basic" name="How soon before countdown is finished to start casting or attacking">
	/// How soon before countdown is finished to start casting or attacking.
	/// </markdown>
	[Range(0, 0.7f, ConfigUnitType.Seconds, 0.002f)]
	public float CountDownAhead { get; set; } = 0.4f;

	[Range(0, 80, ConfigUnitType.Pixels, 0.2f)]
	public float CooldownWindowIconSize { get; set; } = 30;

	[Range(0, 10, ConfigUnitType.Percent, 0.02f)]
	public float ControlWindowNextSizeRatio { get; set; } = 1.5f;

	[Range(0, 80, ConfigUnitType.Pixels, 0.2f)]
	public float ControlWindowGCDSize { get; set; } = 40;

	[Range(0, 80, ConfigUnitType.Pixels, 0.2f)]
	public float ControlWindow0GCDSize { get; set; } = 30;

	[Range(2, 30, ConfigUnitType.Yalms)]
	public float ControlProgressHeight { get; set; } = 8;

	[Range(0, 30, ConfigUnitType.Seconds, 0.02f)]
	public float AutoHealTimeToKill { get; set; } = 8f;

	[JobConfig, Range(0, 0.3f, ConfigUnitType.Seconds, 0.002f)]
	public float MinUpdatingTime { get; set; } = 0.00f;

	/// <markdown file="Basic" name="Action Ahead">
	/// Percent of your GCD time remaining on a GCD cycle before PvP Solver will try to queue the next GCD.
	///
	/// This setting controls how many oGCDs PvP Solver will try to fit in a single GCD window.
	/// Lower numbers mean more oGCDs, but potentially more GCD clipping.
	/// </markdown>
	[JobConfig, Range(0.05f, 0.25f, ConfigUnitType.Percent)]
	[UI("Action Ahead (Percent of your GCD time remaining on a GCD cycle before PvP Solver will try to queue the next GCD)", Filter = BasicTimer,
	Description = "This setting controls how many oGCDs PvP Solver will try to fit in a single GCD window\nLower numbers mean more oGCDs, but potentially more GCD clipping")]
	private readonly float _action6head = 0.25f;

	/// <summary>
	/// Remove extra lag-induced animation lock delay from instant casts (read tooltip!)
	/// Do NOT use with XivAlexander or NoClippy - this should automatically disable itself if they are detected, but double check first!
	/// </summary>
	[ConditionBool, UI("Remove extra lag-induced animation lock delay from instant casts (read tooltip!)",
	Description = "Do NOT use with XivAlexander or NoClippy - this should automatically disable itself if they are detected, but double check first!",
	Filter = Extra)]
	private static readonly bool _removeAnimationLockDelay = false;

	/// <summary>
	/// Animation lock max. simulated delay in milliseconds
	/// Configures the maximum simulated delay in milliseconds when using animation lock removal - this is required and cannot be reduced to zero.
	/// Setting this to 20ms will enable triple-weaving when using autorotation. The minimum setting to remove triple-weaving is 26ms.
	/// The minimum of 20ms has been accepted by FFLogs and should not cause issues with your logs.
	/// </summary>
	[Range(20, 50, ConfigUnitType.None, 1f)]
	public int AnimationLockDelayMax2 { get; set; } = 26;

	[Range(0.3f, 0.95f, ConfigUnitType.None, 0.01f)]
	public float AnimLockDelaySmoothing { get; set; } = 0.3f;

	/// <summary>
	/// Remove extra framerate-induced cooldown delay
	/// Dynamically adjusts cooldown and animation locks to ensure queued actions resolve immediately regardless of framerate limitations
	/// </summary>
	[ConditionBool, UI("Remove extra framerate-induced cooldown delay",
	Description = "Dynamically adjusts cooldown and animation locks to ensure queued actions resolve immediately regardless of framerate limitations",
	Filter = Extra)]
	private static readonly bool _removeCooldownDelay = false;

	[Range(10, 150, ConfigUnitType.None, 1f)]
	public int CooldownAdjustMaxMs { get; set; } = 100;

		[UI("Highlight color.", Parent = nameof(TeachingMode))]
	public Vector4 TeachingModeColor { get; set; } = new(0f, 1f, 0f, 1f);

	[UI("Target color", Parent = nameof(TargetColor))]
	public Vector4 TargetColor { get; set; } = new(1f, 0.2f, 0f, 0.8f);

	[UI("Locked Control Window's Background", Parent = nameof(ShowControlWindow))]
	public Vector4 ControlWindowLockBg { get; set; } = new(0, 0, 0, 0.55f);

	[UI("Unlocked Control Window's Background", Parent = nameof(ShowControlWindow))]
	public Vector4 ControlWindowUnlockBg { get; set; } = new(0, 0, 0, 0.75f);

	[UI("Info Window's Background", Filter = UiWindows)]
	public Vector4 InfoWindowBg { get; set; } = new(0, 0, 0, 0.4f);
	#endregion

	#region Target
	[ConditionBool, UI("Use movement actions towards the object/mob in the center of the screen",
	Description = "If enabled, movement actions target the object or mob at the center of your screen. If disabled, they target the object or mob your character is facing.",
	Filter = TargetConfig, Section = 2)]
	private static readonly bool _moveTowardsScreenCenter = false;

	[ConditionBool, UI("Prioritize mob/object targets with attack markers",
	Description = "Targets with attack markers will be prioritized for actions.",
	Filter = TargetConfig)]
	private static readonly bool _chooseAttackMark = true;

	[ConditionBool, UI("Prioritize enemy parts",
	Description = "Enemy parts, such as Titan's Heart, will be prioritized as targets.",
	Filter = TargetConfig)]
	private static readonly bool _prioEnemyParts = true;

	[ConditionBool, UI("Never attack targets with stop markers.",
	Description = "Targets with stop markers will not be attacked.",
	Filter = TargetConfig)]
	private static readonly bool _filterStopMark = true;

	[ConditionBool, UI("Treat 1hp targets as invincible.",
	Description = "Targets with only 1 HP will be treated as invincible and ignored; for rare cases where target is invincible but is not given a status for it.",
	Filter = TargetConfig)]
	private static readonly bool _filterOneHPInvincible = true;

	[ConditionBool, UI("Move to the furthest position for targeting area movement actions.",
		Filter = TargetConfig, Section = 2)]
	private static readonly bool _moveAreaActionFarthest = false;

	[ConditionBool, UI("Hard Target for all actions", Description = "If this is disabled, PvP Solver will only use the game's built-in soft-targeting for allies for heals, shields, etc.",
		Filter = TargetConfig, Section = 3)]
	private static readonly bool _switchTargetFriendly2 = false;

	[ConditionBool, UI("Set target to closest targetable enemy if no valid action target nearby and target not set (This works in Manual mode as well)",
		Filter = TargetConfig, Section = 3)]
	private static readonly bool _targetFreely = false;

	[ConditionBool, UI("Only attack targets in view.",
		Filter = TargetConfig, Section = 1)]
	private static readonly bool _onlyAttackInView = false;

	[ConditionBool, UI("Only attack targets in vision cone",
				Filter = TargetConfig, Section = 1)]
	private static readonly bool _onlyAttackInVisionCone = false;

	[ConditionBool, UI("Ignore target dummies",
					   Filter = TargetConfig, Section = 1)]
	private static readonly bool _disableTargetDummys = false;

	[ConditionBool, UI("Delay autotarget. (Experimental)",
		Filter = TargetConfig)]
	private static readonly bool _targetDelayEnable = false;

	[Range(0, 3, ConfigUnitType.Seconds)]
	public Vector2 TargetDelay { get; set; } = new(0f, 0f);

	[Range(0, 90, ConfigUnitType.Degree, 0.02f)]
	public float MoveTargetAngle { get; set; } = 24;

	[ConditionBool, UI("Treat target dummy as a boss.",
		Filter = TargetConfig, Section = 3)]
	private static readonly bool _dummyBoss = true;

	[Range(10, 1800, ConfigUnitType.Seconds, 0.02f)]
	public float BossTimeToKill { get; set; } = 90;

	[Range(0, 60, ConfigUnitType.Seconds, 0.02f)]
	public float DyingTimeToKill { get; set; } = 10;

	[Range(0, 0.1f, ConfigUnitType.Percent, 0.01f)]
	public float IsDyingConfig { get; set; } = 0.02f;

	[ConditionBool, UI("Prioritize Low HP targets instead of High HP targets when using Small Target and multiple Small targets present.",
		Filter = TargetConfig)]
	private static readonly bool _smallHP = false;

	[ConditionBool, UI("Prioritize Low HP targets instead of High HP targets when using Big Target and multiple Big targets present.",
		Filter = TargetConfig)]
	private static readonly bool _bigHP = false;

	[ConditionBool, UI("PvP: stay on the current target while your own timed debuff is still ticking on it.",
		Description = "Stops auto-target from jumping off an enemy you have just committed a timed debuff to (Wildfire and similar) because another enemy briefly scores better under the active targeting mode. Only holds the target while it is still a legal target for the action being used, so it never blocks a switch the action could not make anyway.",
		Filter = TargetConfig)]
	private static readonly bool _stickyTarget = true;

	[Range(1, 60, ConfigUnitType.Seconds, 0.02f)]
	public float StickyTargetMaxRemaining { get; set; } = 30f;

	[UI("/pvpsolver Cycle behaviour", Filter = TargetConfig)]
	public CycleType CycleType { get; set; } = CycleType.CycleNormal;

	[JobConfig, UI("Engage settings", Filter = TargetConfig, PvPFilter = JobFilterType.NoJob)]
	private readonly TargetHostileType _hostileType = TargetHostileType.AllTargetsWhenSoloInDuty;
	#endregion

	#region Integer

	public int ActionSequencerIndex { get; set; }

	[UI("The modifier key to unlock the movement temporarily", Description = "RB is for gamepad player", Parent = nameof(PoslockCasting))]
	public ConsoleModifiers PoslockModifier { get; set; }

	[Range(0, 5, ConfigUnitType.None, 1)]
	[UI("Random range of simulated presses per action", Parent = nameof(KeyboardNoise))]
	public Vector2Int KeyboardNoisePresses { get; set; } = new(2, 3);

	[Range(0, 10, ConfigUnitType.None)]
	public int TargetingIndex { get; set; }

	#endregion

	#region Jobs

	[JobConfig, UI("MP threshold under which to use Lucid Dreaming", Filter = HealingActionCondition)]
	[Range(0, 10000, ConfigUnitType.None)]
	private readonly int _lucidDreamingMpThreshold = 6000;

	/// <markdown file="Auto" name="HP threshold for AoE healing oGCDs (Heal-over-time)" section="Healing Usage and Control" subsection="RotationSolver.Basic.Configuration.Configs._autoHeal">
	/// Relates to **oGCD** AoE healing abilities **when they have a heal-over-time already applied to them, by you or a teammate**.
	/// 
	/// This is calculated with the <see cref="RotationSolver.Basic.Configuration.Configs.HealthDifference">standard AoE healing deviation</see>.
	///
	/// Use the button this setting to calculate your preferred values.
	/// </markdown>
	[JobConfig, Range(0, 1, ConfigUnitType.Percent)]
	private readonly float _healthAreaAbilityHot = 0.55f;

	/// <markdown file="Auto" name="HP threshold for AoE healing GCDs (Heal-over-time)" section="Healing Usage and Control" subsection="RotationSolver.Basic.Configuration.Configs._autoHeal">
	/// Relates to **GCD** AoE healing abilities **when they have a heal-over-time already applied to them, by you or a teammate**.
	/// 
	/// This is calculated with the <see cref="RotationSolver.Basic.Configuration.Configs.HealthDifference">standard AoE healing deviation</see>.
	///
	/// Use the button this setting to calculate your preferred values.
	/// </markdown>
	[JobConfig, Range(0, 1, ConfigUnitType.Percent)]
	private readonly float _healthAreaSpellHot = 0.55f;

	/// <markdown file="Auto" name="HP threshold for AoE healing GCDs (No heal-over-time)" section="Healing Usage and Control" subsection="RotationSolver.Basic.Configuration.Configs._autoHeal">
	/// Relates to **oGCD** AoE healing abilities.
	/// 
	/// This is calculated with the <see cref="RotationSolver.Basic.Configuration.Configs.HealthDifference">standard AoE healing deviation</see>.
	///
	/// Use the button this setting to calculate your preferred values.
	/// </markdown> 
	[JobConfig, Range(0, 1, ConfigUnitType.Percent)]
	private readonly float _healthAreaAbility = 0.75f;

	/// <markdown file="Auto" name="HP threshold for AoE healing GCDs (No heal-over-time)" section="Healing Usage and Control" subsection="RotationSolver.Basic.Configuration.Configs._autoHeal">
	/// Relates to **GCD** AoE healing abilities.
	/// 
	/// This is calculated with the <see cref="RotationSolver.Basic.Configuration.Configs.HealthDifference">standard AoE healing deviation</see>.
	///
	/// Use the button this setting to calculate your preferred values.
	/// </markdown> 
	[JobConfig, Range(0, 1, ConfigUnitType.Percent)]
	private readonly float _healthAreaSpell = 0.65f;

	/// <markdown file="Auto" name="HP threshold for single-target healing oGCDs (Heal-over-time)" section="Healing Usage and Control" subsection="RotationSolver.Basic.Configuration.Configs._autoHeal">
	/// Relates to **oGCD** single-target healing abilities **when they have a heal-over-time already applied to them, by you or a teammate**.
	/// </markdown>
	[JobConfig, Range(0, 1, ConfigUnitType.Percent)]
	private readonly float _healthSingleAbilityHot = 0.65f;

	/// <markdown file="Auto" name="HP threshold for single-target healing GCDs (Heal-over-time)" section="Healing Usage and Control" subsection="RotationSolver.Basic.Configuration.Configs._autoHeal">
	/// Relates to **GCD** single-target healing abilities **when they have a heal-over-time already applied to them, by you or a teammate**.
	/// </markdown>
	[JobConfig, Range(0, 1, ConfigUnitType.Percent)]
	private readonly float _healthSingleSpellHot = 0.55f;

	/// <markdown file="Auto" name="HP threshold for single-target healing oGCDs (No Heal-over-time)" section="Healing Usage and Control" subsection="RotationSolver.Basic.Configuration.Configs._autoHeal">
	/// Relates to **oGCD** AoE healing abilities.
	/// </markdown>
	[JobConfig, Range(0, 1, ConfigUnitType.Percent)]
	private readonly float _healthSingleAbility = 0.7f;

	/// <markdown file="Auto" name="HP threshold for single-target healing GCDs (No Heal-over-time)" section="Healing Usage and Control" subsection="RotationSolver.Basic.Configuration.Configs._autoHeal">
	/// Relates to **GCD** single-target healing abilities.
	/// </markdown>
	[JobConfig, Range(0, 1, ConfigUnitType.Percent)]
	private readonly float _healthSingleSpell = 0.65f;

	/// <markdown file="Auto" name="The HP% for tank to use invulnerability" section="Action Usage and Control">
	/// The threshold to automatically use your tank invulnerability action when falling below the HP percentage set.
	/// </markdown>
	[JobConfig, Range(0, 1, ConfigUnitType.Percent, 0.02f)]
	[UI("The HP%% for tank to use invulnerability",
		Filter = AutoActionUsage, Section = 3,
		PvEFilter = JobFilterType.Tank, PvPFilter = JobFilterType.NoJob)]
	private readonly float _healthForDyingTanks = 0.15f;

	[JobConfig]
	private readonly string _PvPRotationChoice = string.Empty;

	[JobConfig]
	private readonly string _rotationChoice = string.Empty;
	#endregion

	[JobConfig]
	private readonly ConcurrentDictionary<uint, ActionConfig> _rotationActionConfig = new();

	[JobChoiceConfig]
	private readonly ConcurrentDictionary<string, string> _rotationConfigurations = new();


	public void Save()
	{
#if DEBUG
		PluginLog.Information("Saved configurations.");
#endif
		var path = Svc.PluginInterface.ConfigFile.FullName;
		var tempPath = path + ".tmp";
		File.WriteAllText(tempPath, JsonConvert.SerializeObject(this, Formatting.Indented));
		File.Move(tempPath, path, true);
	}

	public static Configs Migrate(Configs oldConfigs)
	{
		// Implement migration logic if needed
		if (oldConfigs.Version != CurrentVersion)
		{
			// Reset to default if versions do not match
			return new Configs();
		}
		return oldConfigs;
	}

	public void Backup()
	{
		Save();
		File.Copy(Svc.PluginInterface.ConfigFile.FullName, Svc.PluginInterface.ConfigFile.Directory + "\\RotationSolver_Backup.json", true);
		Svc.Toasts.ShowNormal("Configs backed up.");
	}

	public void Restore()
	{
		File.Copy(Svc.PluginInterface.ConfigFile.FullName, Svc.PluginInterface.ConfigFile.Directory + "\\RotationSolver_SafetySave.json", true);
		File.Copy(Svc.PluginInterface.ConfigFile.Directory + "\\RotationSolver_Backup.json", Svc.PluginInterface.ConfigFile.FullName, true);

		Configs restoredConfigs = JsonConvert.DeserializeObject<Configs>(
									  File.ReadAllText(Svc.PluginInterface.ConfigFile.FullName))
								  ?? new Configs();

		if (restoredConfigs.Version != CurrentVersion)
		{
			Svc.Toasts.ShowNormal("Backed up configs are not compatible with the current version.");
			return;
		}

		Service.Config = restoredConfigs;
		Save();
		Svc.Toasts.ShowNormal("Configs restored. Closing to set.");
		DataCenter.HoldingRestore = true;
	}
}
