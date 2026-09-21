using Lalalazy.Crucible;

namespace LazyCrucible;

/// <summary> What the player carries, as the shop / spoils / treasure screens list it. </summary>
public sealed record Inventory(IReadOnlyList<int> Items, IReadOnlyList<int> Gear)
{
    /// <summary> Crucible item slots (duplicates allowed). </summary>
    public const int ItemSlots = 10;

    /// <summary> Beast gear slots (no duplicates). </summary>
    public const int GearSlots = 10;

    public static readonly Inventory Empty = new([], []);

    public bool ItemsFull => Items.Count >= ItemSlots;
    public bool GearFull => Gear.Count >= GearSlots;
    public bool Owns(int gearRow) => Gear.Contains(gearRow);
    public int HealingHeld => Items.Count(CrucibleItems.IsHealing);

    public Inventory With(int row) => CrucibleItems.Get(row).Type switch
    {
        CrucibleItemType.Gear => this with { Gear = [.. Gear, row] },
        CrucibleItemType.Item => this with { Items = [.. Items, row] },
        _ => this,
    };
}

/// <summary>
///     Survival value of a Crucible item, beast gear or feed for the run ahead. PURE. The numbers come from the
///     item's own tooltip (generated table); the weights below are the policy: staying alive (HP, damage taken,
///     resistance to what the fights ahead inflict, heals, revives) outranks damage, and score-only items are worth
///     almost nothing. Every value comes with the short reason shown in chat and in the guide window.
/// </summary>
internal static class ItemValue
{
    /// <summary> Value below which nothing is bought or taken on its own merit. </summary>
    public const int Worthwhile = 12;

    /// <summary> Value of an item / gear piece for the player (feed has its own scale: <see cref="FeedPolicy.Value"/>). </summary>
    public static int Of(int row, RunContext ctx, Inventory inv, IReadOnlyList<FamiliarState>? familiars, List<string>? why = null)
    {
        var item = CrucibleItems.Get(row);
        if (!item.IsDefined)
            return 0;
        return item.Type switch
        {
            CrucibleItemType.Gear => Gear(item, ctx, inv, why),
            CrucibleItemType.Item => Consumable(item, ctx, inv, familiars, why),
            CrucibleItemType.Feed => familiars is { Count: > 0 } && FeedPolicy.BestTarget(row, familiars, ctx) is { } t ? t.Score / 2 : 0,
            _ => 0,
        };
    }

    private static int Resist(CrucibleStatus resists, RunContext ctx, int certain, int possible, List<string>? why)
    {
        var v = 0;
        foreach (var s in RunContext.Each(resists))
        {
            var level = ctx.ThreatLevel(RunContext.ThreatOf(s));
            if (level >= 2)
            {
                v += certain;
                why?.Add($"{RunContext.StatusName(s)} resist for a fight ahead");
            }
            else if (level == 1)
            {
                v += possible;
                why?.Add($"{RunContext.StatusName(s)} resist for a possible fight");
            }
            else
                v += 2;
        }
        return v;
    }

