namespace RotationSolver.Basic.Rotations.Basic;

public partial class AstrologianRotation : CustomRotation
{
	#region JobGauge

	/// <summary>
	/// 
	/// </summary>
	public override MedicineType MedicineType => MedicineType.Mind;

	/// <summary>
	/// NONE = 0, BALANCE = 1, BOLE = 2, ARROW = 3, SPEAR = 4, EWERS = 5, SPIRE = 6
	/// </summary>
	protected static CardType[] DrawnCard => JobGauge.DrawnCards;
	/// <summary>
	/// 
	/// </summary>
	public static bool HasBalance
	{
		get
		{
			foreach (var card in DrawnCard) { if (card == CardType.Balance) return true; }
			return false;
		}
	}

	/// <summary>
	/// 
	/// </summary>
	public static bool HasBole
	{
		get
		{
			foreach (var card in DrawnCard) { if (card == CardType.Bole) return true; }
			return false;
		}
	}

	/// <summary>
	/// 
	/// </summary>
	public static bool HasArrow
	{
		get
		{
			foreach (var card in DrawnCard) { if (card == CardType.Arrow) return true; }
			return false;
		}
	}

	/// <summary>
	/// 
	/// </summary>
	public static bool HasSpear
	{
		get
		{
			foreach (var card in DrawnCard) { if (card == CardType.Spear) return true; }
			return false;
		}
	}

	/// <summary>
	/// 
	/// </summary>
	public static bool HasEwer
	{
		get
		{
			foreach (var card in DrawnCard) { if (card == CardType.Ewer) return true; }
			return false;
		}
	}

	/// <summary>
	/// 
	/// </summary>
	public static bool HasSpire
	{
		get
		{
			foreach (var card in DrawnCard) { if (card == CardType.Spire) return true; }
			return false;
		}
	}

	/// <summary>
	/// Indicates the state of Minor Arcana and which card will be used next when activating Minor Arcana, LORD = 7, LADY = 8
	/// </summary>
	protected static CardType DrawnCrownCard => JobGauge.DrawnCrownCard;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasLord => DrawnCrownCard == CardType.Lord;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasLady => DrawnCrownCard == CardType.Lady;

	/// <summary>
	///  Can use Umbral or Astral draw, active draw matching what the next draw will be, ASTRAL, UMBRAL
	/// </summary>
	protected static DrawType ActiveDraw => JobGauge.ActiveDraw;

	/// <summary>
	/// Has NeutralSect
	/// </summary>
	public static bool HasNeutralSect => StatusHelper.PlayerHasStatus(true, StatusID.NeutralSect);

	/// <summary>
	/// Has Lightspeed.
	/// </summary>
	public static bool HasLightspeed => StatusHelper.PlayerHasStatus(true, StatusID.Lightspeed);

	/// <summary>
	/// Has Divination.
	/// </summary>
	public static bool HasDivination => StatusHelper.PlayerHasStatus(true, StatusID.Divination);

	/// <summary>
	/// Has Macrocosmos.
	/// </summary>
	public static bool HasMacrocosmos => StatusHelper.PlayerHasStatus(true, StatusID.Macrocosmos);

	/// <summary>
	/// Is holding bubble.
	/// </summary>
	public static bool HasCollectiveUnconscious => StatusHelper.PlayerHasStatus(true, StatusID.CollectiveUnconscious_848);

	/// <summary>
	/// Able to execute Giant Dominance Stellar Detonation.
	/// </summary>
	public static bool HasGiantDominance => StatusHelper.PlayerHasStatus(true, StatusID.GiantDominance);

	/// <summary>
	/// Able to execute Earthly Dominance Stellar Detonation.
	/// </summary>
	public static bool HasEarthlyDominance => StatusHelper.PlayerHasStatus(true, StatusID.EarthlyDominance);

	/// <summary>
	/// Has Synastry.
	/// </summary>
	public static bool HasSynastry => StatusHelper.PlayerHasStatus(true, StatusID.Synastry);
	#endregion

	#region Debug

	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text($"DrawnCard: {string.Join(", ", DrawnCard)}");
		ImGui.Text($"DrawnCrownCard: {DrawnCrownCard}");
		ImGui.Text($"ActiveDraw: {ActiveDraw}");
		ImGui.Text($"RaiseMPMinimum: {RaiseMPMinimum}");
	}
	#endregion


	#region PvP Actions
	static partial void ModifyFallMaleficPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyFallMaleficPvP_29246(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.DoubleCastPvP) == ActionID.FallMaleficPvP_29246;
		setting.MPOverride = () => 0;
	}

	static partial void ModifyAspectedBeneficPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyAspectedBeneficPvP_29247(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.DoubleCastPvP) == ActionID.AspectedBeneficPvP_29247;
		setting.MPOverride = () => 0;
	}

	static partial void ModifyGravityIiPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyGravityIiPvP_29248(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.DoubleCastPvP) == ActionID.GravityIiPvP_29248;
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyDoubleCastPvP(ref ActionSetting setting)
	{
		// You should never send the server this Action.
		setting.ActionCheck = () => false;
		setting.IsFriendly = true;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyMacrocosmosPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = false;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyMicrocosmosPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.MacrocosmosPvP) == ActionID.MicrocosmosPvP;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyMinorArcanaPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = true;
	}

	static partial void ModifyLadyOfCrownsPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.MinorArcanaPvP) == ActionID.LadyOfCrownsPvP;
		setting.IsFriendly = true;
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyLordOfCrownsPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.MinorArcanaPvP) == ActionID.LordOfCrownsPvP;
		setting.IsFriendly = false;
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyOraclePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => StatusHelper.PlayerHasStatus(true, StatusID.Divining_4332);
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyEpicyclePvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
		setting.IsFriendly = true;
	}

	static partial void ModifyRetrogradePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => StatusHelper.PlayerHasStatus(true, StatusID.RetrogradeReady);
		setting.IsFriendly = true;
		setting.MPOverride = () => 0;
		setting.SpecialType = SpecialActionType.MovingBackward;
	}
	#endregion
}