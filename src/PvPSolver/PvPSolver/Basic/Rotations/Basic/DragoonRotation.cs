using Dalamud.Interface.Colors;

namespace RotationSolver.Basic.Rotations.Basic;

public partial class DragoonRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Strength;

	#region Job Gauge
	/// <summary>
	/// 
	/// </summary>
	public static byte EyeCount => JobGauge.EyeCount;

	/// <summary>
	/// Firstminds Count
	/// </summary>
	public static byte FocusCount => JobGauge.FirstmindsFocusCount;

	/// <summary>
	/// 
	/// </summary>
	private static float LOTDTimeRaw => JobGauge.LOTDTimer / 1000f;

	/// <summary>
	/// 
	/// </summary>
	public static float LOTDTime => LOTDTimeRaw - DataCenter.DefaultGCDRemain;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="time"></param>
	/// <returns></returns>
	protected static bool LOTDEndAfter(float time)
	{
		return LOTDTime <= time;
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="gctCount"></param>
	/// <param name="offset"></param>
	/// <returns></returns>
	protected static bool LOTDEndAfterGCD(uint gctCount = 0, float offset = 0)
	{
		return LOTDEndAfter(GCDTime(gctCount, offset));
	}
	#endregion


	#region Draw Debug

	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("EyeCount: " + EyeCount.ToString());
		ImGui.Text("FocusCount: " + FocusCount.ToString());
		ImGui.Text("LOTDTimeRaw: " + LOTDTimeRaw.ToString());
		ImGui.Text("LOTDTime: " + LOTDTime.ToString());
	}
	#endregion


	#region PvP Actions

	static partial void ModifyRaidenThrustPvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyFangAndClawPvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyWheelingThrustPvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyDrakesbanePvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyChaoticSpringPvP(ref ActionSetting setting)
	{
		setting.IgnoreGuard = true;
	}

	static partial void ModifyGeirskogulPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyHighJumpPvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
	}

	static partial void ModifyElusiveJumpPvP(ref ActionSetting setting)
	{
		setting.SpecialType = SpecialActionType.MovingBackward;
		setting.IsFriendly = true;
	}

	static partial void ModifyHorridRoarPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = false;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyHeavensThrustPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.RaidenThrustPvP) == ActionID.HeavensThrustPvP;
		setting.MPOverride = () => 0;
	}

	static partial void ModifyStarcrossPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.RaidenThrustPvP) == ActionID.StarcrossPvP;
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyNastrondPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.GeirskogulPvP) == ActionID.NastrondPvP;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyWyrmwindThrustPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.ElusiveJumpPvP) == ActionID.WyrmwindThrustPvP;
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}
	#endregion

}