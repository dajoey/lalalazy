using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Hooks;
using ECommons.Hooks.ActionEffectTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lalalazy.HookGuard;
using Action = Lumina.Excel.Sheets.Action;

namespace RotationSolver.ActionTimeline;

/// <summary>
/// Manages action timeline data for timeline visualization
/// </summary>
public class ActionTimelineManager : IDisposable
{
	internal const byte GCDCooldownGroup = 58;

	private const int Capacity = 4096;

	private const double LongestItemSeconds = 30;

	private const float CastRestartSeconds = 0.5f;

	private const double CastEffectEarly = 0.75;
	private const double CastEffectLate = 2.0;

	private static ActionTimelineManager? _instance;

	public static ActionTimelineManager Instance => _instance ??= new ActionTimelineManager();

	public static ActionTimelineManager? Current => _instance;

	private delegate void OnActorControlDelegate(uint entityId, uint type, uint buffID, uint direct, uint actionId, uint sourceId, uint arg7, uint arg8, uint arg9, uint arg10, ulong targetId, byte arg12);
	[Signature("E8 ?? ?? ?? ?? 0F B7 0B 83 E9 64", DetourName = nameof(OnActorControl))]
#pragma warning disable CS0649
	private Hook<OnActorControlDelegate>? _onActorControlHook;
#pragma warning restore CS0649

	private delegate void OnCastDelegate(uint sourceId, IntPtr sourceCharacter);
	[Signature("40 53 57 48 81 EC ?? ?? ?? ?? 48 8B FA 8B D1", DetourName = nameof(OnCast))]
#pragma warning disable CS0649
	private Hook<OnCastDelegate>? _onCastHook;
#pragma warning restore CS0649

	// Detours of this module running right now; DisposeHooks drains this before pulling the hooks
	// (a detour that outlives its hook is the 2026-08-31 / 2026-10-03 game crash family).
	private static readonly HookInFlight InFlight = new();

	public DateTime EndTime { get; private set; } = DateTime.Now;

	private ActionTimelineManager()
	{
		ActionEffect.ActionEffectEvent += ActionFromSelf;
	}

	public void Dispose()
	{
		ActionEffect.ActionEffectEvent -= ActionFromSelf;
		DisposeHooks();
		GC.SuppressFinalize(this);
	}

	/// <summary>
	///     Disables both hooks, waits for the detours still inside them, then disposes and drops them.
	///     Kept free of Dalamud services on purpose: it is exactly the draining teardown of the
	///     2026-10-03 hot-reload crash family, so it stays callable with the plugin half-torn-down.
	/// </summary>
	private void DisposeHooks()
	{
		_onActorControlHook?.Disable();
		_onCastHook?.Disable();
		InFlight.Drain("RotationSolver.ActionTimeline.ActionTimelineManager", static m => Svc.Log.Warning(m));
		_onActorControlHook?.Dispose();
		_onActorControlHook = null;
		_onCastHook?.Dispose();
		_onCastHook = null;
	}

	private static unsafe float GCD
	{
		get
		{
			var manager = ActionManager.Instance();
			if (manager == null)
			{
				return 0f;
			}

			var group = manager->GetRecastGroupDetail(GCDCooldownGroup - 1);
			return group == null ? 0f : group->Total;
		}
	}

	public void Update()
	{
		var now = DateTime.Now;
		UpdateCasting(now);
		DropOverdueCasts(now);
		UpdateCombatState(now);
	}

	public void CollectItems(DateTime since, List<TimelineItem> into)
	{
		var cutoff = since.AddSeconds(-LongestItemSeconds);
		for (var i = 0; i < _count; i++)
		{
			var item = _items[(_head - 1 - i + Capacity) % Capacity];
			if (item.StartTime < cutoff)
			{
				break;
			}

			if (item.EndTime > since)
			{
				into.Add(item);
			}
		}
	}

	private static bool TryGetAction(uint actionId, out Action action)
	{
		return Svc.Data.GetExcelSheet<Action>().TryGetRow(actionId, out action);
	}

	private static TimelineItemType GetActionType(uint actionId, Action? row)
	{
		if (row is not { } action)
		{
			return TimelineItemType.OGCD;
		}

		if (actionId == 3)
		{
			return TimelineItemType.OGCD; // Sprint
		}

		var isRealGcd = action.CooldownGroup == GCDCooldownGroup || action.AdditionalCooldownGroup == GCDCooldownGroup;
		return action.ActionCategory.RowId == 1 // AutoAttack
			? TimelineItemType.AutoAttack
			: !isRealGcd && action.ActionCategory.RowId == 4 ? TimelineItemType.OGCD // Ability
			: TimelineItemType.GCD;
	}

	private void AddItem(TimelineItem item)
	{
		_items[_head] = item;
		_head = (_head + 1) % Capacity;
		_count = Math.Min(_count + 1, Capacity);
	}

