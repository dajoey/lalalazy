using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace LazyFoodBuff;

internal class FoodService
{
    // Well Fed status ID.
    private const uint WellFedStatusId = 48;

    // Food refresh: eating extends the timer by up to 30 minutes total cap.
    private const uint FoodMaxDurationMinutes = 30;

    // Combat duty TerritoryIntendedUse values (from ECommons TerritoryIntendedUseEnum).
    private static readonly HashSet<uint> CombatDutyIntendedUses = new()
    {
        3,    // Dungeon
        8,    // Alliance Raid
        10,   // Trial
        16,   // Raid
        17,   // Raid (alternate)
        33,   // Treasure Map Duty (has combat)
        52,   // Large Scale Raid (Bozja Dalriada etc.)
        53,   // Large Scale Savage Raid
        57,   // Criterion Duty
        58,   // Criterion Savage Duty
    };

    private readonly Plugin _plugin;
    private readonly List<Food> _allFoods;
    private DateTime _nextAttempt = DateTime.MinValue;

    // Warning state — tracks whether we've already warned for the current food session.
    private bool _warningFired;
    private uint _lastFoodParam; // Track active food to reset warning when food changes.

    private string _lastSkipReason = "(no tick yet)";

    public IReadOnlyList<Food> AllFoods => _allFoods;

    public FoodService(Plugin plugin)
    {
        _plugin = plugin;

        var itemSheet = Plugin.Data.GetExcelSheet<Item>();
        var foods = new List<Food>();
        if (itemSheet != null)
        {
            foreach (var item in itemSheet)
            {
 // Food items have an ItemAction reference with Data[0] == 844 (food type).
                // The skeptic-recommended approach: check ItemAction.RowId != 0,
                // then verify via ItemAction.Data[1] (ItemFood row ID) != 0.
                if (item.ItemAction.RowId == 0) continue;
                var action = item.ItemAction.Value;
                if (action.Data.Count < 2 || action.Data[1] == 0) continue;

                foods.Add(new Food(item));
            }
        }
        _allFoods = foods;
        Plugin.Log.Information($"LazyFoodBuff: indexed {_allFoods.Count} food items");
    }

    public void Tick()
    {
        var cfg = _plugin.Config;
        if (!cfg.MasterEnable) { _lastSkipReason = "MasterEnable=false"; return; }
        if (DateTime.UtcNow < _nextAttempt) return;

        var local = Plugin.Objects.LocalPlayer;
        if (local == null) { _lastSkipReason = "no LocalPlayer"; return; }
        if (local.IsDead) { _lastSkipReason = "player dead"; return; }
        if (local.MaxHp == 0) { _lastSkipReason = "MaxHp=0"; return; }

        // Player state guards.
        if (Plugin.Condition[ConditionFlag.BetweenAreas])
        { _lastSkipReason = "between areas"; return; }
        if (Plugin.Condition[ConditionFlag.OccupiedInEvent])
        { _lastSkipReason = "occupied in event"; return; }
        if (Plugin.Condition[ConditionFlag.OccupiedInQuestEvent])
        { _lastSkipReason = "occupied in quest event"; return; }

        // Duty gate.
        if (cfg.OnlyInCombatDuty && !IsInCombatDuty())
        {
            // Still check warning even outside duty if food is active.
            CheckWarning();
            _lastSkipReason = "OnlyInCombatDuty=true and not in combat duty";
            _nextAttempt = DateTime.UtcNow.AddMilliseconds(500);
            return;
        }

        var jobId = local.ClassJob.RowId;
        var jobSettings = cfg.GetJobSettings(jobId);

        // Check current food buff.
        var hasFood = TryGetWellFedStatus(out var activeFoodRowId, out var remainingTime);

        // Warning check (runs regardless of whether we need to eat).
        CheckWarning();

        // Decide if we need to eat.
        var needFood = false;
        var refreshThreshold = TimeSpan.FromMinutes(cfg.RefreshThresholdMinutes);

        if (!hasFood)
        {
            needFood = true;
        }
        else if (remainingTime <= refreshThreshold)
        {
            // Don't refresh if already eating the food we'd select (skeptic concern #8).
            var selectedFood = SelectFood(jobSettings, jobId);
            if (selectedFood != null && selectedFood.ItemFoodRowId == activeFoodRowId)
            {
                // Same food type already active — refresh to extend duration.
                needFood = true;
            }
            else if (selectedFood != null)
            {
                // Different food available — switch.
                needFood = true;
            }
        }

        if (!needFood)
        {
            _lastSkipReason = hasFood
                ? $"food active ({remainingTime.TotalMinutes:F1}min > {cfg.RefreshThresholdMinutes:F0}min threshold)"
                : "no food needed";
            _nextAttempt = DateTime.UtcNow.AddMilliseconds(500);
            return;
        }

        // Select food based on job settings.
        var food = SelectFood(jobSettings, jobId);
        if (food == null)
        {
            _lastSkipReason = "no food available in inventory";
            _nextAttempt = DateTime.UtcNow.AddMilliseconds(2000);
            return;
        }

        // Check action status before using.
        var targetId = local.GameObjectId;
        var status = food.GetActionStatus(targetId);
        if (status != 0)
        {
            _lastSkipReason = $"GetActionStatus={status} for {food.Name}";
            _nextAttempt = DateTime.UtcNow.AddMilliseconds(1000);
            return;
        }

        if (food.TryUse(targetId))
        {
            _warningFired = false; // Reset warning on new food consumption.
            _lastSkipReason = $"ate food {food.Name}";
            Plugin.Log.Information($"LazyFoodBuff: ate food {food.Name} ({food.Id})");
            _nextAttempt = DateTime.UtcNow.AddMilliseconds(750);
        }
        else
        {
            _lastSkipReason = $"TryUse failed for {food.Name}";
            _nextAttempt = DateTime.UtcNow.AddMilliseconds(1000);
        }
    }

