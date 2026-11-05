using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.DutyState;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using Lalalazy.Changelog;
using Lalalazy.Hub;
using Lumina.Excel.Sheets;
using RebornMaterial;
using RotationSolver.ActionTimeline;
using RotationSolver.Basic.Configuration;
using RotationSolver.Commands;
using RotationSolver.Data;
using RotationSolver.IPC;
using RotationSolver.UI;
using RotationSolver.UI.Pvp;
using RotationSolver.UI.HighlightTeachingMode;
using RotationSolver.UI.HighlightTeachingMode.ElementSpecial;
using RotationSolver.Updaters;
using Player = ECommons.GameHelpers.Player;

namespace RotationSolver;

public sealed class PvPSolverPlugin : IDalamudPlugin, IDisposable
{
	private readonly WindowSystem windowSystem;

	private static RotationConfigWindow? _rotationConfigWindow;
	private static PvpSettingsWindow? _pvpSettingsWindow;
	private static ControlWindow? _controlWindow;
	private static NextActionWindow? _nextActionWindow;
	private static InterceptedActionWindow? _interceptedActionWindow;
	private static ActionTimelineWindow? _actionTimelineWindow;
	private static OverlayWindow? _overlayWindow;
	private static StateControlWindow? _stateControlWindow;
	private static EasterEggWindow? _easterEggWindow;
	private static FirstStartTutorialWindow? _firstStartTutorialWindow;
	// Shared lalalazy "What's new" popup - fork wiring, seen-version in a sidecar json (NOT Configs).
	private static ChangelogGate? _changelog;

	private static readonly List<IDisposable> _dis = [];
	public static string Name => "PvP Solver";
	internal static readonly List<DrawingHighlightHotbarBase> _drawingElements = [];

	public static DalamudLinkPayload OpenLinkPayload { get; private set; } = null!;
	public static DalamudLinkPayload? HideWarningLinkPayload { get; private set; }
	private static readonly Random _random = new();

	internal IPCProvider IPCProvider;
	private readonly LalaHubProvider? _hub;
	public PvPSolverPlugin(IDalamudPluginInterface pluginInterface)
	{
		// Read BEFORE anything touches/saves the config: tells the changelog gate "update" from "fresh install".
		var existingInstall = pluginInterface.ConfigFile.Exists;
		ECommonsMain.Init(pluginInterface, this, ECommons.Module.DalamudReflector, ECommons.Module.ObjectFunctions);
		PluginLog.Information("PvPSolverPlugin constructor START");
		_ = Svc.Framework.RunOnTick(() =>
		{
			_ = ThreadLoadImageHandler.TryGetIconTextureWrap(0, true, out _);
		});
		IconSet.Init();

		_dis.Add(new Service());

		ActionTracer.Init();

		IPCProvider = new();

		_rotationConfigWindow = new();
		_pvpSettingsWindow = new();
		_controlWindow = new();
		_nextActionWindow = new();
		_interceptedActionWindow = new();
		_actionTimelineWindow = new();
		_overlayWindow = new();
		_stateControlWindow = new();
		_easterEggWindow = new();
		_firstStartTutorialWindow = new();
		_updateNotesWindow = new();

		windowSystem = new WindowSystem(Name);
		windowSystem.AddWindow(_rotationConfigWindow);
		windowSystem.AddWindow(_pvpSettingsWindow);
		windowSystem.AddWindow(_controlWindow);
		windowSystem.AddWindow(_nextActionWindow);
		windowSystem.AddWindow(_interceptedActionWindow);
		windowSystem.AddWindow(_actionTimelineWindow);
		windowSystem.AddWindow(_overlayWindow);
		windowSystem.AddWindow(_stateControlWindow);
		windowSystem.AddWindow(_easterEggWindow);
		windowSystem.AddWindow(_firstStartTutorialWindow);

		// Shared "What's new" popup (repo standing rule): shows this plugin's CHANGELOG once after an
		// update. Fork: SidecarSeenStore keeps the seen-version out of the upstream Configs class.
		_changelog = new ChangelogGate(new ChangelogGate.Options
		{
			PluginAssembly = typeof(PvPSolverPlugin).Assembly,
			DisplayName = "PvP Solver",
			ChangelogPath = "src/PvPSolver/CHANGELOG.md",
			Framework = Svc.Framework,
			ClientState = Svc.ClientState,
			Condition = Svc.Condition,
			Log = Svc.Log,
			Windows = windowSystem,
			ExistingInstall = existingInstall,
			SeenStore = new SidecarSeenStore(Svc.PluginInterface, (ex, msg) => Svc.Log.Warning(ex, msg)),
		});

		//Notify.Success("Overlay Window was added!");

		Svc.PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
		Svc.PluginInterface.UiBuilder.OpenMainUi += OnOpenConfigUi;
		Svc.PluginInterface.UiBuilder.Draw += OnDraw;
	}

