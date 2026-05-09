namespace RotationSolver.Basic.Rotations.Basic;

public partial class ScholarRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Mind;

	#region Job Gauge

	/// <summary>
	/// 
	/// </summary>
	public static byte FairyGauge => JobGauge.FairyGauge;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasAetherflow => JobGauge.Aetherflow > 0;

	/// <summary>
	/// 
	/// </summary>
	public static byte SCHAetherFlowStacks => JobGauge.Aetherflow;

	private static float SeraphTimeRaw => JobGauge.SeraphTimer / 1000f;

	/// <summary>
	/// 
	/// </summary>
	public static float SeraphTime => SeraphTimeRaw - DataCenter.DefaultGCDRemain;

	/// <summary>
	/// 
	/// </summary>
	public static bool FairyDismissed => JobGauge.DismissedFairy > 0;
	#endregion

	#region Status Tracking
	/// <summary>
	/// Has Impact Imminent.
	/// </summary>
	public static bool HasImpactImminent => StatusHelper.PlayerHasStatus(true, StatusID.ImpactImminent);

	/// <summary>
	/// Has Dissipation.
	/// </summary>
	public static bool HasDissipation => StatusHelper.PlayerHasStatus(true, StatusID.Dissipation);

	/// <summary>
	/// Has EmergencyTactics.
	/// </summary>
	public static bool HasEmergencyTactics => StatusHelper.PlayerHasStatus(true, StatusID.EmergencyTactics);

	/// <summary>
	/// Has Recitation.
	/// </summary>
	public static bool HasRecitation => StatusHelper.PlayerHasStatus(true, StatusID.Recitation);
	#endregion


	#region Debug

	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("FairyGauge: " + FairyGauge.ToString());
		ImGui.Text("HasAetherflow: " + HasAetherflow.ToString());
		ImGui.Text("SCHAetherFlowStacks: " + SCHAetherFlowStacks.ToString());
		ImGui.Text("SeraphTime: " + SeraphTime.ToString());
		ImGui.Text("Has Fairy Out: " + DataCenter.HasPet().ToString());
		ImGui.Text("FairyDismissed: " + FairyDismissed.ToString());
	}
	#endregion


	#region PvP Actions
	static partial void ModifyBroilIvPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyBiolysisPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyChainStratagemPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = false;
		setting.IgnoreGuard = true;
	}

	static partial void ModifySummonSeraphPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = true;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifySeraphicHaloPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.BroilIvPvP) == ActionID.SeraphicHaloPvP;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifySeraphicVeilPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			IsEnabled = false,
		};
	}

	static partial void ModifyAdloquiumPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyDeploymentTacticsPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.IsFriendly = false;
		setting.TargetStatusNeed = [StatusID.Biolysis_3089];
		setting.StatusProvide = [StatusID.Biolysis_3089];
	}

	static partial void ModifyExpedientPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyAccessionPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => StatusHelper.PlayerHasStatus(true, StatusID.Seraphism_4327);
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	#endregion
}