    /// <summary>
    /// Select food for the current job based on settings.
    /// Priority: Manual food (if in inventory) → AutoSelect fallback → AutoSelect.
    /// </summary>
    private Food? SelectFood(JobFoodSettings jobSettings, uint jobId)
    {
        if (jobSettings.Mode == FoodSelectionMode.Manual && jobSettings.ManualFoodItemId != 0)
        {
            // Find the manual food.
            var manualFood = _allFoods.FirstOrDefault(f => f.Id == jobSettings.ManualFoodItemId);
            if (manualFood != null)
            {
                // Check if it's in inventory (NQ or HQ as configured).
                var hasInInventory = jobSettings.ManualFoodIsHQ
                    ? manualFood.InventoryCount(true) > 0
                    : manualFood.InventoryCount(false) > 0;

                if (hasInInventory)
                    return manualFood;

                // Also check if we have either quality (prefer what's available).
                if (manualFood.InventoryCount(true) > 0 || manualFood.InventoryCount(false) > 0)
                    return manualFood;
            }

            // Manual food not in inventory — fall back?
            if (!jobSettings.FallbackToAutoSelect)
                return null;
        }

        // AutoSelect: score all foods against job priorities.
        return FoodRecommender.RecommendBest(_allFoods, jobId);
    }

    private void CheckWarning()
    {
        var cfg = _plugin.Config;
        if (!cfg.WarningEnabled) return;
        if (!TryGetWellFedStatus(out _, out var remainingTime)) return;

        // Reset warning if food changed.
        TryGetWellFedStatus(out var currentParam, out _);
        if (currentParam != _lastFoodParam)
        {
            _warningFired = false;
            _lastFoodParam = currentParam;
        }

        var threshold = TimeSpan.FromMinutes(cfg.WarningThresholdMinutes);
        if (remainingTime <= threshold && !_warningFired)
        {
            _warningFired = true;

            // Sound playback removed — IGameGui.PlaySoundEffect does not exist in API 15.
            // ChatGui.PrintError below provides the visible warning notification.

            // In-game chat notification (visible as red error text in chat log).
            try
            {
                var msg = $"[LazyFoodBuff] Food buff expires in {remainingTime.TotalMinutes:F0} minute(s)!";
                Plugin.ChatGui.PrintError(msg);
                Plugin.Log.Information($"LazyFoodBuff: WARNING — {msg}");
            }
            catch { /* ignore */ }
        }
    }

