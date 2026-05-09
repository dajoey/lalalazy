namespace RotationSolver.Basic.Rotations.Basic;

public partial class WhiteMageRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Mind;


	/// <inheritdoc/>
	public override bool IsBursting()
	{
		if (StatusHelper.PlayerHasStatus(true, StatusID.PresenceOfMind))
		{
			return true; // Either have presence of mind or more than 15 seconds until we can presence of mind, use burst skills
		}
		return false;
	}

	/// <inheritdoc/>
	public static bool ThinAirState()
	{
		if (!DataCenter.IsPvP && (HasThinAir || IsLastAction(ActionID.ThinAirPvE)))
		{
			return true;
		}
		return false;
	}

	#region Job Gauge
	/// <summary>
	/// Represents the number of Lily stacks.
	/// </summary>
	public static byte Lily => JobGauge.Lily;

	/// <summary>
	/// Represents the number of Blood Lily stacks.
	/// </summary>
	public static byte BloodLily => JobGauge.BloodLily;

	/// <summary>
	/// Gets the raw Lily timer value in seconds.
	/// </summary>
	private static float LilyTimeRaw => JobGauge.LilyTimer / 1000f;

	/// <summary>
	/// Gets the Lily timer value adjusted by the default GCD remain.
	/// </summary>
	public static float LilyTime => LilyTimeRaw + DataCenter.DefaultGCDRemain;

	/// <summary>
	/// Determines if the Lily timer will expire after the specified time.
	/// </summary>
	/// <param name="time">The time in seconds to check against the Lily timer.</param>
	/// <returns>True if the Lily timer will expire after the specified time; otherwise, false.</returns>
	protected static bool LilyAfter(float time)
	{
		return LilyTime <= time;
	}

	/// <summary>
	/// Determines if the Lily timer will expire after a specified number of GCDs and an optional offset.
	/// </summary>
	/// <param name="gcdCount">The number of GCDs to check against the Lily timer.</param>
	/// <param name="offset">An optional offset in seconds to add to the GCD time.</param>
	/// <returns>True if the Lily timer will expire after the specified number of GCDs and offset; otherwise, false.</returns>
	protected static bool LilyAfterGCD(uint gcdCount = 0, float offset = 0)
	{
		return LilyAfter(GCDTime(gcdCount, offset));
	}

	/// <summary>
	/// Gets the remaining number of Sacred Sight stacks.
	/// </summary>
	public static byte SacredSightStacks
	{
		get
		{
			byte stacks = StatusHelper.PlayerStatusStack(true, StatusID.SacredSight);
			return stacks == byte.MaxValue ? (byte)3 : stacks;
		}
	}
	#endregion

	#region Status Tracking
	/// <summary>
	/// Player has Thin Air.
	/// </summary>
	public static bool HasThinAir => StatusHelper.PlayerHasStatus(true, StatusID.ThinAir);

	/// <summary>
	/// Player has Presence Of Mind.
	/// </summary>
	public static bool HasPresenceOfMind => StatusHelper.PlayerHasStatus(true, StatusID.PresenceOfMind);
	#endregion

	#region Debug
	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("SacredSightStacks: " + SacredSightStacks.ToString());
		ImGui.Text("LilyTime: " + LilyTime.ToString());
		ImGui.Text("BloodLilyStacks: " + BloodLily.ToString());
		ImGui.Text("Lily: " + Lily.ToString());
	}
	#endregion


	#region PvP Actions
	static partial void ModifyGlareIiiPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyCureIiPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyAfflatusMiseryPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyAquaveilPvP(ref ActionSetting setting)
	{
		setting.TargetStatusNeed = StatusHelper.PurifyPvPStatuses;
		setting.IsFriendly = true;
		setting.TargetType = TargetType.Dispel;
	}

	static partial void ModifyMiracleOfNaturePvP(ref ActionSetting setting)
	{

	}

	static partial void ModifySeraphStrikePvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyGlareIvPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.GlareIiiPvP) == ActionID.GlareIvPvP;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyCureIiiPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.CureIiPvP) == ActionID.CureIiiPvP;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}
	#endregion
}