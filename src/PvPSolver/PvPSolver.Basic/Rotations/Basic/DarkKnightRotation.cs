namespace RotationSolver.Basic.Rotations.Basic;

public partial class DarkKnightRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Strength;

	#region Job Gauge

	/// <summary>
	/// 
	/// </summary>
	public static byte Blood => JobGauge.Blood;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasDarkArts => JobGauge.HasDarkArts;

	/// <summary>
	/// New with Dalamud 12 but likely unneeded as we use GetAdjustedActionId
	/// </summary>
	public static DeliriumStep DeliriumComboStep => JobGauge.DeliriumComboStep;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasDelirium => DeliriumStacks > 0;

	private static float DarkSideTimeRemainingRaw => JobGauge.DarksideTimeRemaining / 1000f;

	/// <summary>
	/// 
	/// </summary>
	public static float DarkSideTime => DarkSideTimeRemainingRaw - DataCenter.DefaultGCDRemain;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="time"></param>
	/// <returns></returns>
	protected static bool DarkSideEndAfter(float time)
	{
		return DarkSideTime <= time;
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="gctCount"></param>
	/// <param name="offset"></param>
	/// <returns></returns>
	protected static bool DarkSideEndAfterGCD(uint gctCount = 0, float offset = 0)
	{
		return DarkSideEndAfter(GCDTime(gctCount, offset));
	}

	private static float ShadowTimeRemainingRaw => JobGauge.ShadowTimeRemaining / 1000f;

	/// <summary>
	/// 
	/// </summary>
	public static float ShadowTime => ShadowTimeRemainingRaw - DataCenter.DefaultGCDRemain;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="time"></param>
	/// <returns></returns>
	protected static bool ShadowTimeEndAfter(float time)
	{
		return ShadowTimeRemainingRaw <= time;
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="gctCount"></param>
	/// <param name="offset"></param>
	/// <returns></returns>
	protected static bool ShadowTimeEndAfterGCD(uint gctCount = 0, float offset = 0)
	{
		return ShadowTimeEndAfter(GCDTime(gctCount, offset));
	}
	#endregion

	#region Status Tracking
	/// <summary>
	/// Holds the remaining amount of BloodWeapon stacks
	/// </summary>
	public static byte BloodWeaponStacks
	{
		get
		{
			byte stacks = StatusHelper.PlayerStatusStack(true, StatusID.BloodWeapon);
			return stacks == byte.MaxValue ? (byte)3 : stacks;
		}
	}

	/// <summary>
	/// Holds the remaining amount of Delirium stacks
	/// </summary>
	public static byte DeliriumStacks
	{
		get
		{
			byte stacks = StatusHelper.PlayerStatusStack(true, StatusID.Delirium_3836);
			return stacks == byte.MaxValue ? (byte)3 : stacks;
		}
	}

	/// <summary>
	/// Holds the remaining amount of Delirium stacks
	/// </summary>
	public static byte LowDeliriumStacks
	{
		get
		{
			byte stacks = StatusHelper.PlayerStatusStack(true, StatusID.Delirium_1972);
			return stacks == byte.MaxValue ? (byte)3 : stacks;
		}
	}

	/// <summary>
	/// 
	/// </summary>
	protected static bool HasDarkArtsPvP => Player != null && StatusHelper.PlayerHasStatus(true, StatusID.DarkArts_3034);
	#endregion


	#region Draw Debug

	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("BloodWeaponStacks: " + BloodWeaponStacks.ToString());
		ImGui.Text("DeliriumStacks: " + DeliriumStacks.ToString());
		ImGui.Text("LowDeliriumStacks: " + LowDeliriumStacks.ToString());
		ImGui.Text("ShadowTime: " + ShadowTime.ToString());
		ImGui.Text("DarkSideTime: " + DarkSideTime.ToString());
		ImGui.Text("HasDarkArts: " + HasDarkArts.ToString());
		ImGui.Text("Blood: " + Blood.ToString());
		ImGui.Text("HasDelirium: " + HasDelirium.ToString());
		ImGui.Text("DeliriumComboStep: " + DeliriumComboStep.ToString());
	}
	#endregion


	#region PvP Actions Unassignable
	/// <summary>
	/// 
	/// </summary>
	public static bool ScarletDeliriumPvPReady => Service.GetAdjustedActionId(ActionID.SouleaterPvP) == ActionID.ScarletDeliriumPvP;

	/// <summary>
	/// 
	/// </summary>
	public static bool ComeuppancePvPReady => Service.GetAdjustedActionId(ActionID.SouleaterPvP) == ActionID.ComeuppancePvP;

	/// <summary>
	/// 
	/// </summary>
	public static bool TorcleaverPvPReady => Service.GetAdjustedActionId(ActionID.SouleaterPvP) == ActionID.TorcleaverPvP;

	/// <summary>
	/// 
	/// </summary>
	public static bool SaltAndDarknessPvPReady => Service.GetAdjustedActionId(ActionID.SaltedEarthPvP) == ActionID.SaltAndDarknessPvP;
	#endregion

	#region PvP Actions
	/// <summary>
	/// 
	/// </summary>
	static partial void ModifyShadowbringerPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Player?.CurrentHp > 12000 || StatusHelper.PlayerHasStatus(true, StatusID.DarkArts_3034);
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}
	static partial void ModifyPlungePvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
	}

	static partial void ModifyScarletDeliriumPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => ScarletDeliriumPvPReady;
		setting.MPOverride = () => 0;
	}

	static partial void ModifyComeuppancePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => ComeuppancePvPReady;
		setting.MPOverride = () => 0;
	}

	static partial void ModifyTorcleaverPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => TorcleaverPvPReady;
		setting.MPOverride = () => 0;
	}

	static partial void ModifyDisesteemPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.Scorn_4290];
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifySaltAndDarknessPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => SaltAndDarknessPvPReady;
	}

	static partial void ModifySaltedEarthPvP(ref ActionSetting setting)
	{
		setting.TargetType = TargetType.Self;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyImpalementPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = false;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	#endregion
}