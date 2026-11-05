using Dalamud.Interface.Colors;

namespace RotationSolver.Basic.Rotations.Basic;

public partial class MachinistRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Dexterity;

	#region Job Gauge
	/// <summary>
	/// Gets a value indicating whether the player is currently Overheated.
	/// </summary>
	public static bool IsOverheated => JobGauge.IsOverheated;

	/// <summary>
	/// Gets a value indicating whether the player has an active Robot.
	/// </summary>
	public static bool IsRobotActive => JobGauge.IsRobotActive;

	/// <summary>
	/// Gets the current Heat level.
	/// </summary>
	public static byte Heat => JobGauge.Heat;

	/// <summary>
	/// Gets the current Overheated Stacks.
	/// </summary>
	public static byte OverheatedStacks
	{
		get
		{
			byte stacks = StatusHelper.PlayerStatusStack(true, StatusID.Overheated);
			return stacks == byte.MaxValue ? (byte)5 : stacks;
		}
	}

	/// <summary>
	/// Gets the current Battery level.
	/// </summary>
	public static byte Battery => JobGauge.Battery;

	/// <summary>
	/// Gets the battery level of the last summon (robot).
	/// </summary>
	public static byte LastSummonBatteryPower => JobGauge.LastSummonBatteryPower;

	private static float OverheatTimeRemainingRaw => JobGauge.OverheatTimeRemaining / 1000f;

	private static float SummonTimeRemainingRaw => JobGauge.SummonTimeRemaining / 1000f;

	/// <summary>
	/// Gets the time remaining for Overheat in seconds minus the DefaultGCDRemain.
	/// </summary>
	public static float OverheatTime => OverheatTimeRemainingRaw - DataCenter.DefaultGCDRemain;

	/// <summary>
	/// Gets the time remaining for the Rook or Queen in seconds minus the DefaultGCDRemain.
	/// </summary>
	public static float SummonTime => SummonTimeRemainingRaw - DataCenter.DefaultGCDRemain;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="time"></param>
	/// <returns></returns>
	protected static bool OverheatedEndAfter(float time)
	{
		return OverheatTime <= time;
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="gctCount"></param>
	/// <param name="offset"></param>
	/// <returns></returns>
	protected static bool OverheatedEndAfterGCD(uint gctCount = 0, float offset = 0)
	{
		return OverheatedEndAfter(GCDTime(gctCount, offset));
	}
	#endregion

	#region Status Tracking
	/// <summary>
	/// 
	/// </summary>
	public static bool HasWildfire => StatusHelper.PlayerHasStatus(true, StatusID.Wildfire_1946);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasHypercharged => StatusHelper.PlayerHasStatus(true, StatusID.Hypercharged);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasReassembled => StatusHelper.PlayerHasStatus(true, StatusID.Reassembled);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasOverheated => StatusHelper.PlayerHasStatus(true, StatusID.Overheated);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasExcavatorReady => StatusHelper.PlayerHasStatus(true, StatusID.ExcavatorReady);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasFullMetalMachinist => StatusHelper.PlayerHasStatus(true, StatusID.FullMetalMachinist);
	#endregion


	#region Debug Display
	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("IsOverheated: " + IsOverheated.ToString());
		ImGui.Text("IsRobotActive: " + IsRobotActive.ToString());
		ImGui.Text("Heat: " + Heat.ToString());
		ImGui.Text("Battery: " + Battery.ToString());
		ImGui.Text("LastSummonBatteryPower: " + LastSummonBatteryPower.ToString());
		ImGui.Text("SummonTimeRemainingRaw: " + SummonTimeRemainingRaw.ToString());
		ImGui.Text("SummonTime: " + SummonTime.ToString());
		ImGui.Text("OverheatTimeRemainingRaw: " + OverheatTimeRemainingRaw.ToString());
		ImGui.Text("OverheatTime: " + OverheatTime.ToString());
		ImGui.Text("OverheatedStacks: " + OverheatedStacks.ToString());
		ImGui.Spacing();
	}
	#endregion


	#region PvP

	static partial void ModifyAnalysisPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = true;
		setting.StatusProvide = [StatusID.Analysis];
	}

	static partial void ModifyDrillPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.DrillPrimed];
		setting.IgnoreGuard = true;
	}

	static partial void ModifyBioblasterPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.BioblasterPrimed];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyAirAnchorPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.AirAnchorPrimed];
	}

	static partial void ModifyChainSawPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.ChainSawPrimed];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyBishopAutoturretPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyFullMetalFieldPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyAetherMortarPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			IsEnabled = false,
		};
	}
	#endregion
}