    private static int Gear(CrucibleItem g, RunContext ctx, Inventory inv, List<string>? why)
    {
        if (inv.Owns(g.Row))
        {
            why?.Add("already owned");
            return 0;
        }
        if (inv.GearFull)
        {
            why?.Add("gear slots full");
            return 0;
        }

        var v = 0;
        switch (g.Role)
        {
            case ItemRole.SelfRevive:
                why?.Add("revives you once");
                return 40;
            case ItemRole.AfterBattleHeal:
                why?.Add("heals 25% after each battle");
                return 20 + 5 * Math.Min(ctx.FightsAhead, 4);
            case ItemRole.CampPotion:
                why?.Add("potion at each campsite");
                return ctx.CampAhead ? 12 : 2;
            case ItemRole.ExtraTreasure:
                why?.Add("two picks on treasure spaces");
                return 6;
            case ItemRole.Flee:
                v += 4;
                break;
        }

        if (g.MaxHp > 0)
        {
            v += g.MaxHp;
            why?.Add($"max HP +{g.MaxHp}%");
        }
        if (g.DamageTaken > 0)
        {
            v += 2 * g.DamageTaken;
            why?.Add($"damage taken -{g.DamageTaken}%");
        }
        if (g.PhysVuln + g.MagicVuln > 0)
        {
            v += g.PhysVuln + g.MagicVuln;
            why?.Add(g.PhysVuln > 0 && g.MagicVuln > 0 ? "less damage taken"
                : g.PhysVuln > 0 ? $"physical damage taken -{g.PhysVuln}%" : $"magic damage taken -{g.MagicVuln}%");
        }
        v += Resist(g.Resists, ctx, 35, 18, why);

        var dmg = g.DamageDealt + Math.Max(g.PhysDamage, g.MagicDamage);
        if ((g.Flags & ItemFlags.PetGear) != 0)
        {
            v += dmg / 2;
            if (dmg > 0)
                why?.Add("damage, familiar too");
        }
        else if (dmg > 0)
        {
            v += dmg / 3;
            if (why is { Count: 0 })
                why.Add($"damage +{dmg}%");
        }

        var axe = AxeElement(g.Name);
        if (axe != CrucibleWeakness.None)
        {
            var hits = ctx.WeaknessAhead.GetValueOrDefault(axe);
            v += 8 + 6 * Math.Min(hits, 3);
            why?.Add(hits > 0 ? $"{axe.ToString().ToLowerInvariant()} autos for {hits} weak fight{(hits == 1 ? "" : "s")} ahead" : "elemental autos");
        }

        v -= HazardCost(g, why);
        return Math.Max(v, 0);
    }

    private static CrucibleWeakness AxeElement(string name) => name switch
    {
        "Flame-wreathed Axe" => CrucibleWeakness.Fire,
        "Icebitten Axe" => CrucibleWeakness.Ice,
        "Thunderstruck Axe" => CrucibleWeakness.Lightning,
        "Earthcrushed Axe" => CrucibleWeakness.Earth,
        "Deepdrowned Axe" => CrucibleWeakness.Water,
        "Windblown Axe" => CrucibleWeakness.Wind,
        _ => CrucibleWeakness.None,
    };

    private static int HazardCost(CrucibleItem g, List<string>? why)
    {
        var cost = 0;
        if ((g.Hazards & ItemHazard.MaxHpDown) != 0)
            cost += 2 * Math.Abs(Math.Min(g.MaxHp, 0));
        if ((g.Hazards & ItemHazard.Slow) != 0)
            cost += 10;
        if ((g.Hazards & ItemHazard.SelfDot) != 0)
            cost += 15;
        if ((g.Hazards & ItemHazard.SelfStatus) != 0)
            cost += 10;
        if ((g.Hazards & ItemHazard.MoveSpeedDown) != 0)
            cost += 8;
        if (cost > 0)
            why?.Add("has a drawback");
        return cost;
    }

