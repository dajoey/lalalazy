namespace RotationSolver.Basic.Rotations.Basic;

public partial class PaladinRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Strength;

	/// <summary>
	/// 
	/// </summary>
	public override bool CanHealAreaAbility => false;

	/// <inheritdoc/>
	public override bool IsBursting()
	{
		if (StatusHelper.PlayerHasStatus(true, StatusID.FightOrFlight))
		{
			return true;
		}
		return false;
	}

	/// <summary>
	/// Holds the remaining amount of Requiescat stacks
	/// </summary>
	public static byte RequiescatStacks
	{
		get
		{
			byte stacks = StatusHelper.PlayerStatusStack(true, StatusID.Requiescat);
			return stacks == byte.MaxValue ? (byte)5 : stacks;
		}
	}

	#region Job Gauge
	/// <summary>
	/// Gets the current level of the Oath gauge.
	/// </summary>
	public static byte OathGauge => JobGauge.OathGauge;
	#endregion

	#region Status Tracking

	/// <summary>
	/// 
	/// </summary>
	public static bool HasDivineMight => StatusHelper.PlayerHasStatus(true, StatusID.DivineMight);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasFightOrFlight => StatusHelper.PlayerHasStatus(true, StatusID.FightOrFlight);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasConfiteorReady => StatusHelper.PlayerHasStatus(true, StatusID.ConfiteorReady);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasAtonementReady => StatusHelper.PlayerHasStatus(true, StatusID.AtonementReady);
	#endregion


	#region Debug
	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("RequiescatStacks: " + RequiescatStacks.ToString());
		ImGui.Text("OathGauge: " + OathGauge.ToString());
		ImGui.Text("HasDivineMight: " + HasDivineMight.ToString());
		ImGui.Text("HasFightOrFlight: " + HasFightOrFlight.ToString());
		ImGui.Text("Can Heal Area Ability: " + CanHealAreaAbility.ToString());
		ImGui.Text("Can Heal Single Spell: " + CanHealSingleSpell.ToString());
		ImGui.Spacing();
		ImGui.Text("HasConfiteorReady: " + HasConfiteorReady.ToString());
		ImGui.Spacing();
		ImGui.Text("HasAtonementReady: " + HasAtonementReady.ToString());
	}

	#endregion


	#region PvP
	/// <summary>
	/// 
	/// </summary>
	public static bool BladeOfTruthPvPReady => Service.GetAdjustedActionId(ActionID.BladeOfFaithPvP) == ActionID.BladeOfTruthPvP;

	/// <summary>
	/// 
	/// </summary>
	public static bool BladeOfValorPvPReady => Service.GetAdjustedActionId(ActionID.BladeOfFaithPvP) == ActionID.BladeOfValorPvP;

	/// <summary>
	/// 
	/// </summary>
	public static bool ConfiteorPvPReady => Service.GetAdjustedActionId(ActionID.ImperatorPvP) == ActionID.ConfiteorPvP;
	static partial void ModifyFastBladePvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyRiotBladePvP(ref ActionSetting setting)
	{
		setting.ComboIds = [ActionID.FastBladePvP];
	}

	static partial void ModifyRoyalAuthorityPvP(ref ActionSetting setting)
	{
		setting.ComboIds = [ActionID.RiotBladePvP];
	}

	static partial void ModifyAtonementPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.AtonementReady_2015];
		setting.MPOverride = () => 0;
	}

	static partial void ModifySupplicationPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.SupplicationReady_4281];
		setting.MPOverride = () => 0;
	}

	static partial void ModifySepulchrePvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.SepulchreReady_4282];
		setting.MPOverride = () => 0;
	}

	static partial void ModifyHolySpiritPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyShieldSmitePvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1
		};
	}

	static partial void ModifyImperatorPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1
		};
	}

	static partial void ModifyHolySheltronPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = true;
	}

	static partial void ModifyConfiteorPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => ConfiteorPvPReady;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1
		};
	}

	static partial void ModifyIntervenePvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
	}

	static partial void ModifyBladeOfFaithPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.BladeOfFaithReady];
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1
		};
	}

	static partial void ModifyBladeOfTruthPvP(ref ActionSetting setting)
	{
		// setting.ActionCheck = () => BladeOfTruthPvPReady;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1
		};
	}

	static partial void ModifyBladeOfValorPvP(ref ActionSetting setting)
	{
		// setting.ActionCheck = () => BladeOfValorPvPReady;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1
		};
	}

	static partial void ModifyGuardianPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.HallowedGround_1302];
		setting.TargetType = TargetType.LowHP;
		setting.IsFriendly = true;
	}
	#endregion
}
