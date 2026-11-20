using System.ComponentModel;

namespace RotationSolver.Data
{
	internal enum UiString
	{
		[Description("No rotation is loaded here. Job options are in the PvP Solver settings window (/pvpsolver).")]
		ConfigWindow_NoRotation,
		[Description("Remove")]
		ConfigWindow_List_Remove,

		[Description("Analyzes PvP combat information in every frame and finds the best action.")]
		ConfigWindow_About_Punchline,

		[Description("Click to switch rotations")]
		ConfigWindow_Helper_SwitchRotation,

		[Description("Search Result")]
		ConfigWindow_Search_Result,

		[Description("This includes almost all information available in one combat frame, including the status of all party members, hostile target statuses, skill cooldowns, MP and HP of characters, character locations, hostile target casting status, combo state, combat duration, player level, etc.\n\nIt will then highlight the best action on the hotbar, or help you click it.")]
		ConfigWindow_About_Description,

		[Description("This is designed for PvP combat only. Do not use in PvE zones. Ensure you pay attention to mechanics and map conditions!")]
		ConfigWindow_About_Warning,

		[Description("PvP Solver has helped you by clicking actions {0:N0} times.")]
		ConfigWindow_About_ClickingCount,

		[Description("State Macros")]
		ConfigWindow_About_Macros,

		[Description("Action and Setting Macros")]
		ConfigWindow_About_SettingMacros,

		[Description("Compatibility")]
		ConfigWindow_About_Compatibility,


		[Description("Links")]
		ConfigWindow_About_Links,

		[Description("PvP Solver helps you choose targets and click actions. Any plugin that changes these will affect its decisions.\n\nHere is a list of known incompatible plugins:")]
		ConfigWindow_About_Compatibility_Description,

		[Description("Cannot properly execute the behavior that PvP Solver wants to perform.")]
		ConfigWindow_About_Compatibility_Mistake,

		[Description("Conflicts with PvP Solver decision-making")]
		ConfigWindow_About_Compatibility_Mislead,

		[Description("Causes the game to crash")]
		ConfigWindow_About_Compatibility_Crash,

		[Description("Open Config Folder")]
		ConfigWindow_About_OpenConfigFolder,

		[Description("Description")]
		ConfigWindow_Rotation_Description,

		[Description("Configuration")]
		ConfigWindow_Rotation_Configuration,

		[Description("Status")]
		ConfigWindow_Rotation_Status,

		[Description("Used to customize when PvP Solver uses specific actions automatically. Click on an action's icon in the left list. Below, you may set the conditions for when that specific action is used. Each action can have different conditions to override the default rotation behavior.")]
		ConfigWindow_Actions_Description,

		[Description("Show on CD window")]
		ConfigWindow_Actions_ShowOnCDWindow,

		[Description("Prevent this action against a curated list of mobs (ie. Jagd Dolls)")]
		ConfigWindow_Actions_IsRestrictedDOT,

		[Description("Allow action to be restricted by the minimum HP feature")]
		ConfigWindow_Actions_MinHPFeature,

		[Description("If target is below this percent, do not use this action")]
		ConfigWindow_Actions_MinHPPercent,

		[Description("Time-to-kill threshold required for this action to be used")]
		ConfigWindow_Actions_TTK,

		[Description("Number of targets needed to use this action")]
		ConfigWindow_Actions_AoeCount,

		[Description("Should this action check needed status effects")]
		ConfigWindow_Actions_CheckStatus,

		[Description("Number of GCDs before the DOT/Status effect is reapplied")]
		ConfigWindow_Actions_GcdCount,

		[Description("HP ratio for automatic healing (only applies to healing actions)")]
		ConfigWindow_Actions_HealRatio,

		[Description("In this window, you can set parameters that can be customized using lists.")]
		ConfigWindow_List_Description,

		[Description("Statuses")]
		ConfigWindow_List_Statuses,

