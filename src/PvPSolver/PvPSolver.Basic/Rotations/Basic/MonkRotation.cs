using Dalamud.Interface.Colors;

namespace RotationSolver.Basic.Rotations.Basic;

public partial class MonkRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Strength;

	#region Job Gauge
	/// <summary>
	/// 
	/// </summary>
	protected static BeastChakra[] BeastChakras => JobGauge.BeastChakra;

	/// <summary>
	/// 
	/// </summary>
	public static byte Chakra => JobGauge.Chakra;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasSolar => JobGauge.Nadi.HasFlag(Nadi.Solar);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasLunar => JobGauge.Nadi.HasFlag(Nadi.Lunar);

	/// <summary>
	/// .
	/// </summary>
	public static bool NoNadi => JobGauge.Nadi.HasFlag((Nadi)0);

	/// <summary>
	/// Gets the amount of available Opo-opo Fury stacks.
	/// </summary>
	public static int OpoOpoFury => JobGauge.OpoOpoFury;

	/// <summary>
	/// 
	/// </summary>
	public static bool OpoOpoUnlocked => DataCenter.PlayerSyncedLevel() >= 50;

	/// <summary>
	/// Gets the amount of available Raptor Fury stacks.
	/// </summary>
	public static int RaptorFury => JobGauge.RaptorFury;

	/// <summary>
	/// 
	/// </summary>
	public static bool RaptorUnlocked => DataCenter.PlayerSyncedLevel() >= 18;

	/// <summary>
	/// Gets the amount of available Coeurl Fury stacks.
	/// </summary>
	public static int CoeurlFury => JobGauge.CoeurlFury;

	/// <summary>
	/// 
	/// </summary>
	public static bool CoeurlUnlocked => DataCenter.PlayerSyncedLevel() >= 30;

	/// <summary>
	/// Determines whether all elements in the <see cref="BeastChakras"/> array are the same.
	/// </summary>
	/// <returns>
	/// <c>true</c> if all elements are equal; otherwise, <c>false</c>.
	/// </returns>
	public static bool BeastChakrasAllSame()
	{
		var first = BeastChakras[0];
		for (int i = 1; i < BeastChakras.Length; i++)
		{
			if (!BeastChakras[i].Equals(first))
				return false;
		}
		return true;
	}

	/// <summary>
	/// Determines whether all elements in the <see cref="BeastChakras"/> array are different from each other.
	/// </summary>
	/// <returns>
	/// <c>true</c> if all elements are unique or the array is empty; otherwise, <c>false</c>.
	/// </returns>
	public static bool BeastChakrasAllDifferent()
	{
		for (int i = 0; i < BeastChakras.Length; i++)
		{
			for (int j = i + 1; j < BeastChakras.Length; j++)
			{
				if (BeastChakras[i].Equals(BeastChakras[j]))
					return false;
			}
		}
		return true;
	}

	/// <summary>
	/// Determines whether the specified <paramref name="value"/> exists in the <see cref="BeastChakras"/> array.
	/// </summary>
	/// <param name="value">The <see cref="BeastChakra"/> value to search for.</param>
	/// <returns>
	/// <c>true</c> if the value is found; otherwise, <c>false</c>.
	/// </returns>
	public static bool BeastChakrasContains(BeastChakra value)
	{
		for (int i = 0; i < BeastChakras.Length; i++)
		{
			if (BeastChakras[i].Equals(value))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Determines whether all elements in the <see cref="BeastChakras"/> array do <b>not</b> equal the specified <paramref name="value"/>.
	/// </summary>
	/// <param name="value">The <see cref="BeastChakra"/> value to compare against each element.</param>
	/// <returns>
	/// <c>true</c> if none of the elements equal <paramref name="value"/>; otherwise, <c>false</c>.
	/// </returns>
	public static bool BeastChakrasAllNot(BeastChakra value)
	{
		for (int i = 0; i < BeastChakras.Length; i++)
		{
			if (BeastChakras[i].Equals(value))
				return false;
		}
		return true;
	}
	#endregion

	#region Status Tracking

	/// <summary>
	/// 
	/// </summary>
	public static bool InBrotherhood => StatusHelper.PlayerHasStatus(true, StatusID.Brotherhood);

	/// <summary>
	/// 
	/// </summary>
	public static bool InOpoopoForm => StatusHelper.PlayerHasStatus(true, StatusID.OpoopoForm);

	/// <summary>
	/// 
	/// </summary>
	public static bool InRaptorForm => StatusHelper.PlayerHasStatus(true, StatusID.RaptorForm);

	/// <summary>
	/// 
	/// </summary>
	public static bool InCoeurlForm => StatusHelper.PlayerHasStatus(true, StatusID.CoeurlForm);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasFormlessFist => StatusHelper.PlayerHasStatus(true, StatusID.FormlessFist);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasRiddleOfFire => StatusHelper.PlayerHasStatus(true, StatusID.RiddleOfFire);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasPerfectBalance => StatusHelper.PlayerHasStatus(true, StatusID.PerfectBalance);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasFiresRumination => StatusHelper.PlayerHasStatus(true, StatusID.FiresRumination);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasWindsRumination => StatusHelper.PlayerHasStatus(true, StatusID.WindsRumination);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasBrotherhood => StatusHelper.PlayerHasStatus(true, StatusID.Brotherhood);

	#endregion


	#region Draw Debug

	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text($"BeastChakrasAllSame: {BeastChakrasAllSame()}");
		ImGui.Text($"BeastChakrasContains(BeastChakra.None): {BeastChakrasContains(BeastChakra.None)}");
		ImGui.Text($"All Beast Chakras filled: {BeastChakrasAllNot(BeastChakra.None)}");
		ImGui.Text($"CoeurlFury: {CoeurlFury}");
		ImGui.Text($"RaptorFury: {RaptorFury}");
		ImGui.Text($"OpoOpoFury: {OpoOpoFury}");
		ImGui.Text($"NoNadi: {NoNadi}");
		ImGui.Text($"HasLunar: {HasLunar}");
		ImGui.Text($"HasSolar: {HasSolar}");
		ImGui.Text($"Chakra: {Chakra}");
		ImGui.Text($"BeastChakras: {string.Join(", ", BeastChakras)}");
	}
	#endregion


	#region PvP Actions

	static partial void ModifyDragonKickPvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyTwinSnakesPvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyDemolishPvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyLeapingOpoPvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyRisingRaptorPvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyPouncingCoeurlPvP(ref ActionSetting setting)
	{
	}

	static partial void ModifyPhantomRushPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyWindsReplyPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.WindsRumination];
		setting.TargetStatusProvide = [StatusID.PressurePoint];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyRisingPhoenixPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.FireResonance];
		setting.IsFriendly = false;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyRiddleOfEarthPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.EarthResonance];
		setting.IsFriendly = true;
	}

	static partial void ModifyFiresReplyPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyEarthsReplyPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.EarthResonance];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyThunderclapPvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
	}


	#endregion
}
