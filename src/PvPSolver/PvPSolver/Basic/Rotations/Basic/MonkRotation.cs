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
	public static BeastChakra BeastChakra1 => JobGauge.BeastChakra1;

	/// <summary>
	///
	/// </summary>
	public static BeastChakra BeastChakra2 => JobGauge.BeastChakra2;

	/// <summary>
	///
	/// </summary>
	public static BeastChakra BeastChakra3 => JobGauge.BeastChakra3;

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
	/// Determines whether all three Beast Chakra slots are the same.
	/// </summary>
	/// <returns>
	/// <c>true</c> if all slots are equal; otherwise, <c>false</c>.
	/// </returns>
	public static bool BeastChakrasAllSame()
	{
		return BeastChakra1 == BeastChakra2 && BeastChakra2 == BeastChakra3;
	}

	/// <summary>
	/// Determines whether all three Beast Chakra slots are different from each other.
	/// </summary>
	/// <returns>
	/// <c>true</c> if all slots are unique; otherwise, <c>false</c>.
	/// </returns>
	public static bool BeastChakrasAllDifferent()
	{
		return BeastChakra1 != BeastChakra2 && BeastChakra1 != BeastChakra3 && BeastChakra2 != BeastChakra3;
	}

	/// <summary>
	/// Determines whether the specified <paramref name="value"/> is in any of the three Beast Chakra slots.
	/// </summary>
	/// <param name="value">The <see cref="BeastChakra"/> value to search for.</param>
	/// <returns>
	/// <c>true</c> if the value is found; otherwise, <c>false</c>.
	/// </returns>
	public static bool BeastChakrasContains(BeastChakra value)
	{
		return BeastChakra1 == value || BeastChakra2 == value || BeastChakra3 == value;
	}

	/// <summary>
	/// Determines whether none of the three Beast Chakra slots equal the specified <paramref name="value"/>.
	/// </summary>
	/// <param name="value">The <see cref="BeastChakra"/> value to compare against each slot.</param>
	/// <returns>
	/// <c>true</c> if none of the slots equal <paramref name="value"/>; otherwise, <c>false</c>.
	/// </returns>
	public static bool BeastChakrasAllNot(BeastChakra value)
	{
		return !BeastChakrasContains(value);
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
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
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

	static partial void ModifyMeteodrivePvP(ref ActionSetting setting)
	{
		// Meteodrive removes the target's Guard (game text), so a Guarded enemy is a valid target.
		setting.IgnoreGuard = true;
	}
}