		[Description("Actions")]
		ConfigWindow_List_Actions,

		[Description("Map-specific settings")]
		ConfigWindow_List_Territories,

		[Description("Status name or ID")]
		ConfigWindow_List_StatusNameOrId,

		[Description("Invulnerability")]
		ConfigWindow_List_Invincibility,

		[Description("Priority")]
		ConfigWindow_List_Priority,

		[Description("Dispellable debuffs")]
		ConfigWindow_List_DangerousStatus,

		[Description("No-casting debuffs")]
		ConfigWindow_List_NoCastingStatus,

		[Description("Ignores target if it has one of these statuses")]
		ConfigWindow_List_InvincibilityDesc,

		[Description("Attacks the target first if it has one of these statuses")]
		ConfigWindow_List_PriorityDesc,

		[Description("Dispellable debuffs list")]
		ConfigWindow_List_DangerousStatusDesc,

		[Description("Do not take action if you have one of these debuffs")]
		ConfigWindow_List_NoCastingStatusDesc,

		[Description("Copy to Clipboard")]
		ConfigWindow_Actions_Copy,

		[Description("From Clipboard")]
		ActionSequencer_FromClipboard,

		[Description("Add Status")]
		ConfigWindow_List_AddStatus,

		[Description("Action name or ID")]
		ConfigWindow_List_ActionNameOrId,

		[Description("Tank Buster")]
		ConfigWindow_List_HostileCastingTank,

		[Description("AoE")]
		ConfigWindow_List_HostileCastingArea,

		[Description("Knockback")]
		ConfigWindow_List_HostileCastingKnockback,

		[Description("Gaze/Stop")]
		ConfigWindow_List_HostileCastingStop,

		[Description("Use tank personal damage mitigation abilities if the target is casting any of these actions")]
		ConfigWindow_List_HostileCastingTankDesc,

		[Description("Use AoE damage mitigation abilities if the target is casting any of these actions")]
		ConfigWindow_List_HostileCastingAreaDesc,

		[Description("Use knockback prevention abilities if the target is casting any of these actions")]
		ConfigWindow_List_HostileCastingKnockbackDesc,

		[Description("Stop casting or taking actions if the enemy is casting this ability")]
		ConfigWindow_List_HostileCastingStopDesc,

		[Description("Add Action")]
		ConfigWindow_List_AddAction,

		[Description("Don't target")]
		ConfigWindow_List_NoHostile,

		[Description("Don't provoke")]
		ConfigWindow_List_NoProvoke,

		[Description("Beneficial AoE locations")]
		ConfigWindow_List_BeneficialPositions,

		[Description("Enemies that will never be targeted")]
		ConfigWindow_List_NoHostileDesc,

		[Description("The name of the enemy that you don't want to target")]
		ConfigWindow_List_NoHostilesName,

		[Description("Enemies that will never be provoked")]
		ConfigWindow_List_NoProvokeDesc,

		[Description("The name of the enemy that you don't want to provoke")]
		ConfigWindow_List_NoProvokeName,

		[Description("Add beneficial AoE location")]
		ConfigWindow_List_AddPosition,

		[Description("Normal Targets")]
		NormalTargets,

		[Description("Targets with Heal-over-Time")]
		HotTargets,

		[Description("HP threshold for AoE healing oGCDs")]
		HpAoe0Gcd,

		[Description("HP threshold for AoE healing GCDs")]
		HpAoeGcd,

		[Description("HP threshold for single-target healing oGCDs")]
		HpSingle0Gcd,

		[Description("HP threshold for single-target healing GCDs")]
		HpSingleGcd,

		[Description("No Move")]
		InfoWindowNoMove,

		[Description("Move")]
		InfoWindowMove,

		[Description("Setting Search")]
		ConfigWindow_Searching,

		[Description("Timer")]
		ConfigWindow_Basic_Timer,

		[Description("Auto Switch")]
		ConfigWindow_Basic_AutoSwitch,

