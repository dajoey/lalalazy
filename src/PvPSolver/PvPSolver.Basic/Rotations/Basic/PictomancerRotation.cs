using Dalamud.Interface.Colors;

namespace RotationSolver.Basic.Rotations.Basic;

public partial class PictomancerRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Intelligence;

	#region Job Gauge
	/// <summary>
	/// Tracks use of subjective pallete
	/// </summary>
	public static byte PaletteGauge => JobGauge.PalleteGauge;

	/// <summary>
	/// Number of paint the player has.
	/// </summary>
	public static byte Paint => JobGauge.Paint;

	/// <summary>
	/// Creature Motif Stack
	/// </summary>
	public static bool CreatureMotifDrawn => JobGauge.CreatureMotifDrawn;

	/// <summary>
	/// Weapon Motif Stack
	/// </summary>
	public static bool WeaponMotifDrawn => JobGauge.WeaponMotifDrawn;

	/// <summary>
	/// Landscape Motif Stack
	/// </summary>
	public static bool LandscapeMotifDrawn => JobGauge.LandscapeMotifDrawn;

	/// <summary>
	/// Moogle Portrait Stack
	/// </summary>
	public static bool MooglePortraitReady => JobGauge.MooglePortraitReady;

	/// <summary>
	/// Madeen Portrait Stack
	/// </summary>
	public static bool MadeenPortraitReady => JobGauge.MadeenPortraitReady;

	/// <summary>
	/// Which creature flags are present. Pom = 1, Wings = 2, Claw = 4, MooglePortait = 0x10, MadeenPortrait = 0x20, these are the small paintings above Maw/Pom
	/// </summary>
	public static CreatureFlags CreatureFlags => JobGauge.CreatureFlags;

	/// <summary>
	/// Which canvas flags are present.  Pom = 1, Wing = 2, Claw = 4, Maw = 8, Weapon = 0x10, Landscape = 0x20, these are the motif flags
	/// </summary>
	public static CanvasFlags CanvasFlags => JobGauge.CanvasFlags;
	#endregion

	#region Flag Tracking
	/// <summary>
	/// Is Pom Motif ready
	/// </summary>
	public static bool IsPomMotifReady => (byte)JobGauge.CreatureFlags is 32 or 0;

	/// <summary>
	/// Is Wing Motif ready
	/// </summary>
	public static bool IsWingMotifReady => (byte)JobGauge.CreatureFlags is 33 or 1;

	/// <summary>
	/// Is Claw Motif ready
	/// </summary>
	public static bool IsClawMotifReady => (byte)JobGauge.CreatureFlags is 19 or 3;

	/// <summary>
	/// Is Maw Motif ready
	/// </summary>
	public static bool IsMawMotifReady => (byte)JobGauge.CreatureFlags is 23 or 7;

	/// <summary>
	/// Is Wing ready
	/// </summary>
	public static bool IsPomMuseReady => ((byte)JobGauge.CanvasFlags & 1) == 1 || ((byte)JobGauge.CanvasFlags & 17) == 17 || ((byte)JobGauge.CanvasFlags & 33) == 33 || ((byte)JobGauge.CanvasFlags & 49) == 49;

	/// <summary>
	/// Is Claw ready
	/// </summary>
	public static bool IsWingMuseReady => ((byte)JobGauge.CanvasFlags & 2) == 2 || ((byte)JobGauge.CanvasFlags & 18) == 18 || ((byte)JobGauge.CanvasFlags & 34) == 34 || ((byte)JobGauge.CanvasFlags & 50) == 50;

	/// <summary>
	/// Is Pom ready
	/// </summary>
	public static bool IsClawMuseReady => ((byte)JobGauge.CanvasFlags & 4) == 4 || ((byte)JobGauge.CanvasFlags & 20) == 20 || ((byte)JobGauge.CanvasFlags & 36) == 36 || ((byte)JobGauge.CanvasFlags & 52) == 52;

	/// <summary>
	/// Is Maw ready
	/// </summary>
	public static bool IsMawMuseReady => ((byte)JobGauge.CanvasFlags & 8) == 8 || ((byte)JobGauge.CanvasFlags & 24) == 24 || ((byte)JobGauge.CanvasFlags & 40) == 40 || ((byte)JobGauge.CanvasFlags & 56) == 56;

	/// <summary>
	/// Is Hammer ready
	/// </summary>
	public static bool IsHammerMuseReady => ((byte)JobGauge.CanvasFlags & 16) == 16 || ((byte)JobGauge.CanvasFlags & 17) == 17 || ((byte)JobGauge.CanvasFlags & 18) == 18 || ((byte)JobGauge.CanvasFlags & 20) == 20
		|| ((byte)JobGauge.CanvasFlags & 24) == 24 || ((byte)JobGauge.CanvasFlags & 48) == 48 || ((byte)JobGauge.CanvasFlags & 49) == 49
		|| ((byte)JobGauge.CanvasFlags & 50) == 50 || ((byte)JobGauge.CanvasFlags & 52) == 52
		|| ((byte)JobGauge.CanvasFlags & 56) == 56;

	/// <summary>
	/// Is Starry ready
	/// </summary>
	public static bool IsStarryMuseReady => ((byte)JobGauge.CanvasFlags & 32) == 32 || ((byte)JobGauge.CanvasFlags & 33) == 33 || ((byte)JobGauge.CanvasFlags & 34) == 34 || ((byte)JobGauge.CanvasFlags & 36) == 36
		|| ((byte)JobGauge.CanvasFlags & 40) == 40 || ((byte)JobGauge.CanvasFlags & 48) == 48 || ((byte)JobGauge.CanvasFlags & 49) == 49
		|| ((byte)JobGauge.CanvasFlags & 50) == 50 || ((byte)JobGauge.CanvasFlags & 52) == 52
		|| ((byte)JobGauge.CanvasFlags & 56) == 56;
	#endregion


	#region Debug
	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text($"HasRainbowBright: {HasRainbowBright}");
		ImGui.Text($"PaletteGauge: {PaletteGauge}");
		ImGui.Text($"Paint: {Paint}");
		ImGui.Text($"CreatureMotifDrawn: {CreatureMotifDrawn}");
		ImGui.Text($"WeaponMotifDrawn: {WeaponMotifDrawn}");
		ImGui.Text($"LandscapeMotifDrawn: {LandscapeMotifDrawn}");
		ImGui.Text($"MooglePortraitReady: {MooglePortraitReady}");
		ImGui.Text($"MadeenPortraitReady: {MadeenPortraitReady}");
		ImGui.Text($"CreatureFlags: {CreatureFlags}");
		ImGui.Text($"isPomMotifReady: {IsPomMotifReady}");
		ImGui.Text($"isWingMotifReady: {IsWingMotifReady}");
		ImGui.Text($"isClawMotifReady: {IsClawMotifReady}");
		ImGui.Text($"isMawMotifReady: {IsMawMotifReady}");
		ImGui.Text($"CanvasFlags: {CanvasFlags}");
		ImGui.Text($"isPomMuseReady: {IsPomMuseReady}");
		ImGui.Text($"isWingMuseReady: {IsWingMuseReady}");
		ImGui.Text($"isClawMuseReady: {IsClawMuseReady}");
		ImGui.Text($"isMawMuseReady: {IsMawMuseReady}");
		ImGui.Text($"isHammerMuseReady: {IsHammerMuseReady}");
		ImGui.Text($"isStarryMuseReady: {IsStarryMuseReady}");
		ImGui.Text($"MaxStrikingMuse: {MaxStrikingMuse}");
		ImGui.Text($"Level100: {Level100}");
		ImGui.Text($"HasSubtractivePalette: {HasSubtractivePalette}");
		ImGui.Text($"HasAetherhues: {HasAetherhues}");
		ImGui.Text($"HasAetherhues2: {HasAetherhues2}");
		ImGui.Text($"HasSubtractiveSpectrum: {HasSubtractiveSpectrum}");
		ImGui.Text($"HasHyperphantasia: {HasHyperphantasia}");
		ImGui.Text($"HasHammerTime: {HasHammerTime}");
		ImGui.Text($"HasMonochromeTones: {HasMonochromeTones}");
		ImGui.Text($"HasStarryMuse: {HasStarryMuse}");
		ImGui.Text($"HammerStacks: {HammerStacks}");
		ImGui.Text($"SubtractiveStacks: {SubtractiveStacks}");
	}
	#endregion

	#region Job States

	/// <summary>
	/// Number of max charges Striking Muse can have
	/// </summary>
	public static byte MaxStrikingMuse => EnhancedPictomancyIiTrait.EnoughLevel ? (byte)2 : (byte)1;

	/// <summary>
	/// Determines if player is max level or not
	/// </summary>
	public static bool Level100 => EnhancedPictomancyVTrait.EnoughLevel;

	#endregion

	#region Statuses
	/// <summary>
	/// Indicates if the player has Aetherhues.
	/// </summary>
	public static bool HasAetherhues => StatusHelper.PlayerHasStatus(true, StatusID.Aetherhues);

	/// <summary>
	/// Indicates if the player has Aetherhues II.
	/// </summary>
	public static bool HasAetherhues2 => StatusHelper.PlayerHasStatus(true, StatusID.AetherhuesIi);

	/// <summary>
	/// Indicates if the player has Subtractive Palette.
	/// </summary>
	public static bool HasSubtractivePalette => StatusHelper.PlayerHasStatus(true, StatusID.SubtractivePalette);

	/// <summary>
	/// Indicates if the player has Subtractive Spectrum.
	/// </summary>
	public static bool HasSubtractiveSpectrum => StatusHelper.PlayerHasStatus(true, StatusID.SubtractiveSpectrum);

	/// <summary>
	/// Indicates if the player has Hyperphantasia.
	/// </summary>
	public static bool HasHyperphantasia => StatusHelper.PlayerHasStatus(true, StatusID.Hyperphantasia);

	/// <summary>
	/// Indicates if the player has Hammer Time.
	/// </summary>
	public static bool HasHammerTime => StatusHelper.PlayerHasStatus(true, StatusID.HammerTime);

	/// <summary>
	/// Indicates if the player has Monochrome Tones.
	/// </summary>
	public static bool HasMonochromeTones => StatusHelper.PlayerHasStatus(true, StatusID.MonochromeTones);

	/// <summary>
	/// Indicates if the player has Starry Muse.
	/// </summary>
	public static bool HasStarryMuse => StatusHelper.PlayerHasStatus(true, StatusID.StarryMuse);

	/// <summary>
	/// Indicates if the player has Rainbow Bright.
	/// </summary>
	public static bool HasRainbowBright => StatusHelper.PlayerHasStatus(true, StatusID.RainbowBright);

	/// <summary>
	/// Indicates if the player has Rainbow Bright.
	/// </summary>
	public static bool HasStarstruck => StatusHelper.PlayerHasStatus(true, StatusID.Starstruck);

	/// <summary>
	/// Holds the remaining amount of HammerTime stacks
	/// </summary>
	public static byte HammerStacks
	{
		get
		{
			byte stacks = StatusHelper.PlayerStatusStack(true, StatusID.HammerTime);
			return stacks == byte.MaxValue ? (byte)3 : stacks;
		}
	}

	/// <summary>
	/// Holds the remaining amount of SubtractivePalette stacks
	/// </summary>
	public static byte SubtractiveStacks
	{
		get
		{
			byte stacks = StatusHelper.PlayerStatusStack(true, StatusID.SubtractivePalette);
			return stacks == byte.MaxValue ? (byte)3 : stacks;
		}
	}

	#endregion


	#region PvP Actions
	static partial void ModifyFireInRedPvP(ref ActionSetting setting)
	{
		// setting.ActionCheck = () => true;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyHolyInWhitePvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyLivingMusePvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			IsEnabled = false,
		};
	}

	static partial void ModifyMogOfTheAgesPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.MooglePortrait];
		setting.MPOverride = () => 0;
		setting.TargetStatusProvide = [StatusID.Silence_1347];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifySmudgePvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.Smudge_4113, StatusID.QuickSketch];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.IsFriendly = true;
	}

	static partial void ModifyTemperaCoatPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.TemperaCoat_4114];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.IsFriendly = true;
	}

	static partial void ModifySubtractivePalettePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.SubtractivePalettePvP) == ActionID.SubtractivePalettePvP;
		setting.StatusProvide = [StatusID.SubtractivePalette_4102];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.IsFriendly = true;
	}

	static partial void ModifyAdventOfChocobastionPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyAeroInGreenPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.Aetherhues_4100];
		setting.StatusProvide = [StatusID.AetherhuesIi_4101];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyWaterInBluePvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.AetherhuesIi_4101];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyBlizzardInCyanPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.SubtractivePalette_4102];
		setting.StatusProvide = [StatusID.Aetherhues_4100];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyStoneInYellowPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.Aetherhues_4100];
		setting.StatusProvide = [StatusID.AetherhuesIi_4101];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyThunderInMagentaPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.AetherhuesIi_4101];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyCometInBlackPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.SubtractivePalette_4102];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyPomMotifPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.PomSketch];
		setting.MPOverride = () => 0;
		setting.StatusProvide = [StatusID.PomMotif];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.IsFriendly = true;
	}

	static partial void ModifyWingMotifPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.WingSketch];
		setting.MPOverride = () => 0;
		setting.StatusProvide = [StatusID.WingMotif];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.IsFriendly = true;
	}

	static partial void ModifyClawMotifPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.ClawSketch];
		setting.MPOverride = () => 0;
		setting.StatusProvide = [StatusID.ClawMotif];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.IsFriendly = true;
	}

	static partial void ModifyMawMotifPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.MawSketch];
		setting.MPOverride = () => 0;
		setting.StatusProvide = [StatusID.MawMotif];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.IsFriendly = true;
	}

	static partial void ModifyCreatureMotifPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			IsEnabled = false,
		};
	}

	static partial void ModifyPomMusePvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.PomMotif];
		setting.MPOverride = () => 0;
		setting.StatusProvide = [StatusID.WingSketch];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyWingedMusePvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.WingMotif];
		setting.MPOverride = () => 0;
		setting.StatusProvide = [StatusID.ClawSketch];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyClawedMusePvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.ClawMotif];
		setting.MPOverride = () => 0;
		setting.StatusProvide = [StatusID.MawSketch];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyFangedMusePvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.MawMotif];
		setting.MPOverride = () => 0;
		setting.StatusProvide = [StatusID.PomSketch, StatusID.MadeenPortrait];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyRetributionOfTheMadeenPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.MadeenPortrait];
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyTemperaGrassaPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.TemperaCoat];
		setting.StatusProvide = [StatusID.TemperaGrassa];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyStarPrismPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.Starstruck_4118];
		setting.MPOverride = () => 0;
		setting.StatusProvide = [StatusID.StarPrism];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}
	#endregion
}