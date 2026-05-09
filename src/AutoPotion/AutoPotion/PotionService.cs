using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AutoPotion;

internal class PotionService
{
    // Deep dungeon regeneration potions. ItemSearchCategory is 0 for these so they
    // don't show up in the regular potion filter; pull them in by item ID.
    //   20309 - Sustaining Potion (Palace of the Dead) - blocked by Rehabilitation
    //   22306 - Eurekan Potion (Heaven-on-High)
    //   38944 - Orthos Potion (Eureka Orthos)
    //   47102 - Pilgrim's Potion
    private static readonly uint[] RegenPotionIds = { 20309, 22306, 38944, 47102 };

    // ContentFinderCondition.ContentType row 21 = "Deep Dungeons" (PotD, HoH, Eureka Orthos).
    // TerritoryIntendedUse is a different sheet and does NOT cover Eureka Orthos.
    private const uint DeepDungeonContentType = 21;

    // Rehabilitation: shared by all deep dungeon medicines (Sustaining, Eurekan,
    // Orthos, Pilgrim's). Blocks reuse for ~30s after a regen potion is used —
    // shorter than the 4.5min recast but the explicit gate avoids any window where
    // we'd attempt a use while regen is still ticking.
    private const uint RehabilitationStatusId = 648;

    private readonly Plugin _plugin;
    private readonly Potion[] _hpPotions;
    private readonly Potion[] _regenPotions;
    private DateTime _nextAttempt = DateTime.MinValue;

    public PotionService(Plugin plugin)
    {
        _plugin = plugin;

        var itemSheet = Plugin.Data.GetExcelSheet<Item>();
        var hp = new List<Potion>();
        var regen = new List<Potion>();
        if (itemSheet != null)
        {
            foreach (var i in itemSheet)
            {
                if (i.ItemAction.RowId == 0) continue;
                if (i.FilterGroup == 8 && i.ItemSearchCategory.RowId == 43)
                {
                    hp.Add(new Potion(i));
                }
                else if (Array.IndexOf(RegenPotionIds, i.RowId) >= 0)
                {
                    regen.Add(new Potion(i));
                }
            }
        }
        _hpPotions = hp.ToArray();
        _regenPotions = regen.ToArray();
        Plugin.Log.Information(
            $"AutoPotion: indexed {_hpPotions.Length} HP potions, {_regenPotions.Length} regen potions");
    }

    public void Tick()
    {
        var cfg = _plugin.Config;
        if (!cfg.MasterEnable) return;
        if (DateTime.UtcNow < _nextAttempt) return;

        var local = Plugin.Objects.LocalPlayer;
        if (local == null) return;
        if (local.IsDead) return;
        if (local.MaxHp == 0) return;

        if (cfg.OnlyInCombat && !Plugin.Condition[ConditionFlag.InCombat]) return;
        if (cfg.OnlyInDuty && !Plugin.Condition[ConditionFlag.BoundByDuty]) return;

        var hpRatio = (float)local.CurrentHp / local.MaxHp;
        var missing = local.MaxHp - local.CurrentHp;
        var inDeepDungeon = IsInDeepDungeon();

        var targetId = local.GameObjectId;

        if (cfg.HpPotionEnable && hpRatio <= cfg.HpPotionThreshold / 100f)
        {
            var picked = PickBest(_hpPotions, local.MaxHp, missing, targetId, isRegen: false);
            if (picked != null && picked.TryUse(targetId))
            {
                _nextAttempt = DateTime.UtcNow.AddMilliseconds(750);
                Plugin.Log.Information($"AutoPotion: used HP potion {picked.Name} ({picked.Id})");
                return;
            }
        }

        if (cfg.RegenPotionEnable && inDeepDungeon && hpRatio <= cfg.RegenPotionThreshold / 100f)
        {
            var picked = PickBest(_regenPotions, local.MaxHp, missing, targetId, isRegen: true);
            if (picked != null && picked.TryUse(targetId))
            {
                _nextAttempt = DateTime.UtcNow.AddMilliseconds(750);
                Plugin.Log.Information($"AutoPotion: used regen potion {picked.Name} ({picked.Id})");
                return;
            }
        }

        // Idle throttle. TryUse pre-checks GetActionStatus and returns false
        // silently when blocked (cooldown, Item Penalty, Silence, between areas,
        // etc.), so polling here never produces in-game feedback.
        _nextAttempt = DateTime.UtcNow.AddMilliseconds(150);
    }

