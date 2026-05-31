using ECommons.ImGuiMethods.TerritorySelection;
using Lumina.Excel.Sheets;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LazyFateAutomation.Helpers.IPC;
using LazyFateAutomation.Helpers.Services;
using LazyFateAutomation.Helpers.Utils;
using ECommons.DalamudServices;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace LazyFateAutomation;

public enum FateSortCriteria {
    HasBonusWithTwist,
    Progress,
    HasBonus,
    TimeRemainingUrgent,
    Distance,
    TimeRemaining,
    Level,
    Name,
}

public class FateSortOrder {
    public FateSortCriteria Criteria { get; set; }
    public bool Descending { get; set; }
}

public class FateToolKit : IFateGrindRunState {
    public static readonly uint[] TwistOfFateStatusIDs = [1288, 1289];
    public string Name => "Fate Tool Kit (Date With Destiny)";
    public string Description => "Fate tracker with additional fate automations. This is a WIP v3 of Date With Destiny.";
    public Configuration Config => Plugin.Config;
    private static IEnumerable<TerritoryType> TerritoryType => Svc.Data.GetExcelSheet<TerritoryType>();

    private const int MinTimeToPrioritise = 240;
    private static readonly CommandRouter<FateToolKit> Router = new(
        CommandNode<FateToolKit>
            .Root()
            .Default(tweak => Plugin.Window.Toggle())
            .Sub("run", "Run until completed count target", node => node
                .ArgInt("count", min: 1)
                .Handle((tweak, args) => tweak.RunUntil(args.Get<int>("count"))))
            .Sub("stop", $"Stops {nameof(FateGrind)} task", node => node.Handle((tweak, _) => tweak.Running = false))
    );

    private static readonly Dictionary<FateSortCriteria, Func<PublicEvent, IComparable>> SortKeys = new() {
        [FateSortCriteria.HasBonusWithTwist] = f => f.HasBonus && Svc.Objects.LocalPlayer != null && Svc.Objects.LocalPlayer.StatusList.FirstOrDefault(x => TwistOfFateStatusIDs.Contains(x.StatusId)) != null,
        [FateSortCriteria.Progress] = f => f.Progress,
        [FateSortCriteria.HasBonus] = f => f.HasBonus,
        // Unactivated fates report negative time; treat them as non-urgent.
        [FateSortCriteria.TimeRemainingUrgent] = f => f.TimeRemaining is >= 0 and < MinTimeToPrioritise,
        [FateSortCriteria.Distance] = f => Svc.Objects.LocalPlayer != null ? Svc.Objects.LocalPlayer.DistanceTo(f.Position) : 0f,
        // Only rank by remaining time for active + urgent fates.
        // Non-urgent and unactivated fates tie here so later criteria (e.g. distance) can decide.
        [FateSortCriteria.TimeRemaining] = f => f.TimeRemaining is >= 0 and < MinTimeToPrioritise ? f.TimeRemaining : MinTimeToPrioritise,
        [FateSortCriteria.Level] = f => f.Level,
        [FateSortCriteria.Name] = f => f.Name,
    };

    public string CurrentState { get; internal set; } = "Idle";
    public int CompletedCount { get; private set; }
    public int? RunUntilCompleted { get; private set; }
    public int? RemainingUntilCompleted => RunUntilCompleted is { } runUntil ? Math.Max(0, runUntil - CompletedCount) : null;
    public int RelicsCompletedForStep => GetRelicsCompletedForStep(GetCurrentMode().RelicItemIds);
    internal HashSet<uint> SelectedSwapZones => Config.SelectedSwapZones;
    
    internal string SelectedModeId {
        get => Config.SelectedModeId;
        set {
            Config.SelectedModeId = value;
            Config.Save();
        }
    }
    
    internal bool PendingStopWhenSafe { get; set; } // task sets running = false once no CurrentFate and !InCombat

    private bool _running;
    public bool Running {
        get => _running;
        internal set {
            _running = value;
            if (value) {
                PendingStopWhenSafe = false;
                CompletedCount = 0;
                Service.Automation.Start(new FateGrind(this));
            }
            else {
                PendingStopWhenSafe = false;
                CurrentState = "Idle";
                try {
                    if (Service.BossMod.IsLoaded) {
                        Service.BossMod.ClearActive();
                    }
                } catch (Exception ex) {
                    Svc.Log.PrintWarning($"Failed to call BossMod ClearActive IPC: {ex.Message}");
                }
                Service.Automation.Stop();
                RunUntilCompleted = null;
            }
        }
    }

