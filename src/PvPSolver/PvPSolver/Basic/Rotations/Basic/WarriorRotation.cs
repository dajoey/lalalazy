using Dalamud.Interface.Colors;

namespace RotationSolver.Basic.Rotations.Basic;

public partial class WarriorRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Strength;

	#region Job Gauge
	/// <summary>
	/// 
	/// </summary>
	public static byte BeastGauge => JobGauge.BeastGauge;

	/// <summary>
	/// 
	/// </summary>
	public static byte OnslaughtMax => EnhancedOnslaughtTrait.EnoughLevel ? (byte)3 : (byte)2;

	/// <summary>
	/// Holds the remaining amount of InnerRelease stacks
	/// </summary>
	public static byte InnerReleaseStacks
	{
		get
		{
			byte stacks = StatusHelper.PlayerStatusStack(true, StatusID.InnerRelease);
			return stacks == byte.MaxValue ? (byte)3 : stacks;
		}
	}

	/// <summary>
	/// Holds the remaining amount of Berserk stacks
	/// </summary>
	public static byte BerserkStacks
	{
		get
		{
			byte stacks = StatusHelper.PlayerStatusStack(true, StatusID.Berserk);
			return stacks == byte.MaxValue ? (byte)3 : stacks;
		}
	}
	#endregion


	#region Debug
	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("InnerReleaseStacks: " + InnerReleaseStacks.ToString());
		ImGui.Text("BerserkStacks: " + BerserkStacks.ToString());
		ImGui.Text("BeastGaugeValue: " + BeastGauge.ToString());
		ImGui.Text("OnslaughtMax: " + OnslaughtMax.ToString());
	}
	#endregion


	#region PvP Actions
	// PvP
	static partial void ModifyInnerChaosPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.InnerChaosReady];
		setting.MPOverride = () => 0;
	}

	static partial void ModifyPrimalRendPvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyPrimalRuinationPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.PrimalRuinationReady_4285];
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyBlotaPvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
	}

	static partial void ModifyOnslaughtPvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
	}

	static partial void ModifyFellCleavePvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.InnerRelease_1303];
	}

	static partial void ModifyPrimalWrathPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = false;
		setting.MPOverride = () => 0;
		setting.StatusNeed = [StatusID.Wrathful_4286];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyOrogenyPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyBloodwhettingPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = true;
	}

	static partial void ModifyChaoticCyclonePvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.ChaoticCycloneReady];
		setting.MPOverride = () => 0;
		setting.IsFriendly = false;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}
	#endregion


	/// <inheritdoc/>
	public override bool IsBursting()
	{
		return StatusHelper.PlayerHasStatus(true, StatusID.SurgingTempest);
	}
}