	private void ActionFromSelf(ActionEffectSet set)
	{
		try
		{
			var player = Player.Object;
			if (player == null || set.Source?.GameObjectId != player.GameObjectId)
			{
				return;
			}

			var now = DateTime.Now;
			var actionId = set.Header.ActionID;
			var type = GetActionType(actionId, set.Action);

			if (type == TimelineItemType.GCD && TakeCastEndingAt(now) is { } cast)
			{
				cast.ActionId = actionId;
				cast.AnimationLockTime = set.Header.AnimationLockTime;
				cast.Name = set.Name;
				cast.Icon = set.IconId;
				cast.State = TimelineItemState.Finished;
				return;
			}

			AddItem(new TimelineItem()
			{
				ActionId = actionId,
				StartTime = now,
				AnimationLockTime = type == TimelineItemType.AutoAttack ? 0 : set.Header.AnimationLockTime,
				GCDTime = type == TimelineItemType.GCD ? GCD : 0,
				Type = type,
				Name = set.Name,
				Icon = set.IconId,
				State = TimelineItemState.Finished,
			});
		}
		catch (Exception ex)
		{
			Svc.Log.Error($"Error recording action effect for the timeline: {ex.Message}");
		}
	}

	private TimelineItem? TakeCastEndingAt(DateTime now)
	{
		var matched = -1;
		for (var i = 0; i < _pendingCasts.Count; i++)
		{
			var fromEnd = SecondsPastCastEnd(_pendingCasts[i], now);
			if (fromEnd >= -CastEffectEarly && fromEnd <= CastEffectLate)
			{
				matched = i;
				break;
			}
		}

		var superseded = matched < 0 ? _pendingCasts.Count : matched;
		for (var i = 0; i < superseded; i++)
		{
			Cancel(_pendingCasts[i], now);
		}

		if (matched < 0)
		{
			_pendingCasts.Clear();
			return null;
		}

		var cast = _pendingCasts[matched];
		_pendingCasts.RemoveRange(0, matched + 1);
		return cast;
	}

	private static double SecondsPastCastEnd(TimelineItem cast, DateTime now)
	{
		return (now - cast.StartTime).TotalSeconds - cast.CastingTime;
	}

	private void DropOverdueCasts(DateTime now)
	{
		while (_pendingCasts.Count > 0 && SecondsPastCastEnd(_pendingCasts[0], now) > CastEffectLate)
		{
			Cancel(_pendingCasts[0], now);
			_pendingCasts.RemoveAt(0);
		}
	}

	private void UpdateCasting(DateTime now)
	{
		// Count this detour so teardown can wait for it; when the hook is gone do nothing at all:
		// neither the original call nor the payload may run, and an exception escaping here takes
		// the game down (2026-10-03 crash family).
		using var inFlight = InFlight.Enter();
		var hook = _onActorControlHook;
		if (hook == null || hook.IsDisposed)
		{
			return;
		}

		hook.Original(entityId, type, buffID, direct, actionId, sourceId, arg7, arg8, arg9, arg10, targetId, arg12);

		var actionId = player.CastActionId;
		var elapsed = player.CurrentCastTime;

		var isNew = actionId != _castActionId || elapsed < _castElapsed - CastRestartSeconds;
		_castActionId = actionId;
		_castElapsed = elapsed;

		if (!isNew || player.CastActionType != (byte)ActionType.Action
			|| !TryGetAction(actionId, out var action)
			|| GetActionType(actionId, action) != TimelineItemType.GCD)
		{
			return;
		}

		for (var i = _pendingCasts.Count - 1; i >= 0; i--)
		{
			if (SecondsPastCastEnd(_pendingCasts[i], now) < -CastEffectEarly)
			{
				Cancel(_pendingCasts[i], now);
				_pendingCasts.RemoveAt(i);
			}
		}

		var cast = new TimelineItem()
		{
			ActionId = actionId,
			StartTime = now.AddSeconds(-elapsed),
			CastingTime = player.TotalCastTime,
			GCDTime = GCD,
			Type = TimelineItemType.GCD,
			Name = action.Name.ExtractText(),
			Icon = action.Icon,
			State = TimelineItemState.Casting,
		};

		AddItem(cast);
		_pendingCasts.Add(cast);
	}

	private static void Cancel(TimelineItem cast, DateTime at)
	{
		cast.State = TimelineItemState.Canceled;
		cast.GCDTime = 0;
		cast.CastingTime = Math.Clamp((float)(at - cast.StartTime).TotalSeconds, 0f, cast.CastingTime);
	}

