using Dalamud.Interface.Colors;

namespace RotationSolver.Basic.Rotations.Basic;

public partial class GunbreakerRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Strength;

	#region Job Gauge
	/// <summary>
	/// Gets the amount of ammo available.
	/// </summary>
	public static byte Ammo => JobGauge.Ammo;

	/// <summary>
	/// 
	/// </summary>
	public static byte AmmoComboStep => JobGauge.AmmoComboStep;

	/// <summary>
	/// Gets the maximum amount of ammo available.
	/// </summary>
	public static byte MaxAmmo()
	{
		if (HasBloodfest)
		{
			if (CartridgeChargeIiTrait.EnoughLevel)
				return 6;
			return 4;
		}
		if (CartridgeChargeIiTrait.EnoughLevel)
			return 3;
		if (CartridgeChargeTrait.EnoughLevel)
			return 2;
		return 0;
	}

	/// <summary>
	/// Gets the maximum amount of ammo available not accounting for Bloodfest.
	/// </summary>
	public static byte NormalMaxAmmo()
	{
		if (CartridgeChargeIiTrait.EnoughLevel)
			return 3;
		if (CartridgeChargeTrait.EnoughLevel)
			return 2;
		return 0;
	}

	/// <summary>
	/// 
	/// </summary>
	public static byte OvercappedAmmo()
	{
		return (byte)(Ammo - NormalMaxAmmo());
	}

	/// <summary>
	/// Gets whether the current ammo is at the maximum allowed.
	/// </summary>
	public static bool IsAmmoCapped => Ammo == MaxAmmo();

	/// <summary>
	/// Gets the max combo time of the Gnashing Fang combo.
	/// </summary>
	public static short MaxTimerDuration => JobGauge.MaxTimerDuration;

	/// <summary>
	/// Gets whether the player is in the Gnashing Fang combo.
	/// </summary>
	public static bool InGnashingFang => AmmoComboStep is 1 or 2;

	/// <summary>
	/// Gets whether the player is in the Reign combo.
	/// </summary>
	public static bool InReignCombo => AmmoComboStep is 3 or 4;

	/// <summary>
	/// Has No Mercy buff.
	/// </summary>
	public static bool HasNoMercy => !StatusHelper.PlayerWillStatusEndGCD(0, 0, true, StatusID.NoMercy);

	/// <summary>
	/// Has No Mercy buff.
	/// </summary>
	public static bool HasBloodfest => !StatusHelper.PlayerWillStatusEndGCD(0, 0, true, StatusID.Bloodfest);

	/// <summary>
	/// Able to execute Sonic Break.
	/// </summary>
	public static bool HasReadyToBreak => !StatusHelper.PlayerWillStatusEndGCD(0, 0, true, StatusID.ReadyToBreak);

	/// <summary>
	/// Able to execute Reign of Beasts.
	/// </summary>
	public static bool HasReadyToReign => !StatusHelper.PlayerWillStatusEndGCD(0, 0, true, StatusID.ReadyToReign);

	/// <summary>
	/// Able to execute Jugular Rip.
	/// </summary>
	public static bool HasReadyToRip => !StatusHelper.PlayerWillStatusEndGCD(0, 0, true, StatusID.ReadyToRip);

	/// <summary>
	/// Able to execute Abdomen Tear.
	/// </summary>
	public static bool HasReadyToTear => !StatusHelper.PlayerWillStatusEndGCD(0, 0, true, StatusID.ReadyToTear);

	/// <summary>
	/// Able to execute Fated Brand.
	/// </summary>
	public static bool HasReadyToRaze => !StatusHelper.PlayerWillStatusEndGCD(0, 0, true, StatusID.ReadyToRaze);

	/// <summary>
	/// Able to execute Eye Gouge.
	/// </summary>
	public static bool HasReadyToGouge => !StatusHelper.PlayerWillStatusEndGCD(0, 0, true, StatusID.ReadyToGouge);

	/// <summary>
	/// Able to execute Hypervelocity.
	/// </summary>
	public static bool HasReadyToBlast => !StatusHelper.PlayerWillStatusEndGCD(0, 0, true, StatusID.ReadyToBlast);


	#endregion


	#region Debug Status

	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("InGnashingFang: " + InGnashingFang.ToString());
		ImGui.Text("InReignCombo: " + InReignCombo.ToString());
		ImGui.Text("HasNoMercy: " + HasNoMercy.ToString());
		ImGui.Text("HasReadyToBreak: " + HasReadyToBreak.ToString());
		ImGui.Text("HasReadyToReign: " + HasReadyToReign.ToString());
		ImGui.Text("HasReadyToRip: " + HasReadyToRip.ToString());
		ImGui.Text("HasReadyToTear: " + HasReadyToTear.ToString());
		ImGui.Text("HasReadyToRaze: " + HasReadyToRaze.ToString());
		ImGui.Text("HasReadyToGouge: " + HasReadyToGouge.ToString());
		ImGui.Text("HasReadyToBlast: " + HasReadyToBlast.ToString());
		ImGui.Spacing();
		//ImGui.Text("NoMercyWindow: " + NoMercyWindow.ToString());
		ImGui.Text("Ammo: " + Ammo.ToString());
		ImGui.Text("AmmoComboStep: " + AmmoComboStep.ToString());
		ImGui.Text("MaxAmmo: " + MaxAmmo().ToString());
		ImGui.Text("Is Ammo Capped: " + IsAmmoCapped.ToString());
		ImGui.Text("MaxTimerDuration: " + MaxTimerDuration.ToString());
		ImGui.Spacing();
		ImGui.Spacing();
		ImGui.Spacing();
		ImGui.TextColored(ImGuiColors.DalamudYellow, "PvP Actions");
		ImGui.Text("SavageClawPvPReady: " + SavageClawPvPReady.ToString());
		ImGui.Text("WickedTalonPvPReady: " + WickedTalonPvPReady.ToString());
		ImGui.Spacing();
		ImGui.Text("HypervelocityPvPReady: " + HypervelocityPvPReady.ToString());
		ImGui.Text("FatedBrandPvPReady: " + FatedBrandPvPReady.ToString());
		ImGui.Text("JugularRipPvPReady: " + JugularRipPvPReady.ToString());
		ImGui.Text("AbdomenTearPvPReady: " + AbdomenTearPvPReady.ToString());
		ImGui.Text("EyeGougePvPReady: " + EyeGougePvPReady.ToString());
		ImGui.Spacing();
		ImGui.Text("HasTerminalTrigger: " + HasTerminalTrigger.ToString());
	}
	#endregion


	#region PvP Actions Unassignable

	/// <summary>
	/// Gnashing Fang 2
	/// </summary>
	public static bool SavageClawPvPReady => Service.GetAdjustedActionId(ActionID.GnashingFangPvP) == ActionID.SavageClawPvP;

	/// <summary>
	/// Gnashing Fang 3
	/// </summary>
	public static bool WickedTalonPvPReady => Service.GetAdjustedActionId(ActionID.GnashingFangPvP) == ActionID.WickedTalonPvP;

	/// <summary>
	/// 
	/// </summary>
	public static bool HypervelocityPvPReady => Service.GetAdjustedActionId(ActionID.ContinuationPvP) == ActionID.HypervelocityPvP;

	/// <summary>
	/// 
	/// </summary>
	public static bool FatedBrandPvPReady => Service.GetAdjustedActionId(ActionID.ContinuationPvP) == ActionID.FatedBrandPvP;

	/// <summary>
	/// 
	/// </summary>
	public static bool JugularRipPvPReady => Service.GetAdjustedActionId(ActionID.ContinuationPvP) == ActionID.JugularRipPvP;

	/// <summary>
	/// 
	/// </summary>
	public static bool AbdomenTearPvPReady => Service.GetAdjustedActionId(ActionID.ContinuationPvP) == ActionID.AbdomenTearPvP;

	/// <summary>
	/// 
	/// </summary>
	public static bool EyeGougePvPReady => Service.GetAdjustedActionId(ActionID.ContinuationPvP) == ActionID.EyeGougePvP;
	#endregion

	/// <summary>
	/// 
	/// </summary>
	public static bool HasTerminalTrigger => StatusHelper.PlayerHasStatus(true, StatusID.RelentlessRush);

	#region PvP Actions

	static partial void ModifyGnashingFangPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyFatedCirclePvP(ref ActionSetting setting)
	{
		setting.IsFriendly = false;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyContinuationPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyRoughDividePvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
	}

	static partial void ModifyBlastingZonePvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyHeartOfCorundumPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifySavageClawPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => SavageClawPvPReady;
	}

	static partial void ModifyWickedTalonPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => WickedTalonPvPReady;
	}

	static partial void ModifyHypervelocityPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => HypervelocityPvPReady;
		setting.MPOverride = () => 0;
	}

	static partial void ModifyFatedBrandPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => FatedBrandPvPReady;
		setting.MPOverride = () => 0;
		setting.IsFriendly = false;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyJugularRipPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => JugularRipPvPReady;
		setting.MPOverride = () => 0;
	}

	static partial void ModifyAbdomenTearPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => AbdomenTearPvPReady;
		setting.MPOverride = () => 0;
	}

	static partial void ModifyEyeGougePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => EyeGougePvPReady;
		setting.MPOverride = () => 0;
	}
	#endregion


	/// <inheritdoc/>
	public override bool IsBursting()
	{
		if (StatusHelper.PlayerHasStatus(true, StatusID.NoMercy))
		{
			return true;
		}
		return false;
	}
}