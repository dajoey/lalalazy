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

    private enum PickMode
    {
        HpBest,                 // Heal-comparison + overshoot guard (HP potions).
        FirstAvailable,         // First entry that passes status checks (MP potions).
        RegenFirstAvailable,    // Same, plus Rehabilitation lockout (deep dungeon regen).
    }

    private readonly Plugin _plugin;
    private readonly Potion[] _hpPotions;
    private readonly Potion[] _mpPotions;
    private readonly Potion[] _regenPotions;
    private DateTime _nextAttempt = DateTime.MinValue;

    // Last reason the previous tick didn't fire a potion. Surfaced by /autopotion debug
    // so the user can tell at a glance which gate is blocking (combat? duty? threshold?
    // no candidate?) without having to enable a verbose log.
    private string _lastSkipReason = "(no tick yet)";

    public PotionService(Plugin plugin)
    {
        _plugin = plugin;

        var itemSheet = Plugin.Data.GetExcelSheet<Item>();
        var hp = new List<Potion>();
        var mp = new List<Potion>();
        var regen = new List<Potion>();
        if (itemSheet != null)
        {
            foreach (var i in itemSheet)
            {
                if (i.ItemAction.RowId == 0) continue;

                if (Array.IndexOf(RegenPotionIds, i.RowId) >= 0)
                {
                    regen.Add(new Potion(i));
                    continue;
                }

                // Name-suffix dispatch only: HP potions end in "Potion", MP potions end in
                // "Ether". 0.2.0.0 keyed on FilterGroup==8 + ItemSearchCategory; 0.2.1.0
                // dropped the category but kept FilterGroup. Both filtered out every Ether
                // in practice. The name suffix is reliable across every expansion and the
                // ItemAction.RowId pre-check rules out non-consumable name collisions.
                var name = i.Name.ExtractText().Trim();
                if (name.EndsWith("Ether", StringComparison.OrdinalIgnoreCase))
                    mp.Add(new Potion(i));
                else if (name.EndsWith("Potion", StringComparison.OrdinalIgnoreCase))
                    hp.Add(new Potion(i));
            }
        }
        _hpPotions = hp.ToArray();
        _mpPotions = mp.ToArray();
        _regenPotions = regen.ToArray();
        Plugin.Log.Information(
            $"AutoPotion: indexed {_hpPotions.Length} HP, {_mpPotions.Length} MP, {_regenPotions.Length} regen potions");
        // One-shot dump so /autopotion debug shows exactly what was indexed and lets us
        // catch missed items without another rebuild.
        foreach (var p in _mpPotions)
            Plugin.Log.Information($"  MP[{p.Id}] {p.Name}");
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

        if (cfg.OnlyInCombat && !Plugin.Condition[ConditionFlag.InCombat])
        { _lastSkipReason = "OnlyInCombat=true and not InCombat"; return; }
        if (cfg.OnlyInDuty && !Plugin.Condition[ConditionFlag.BoundByDuty])
        { _lastSkipReason = "OnlyInDuty=true and not BoundByDuty"; return; }

        var job = cfg.GetJobSettings(local.ClassJob.RowId);

        var hpRatio = (float)local.CurrentHp / local.MaxHp;
        var hpMissing = local.MaxHp - local.CurrentHp;
        var inDeepDungeon = IsInDeepDungeon();

        var targetId = local.GameObjectId;

        if (job.HpPotionEnable && hpRatio <= job.HpPotionThreshold / 100f)
        {
            var picked = PickBest(_hpPotions, local.MaxHp, hpMissing, targetId, PickMode.HpBest);
            if (picked != null && picked.TryUse(targetId))
            {
                _nextAttempt = DateTime.UtcNow.AddMilliseconds(750);
                _lastSkipReason = $"used HP potion {picked.Name}";
                Plugin.Log.Information($"AutoPotion: used HP potion {picked.Name} ({picked.Id})");
                return;
            }
        }

        if (job.MpPotionEnable && local.MaxMp > 0)
        {
            var mpRatio = (float)local.CurrentMp / local.MaxMp;
            if (mpRatio <= job.MpPotionThreshold / 100f)
            {
                var picked = PickBest(_mpPotions, local.MaxMp, 0, targetId, PickMode.FirstAvailable);
                if (picked != null && picked.TryUse(targetId))
                {
                    _nextAttempt = DateTime.UtcNow.AddMilliseconds(750);
                    _lastSkipReason = $"used MP potion {picked.Name}";
                    Plugin.Log.Information($"AutoPotion: used MP potion {picked.Name} ({picked.Id})");
                    return;
                }
            }
        }

        if (job.RegenPotionEnable && inDeepDungeon && hpRatio <= job.RegenPotionThreshold / 100f)
        {
            var picked = PickBest(_regenPotions, local.MaxHp, hpMissing, targetId, PickMode.RegenFirstAvailable);
            if (picked != null && picked.TryUse(targetId))
            {
                _nextAttempt = DateTime.UtcNow.AddMilliseconds(750);
                _lastSkipReason = $"used regen potion {picked.Name}";
                Plugin.Log.Information($"AutoPotion: used regen potion {picked.Name} ({picked.Id})");
                return;
            }
        }

        // No threshold crossed (or no candidate passed status checks). Keep last reason
        // informative so /autopotion debug shows the most recent meaningful state instead
        // of a stale "no tick yet".
        _lastSkipReason = $"no candidate fired (HP={hpRatio:P0}/{job.HpPotionThreshold:F0}%, MP={(local.MaxMp > 0 ? (float)local.CurrentMp / local.MaxMp : 0):P0}/{job.MpPotionThreshold:F0}%, deepDungeon={inDeepDungeon})";

        // Idle throttle. TryUse pre-checks GetActionStatus and returns false silently when
        // blocked (cooldown, Item Penalty, Silence, between areas, etc.), so polling here
        // never produces in-game feedback.
        _nextAttempt = DateTime.UtcNow.AddMilliseconds(150);
    }

    private static Potion? PickBest(Potion[] potions, uint maxResource, uint missing, ulong targetId, PickMode mode)
    {
        // Regen potions: skip entirely while Rehabilitation is up. All deep dungeon
        // medicines share status 648 (Rehabilitation), so this single check covers PotD,
        // HoH, Eureka Orthos, and Variant Dungeons.
        if (mode == PickMode.RegenFirstAvailable && PlayerHasStatus(RehabilitationStatusId)) return null;

        Potion? best = null;
        uint bestHeal = 0;
        foreach (var p in potions)
        {
            // GetActionStatus covers cooldown (582), no-items (583), restricted-to-other-duty
            // (2651), between areas, blocking statuses, etc. in one shot. Pass the player's
            // GameObjectId as target so self-targeted item checks evaluate correctly.
            if (p.GetActionStatus(targetId) != 0) continue;

            // FirstAvailable / RegenFirstAvailable: deep dungeon and MP potions don't expose
            // a usable percent/cap pair via Potion.MaxHealFor (flat-amount ethers and
            // duty-specific actions), so heal-comparison is meaningless. Take the first
            // candidate that passes the status filter.
            if (mode != PickMode.HpBest) return p;

            var heal = p.MaxHealFor(maxResource);
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
        var jobId = local?.ClassJob.RowId ?? 0;
        var job = cfg.GetJobSettings(jobId);
        Plugin.Log.Information("=== AutoPotion debug ===");
        Plugin.Log.Information($"Last tick: {_lastSkipReason}");
        Plugin.Log.Information($"MasterEnable={cfg.MasterEnable} OnlyInCombat={cfg.OnlyInCombat} OnlyInDuty={cfg.OnlyInDuty}");
        Plugin.Log.Information($"Job={jobId} (per-job overrides: {cfg.Jobs.Count})");
        Plugin.Log.Information($"  HpEnable={job.HpPotionEnable} HpThreshold={job.HpPotionThreshold}%");
        Plugin.Log.Information($"  MpEnable={job.MpPotionEnable} MpThreshold={job.MpPotionThreshold}%");
        Plugin.Log.Information($"  RegenEnable={job.RegenPotionEnable} RegenThreshold={job.RegenPotionThreshold}%");
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
            var mpRatio = local.MaxMp > 0 ? (float)local.CurrentMp / local.MaxMp : 0f;
            Plugin.Log.Information($"HP={local.CurrentHp}/{local.MaxHp} ({hpRatio:P0}) MP={local.CurrentMp}/{local.MaxMp} ({mpRatio:P0}) IsDead={local.IsDead}");
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
        Plugin.Log.Information($"MP potions ({_mpPotions.Length}):");
        foreach (var p in _mpPotions)
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
