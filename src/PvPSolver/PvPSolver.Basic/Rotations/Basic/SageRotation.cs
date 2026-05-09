namespace RotationSolver.Basic.Rotations.Basic;

public partial class SageRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Mind;

	#region Job Gauge
	/// <summary>
	/// Gets a value indicating whether Eukrasia is activated. Eukrasia = 1, none = 0
	/// </summary>
	public static bool HasEukrasia => JobGauge.Eukrasia;

	/// <summary>
	/// Gets the amount of Addersgall available.
	/// </summary>
	public static byte Addersgall => AddersgallTrait.EnoughLevel ? JobGauge.Addersgall : (byte)0;

	/// <summary>
	/// Gets the amount of Addersting available.
	/// </summary>
	public static byte Addersting => AdderstingTrait.EnoughLevel ? JobGauge.Addersting : (byte)0;

	private static float AddersgallTimerRaw => JobGauge.AddersgallTimer / 1000f;

	/// <summary>
	/// Gets the amount of milliseconds elapsed until the next Addersgall is available.
	/// This counts from 0 to 20_000.
	/// </summary>
	public static float AddersgallTime => AddersgallTrait.EnoughLevel ? AddersgallTimerRaw - DataCenter.DefaultGCDRemain : 0;

	/// <summary>
	/// Used to determine if the cooldown for the next Addersgall will end within a specified time frame.
	/// </summary>
	/// <param name="time"></param>
	/// <returns></returns>
	protected static bool AddersgallEndAfter(float time)
	{
		return AddersgallTime <= time;
	}

	/// <summary>
	/// Used to determine if the cooldown for the next Addersgall will end within a specified number of GCDs.
	/// </summary>
	/// <param name="gctCount"></param>
	/// <param name="offset"></param>
	/// <returns></returns>
	protected static bool AddersgallEndAfterGCD(uint gctCount = 0, float offset = 0)
	{
		return AddersgallEndAfter(GCDTime(gctCount, offset));
	}

	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("HasEukrasia: " + HasEukrasia.ToString());
		ImGui.Text("Addersgall: " + Addersgall.ToString());
		ImGui.Text("Addersting: " + Addersting.ToString());
		ImGui.Text("AddersgallTime: " + AddersgallTime.ToString());
	}
	#endregion

	#region Status Tracking
	/// <summary>
	/// Player has Kardia.
	/// </summary>
	public static bool HasKardia => StatusHelper.PlayerHasStatus(true, StatusID.Kardia);
	#endregion


	#region PvP Actions
	static partial void ModifyDosisIiiPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyPhlegmaIiiPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyPneumaPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};

	}

	static partial void ModifyEukrasiaPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.Eukrasia_3107];
		setting.IsFriendly = true;
	}

	static partial void ModifyIcarusPvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
	}

	static partial void ModifyToxikonPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyKardiaPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.Kardia_2871];
		setting.TargetStatusProvide = [StatusID.Kardion_2872];
		setting.TargetType = TargetType.Kardia;
	}

	static partial void ModifyEukrasianDosisIiiPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.DosisIiiPvP) == ActionID.EukrasianDosisIiiPvP;
	}

	static partial void ModifyPsychePvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyToxikonIiPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => StatusHelper.PlayerHasStatus(true, StatusID.Addersting);
		setting.TargetStatusProvide = [StatusID.Toxikon];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}


	#endregion
}
