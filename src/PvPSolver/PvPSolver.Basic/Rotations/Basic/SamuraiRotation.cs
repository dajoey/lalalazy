namespace RotationSolver.Basic.Rotations.Basic;

public partial class SamuraiRotation : CustomRotation
{
	#region JobGauge
	/// <summary>
	/// 
	/// </summary>
	public static bool HasSetsu => JobGauge.HasSetsu;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasGetsu => JobGauge.HasGetsu;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasKa => JobGauge.HasKa;

	/// <summary>
	/// 
	/// </summary>
	public static byte Kenki => JobGauge.Kenki;

	/// <summary>
	/// 
	/// </summary>
	public static byte MeditationStacks => JobGauge.MeditationStacks;

	/// <summary>
	/// 
	/// </summary>
	public static Kaeshi Kaeshi => JobGauge.Kaeshi;

	/// <summary>
	/// 
	/// </summary>
	public static byte SenCount
	{
		get
		{
			byte count = 0;
			if (HasGetsu)
			{
				count++;
			}

			if (HasSetsu)
			{
				count++;
			}

			if (HasKa)
			{
				count++;
			}

			return count;
		}
	}
	#endregion

	#region Old Status Tracking

	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Strength;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasMoon => StatusHelper.PlayerHasStatus(true, StatusID.Fugetsu);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasFlower => StatusHelper.PlayerHasStatus(true, StatusID.Fuka);

	/// <summary>
	/// 
	/// </summary>
	public static bool IsMoonTimeLessThanFlower => StatusHelper.PlayerStatusTime(true, StatusID.Fugetsu) < StatusHelper.PlayerStatusTime(true, StatusID.Fuka);
	#endregion

	#region Status Tracking
	/// <summary>
	/// 
	/// </summary>
	public static bool HasMeikyoShisui => StatusHelper.PlayerHasStatus(true, StatusID.MeikyoShisui);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasTendo => StatusHelper.PlayerHasStatus(true, StatusID.Tendo);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasTsubamegaeshiReady => StatusHelper.PlayerHasStatus(true, StatusID.Tsubamegaeshi);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasOgiNamikiri => StatusHelper.PlayerHasStatus(true, StatusID.OgiNamikiri);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasZanshinReady1318 => StatusHelper.PlayerHasStatus(true, StatusID.ZanshinReady);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasZanshinReady => StatusHelper.PlayerHasStatus(true, StatusID.ZanshinReady_3855);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasFugetsuAndFuka => HasFugetsu && HasFuka;

	/// <summary>
	/// 
	/// </summary>
	public static bool WillFugetsuEnd => StatusHelper.PlayerWillStatusEnd(5, true, StatusID.Fugetsu);

	/// <summary>
	/// 
	/// </summary>
	public static bool WillFukaEnd => StatusHelper.PlayerWillStatusEnd(5, true, StatusID.Fuka);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasFugetsu => StatusHelper.PlayerHasStatus(true, StatusID.Fugetsu);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasFuka => StatusHelper.PlayerHasStatus(true, StatusID.Fuka);

	/// <summary>
	/// 
	/// </summary>
	public static float? FugetsuTime => StatusHelper.PlayerStatusTime(true, StatusID.Fugetsu);

	/// <summary>
	/// 
	/// </summary>
	public static float? FukaTime => StatusHelper.PlayerStatusTime(true, StatusID.Fuka);

	/// <summary>
	/// 
	/// </summary>
	public static string? FugetsuOrFukaEndsFirst
	{
		get
		{
			if (!HasFugetsuAndFuka)
				return null;
			if (FugetsuTime == null || FukaTime == null)
				return null;
			if (FugetsuTime < FukaTime)
				return "Fugetsu";
			if (FukaTime < FugetsuTime)
				return "Fuka";
			return "Equal";
		}
	}
	#endregion


	#region Debug
	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("HasSetsu: " + HasSetsu.ToString());
		ImGui.Text("HasGetsu: " + HasGetsu.ToString());
		ImGui.Text("HasKa: " + HasKa.ToString());
		ImGui.Text("Kenki: " + Kenki.ToString());
		ImGui.Text("MeditationStacks: " + MeditationStacks.ToString());
		ImGui.Text("Kaeshi: " + Kaeshi.ToString());
		ImGui.Text("SenCount: " + SenCount.ToString());
		ImGui.Text("HasMoon: " + HasMoon.ToString());
		ImGui.Text("HasFlower: " + HasFlower.ToString());
		ImGui.Text("HaveMeikyoShisui: " + HasMeikyoShisui.ToString());
	}
	#endregion


	#region PvP Actions

	static partial void ModifyYukikazePvP(ref ActionSetting setting)
	{
		setting.MPOverride = () => 0;
	}

	static partial void ModifyGekkoPvP(ref ActionSetting setting)
	{
		setting.MPOverride = () => 0;
	}

	static partial void ModifyKashaPvP(ref ActionSetting setting)
	{
		setting.MPOverride = () => 0;
	}

	static partial void ModifyOgiNamikiriPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyHissatsuChitenPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyMineuchiPvP(ref ActionSetting setting)
	{
		setting.TargetStatusNeed = [StatusID.Kuzushi];
	}

	static partial void ModifyMeikyoShisuiPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = true;
	}

	static partial void ModifyHyosetsuPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.YukikazePvP) == ActionID.HyosetsuPvP;
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyMangetsuPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.YukikazePvP) == ActionID.MangetsuPvP;
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyOkaPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.YukikazePvP) == ActionID.OkaPvP;
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyKaeshiNamikiriPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.OgiNamikiriPvP) == ActionID.KaeshiNamikiriPvP;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyZanshinPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.HissatsuChitenPvP) == ActionID.ZanshinPvP;
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyTendoSetsugekkaPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.MeikyoShisuiPvP) == ActionID.TendoSetsugekkaPvP;
		setting.MPOverride = () => 0;
	}

	static partial void ModifyTendoKaeshiSetsugekkaPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.MeikyoShisuiPvP) == ActionID.TendoKaeshiSetsugekkaPvP;
	}

	static partial void ModifyHissatsuSotenPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.Kaiten_3201];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}
	#endregion

	/// <inheritdoc/>
	public override bool IsBursting()
	{
		return StatusHelper.PlayerHasStatus(true, StatusID.Fugetsu) && StatusHelper.PlayerHasStatus(true, StatusID.Fuka);
	}
}