		[Description("Others")]
		ConfigWindow_Basic_Others,

		[Description("Information")]
		ConfigWindow_UI_Information,

		[Description("Windows")]
		ConfigWindow_UI_Windows,

		[Description("Change how PvP Solver automatically uses actions")]
		ConfigWindow_Auto_Description,

		[Description("Action Usage and Control")]
		ConfigWindow_Auto_ActionUsage,

		[Description("Which actions PvP Solver can use")]
		ConfigWindow_Auto_ActionUsage_Description,

		[Description("Healing Usage and Control")]
		ConfigWindow_Auto_HealingCondition,

		[Description("How PvP Solver should use healing abilities")]
		ConfigWindow_Auto_HealingCondition_Description,

		[Description("Configuration")]
		ConfigWindow_Target_Config,

		[Description("Hostile")]
		ConfigWindow_List_Hostile,

		[Description("Enemy targeting logic. Adding more options cycles them when using /pvpsolver Auto.\nUse /pvpsolver Settings TargetingTypes add <option> to add,\n/pvpsolver Settings TargetingTypes remove <option> to remove,\nand /pvpsolver Settings TargetingTypes removeall to remove all options.")]
		ConfigWindow_Param_HostileDesc,

		[Description("Move Up")]
		ConfigWindow_Actions_MoveUp,

		[Description("Move Down")]
		ConfigWindow_Actions_MoveDown,

		[Description("Hostile target selection condition")]
		ConfigWindow_Param_HostileCondition,

		[Description("PvP Solver focuses on the rotation itself. These are side features. Subject to removal at any time.")]
		ConfigWindow_Extra_Description,

		[Description("Event")]
		ConfigWindow_EventItem,

		[Description("Internal")]
		ConfigWindow_Internal,

		[Description("Others")]
		ConfigWindow_Extra_Others,

		[Description("Add Events")]
		ConfigWindow_Events_AddEvent,

		[Description("In this window, you can set which macro will be triggered after using an action.")]
		ConfigWindow_Events_Description,

		[Description("Delete Event")]
		ConfigWindow_Events_RemoveEvent,

		[Description("PvP")]
		ConfigWindow_PvpSpecific,

		[Description("Click to execute the command")]
		ConfigWindow_Helper_RunCommand,

		[Description("Right-click to copy the command")]
		ConfigWindow_Helper_CopyCommand,

		[Description("Macro No.")]
		ConfigWindow_Events_MacroIndex,

		[Description("Is Shared")]
		ConfigWindow_Events_ShareMacro,

		[Description("Action Name")]
		ConfigWindow_Events_ActionName,

		[Description("Cannot find the action. Please check the action name.")]
		CommandsInsertActionFailure,

		[Description("Open config window")]
		Commands_Rotation,

		[Description("Start PvP Solver combat rotation state")]
		Commands_Start,

		[Description("Disable PvP Solver combat rotation state")]
		Commands_Off,

		[Description("PvP Solver Settings v")]
		ConfigWindowHeader,

		[Description("This config is job-specific")]
		JobConfigTip,

		[Description("This option is unavailable with your current job\n \nRoles or jobs needed:\n{0}")]
		NotInJob,

	}

	public static class EnumExtensions
	{
		private static readonly Dictionary<Enum, string> _enumDescriptions = [];

		public static string GetDescription(this Enum value)
		{
			if (_enumDescriptions.TryGetValue(value, out string? description))
			{
				return description;
			}

			FieldInfo? field = value.GetType().GetField(value.ToString());
			if (field == null)
			{
				_enumDescriptions.Add(value, value.ToString());
				return value.ToString();
			}

			DescriptionAttribute? attribute = field.GetCustomAttribute<DescriptionAttribute>();

			string descString = attribute == null ? value.ToString() : attribute.Description;
			_enumDescriptions.Add(value, descString);
			return descString;
		}
	}
}