		//HotbarHighlightDrawerManager.Init();

		MajorUpdater.Enable();
		PluginLog.Information("PvPSolverPlugin constructor: MajorUpdater.Enable() done");
		Watcher.Enable();
		ActionQueueManager.Enable();
		OtherConfiguration.Init();
		ActionContextMenu.Init();
		HotbarHighlightManager.Init();

		// DutyState events removed — Dalamud API 15 changed delegate signatures
		Svc.ClientState.TerritoryChanged += ClientState_TerritoryChanged;
		ClientState_TerritoryChanged(Svc.ClientState.TerritoryType);

		static void ClientState_TerritoryChanged(uint id)
		{
			await textureWarmupTask;
		}
		catch (OperationCanceledException)
		{
			PluginLog.Warning("Texture warmup was canceled; continuing plugin load.");
		}

			if (id == 0)
			{
				PluginLog.Information("Invalid territory id: 0");
				return;
			}

			TerritoryType territory = Service.GetSheet<TerritoryType>().GetRow((ushort)id);

			ChangeUITranslation();

			OpenLinkPayload = Svc.Chat.AddChatLinkHandler(0, (guid, seString) =>
			{
				if (guid == 0)
				{
					OpenConfigWindow();
				}
			});
			HideWarningLinkPayload = Svc.Chat.AddChatLinkHandler(1, (guid, seString) =>
			{
				if (guid == 0)
				{
					Service.Config.HideWarning.Value = true;
					Svc.Chat.Print("Warning has been hidden.");
				}
			});
		}, CancellationToken.None);
	}

		ChangeUITranslation();

		if (!TargetFilter.PlayerJobCategory(JobRole.Tank) && !TargetFilter.PlayerJobCategory(JobRole.Healer))
		{
			return;
		}

		if (DataCenter.Territory?.IsHighEndDuty ?? false)
		{
			var warning = string.Format(UiString.HighEndWarning.GetDescription(), DataCenter.Territory.ContentFinderName);
			BasicWarningHelper.AddSystemWarning(warning);
		}
	}

	private static void DutyState_DutyWiped(IDutyStateEventArgs e)
	{
		if (!Player.Available)
		{
			await DownloadHelper.DownloadAsync();
		});

		// Quick controls for the lalalazy hub window (src/Shared/LalaHub). Last on purpose: Dalamud never calls Dispose on a
		// constructor that threw, so a half-built plugin must not leave its endpoints registered. Never throws; null when
		// nothing was registered.
		_hub = LalaHubProvider.TryCreate(pluginInterface, Svc.Log, "PvPSolver", typeof(PvPSolverPlugin).Assembly.GetName().Version?.ToString() ?? "", ep => HubAdapter.Declare(ep));
	}

	private void OnDraw()
	{
		if (Svc.GameGui.GameUiHidden)
		{
			return;
		}

		M3.BeginFrame();
		windowSystem.Draw();
	}

	internal static void ChangeUITranslation()
	{
		_mainWindow!.WindowName = UiString.ConfigWindowHeader.GetDescription()
			+ (typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "?.?.?") + "###rsrConfigWindow";

		RSCommands.Disable();
		RSCommands.Enable();
	}

	private void OnOpenConfigUi()
	{
		OpenConfigWindow();
	}

	/// <summary>Opens (or closes) the PvP settings window: the default way to open the settings.</summary>
	internal static void OpenConfigWindow()
	{
		_pvpSettingsWindow?.Toggle();
	}

	/// <summary>
	/// Opens the full Rotation Solver settings window, optionally on a tab (a member name of its tab enum, or a
	/// word <see cref="RotationSolver.Decisions.AdvancedTab"/> knows). Never goes through the PvP settings window.
	/// </summary>
	internal static void OpenAdvancedWindow(string? tabName = null)
	{
		if (_rotationConfigWindow == null)
		{
			return;
		}

		_rotationConfigWindow.IsOpen = true;
		string? resolved = RotationSolver.Decisions.AdvancedTab.Resolve(tabName);
		if (resolved != null && Enum.TryParse(resolved, true, out RotationConfigWindowTab tab))
		{
			_rotationConfigWindow.SetActiveTab(tab);
		}
	}

	/// <summary>`/pvpsolver changelog` - reopen the "What's new" popup on demand.</summary>
	internal static void ShowChangelog()
	{
		_changelog?.ShowNow();
	}

	internal static void OpenTicTacToe()
	{
		_easterEggWindow?.IsOpen = true;
	}

	internal static void ShowConfigWindow(MainWindowTab? tab = null)
	{
		if (_mainWindow == null)
		{
			return;
		}

		_mainWindow.IsOpen = true;
		_mainWindow.Restore();
		if (tab.HasValue)
		{
			_mainWindow.SetActiveTab(tab.Value);
		}
	}

	internal static void OpenFirstStartTutorial()
	{
		if (_firstStartTutorialWindow?.IsOpen == true)
		{
			return;
		}

		_firstStartTutorialWindow?.Toggle();
	}

	internal static void ShowFirstStartTutorialIfNeeded()
	{
		_firstStartTutorialWindow?.OpenIfFirstStart();
	}

	internal static void OpenChangelog()
	{
		_updateNotesWindow?.IsOpen = true;
	}

	internal static void ShowChangelogIfUpdated()
	{
		_updateNotesWindow?.OpenIfUpdated();
	}

	internal static void UpdateDisplayWindow()
	{
		var isValid = MajorUpdater.IsValid && DataCenter.CurrentRotation != null;

		isValid &= !Service.Config.OnlyShowWithHostileOrInDuty
				|| Svc.Condition[ConditionFlag.BoundByDuty]
				|| AnyHostileTargetWithinDistance(25);

		_fullControlWindow!.IsOpen = isValid && Service.Config.ShowControlWindow;
		_nextActionWindow!.IsOpen = isValid && Service.Config.ShowNextActionWindow;
		_interceptedActionWindow!.IsOpen = isValid && Service.Config.ShowInterceptedActionWindow;
		UpdateActionTimeline(isValid);
		_overlayWindow!.IsOpen = isValid && Service.Config.TeachingMode;
	}

	private static void UpdateActionTimeline(bool isValid)
	{
		var config = Service.Config;
		if (!config.ShowActionTimelineWindow)
		{
			_actionTimelineWindow!.IsOpen = false;
			ActionTimelineManager.DisposeInstance();
			return;
		}

		ActionTimelineManager.Instance.Update();

		_actionTimelineWindow!.IsOpen = isValid
			&& (!config.ActionTimelineOnlyWhenActive || DataCenter.IsActivated())
			&& (!config.ActionTimelineOnlyInCombat || DataCenter.InCombat);
	}

	private static bool AnyHostileTargetWithinDistance(float distance)
	{
		foreach (var target in DataCenter.AllHostileTargets)
		{
			if (target.DistanceToPlayer() < distance)
			{
				return true;
			}
		}
		return false;
	}

	public async ValueTask DisposeAsync()
	{
		MajorUpdater.Dispose();

		ActionTracer.Shutdown();

	public async Task Dispose()
	{
		_hub?.Dispose();   // first: a provider must never outlive its plugin
		Service.Config.Save();

		var saveLists = OtherConfiguration.Save();

		_changelog?.Dispose();
		_changelog = null;
		RSCommands.Disable();
		Watcher.Disable();
		ActionQueueManager.Disable();
		ActionManagerEx.DisposeInstance();
		BMRPlanUpdater.Disable();
		ActionContextMenu.Dispose();
		Svc.PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
		Svc.PluginInterface.UiBuilder.OpenMainUi -= OnOpenConfigUi;
		Svc.PluginInterface.UiBuilder.Draw -= OnDraw;

		Svc.DutyState.DutyStarted -= DutyState_DutyStarted;
		Svc.DutyState.DutyWiped -= DutyState_DutyWiped;
		Svc.DutyState.DutyCompleted -= DutyState_DutyCompleted;
		Svc.ClientState.TerritoryChanged -= ClientState_TerritoryChanged;

		Svc.Chat.RemoveChatLinkHandler();
		OpenLinkPayload = null!;
		HideWarningLinkPayload = null;

		foreach (var item in _dis)
		{
			item.Dispose();
		}
		_dis.Clear();

		HotbarDisabledColor.ResetOnUnload();
		HotbarHighlightManager.Dispose();
		ActionTimelineManager.DisposeInstance();
		M3.Dispose();

		BMRInfo_IPCSubscriber.Dispose();
		BMRTimeline_IPCSubscriber.Dispose();
		BMRPlan_IPCSubscriber.Dispose();
		Wrath_IPCSubscriber.Dispose();

		await saveLists;

		ECommonsMain.Dispose();
	}
}