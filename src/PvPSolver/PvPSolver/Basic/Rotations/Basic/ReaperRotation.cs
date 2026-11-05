using Dalamud.Interface.Colors;

namespace RotationSolver.Basic.Rotations.Basic;

public partial class ReaperRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Strength;

	#region JobGauge
	/// <summary>
	/// 
	/// </summary>
	public static byte Soul => JobGauge.Soul;

	/// <summary>
	/// 
	/// </summary>
	public static byte Shroud => JobGauge.Shroud;

	/// <summary>
	/// 
	/// </summary>
	public static float EnshroudedTiemRemaining => JobGauge.EnshroudedTimeRemaining;

	/// <summary>
	/// 
	/// </summary>
	public static byte LemureShroud => JobGauge.LemureShroud;

	/// <summary>
	/// 
	/// </summary>
	public static byte VoidShroud => JobGauge.VoidShroud;
	#endregion

	#region Status Tracking
	/// <summary>
	/// 
	/// </summary>
	public static bool HasEnshrouded => StatusHelper.PlayerHasStatus(true, StatusID.Enshrouded);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasEnshroudedPvP => StatusHelper.PlayerHasStatus(true, StatusID.Enshrouded_2863);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasSoulReaver => StatusHelper.PlayerHasStatus(true, StatusID.SoulReaver);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasSoulsow => StatusHelper.PlayerHasStatus(true, StatusID.Soulsow);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasEnhancedGallows => StatusHelper.PlayerHasStatus(true, StatusID.EnhancedGallows);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasEnhancedGibbet => StatusHelper.PlayerHasStatus(true, StatusID.EnhancedGibbet);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasEnhancedVoidReaping => StatusHelper.PlayerHasStatus(true, StatusID.EnhancedVoidReaping);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasEnhancedCrossReaping => StatusHelper.PlayerHasStatus(true, StatusID.EnhancedCrossReaping);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasExecutioner => StatusHelper.PlayerHasStatus(true, StatusID.Executioner);

	/// <summary>
	/// Able to execute Enshroud
	/// </summary>
	public static bool HasIdealHost => StatusHelper.PlayerHasStatus(true, StatusID.IdealHost);

	/// <summary>
	/// Able to execute Plentiful Harvest
	/// </summary>
	public static bool HasImmortalSacrifice => StatusHelper.PlayerHasStatus(true, StatusID.ImmortalSacrifice);

	/// <summary>
	/// PvP version of Immortal Sacrifice
	/// </summary>
	public static bool HasImmortalSacrificePvP => StatusHelper.PlayerHasStatus(true, StatusID.ImmortalSacrifice_3204);

	/// <summary>
	/// Grants Immortal Sacrifice to the reaper who applied this effect when duration expires
	/// </summary>
	public static bool HasBloodsownCircleOther => StatusHelper.PlayerHasStatus(true, StatusID.BloodsownCircle);

	/// <summary>
	/// Able to gain stacks of Immortal Sacrifice from party members under the effect of your Circle of Sacrifice
	/// </summary>
	public static bool HasBloodsownCircleSelf => StatusHelper.PlayerHasStatus(true, StatusID.BloodsownCircle_2972);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasArcaneCircle => StatusHelper.PlayerHasStatus(true, StatusID.ArcaneCircle);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasOblatio => StatusHelper.PlayerHasStatus(true, StatusID.Oblatio);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasPerfectioParata => StatusHelper.PlayerHasStatus(true, StatusID.PerfectioParata);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasDeathWarrantPvP => StatusHelper.PlayerHasStatus(true, StatusID.DeathWarrant_4308);

	/// <summary>
	/// 
	/// </summary>
	public static bool WillDeathWarrantPvPEnd => StatusHelper.PlayerWillStatusEndGCD(1, 0, true, StatusID.DeathWarrant_4308);

	/// <summary>
	/// 
	/// </summary>
	public static bool NotInActiveCombo => !HasSoulReaver && !HasEnshrouded && !HasExecutioner;
	#endregion


	#region Debug

	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("EnshroudedTiemRemaining: " + EnshroudedTiemRemaining.ToString());
		ImGui.Text("HasEnshrouded: " + HasEnshrouded.ToString());
		ImGui.Text("HasSoulReaver: " + HasSoulReaver.ToString());
		ImGui.Text("HasExecutioner: " + HasExecutioner.ToString());
		ImGui.Text("HasIdealHost: " + HasIdealHost.ToString());
		ImGui.Text("HasOblatio: " + HasOblatio.ToString());
		ImGui.Text("HasPerfectioParata: " + HasPerfectioParata.ToString());
		ImGui.Text("Soul: " + Soul.ToString());
		ImGui.Text("Shroud: " + Shroud.ToString());
		ImGui.Text("LemureShroud: " + LemureShroud.ToString());
		ImGui.Text("VoidShroud: " + VoidShroud.ToString());
	}
	#endregion


	#region PvP Actions
	static partial void ModifySlicePvP(ref ActionSetting setting)
	{
		setting.MPOverride = () => 0;
	}

	static partial void ModifyWaxingSlicePvP(ref ActionSetting setting)
	{
		setting.MPOverride = () => 0;
	}

	static partial void ModifyInfernalSlicePvP(ref ActionSetting setting)
	{
		setting.MPOverride = () => 0;
	}

	static partial void ModifyHarvestMoonPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyPlentifulHarvestPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyGrimSwathePvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyDeathWarrantPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.DeathWarrant_4308];
	}

	static partial void ModifyHellsIngressPvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
		setting.IsFriendly = true;
	}

	static partial void ModifyRegressPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.HellsIngressPvP) == ActionID.RegressPvP;
		setting.SpecialType = SpecialActionType.MovingBackward;
		setting.IsFriendly = true;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyArcaneCrestPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = true;
	}

	static partial void ModifyExecutionersGuillotinePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.SlicePvP) == ActionID.ExecutionersGuillotinePvP;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyVoidReapingPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.Enshrouded_2863];
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.SlicePvP) == ActionID.VoidReapingPvP;
	}

	static partial void ModifyCrossReapingPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.Enshrouded_2863];
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.SlicePvP) == ActionID.CrossReapingPvP;
	}

	static partial void ModifyLemuresSlicePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.GrimSwathePvP) == ActionID.LemuresSlicePvP;
		setting.StatusNeed = [StatusID.Enshrouded_2863];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyFateSealedPvP(ref ActionSetting setting)
	{
		setting.IgnoreGuard = true;
		setting.StatusNeed = [StatusID.DeathWarrant_4308];
	}

	static partial void ModifyPerfectioPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.PerfectioParata_4309];
		setting.IsFriendly = false;
		setting.TargetType = TargetType.LowHPPercent;
		setting.IgnoreGuard = true;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyCommunioPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.Enshrouded_2863];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}
	#endregion
}