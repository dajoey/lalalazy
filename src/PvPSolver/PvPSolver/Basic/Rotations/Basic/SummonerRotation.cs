using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.Game.Gauge;

namespace RotationSolver.Basic.Rotations.Basic;

public partial class SummonerRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Intelligence;


	#region JobGauge

	/// <summary>
	/// 
	/// </summary>
	public static SummonPet ReturnSummons => JobGauge.ReturnSummon;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasAetherflowStacks => JobGauge.HasAetherflowStacks;

	/// <summary>
	/// 
	/// </summary>
	public static byte AttunementCount => JobGauge.AttunementCount;


	/// <summary>
	/// 
	/// </summary>
	public static bool IsSolarBahamutReady => JobGauge.AetherFlags.HasFlag((Dalamud.Game.ClientState.JobGauge.Enums.AetherFlags)8) || JobGauge.AetherFlags.HasFlag((Dalamud.Game.ClientState.JobGauge.Enums.AetherFlags)12);

	/// <summary>
	/// 
	/// </summary>
	public static bool IsBahamutReady => !IsPhoenixReady && !IsSolarBahamutReady;

	/// <summary>
	/// 
	/// </summary>
	public static bool IsPhoenixReady => JobGauge.AetherFlags.HasFlag((Dalamud.Game.ClientState.JobGauge.Enums.AetherFlags)4) && !JobGauge.AetherFlags.HasFlag((Dalamud.Game.ClientState.JobGauge.Enums.AetherFlags)8);

	/// <summary>
	/// 
	/// </summary>
	public static bool IsIfritReady => JobGauge.IsIfritReady;

	/// <summary>
	/// 
	/// </summary>
	public static bool IsTitanReady => JobGauge.IsTitanReady;

	/// <summary>
	/// 
	/// </summary>
	public static bool IsGarudaReady => JobGauge.IsGarudaReady;

	/// <summary>
	/// 
	/// </summary>
	public static bool InTitan => JobGauge.IsTitanAttuned;

	/// <summary>
	/// 
	/// </summary>
	public static bool InIfrit => JobGauge.IsIfritAttuned;

	/// <summary>
	/// 
	/// </summary>
	public static bool InGaruda => JobGauge.IsGarudaAttuned;

	/// <summary>
	/// 
	/// </summary>
	/// <summary>
	/// 
	/// </summary>
	public static byte SMNAetherflowStacks => JobGauge.AetherflowStacks;

	/// <summary>
	/// 
	/// </summary>
	public static float SummonTimeRaw => JobGauge.SummonTimerRemaining / 1000f;

	/// <summary>
	/// 
	/// </summary>
	public static float SummonTime => SummonTimeRaw - DataCenter.DefaultGCDRemain;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="time"></param>
	/// <returns></returns>
	protected static bool SummonTimeEndAfter(float time)
	{
		return SummonTime <= time;
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="gcdCount"></param>
	/// <param name="offset"></param>
	/// <returns></returns>
	protected static bool SummonTimeEndAfterGCD(uint gcdCount = 0, float offset = 0)
	{
		return SummonTimeEndAfter(GCDTime(gcdCount, offset));
	}

	private static float AttunmentTimeRaw => JobGauge.AttunementTimerRemaining / 1000f;

	/// <summary>
	/// 
	/// </summary>
	public static float AttunmentTime => AttunmentTimeRaw - DataCenter.DefaultGCDRemain;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="time"></param>
	/// <returns></returns>
	protected static bool AttunmentTimeEndAfter(float time)
	{
		return AttunmentTime <= time;
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="gcdCount"></param>
	/// <param name="offset"></param>
	/// <returns></returns>
	protected static bool AttunmentTimeEndAfterGCD(uint gcdCount = 0, float offset = 0)
	{
		return AttunmentTimeEndAfter(GCDTime(gcdCount, offset));
	}

	/// <summary>
	/// 
	/// </summary>
	public static bool HasSummon => DataCenter.HasPet() && SummonTimeEndAfterGCD();

	/// <summary>
	/// 
	/// </summary>

	/// <summary>
	/// 
	/// </summary>

	/// <summary>
	/// 
	/// </summary>
	public static bool NoPrimalReady => !IsIfritReady && !IsGarudaReady && !IsTitanReady;

	/// <summary>
	/// 
	/// </summary>
	public static bool AnyPrimalReady => IsIfritReady || IsGarudaReady || IsTitanReady;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasAnyFavor => HasGarudaFavor || HasIfritFavor || HasTitanFavor;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasAnyAttunement => InGaruda || InIfrit || InTitan;

	/// <summary>
	/// 
	/// </summary>
	public static bool NoAttunement => !InIfrit && !InGaruda && !InTitan;

	/// <summary>
	/// 
	/// </summary>
	#endregion

	#region Status
	/// <summary>
	/// 
	/// </summary>
	public static bool HasFurtherRuin => StatusHelper.PlayerHasStatus(true, StatusID.FurtherRuin_2701);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasCrimsonStrike => StatusHelper.PlayerHasStatus(true, StatusID.CrimsonStrikeReady_4403);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasRadiantAegis => StatusHelper.PlayerHasStatus(true, StatusID.RadiantAegis);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasGarudaFavor => StatusHelper.PlayerHasStatus(true, StatusID.GarudasFavor);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasIfritFavor => StatusHelper.PlayerHasStatus(true, StatusID.IfritsFavor);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasTitanFavor => StatusHelper.PlayerHasStatus(true, StatusID.TitansFavor);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasSearingLight => StatusHelper.PlayerHasStatus(true, StatusID.SearingLight);

	#endregion


	#region Draw Debug

	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("ReturnSummons: " + ReturnSummons.ToString());
		ImGui.Text("HasAetherflowStacks: " + HasAetherflowStacks.ToString());
		ImGui.Text("Attunement: " + AttunementCount.ToString());
		ImGui.Spacing();
		LalaUiKit.Widgets.StateText("IsSolarBahamutReady: " + IsSolarBahamutReady.ToString(), IsSolarBahamutReady ? LalaUiKit.UiState.Ready : LalaUiKit.UiState.Idle);
		LalaUiKit.Widgets.StateText("IsBahamutReady: " + IsBahamutReady.ToString(), IsBahamutReady ? LalaUiKit.UiState.Ready : LalaUiKit.UiState.Idle);
		LalaUiKit.Widgets.StateText("IsPhoenixReady: " + IsPhoenixReady.ToString(), IsPhoenixReady ? LalaUiKit.UiState.Ready : LalaUiKit.UiState.Idle);
		LalaUiKit.Widgets.StateText("IsIfritReady: " + InGaruda.ToString(), IsIfritReady ? LalaUiKit.UiState.Ready : LalaUiKit.UiState.Idle);
		LalaUiKit.Widgets.StateText("IsTitanReady: " + IsTitanReady.ToString(), IsTitanReady ? LalaUiKit.UiState.Ready : LalaUiKit.UiState.Idle);
		LalaUiKit.Widgets.StateText("IsGarudaReady: " + IsGarudaReady.ToString(), IsGarudaReady ? LalaUiKit.UiState.Ready : LalaUiKit.UiState.Idle);
		ImGui.Spacing();
		LalaUiKit.Widgets.StateText("InIfrit: " + InIfrit.ToString(), InIfrit ? LalaUiKit.UiState.Ready : LalaUiKit.UiState.Idle);
		LalaUiKit.Widgets.StateText("InTitan: " + InTitan.ToString(), InTitan ? LalaUiKit.UiState.Ready : LalaUiKit.UiState.Idle);
		LalaUiKit.Widgets.StateText("InGaruda: " + InGaruda.ToString(), InGaruda ? LalaUiKit.UiState.Ready : LalaUiKit.UiState.Idle);
		ImGui.Spacing();
		ImGui.Text("SMNAetherflowStacks: " + SMNAetherflowStacks.ToString());
		ImGui.Text("SummonTime: " + SummonTime.ToString());
		ImGui.Text("AttunmentTime: " + AttunmentTime.ToString());
		ImGui.Text("HasSummon: " + HasSummon.ToString());
		ImGui.Text("Can Heal Single Spell: " + CanHealSingleSpell.ToString());
	}
	#endregion


	#region PvP Actions
	static partial void ModifyRuinIiiPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyRuinIvPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.RuinIiiPvP) == ActionID.RuinIvPvP;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyMountainBusterPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.TargetStatusProvide = [StatusID.Stun_1343];
	}

	static partial void ModifySlipstreamPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.TargetStatusProvide = [StatusID.Slipping];
	}

	static partial void ModifyCrimsonCyclonePvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.HostileMovingForward;
		setting.StatusProvide = [StatusID.CrimsonStrikeReady_4403];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyCrimsonStrikePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.CrimsonCyclonePvP) == ActionID.CrimsonStrikePvP;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyRadiantAegisPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.RadiantAegis_3224];
	}

	static partial void ModifyNecrotizePvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.FurtherRuin_4399];
	}

	static partial void ModifyDeathflarePvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.DreadwyrmTrance_3228];
	}

	static partial void ModifyAstralImpulsePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.RuinIiiPvP) == ActionID.AstralImpulsePvP;
	}

	static partial void ModifyBrandOfPurgatoryPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.FirebirdTrance];
	}

	static partial void ModifyFountainOfFirePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.RuinIiiPvP) == ActionID.FountainOfFirePvP;
	}
	#endregion

}