    private static Potion? PickBest(Potion[] potions, uint maxHp, uint missing, ulong targetId, bool isRegen)
    {
        // For regen potions: skip entirely while Rehabilitation is up. All deep
        // dungeon medicines share status 648, so this single check covers PotD,
        // HoH, Eureka Orthos, and Variant Dungeons.
        if (isRegen && PlayerHasStatus(RehabilitationStatusId)) return null;

        Potion? best = null;
        uint bestHeal = 0;
        foreach (var p in potions)
        {
            // GetActionStatus covers cooldown (582), no-items (583), restricted-to-other-duty
            // (2651), between areas, blocking statuses, etc. in one shot. Pass the player's
            // GameObjectId as target so self-targeted item checks evaluate correctly.
            if (p.GetActionStatus(targetId) != 0) continue;
            // Regen potions: deep dungeon potions have a different ItemAction Data layout,
            // so MaxHealFor returns 0 and heal-comparison logic is meaningless. There is
            // only ever one duty-specific candidate usable at a time anyway — return the
            // first one that passes the status filter.
            if (isRegen) return p;
            var heal = p.MaxHealFor(maxHp);
            // For HP potions, don't fire if the heal would overshoot (waste).
            if (missing < heal) continue;
            if (heal > bestHeal)
            {
                best = p;
                bestHeal = heal;
            }
        }
        return best;
    }

    private static bool PlayerHasStatus(uint statusId)
    {
        var local = Plugin.Objects.LocalPlayer;
        if (local?.StatusList == null) return false;
        foreach (var s in local.StatusList)
        {
            if (s != null && s.StatusId == statusId) return true;
        }
        return false;
    }

    private static bool IsInDeepDungeon()
    {
        var territoryId = Plugin.ClientState.TerritoryType;
        if (territoryId == 0) return false;
        var sheet = Plugin.Data.GetExcelSheet<TerritoryType>();
        if (sheet == null) return false;
        if (!sheet.TryGetRow(territoryId, out var row)) return false;
        return row.ContentFinderCondition.Value.ContentType.RowId == DeepDungeonContentType;
    }

    public void LogDebugState()
    {
        var cfg = _plugin.Config;
        var local = Plugin.Objects.LocalPlayer;
        var territoryId = Plugin.ClientState.TerritoryType;
        Plugin.Log.Information("=== AutoPotion debug ===");
        Plugin.Log.Information($"MasterEnable={cfg.MasterEnable} HpEnable={cfg.HpPotionEnable} RegenEnable={cfg.RegenPotionEnable}");
        Plugin.Log.Information($"HpThreshold={cfg.HpPotionThreshold}% RegenThreshold={cfg.RegenPotionThreshold}%");
        Plugin.Log.Information($"OnlyInCombat={cfg.OnlyInCombat} OnlyInDuty={cfg.OnlyInDuty}");
        Plugin.Log.Information($"InCombat={Plugin.Condition[ConditionFlag.InCombat]} BoundByDuty={Plugin.Condition[ConditionFlag.BoundByDuty]}");
        Plugin.Log.Information($"TerritoryId={territoryId} IsInDeepDungeon={IsInDeepDungeon()}");

        var sheet = Plugin.Data.GetExcelSheet<TerritoryType>();
        if (sheet != null && sheet.TryGetRow(territoryId, out var row))
        {
            Plugin.Log.Information(
                $"  IntendedUse={row.TerritoryIntendedUse.RowId} CFC={row.ContentFinderCondition.RowId} " +
                $"ContentType={row.ContentFinderCondition.Value.ContentType.RowId} " +
                $"Place={row.PlaceName.Value.Name.ExtractText()}");
        }

        if (local != null)
        {
            var hpRatio = local.MaxHp > 0 ? (float)local.CurrentHp / local.MaxHp : 0f;
            Plugin.Log.Information($"HP={local.CurrentHp}/{local.MaxHp} ({hpRatio:P0}) IsDead={local.IsDead}");
        }
        else { Plugin.Log.Information("LocalPlayer=null"); }

        var logMessageSheet = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.LogMessage>();
        string ResolveStatus(uint code)
        {
            if (code == 0) return "0(OK)";
            if (logMessageSheet != null && logMessageSheet.TryGetRow(code, out var msg))
                return $"{code}('{msg.Text.ExtractText()}')";
            return code.ToString();
        }

        Plugin.Log.Information($"HP potions ({_hpPotions.Length}):");
        foreach (var p in _hpPotions)
            Plugin.Log.Information($"  {p.Id} {p.Name} hq={p.InventoryCount(true)} nq={p.InventoryCount(false)} cd={p.IsOnCooldown()} status={ResolveStatus(p.GetActionStatus(local?.GameObjectId ?? 0xE000_0000))}");
        Plugin.Log.Information($"Regen potions ({_regenPotions.Length}):");
        foreach (var p in _regenPotions)
            Plugin.Log.Information($"  {p.Id} {p.Name} hq={p.InventoryCount(true)} nq={p.InventoryCount(false)} cd={p.IsOnCooldown()} status={ResolveStatus(p.GetActionStatus(local?.GameObjectId ?? 0xE000_0000))}");

        if (local?.StatusList != null)
        {
            var statusSheet = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Status>();
            Plugin.Log.Information($"Player statuses ({local.StatusList.Length}):");
            foreach (var s in local.StatusList)
            {
                if (s == null || s.StatusId == 0) continue;
                var name = "?";
                if (statusSheet != null && statusSheet.TryGetRow(s.StatusId, out var statusRow))
                    name = statusRow.Name.ExtractText();
                Plugin.Log.Information($"  {s.StatusId} {name} remaining={s.RemainingTime:F1}s stacks={s.Param}");
            }
        }
    }
}
