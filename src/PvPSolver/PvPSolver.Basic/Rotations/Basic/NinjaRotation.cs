using Dalamud.Interface.Colors;

namespace RotationSolver.Basic.Rotations.Basic;

public partial class NinjaRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Dexterity;

	#region Job Gauge
	/// <summary>
	/// Gets the amount of Ninki available.
	/// </summary>
	public static byte Ninki => JobGauge.Ninki;

	/// <summary>
	/// Gets the current charges for Kazematoi.
	/// </summary>
	public static byte Kazematoi => JobGauge.Kazematoi;

	/// <summary>
	/// Is enough level for Jin
	/// </summary>
	public static bool HasJin => IncreaseAttackSpeedTrait.EnoughLevel;

	/// <summary>
	/// Checks if no ninjutsu action is currently selected or if the Rabbit Medium has been invoked.
	/// </summary>
	public static bool NoNinjutsu => !IsExecutingMudra;

	/// <summary>
	/// Holds the remaining amount of Delirium stacks
	/// </summary>
	public static byte RaijuStacks
	{
		get
		{
			byte stacks = StatusHelper.PlayerStatusStack(true, StatusID.RaijuReady);
			return stacks == byte.MaxValue ? (byte)3 : stacks;
		}
	}
	#endregion


	/// <summary>
	/// 
	/// </summary>
	public static bool HasKassatsu => StatusHelper.PlayerHasStatus(true, StatusID.Kassatsu);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasRaijuReady => StatusHelper.PlayerHasStatus(true, StatusID.RaijuReady);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasHidden => StatusHelper.PlayerHasStatus(true, StatusID.Hidden_1316);

	/// <summary>
	/// 
	/// </summary>
	public static bool IsExecutingMudra => StatusHelper.PlayerHasStatus(true, StatusID.Mudra) || StatusHelper.PlayerHasStatus(true, StatusID.TenChiJin);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasDoton => StatusHelper.PlayerHasStatus(true, StatusID.Doton);

	/// <summary>
	/// 
	/// </summary>
	public static bool IsShadowWalking => StatusHelper.PlayerHasStatus(true, StatusID.ShadowWalker);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasPhantomKamaitachi => StatusHelper.PlayerHasStatus(true, StatusID.PhantomKamaitachiReady);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasTenChiJin => StatusHelper.PlayerHasStatus(true, StatusID.TenChiJin);

	/// <summary>
	/// 
	/// </summary>
	public static bool IsHidden => StatusHelper.PlayerHasStatus(true, StatusID.Hidden);

	#region Draw Debug
	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text($"Ninki: {Ninki}");
		ImGui.Text($"Kazematoi: {Kazematoi}");
		ImGui.Text($"HasJin: {HasJin}");
		ImGui.Text($"NoNinjutsu: {NoNinjutsu}");
		ImGui.Text($"RaijuStacks: {RaijuStacks}");
	}
	#endregion

	#region Mudra
	#endregion


	#region PvP Actions
	static partial void ModifySpinningEdgePvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyGustSlashPvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyAeolianEdgePvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyFumaShurikenPvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyDokumoriPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyThreeMudraPvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyBunshinPvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyZeshoMeppoPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.SpinningEdgePvP) == ActionID.ZeshoMeppoPvP;
		setting.MPOverride = () => 0;
	}

	static partial void ModifyAssassinatePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.SpinningEdgePvP) == ActionID.AssassinatePvP;
		setting.IgnoreGuard = true;
	}

	static partial void ModifyForkedRaijuPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.SpinningEdgePvP) == ActionID.ForkedRaijuPvP &&
									!StatusHelper.PlayerHasStatus(true, StatusID.SealedForkedRaiju);
		setting.MPOverride = () => 0;
	}

	static partial void ModifyFleetingRaijuPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.SpinningEdgePvP) == ActionID.FleetingRaijuPvP;
		setting.MPOverride = () => 0;
	}

	static partial void ModifyHyoshoRanryuPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.FumaShurikenPvP) == ActionID.HyoshoRanryuPvP &&
									!StatusHelper.PlayerHasStatus(true, StatusID.SealedHyoshoRanryu);
		setting.MPOverride = () => 0;
	}

	static partial void ModifyGokaMekkyakuPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.DokumoriPvP) == ActionID.GokaMekkyakuPvP &&
									!StatusHelper.PlayerHasStatus(true, StatusID.SealedGokaMekkyaku);
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyMeisuiPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.ThreeMudraPvP) == ActionID.MeisuiPvP &&
									!StatusHelper.PlayerHasStatus(true, StatusID.SealedMeisui);
		setting.MPOverride = () => 0;
	}

	static partial void ModifyHutonPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.BunshinPvP) == ActionID.HutonPvP &&
									!StatusHelper.PlayerHasStatus(true, StatusID.SealedHuton);
		setting.MPOverride = () => 0;
		setting.IsFriendly = true;
	}

	static partial void ModifyHollowNozuchiPvP(ref ActionSetting setting)
	{
		//this isn't a real action
	}

	static partial void ModifyDotonPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.ShukuchiPvP) == ActionID.DotonPvP;
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyShukuchiPvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
	}
	#endregion



	protected sealed override bool DefenseAreaAbility(IAction nextGCD, out IAction? act)
	{
		return base.DefenseAreaAbility(nextGCD, out act);
	}

	protected override bool DefenseSingleAbility(IAction nextGCD, out IAction? act)
	{
		return base.DefenseSingleAbility(nextGCD, out act);
	}
}