    public void Enable() => Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "FateReward", OnFateRewardPostSetup);
    public void Disable() => Svc.AddonLifecycle.UnregisterListener(OnFateRewardPostSetup);

    private void OnFateRewardPostSetup(AddonEvent type, AddonArgs args) {
        if (!Running)
            return;

        CompletedCount++;
        StopIfNoRemaining();
    }

    private void RunUntil(int runUntil) {
        RunUntilCompleted = runUntil;
        if (!Running)
            Running = true;
        else
            StopIfNoRemaining();
    }

    internal void StopIfNoRemaining() {
        if (RunUntilCompleted is { } runUntil && CompletedCount >= runUntil)
            PendingStopWhenSafe = true;
        else if (GetCurrentMode().IsComplete(this))
            PendingStopWhenSafe = true;
    }

    internal IFateGrindMode GetCurrentMode() {
        var displayName = SelectedModeId;
        if (string.IsNullOrEmpty(displayName))
            return FateGrindModes.GetNoneMode() ?? FateGrindModes.All[0];
        return FateGrindModes.GetByDisplayName(displayName) ?? FateGrindModes.GetNoneMode() ?? FateGrindModes.All[0];
    }

    /// <summary>Returns whether the relic (by item ID) has completed the associated quest for this step. Fill in with quest/achievement check.</summary>
    public static bool IsRelicStepComplete(uint relicItemId) {
        // TODO: check quest (or achievement) for this relic; return true when the step is done for that relic
        return false;
    }

    internal static int GetRelicsCompletedForStep(IReadOnlyList<uint>? relicItemIds)
        => relicItemIds is { Count: > 0 } ids ? ids.Count(IsRelicStepComplete) : 0;

    /// <summary>Zones used for swap rotation: mode's allowed zones if set, otherwise selected swap zones, unfiltered.</summary>
    internal IReadOnlySet<uint>? GetRawSwapZones() => GetCurrentMode().GetAllowedZones() ?? (SelectedSwapZones.Count > 0 ? SelectedSwapZones : null);

    /// <summary>Zones used for swap rotation: mode's allowed zones if set, otherwise selected swap zones, filtered by user exclusions.</summary>
    internal IReadOnlySet<uint>? GetEffectiveSwapZones() {
        var zones = GetRawSwapZones();
        if (zones == null) return null;
        return zones.Where(z => !Config.ExcludedSwapZones.Contains(z)).ToHashSet();
    }

    /// <summary>True when the current mode defines its own zones; territory selector is disabled to avoid confusion.</summary>
    internal bool ModeSuppliesSwapZones => GetCurrentMode().GetAllowedZones() != null;

    /// <summary>Next zone to swap to; prefers zones where a mode item target is not yet met (e.g. relic atma).</summary>
    internal uint? GetNextPreferredSwapZone(uint currentTerritoryId) {
        var targets = GetCurrentMode().GetZoneItemTargets(this);
        if (targets != null) {
            var incomplete = targets.Where(t => GetItemCount(t.ItemId) < t.RequiredCount).Select(t => t.TerritoryId).Distinct().ToList();
            incomplete = incomplete.Where(z => !Config.ExcludedSwapZones.Contains(z)).ToList();
            if (incomplete.Count > 0) {
                var next = incomplete.FirstOrDefault(z => z != currentTerritoryId);
                if (next != 0) return next;
                return incomplete[0];
            }
        }
        return GetNextSelectedSwapZone(currentTerritoryId);
    }

    internal static string GetZoneName(uint territoryId) {
        var territory = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRow(territoryId);
        if (territory == null) return $"Zone {territoryId}";
        var name = territory.Value.PlaceName.Value.Name.ToString();
        if (string.IsNullOrEmpty(name)) {
            name = territory.Value.Name.ToString();
        }
        return string.IsNullOrEmpty(name) ? $"Zone {territoryId}" : name;
    }

    private static unsafe int GetItemCount(uint itemId) => FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance()->GetInventoryItemCount(itemId);

    internal void SyncRunningState() {
        if (Running && !Service.Automation.Running)
            Running = false;
    }

    internal bool HasSelectedSwapZones => SelectedSwapZones.Count > 0;

    private int _selectedZoneRotation = -1;

    /// <summary>Zone list for rotation. Gemstone mode: order by ExVersion descending (later expansions first).</summary>
    private List<uint> GetOrderedSwapZones(IReadOnlySet<uint> pool) {
        var distinct = pool.Where(id => id != 0).Distinct().ToList();
        if (distinct.Count == 0) return [];
        if (GetCurrentMode().DisplayName != "Gemstones")
            return [.. distinct.OrderBy(id => id)];
        return [.. TerritoryType.Where(r => pool.Contains(r.RowId)).OrderByDescending(r => r.ExVersion.RowId).Select(r => r.RowId)];
    }

    internal uint? GetNextSelectedSwapZone(uint currentTerritoryId) {
        var pool = GetEffectiveSwapZones();
        if (pool is null || pool.Count == 0)
            return null;

        var zones = GetOrderedSwapZones(pool);

        if (zones.Count == 0)
            return null;

        if (zones.Count == 1)
            return zones[0];

        _selectedZoneRotation = (_selectedZoneRotation + 1) % zones.Count;
        if (zones[_selectedZoneRotation] == currentTerritoryId)
            _selectedZoneRotation = (_selectedZoneRotation + 1) % zones.Count;
        return zones[_selectedZoneRotation];
    }

    internal void OpenZoneSelector() {
        var selector = new TerritorySelector(SelectedSwapZones, (_, selected) => {
            SelectedSwapZones.Clear();
            foreach (var zoneId in selected)
                SelectedSwapZones.Add(zoneId);
        }, "FTK Zones");

        var allowedIds = TerritoryType.Where(row => row.IsInUse && row.TerritoryIntendedUse.Value.StructsEnum is TerritoryIntendedUse.Overworld && !row.IsPvpZone).Select(row => row.RowId).ToHashSet();
        selector.HiddenTerritories = [.. TerritoryType.Select(row => row.RowId).Where(id => !allowedIds.Contains(id))];

        selector.HiddenCategories = [TerritorySelector.Category.All];
        selector.SelectedCategory = TerritorySelector.Category.World;
    }

    public void ToggleRunning() {
        RunUntilCompleted = null;
        Running ^= true;
    }

    public void OnCommand(string _, string arguments) {
        var result = Router.Execute(arguments, this, "/lazyfate");
        if (!string.IsNullOrWhiteSpace(result.Help)) {
            ModuleMessage(result.Help);
            return;
        }

        if (!result.Success) {
            result.Error?.ModuleMessage(this);
            result.Usage?.ModuleMessage(this);
        }
    }

    public void ModuleMessage(string messageTemplate) {
        var message = new XivChatEntry {
            Message = new SeStringBuilder()
                .AddUiForeground($"[{Name}] ", 62)
                .Append(messageTemplate)
                .Build()
        };

        Svc.Chat.Print(message);
    }

    internal bool IsBlacklisted(PublicEvent f)
        => Config.Blacklist.TryGetValue(f.FateType, out var set) && set.Contains(f.Id);

    public void ToggleBlacklist(PublicEvent f) {
        if (!Config.Blacklist.TryGetValue(f.FateType, out var set))
            Config.Blacklist[f.FateType] = set = [];

        if (!set.Add(f.Id))
            set.Remove(f.Id);
    }

    public bool FateConditions(PublicEvent f)
        => f.Duration <= Config.MaxDuration
        && f.Progress <= Config.MaxProgress
        && (f.TimeRemaining < 0 || f.TimeRemaining > Config.MinTimeRemaining)
        && !IsBlacklisted(f)
        && !f.IsPending;

    public (bool IsEligible, List<string> FailedConditions) GetFateConditionDetails(PublicEvent f) {
        var failed = new List<string>();

        if (f.Duration > Config.MaxDuration)
            failed.Add($"Duration {f.Duration}s > MaxDuration {Config.MaxDuration}s");

        if (f.Progress > Config.MaxProgress)
            failed.Add($"Progress {f.Progress}% > MaxProgress {Config.MaxProgress}%");

        if (f.TimeRemaining >= 0 && f.TimeRemaining <= Config.MinTimeRemaining)
            failed.Add($"TimeRemaining {f.TimeRemaining:F0}s <= MinTimeRemaining {Config.MinTimeRemaining}s");

        if (IsBlacklisted(f))
            failed.Add("Blacklisted");

        if (f.IsPending)
            failed.Add("Pending (not yet active / not on map)");

        return (failed.Count == 0, failed);
    }

    public IEnumerable<(PublicEvent Fate, bool IsAvailable)> GetOrderedFates() {
        var all = PublicEvent.Fates.ToList();
        if (all.Count == 0)
            yield break;

        var available = all.Where(FateConditions).ToList();
        var unavailable = all.Where(f => !FateConditions(f)).ToList();

        foreach (var f in ApplySortOrder(available, Config.SortOrder))
            yield return (f, true);

        foreach (var f in ApplySortOrder(unavailable, Config.SortOrder))
            yield return (f, false);
    }

    internal static IOrderedEnumerable<PublicEvent> ApplySortOrder(IEnumerable<PublicEvent> source, IReadOnlyList<FateSortOrder> sortOrder) {
        if (!sortOrder.Any())
            return source.OrderBy(_ => 0);

        IOrderedEnumerable<PublicEvent>? ordered = null;

        foreach (var sort in sortOrder) {
            var keySelector = SortKeys.TryGetValue(sort.Criteria, out var key) ? key : (_ => 0);
            ordered = ordered == null
                ? sort.Descending ? source.OrderByDescending(keySelector) : source.OrderBy(keySelector)
                : sort.Descending ? ordered.ThenByDescending(keySelector) : ordered.ThenBy(keySelector);
        }

        return ordered ?? source.OrderBy(_ => 0);
    }
}

public static class CommandExtensions {
    public static void ModuleMessage(this string messageTemplate, FateToolKit tweak) => tweak.ModuleMessage(messageTemplate);
    public static void ModuleMessage(this SeString messageTemplate, FateToolKit tweak) => tweak.ModuleMessage(messageTemplate.ToString());
}