    private static int Consumable(CrucibleItem it, RunContext ctx, Inventory inv, IReadOnlyList<FamiliarState>? familiars, List<string>? why)
    {
        if (inv.ItemsFull)
        {
            why?.Add("item slots full");
            return 0;
        }

        var held = inv.HealingHeld;
        var fewerHeals = 4 - Math.Min(held, 3); // 4, 3, 2, 1 quarters as the healing stock grows
        switch (it.Role)
        {
            case ItemRole.Heal:
                why?.Add($"heals {it.Heal}%{(held == 0 ? ", no heals held" : "")}");
                return 5 + it.Heal * fewerHeals / 4;
            case ItemRole.HealParty:
                why?.Add($"heals {it.Heal}% for you and familiars");
                return 5 + it.Heal * 6 / 5 * fewerHeals / 4;
            case ItemRole.AutoHeal:
                why?.Add("auto-heals 40% below half HP");
                return 5 + 45 * fewerHeals / 4;
            case ItemRole.Reraise:
                why?.Add("reraise");
                return it.Name.StartsWith("G2", StringComparison.Ordinal) ? 70 : 55;
            case ItemRole.Rewind:
                why?.Add("retry a lost battle");
                return 60;
            case ItemRole.Revive:
            {
                var neededDown = familiars?.Any(f => f.KnockedOut && ctx.Demand(f.Row) > 0) == true;
                why?.Add(neededDown ? "revives a needed familiar" : "revives a familiar");
                return neededDown ? 45 : 18;
            }
            case ItemRole.Cure:
            {
                var level = ctx.ThreatLevel(RunContext.ThreatOf(it.Cures));
                why?.Add($"cures {RunContext.StatusName(it.Cures)}{(level > 0 ? " (a fight ahead inflicts it)" : "")}");
                return level >= 2 ? 25 : level == 1 ? 14 : 2;
            }
            case ItemRole.Serum:
            case ItemRole.SerumParty:
            {
                var v = Resist(it.Resists, ctx, it.Role == ItemRole.SerumParty ? 32 : 28, 15, why);
                return v;
            }
            case ItemRole.SwiftPoisonResist:
            {
                var level = ctx.ThreatLevel(CrucibleThreat.Poison);
                why?.Add(level > 0 ? "poison resist for a whole fight" : "speed");
                return level >= 2 ? 35 : level == 1 ? 18 : 6;
            }
            case ItemRole.AutoCure:
            {
                var any = ctx.ThreatLevel(CrucibleThreat.Poison) + ctx.ThreatLevel(CrucibleThreat.Paralysis)
                          + ctx.ThreatLevel(CrucibleThreat.Blind) + ctx.ThreatLevel(CrucibleThreat.Petrify)
                          + ctx.ThreatLevel(CrucibleThreat.Sleep);
                why?.Add("cures the next ailment");
                return any > 0 ? 16 : 4;
            }
            case ItemRole.Mitigation:
            {
                var big = Math.Max(ctx.ThreatLevel(CrucibleThreat.Tankbuster), ctx.ThreatLevel(CrucibleThreat.RoomWide));
                why?.Add("damage reduction");
                return 14 + 4 * big;
            }
            case ItemRole.MaxHpBuff:
                why?.Add("max HP for a fight");
                return 15;
            case ItemRole.FangDispel:
            {
                var v = 10;
                if ((ctx.NeedsAhead & CrucibleNeeds.Dispel) != 0)
                {
                    v += 12;
                    why?.Add("AoE and dispels a buff a fight ahead uses");
                }
                else
                    why?.Add("AoE damage");
                if (ctx.ThreatLevel(CrucibleThreat.Adds) > 0)
                    v += 6;
                return v;
            }
            case ItemRole.Fang:
                why?.Add(ctx.ThreatLevel(CrucibleThreat.Adds) > 0 ? "AoE for adds ahead" : "AoE damage");
                return ctx.ThreatLevel(CrucibleThreat.Adds) > 0 ? 18 : 10;
            case ItemRole.Flee:
                why?.Add("flee an enemy space");
                return 8;
            case ItemRole.Absorb:
            case ItemRole.Offense:
                why?.Add("damage or utility");
                return 5;
            case ItemRole.Feral:
                why?.Add("inflicts a status on the user");
                return 0;
            case ItemRole.Score:
                why?.Add("score only");
                return 1;
            default:
                return 3;
        }
    }

    /// <summary> One-line reason: "Master Shield: max HP +35%, damage taken -25%". </summary>
    public static string Describe(int row, List<string> why) =>
        why.Count == 0 ? CrucibleItems.NameOf(row) : $"{CrucibleItems.NameOf(row)}: {string.Join(", ", why)}";
}
