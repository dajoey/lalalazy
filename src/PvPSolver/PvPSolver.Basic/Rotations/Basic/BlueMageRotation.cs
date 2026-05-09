using CombatRole = RotationSolver.Basic.Data.CombatRole;
namespace RotationSolver.Basic.Rotations.Basic;

public partial class BlueMageRotation : CustomRotation
{
	#region Status Tracking
	/// <summary>
	/// 
	/// </summary>
	public static bool WaxingNocturneWillEnd => HasWaxingNocturne && StatusHelper.PlayerWillStatusEndGCD(2, 0, false, StatusID.WaxingNocturne);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasWaxingNocturne => StatusHelper.PlayerHasStatus(true, StatusID.WaxingNocturne);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasWaningNocturne => StatusHelper.PlayerHasStatus(true, StatusID.WaningNocturne);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasBasicInstinct => StatusHelper.PlayerHasStatus(true, StatusID.BasicInstinct);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasSurpanakhasFury => StatusHelper.PlayerHasStatus(true, StatusID.SurpanakhasFury);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasHarmonizedBoost => StatusHelper.PlayerHasStatus(true, StatusID.Boost_1716) || StatusHelper.PlayerHasStatus(true, StatusID.Harmonized);

	/// <summary>
	/// 
	/// </summary>
	public static bool IsTank => StatusHelper.PlayerHasStatus(true, StatusID.AethericMimicryTank);

	/// <summary>
	/// 
	/// </summary>
	public static bool IsDPS => StatusHelper.PlayerHasStatus(true, StatusID.AethericMimicryDps);

	/// <summary>
	/// 
	/// </summary>
	public static bool IsHealer => StatusHelper.PlayerHasStatus(true, StatusID.AethericMimicryHealer);

	/// <summary>
	/// 
	/// </summary>
	public static bool NoMimicry => !IsTank && !IsDPS && !IsHealer;
	#endregion


	/// <summary>
	/// 
	/// </summary>
	public override MedicineType MedicineType => MedicineType.Intelligence;


	/// <summary>
	/// 
	/// </summary>
	public static CombatRole BlueId => IsTank ? CombatRole.Tank : IsHealer ? CombatRole.Healer : CombatRole.DPS;

	/// <summary>
	///
	/// </summary>
	public override void DisplayBaseStatus()
	{
		ImGui.TextWrapped($"Aetheric Mimicry Role: {BlueId}");
	}
}
