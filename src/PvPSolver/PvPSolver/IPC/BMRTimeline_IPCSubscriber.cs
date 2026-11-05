using ECommons.EzIpcManager;
using ECommons.Logging;

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value

namespace RotationSolver.IPC;

internal static class BMRTimeline_IPCSubscriber
{
	private static readonly EzIPCDisposalToken[] _disposalTokens =
		EzIPC.Init(typeof(BMRTimeline_IPCSubscriber), "BossMod", SafeWrapper.AnyException);

	private static readonly PresenceLatch _presence = new();

	// Asked every active frame (MajorUpdater gate) and by the config window: the answer is logged once, then only
	// when it changes, and when BossModReborn goes away the timings read from it are reset once (not left stale).
	internal static bool IsEnabled
	{
		get
		{
			var ready = IPCSubscriber_Common.IsReady("BossModReborn");
			_presence.Observe(ready, LogPresence, DataCenter.ResetBmrData);
			return ready;
		}
	}

	private static void LogPresence(bool ready) => PluginLog.Information(ready
		? "[BMR] BossModReborn is loaded: timeline integration available."
		: "[BMR] BossModReborn is not loaded (no loaded plugin named BossModReborn): timeline integration off, BMR timings reset.");

	[EzIPC("HasActiveModule", true)]
	internal static readonly Func<bool>? HasActiveModule;

	[EzIPC("ActiveModuleName", true)]
	internal static readonly Func<string?>? ActiveModuleName;

	[EzIPC("Timeline.NextRaidwideIn", true)]
	internal static readonly Func<float>? NextRaidwideIn;

	[EzIPC("Timeline.NextTankbusterIn", true)]
	internal static readonly Func<float>? NextTankbusterIn;

	[EzIPC("Timeline.NextKnockbackIn", true)]
	internal static readonly Func<float>? NextKnockbackIn;

	[EzIPC("Timeline.NextDowntimeIn", true)]
	internal static readonly Func<float>? NextDowntimeIn;

	[EzIPC("Timeline.NextDowntimeEndIn", true)]
	internal static readonly Func<float>? NextDowntimeEndIn;

	[EzIPC("Timeline.NextVulnerableIn", true)]
	internal static readonly Func<float>? NextVulnerableIn;

	[EzIPC("Timeline.NextVulnerableEndIn", true)]
	internal static readonly Func<float>? NextVulnerableEndIn;

	[EzIPC("Hints.NextDamageIn", true)]
	internal static readonly Func<float>? NextDamageIn;

	[EzIPC("Hints.NextDamageType", true)]
	internal static readonly Func<int>? NextDamageType;

	[EzIPC("Hints.NextRaidwideDamageIn", true)]
	internal static readonly Func<float>? NextRaidwideDamageIn;

	[EzIPC("Hints.NextTankbusterDamageIn", true)]
	internal static readonly Func<float>? NextTankbusterDamageIn;

	[EzIPC("Debug.TimelineWalk", true)]
	internal static readonly Func<string?>? DebugTimelineWalk;

	[EzIPC("Hints.SpecialModeIn", true)]
	internal static readonly Func<float>? SpecialModeIn;

	[EzIPC("Hints.SpecialModeType", true)]
	internal static readonly Func<int>? SpecialModeType;

	internal static void Dispose() => IPCSubscriber_Common.DisposeAll(_disposalTokens);
}