    private static bool TryGetWellFedStatus(out uint itemFoodRowId, out TimeSpan remainingTime)
    {
        itemFoodRowId = 0;
        remainingTime = TimeSpan.Zero;

        var local = Plugin.Objects.LocalPlayer;
        if (local == null) return false;

        // Use Dalamud's built-in StatusList wrapper — no ToStruct() needed.
        foreach (var status in local.StatusList)
        {
            if (status == null) continue;
            if (status.StatusId == WellFedStatusId)
            {
                itemFoodRowId = (uint)status.Param % 10_000;
                remainingTime = TimeSpan.FromSeconds(status.RemainingTime);
                return true;
            }
        }
        return false;
    }

    private static bool IsInCombatDuty()
    {
        var territoryId = Plugin.ClientState.TerritoryType;
        if (territoryId == 0) return false;
        var sheet = Plugin.Data.GetExcelSheet<TerritoryType>();
        if (sheet == null || !sheet.TryGetRow(territoryId, out var row)) return false;

        var intendedUse = row.TerritoryIntendedUse.RowId;

        // Check against the combat duty allow-list.
        if (CombatDutyIntendedUses.Contains(intendedUse)) return true;

        // Variant dungeons count as combat duty.
        if (intendedUse == 4) return true; // Variant Dungeon

        return false;
    }

    public void LogDebugState()
    {
        var cfg = _plugin.Config;
        var local = Plugin.Objects.LocalPlayer;
        var territoryId = Plugin.ClientState.TerritoryType;
        var jobId = local?.ClassJob.RowId ?? 0;
        var job = cfg.GetJobSettings(jobId);

        Plugin.Log.Information("=== LazyFoodBuff debug ===");
        Plugin.Log.Information($"Last tick: {_lastSkipReason}");
        Plugin.Log.Information($"MasterEnable={cfg.MasterEnable} OnlyInCombatDuty={cfg.OnlyInCombatDuty}");
        Plugin.Log.Information($"RefreshThreshold={cfg.RefreshThresholdMinutes}min WarningThreshold={cfg.WarningThresholdMinutes}min");
        Plugin.Log.Information($"Job={jobId} Mode={job.Mode} ManualFood={job.ManualFoodItemId} Fallback={job.FallbackToAutoSelect}");
        Plugin.Log.Information($"InCombatDuty={IsInCombatDuty()}");

        var sheet = Plugin.Data.GetExcelSheet<TerritoryType>();
        if (sheet != null && sheet.TryGetRow(territoryId, out var row))
        {
            Plugin.Log.Information(
                $"  Territory={territoryId} IntendedUse={row.TerritoryIntendedUse.RowId} " +
                $"Place={row.PlaceName.Value.Name.ExtractText()}");
        }

        if (TryGetWellFedStatus(out var foodId, out var remaining))
        {
            Plugin.Log.Information($"Well Fed: ItemFoodRow={foodId} Remaining={remaining.TotalMinutes:F1}min");
        }
        else
        {
            Plugin.Log.Information("Well Fed: not active");
        }

        // Show top 5 recommended foods for current job.
        if (local != null)
        {
            var scored = _allFoods
                .Where(f => f.InventoryCount(true) > 0 || f.InventoryCount(false) > 0)
                .Select(f => (Food: f, Score: FoodRecommender.Score(f, jobId)))
                .OrderByDescending(x => x.Score)
                .Take(5);

            Plugin.Log.Information($"Top 5 foods for job {jobId} (in inventory):");
            foreach (var (food, score) in scored)
            {
                var hq = food.InventoryCount(true);
                var nq = food.InventoryCount(false);
                Plugin.Log.Information($"  [{food.Id}] {food.Name} score={score:F1} hq={hq} nq={nq}");
            }

            // Show active food match.
            var selected = SelectFood(job, jobId);
            Plugin.Log.Information($"Selected food: {selected?.Name ?? "(none)"} ({selected?.Id ?? 0})");
        }
    }
}
