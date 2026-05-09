using Dalamud.Interface.Colors;

namespace RotationSolver.Basic.Rotations.Basic;

public partial class BardRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Dexterity;

	#region Job Gauge
	/// <summary>
	/// Gets the amount of Repertoire accumulated
	/// </summary>
	public static byte Repertoire => JobGauge.Repertoire;

	/// <summary>
	/// Gets the type of song that is active NONE = 0, MAGE = 1, ARMY = 2, WANDERER = 3
	/// </summary>
	protected static Song Song => JobGauge.Song;

	/// <summary>
	/// Gets the type of song that was last played
	/// </summary>
	protected static Song LastSong => JobGauge.LastSong;

	/// <summary>
	/// Gets the amount of Soul Voice accumulated
	/// </summary>
	public static byte SoulVoice => JobGauge.SoulVoice;
	private static float SongTimeRaw => JobGauge.SongTimer / 1000f;

	/// <summary>
	/// Gets the current song timer in milliseconds.
	/// </summary>
	public static float SongTime => SongTimeRaw - DataCenter.DefaultGCDRemain;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="time"></param>
	/// <returns></returns>
	protected static bool SongEndAfter(float time)
	{
		return SongTime <= time;
	}

	/// <summary>
	/// 
	/// </summary>
	public static byte BloodletterMax => EnhancedBloodletterTrait.EnoughLevel ? (byte)3 : (byte)2;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="gctCount"></param>
	/// <param name="offset"></param>
	/// <returns></returns>
	protected static bool SongEndAfterGCD(uint gctCount = 0, float offset = 0)
	{
		return SongEndAfter(GCDTime(gctCount, offset));
	}
	#endregion

	#region Status Tracking
	/// <summary>
	/// Able to execute Raging Strikes.
	/// </summary>
	public static bool HasRagingStrikes => StatusHelper.PlayerHasStatus(true, StatusID.RagingStrikes);

	/// <summary>
	/// Able to execute Barrage.
	/// </summary>
	public static bool HasBarrage => StatusHelper.PlayerHasStatus(true, StatusID.Barrage);

	/// <summary>
	/// Able to execute Hawks Eye.
	/// </summary>
	public static bool HasHawksEye => StatusHelper.PlayerHasStatus(true, StatusID.HawksEye_3861);

	/// <summary>
	/// Able to execute Battle Voice.
	/// </summary>
	public static bool HasBattleVoice => StatusHelper.PlayerHasStatus(true, StatusID.BattleVoice);

	/// <summary>
	/// Able to execute Radiant Finale.
	/// </summary>
	public static bool HasRadiantFinale => StatusHelper.PlayerHasStatus(true, StatusID.RadiantFinale);

	/// <summary>
	///
	/// </summary>
	public static bool HasResonantArrow => StatusHelper.PlayerHasStatus(true, StatusID.ResonantArrowReady);
	#endregion


	#region Draw Debug

	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("Repertoire: " + Repertoire.ToString());
		ImGui.Text("Song: " + Song.ToString());
		ImGui.Text("LastSong: " + LastSong.ToString());
		ImGui.Text("SoulVoice: " + SoulVoice.ToString());
		ImGui.Text("SongTimeRaw: " + SongTimeRaw.ToString());
		ImGui.Text("SongTime: " + SongTime.ToString());
		ImGui.Text("BloodletterMax: " + BloodletterMax.ToString());
	}
	#endregion


	#region PvP
	// PvP
	static partial void ModifyPowerfulShotPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyPitchPerfectPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.Repertoire];
	}

	static partial void ModifyApexArrowPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.BlastArrowReady_3142, StatusID.FrontlinersMarch];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyBlastArrowPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.BlastArrowReady_3142];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifySilentNocturnePvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.Repertoire];
		setting.TargetStatusProvide = [StatusID.Silenced];
	}

	static partial void ModifyRepellingShotPvP(ref ActionSetting setting)
	{
		setting.SpecialType = SpecialActionType.MovingBackward;
	}

	static partial void ModifyEncoreOfLightPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.EncoreOfLightReady];
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyTheWardensPaeanPvP(ref ActionSetting setting)
	{
		setting.TargetStatusNeed = StatusHelper.PurifyPvPStatuses;
		setting.IsFriendly = true;
		setting.TargetType = TargetType.Dispel;
	}

	#endregion
}
