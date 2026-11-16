using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using LalaUiKit;
using RotationSolver.UI.Pvp; // Fork: the tutorial is drawn by the fork's PvpTutorialView and opens PvpSettingsWindow.

namespace RotationSolver.UI;

internal sealed class FirstStartTutorialWindow : Window
{
	// Fork: no native title bar (the drawn header of PvpTutorialView replaces it); the body scrolls in its own child, not the window.
	private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize
		| ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
	private int _stepIndex;

	private static readonly string[] StarterMacros =
	[
		"/pvpsolver Settings AoEType Full\r\n/pvpsolver Auto",
		"/pvpsolver Settings AoEType Cleave\r\n/pvpsolver Manual",
		"/pvpsolver Off",
	];

	// Fork: the steps describe the PvP Solver settings window (its tabs and the on/off bar) instead of the legacy window.
	private static readonly TutorialPage[] Steps =
	[
		new(
			"Welcome",
			"This walkthrough shows how to set up PvP Solver and where each setting lives.",
			Bullets:
			[
				"Open the settings with /pvpsolver or the plugin's settings button.",
				"The window has two groups. Play holds Match, Survival and Targeting. Setup holds My Job, Display and Advanced.",
				"Changes apply and are saved as they are made. Settings can be edited anywhere, in or out of a PvP zone.",
				"Use Next and Back to move through the steps. Closing the window ends the tutorial."
			]),
		new(
			"Turning PvP Solver on",
			"The on and off switch is the button in the bar at the bottom of the settings window.",
			Bullets:
			[
				"In a PvP zone with a character in the world the button reads Turn on. Elsewhere it is unavailable, and its tooltip says why.",
				"While PvP Solver is on the button reads Stop, and the status at the top right reads Running.",
				"PvP Solver can also turn itself on when a match starts, and the chat commands /pvpsolver Auto, Manual and Off do the same by hand."
			]),
		new(
			"Match",
			"Match controls when PvP Solver starts, stops and hands control back.",
			"Match",
			[
				"Choose whether it turns on when a match starts, and whether it turns off when a match ends, when the job changes or during cutscenes.",
				"It can also turn off a set number of seconds after combat has ended.",
				"The Commands card lists every chat command with a Copy button."
			]),
		new(
			"Survival",
			"Survival groups Guard, Purify, movement and the automatic defensive abilities.",
			"Survival",
			[
				"Guard is used automatically at or below the HP percentage set here. One value applies to every job.",
				"Pick which crowd control effects Purify removes: Stun, Silence, Deep Freeze, Miracle of Nature, Heavy and Bind.",
				"The master switch for automatic defensive abilities is here. The list for each job is on the My Job tab."
			]),
		new(
			"Targeting",
			"Targeting orders the target priorities and controls area attacks, burst and stickiness.",
			"Targeting",
			[
				"Area attacks can be Off, Cleave (single-target area attacks only) or Full.",
				"The target list is used from the top. Move entries up or down, add types or reset the list to its default order.",
				"Staying on a target while its own debuff ticks, attacking invulnerable enemies and the tomelith priorities for The Shatter are set here too."
			]),
		new(
			"My Job",
			"My Job edits the options of one job: enablement, rotation options and defensive thresholds.",
			"My Job",
			[
				"Pick any job. A job can be edited in any zone, and it follows the played job until another one is picked.",
				"The switch at the top turns PvP Solver off for that job only.",
				"The job's own options and its defensive abilities, with the HP percentage for each, are below the picker."
			]),
		new(
			"Display",
			"Display controls the overlays, the teaching highlights, messages and tooltips.",
			"Display",
			[
				"Show the current mode in the server info bar, choose what a click on it cycles through, and get a message when the mode changes.",
				"Switch on the Control, Next Action, Cooldown and Timeline windows. They show only in a PvP zone, and the Control window can be locked in place.",
				"Teaching mode highlights the next action on the hotbars so a rotation can be followed by hand."
			]),
		new(
			"Advanced",
			"Advanced holds backup, restore, reset, this tutorial and the debug switch.",
			"Advanced",
			[
				"Back up the settings to a file next to the settings file, and restore them later. A safety copy is kept before a restore.",
				"Reset returns every setting to its default and asks first.",
				"The first-start tutorial can be opened again from here at any time."
			]),
		new(
			"Macros",
			"Starter macros control PvP Solver quickly without opening the settings.",
			"Match",
			[
				"Use the macros below to switch between Auto, Manual and Off instantly.",
				"The Commands card on the Match tab lists every chat command with a Copy button.",
				"Build a small macro bar for fast in-combat control."
			],
			StarterMacros,
			"The full legacy settings are in the Advanced tab, under Full Rotation Solver settings, and open with /pvpsolver advanced."),
	];

	public FirstStartTutorialWindow()
		: base("PvP Solver First Start Tutorial", BaseFlags)
	{
		Size = new Vector2(760, 620); // Fork: room for the header band, page header, card and footer.
		SizeCondition = ImGuiCond.FirstUseEver;
		RespectCloseHotkey = true;
	}

	public override bool DrawConditions()
	{
		return DataCenter.PlayerAvailable();
	}

	public override void Draw()
	{
		// Fork: the dialog shell, step card and footer are drawn by PvpTutorialView; the actions below keep their old effects.
		TutorialAction action = PvpTutorialView.Draw(Steps[_stepIndex], _stepIndex, Steps.Length, CopyMacro, OpenTab);
		switch (action)
		{
			case TutorialAction.Close:
				IsOpen = false;
				break;
			case TutorialAction.Back:
				_stepIndex = Math.Max(0, _stepIndex - 1);
				break;
			case TutorialAction.Next:
				_stepIndex = Math.Min(Steps.Length - 1, _stepIndex + 1);
				break;
			case TutorialAction.Finish:
				FinishTutorial();
				break;
		}
	}

	public override void OnClose()
	{
		MarkTutorialComplete();
		base.OnClose();
	}

	private static void CopyMacro(string macro) // Fork: the copy button's old effect, kept.
	{
		ImGui.SetClipboardText(macro);
		Svc.Toasts.ShowNormal("Macro copied to clipboard.");
	}

	private static void OpenTab(string tab) => PvpSettingsWindow.OpenOn(tab); // Fork: opens the PvP settings window on the tab (was the legacy window).

	private void FinishTutorial()
	{
		MarkTutorialComplete();
		IsOpen = false;
	}

	private static void MarkTutorialComplete()
	{
		Service.Config.TutorialDone = true;
		Service.Config.Save();
	}
}