	internal void CancelInterruptedCast(DateTime now)
	{
		for (var i = _pendingCasts.Count - 1; i >= 0; i--)
		{
			var cast = _pendingCasts[i];
			if (SecondsPastCastEnd(cast, now) < CastEffectEarly && !IsOnCastBar(cast, now))
			{
				Cancel(cast, now);
				_pendingCasts.RemoveAt(i);
				return;
			}
		}
	}

	private static bool IsOnCastBar(TimelineItem cast, DateTime now)
	{
		var player = Player.Object;
		return player is { IsCasting: true }
			&& player.CastActionId == cast.ActionId
			&& Math.Abs((now.AddSeconds(-player.CurrentCastTime) - cast.StartTime).TotalSeconds) < CastRestartSeconds;
	}
	private void UpdateCombatState(DateTime now)
	{
		var inCombat = DataCenter.InCombat;
		if (inCombat && !_wasInCombat)
		{
			_combatStartTime = now;
		}
		else if (!inCombat && _wasInCombat && _combatStartTime is { } combatStart)
		{
			if (Service.Config.ActionTimelineSaveToFile)
			{
				SaveFight(combatStart);
			}

			_combatStartTime = null;
		}

		_wasInCombat = inCombat;
	}

	private void SaveFight(DateTime combatStart)
	{
		using var inFlight = InFlight.Enter();
		var hook = _onCastHook;
		if (hook == null || hook.IsDisposed)
		{
			return;
		}

		hook.Original(sourceId, sourceCharacter);
		// Additional cast handling could go here
	}

	private static bool WriteJson(string filePath, TimelineExportSession session)
	{
		try
		{
			var json = JsonConvert.SerializeObject(session, Formatting.Indented);

			var directory = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}

			File.WriteAllText(filePath, json);
			return true;
		}
		catch (Exception ex)
		{
			Svc.Log.Error($"Failed to export timeline to JSON: {ex.Message}");
			return false;
		}
	}

	private TimelineExportSession CreateExportSession(DateTime combatStart)
	{
		var session = new TimelineExportSession();

		List<TimelineItem> items = [];
		CollectItems(combatStart, items);
		if (items.Count == 0)
		{
			return session;
		}

		items.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

		var startTime = combatStart < items[0].StartTime ? combatStart : items[0].StartTime;
		var endTime = items[0].EndTime;
		foreach (var item in items)
		{
			if (item.EndTime > endTime)
			{
				endTime = item.EndTime;
			}
		}

		session.SessionInfo = new SessionInfo
		{
			StartTime = startTime,
			EndTime = endTime,
			DurationSeconds = (endTime - startTime).TotalSeconds,
			PlayerName = Player.Available && Player.Object != null ? Player.Object.Name.TextValue : "Unknown",
			PlayerJob = Player.Available ? Player.Job.ToString() : "Unknown",
			Territory = DataCenter.Territory?.Name ?? "Unknown",
			Duty = DataCenter.Territory?.ContentFinderName ?? "Unknown",
			ExportedAt = DateTime.Now
		};

		foreach (var item in items)
		{
			session.Actions.Add(new ExportedAction
			{
				Name = item.Name,
				Id = item.ActionId,
				Icon = item.Icon,
				Type = item.Type.ToString(),
				StartTime = item.StartTime,
				EndTime = item.EndTime,
				CombatTimeSeconds = (item.StartTime - startTime).TotalSeconds,
				CastTimeSeconds = Math.Max(item.CastingTime + item.AnimationLockTime, item.GCDTime),
				State = item.State.ToString(),
				Target = "" // We don't currently track target information
			});
		}

		return session;
	}

	private static string GetSuggestedFilename()
	{
		var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		var jobName = Player.Available ? Player.Job.ToString() : "Unknown";
		var dutyName = DataCenter.Territory?.ContentFinderName ?? DataCenter.Territory?.Name ?? "Timeline";

		// Sanitize filename
		dutyName = string.Join("_", dutyName.Split(Path.GetInvalidFileNameChars()));

		return $"{timestamp}_{jobName}_{dutyName}.json";
	}
}

/// <summary>
/// Represents an item in the action timeline
/// </summary>
public class TimelineItem
{
	public uint ActionId { get; set; }
	public DateTime StartTime { get; set; }
	public string Name { get; set; } = "";
	public uint Icon { get; set; }
	public float CastingTime { get; set; }
	public float AnimationLockTime { get; set; }
	public float GCDTime { get; set; }
	public TimelineItemType Type { get; set; }
	public TimelineItemState State { get; set; }

	public DateTime EndTime => StartTime.AddSeconds(Math.Max(CastingTime + AnimationLockTime, GCDTime));
}

/// <summary>
/// Type of timeline item
/// </summary>
public enum TimelineItemType
{
	GCD,
	OGCD,
	AutoAttack
}

/// <summary>
/// State of timeline item
/// </summary>
public enum TimelineItemState
{
	Casting,
	Finished,
	Canceled
}
