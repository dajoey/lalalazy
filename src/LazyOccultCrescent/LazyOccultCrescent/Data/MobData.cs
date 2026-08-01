using System.Collections.Generic;
using System.Globalization;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace LazyOccultCrescent.Data;

public static class MobData
{
    private static Dictionary<Mob, string> NameCache = [];

    public static List<Mob> MobsWithSpawnCondition { get; private set; } =
    [
        Mob.Armor,
        Mob.Bomb,
        Mob.Caoineag,
        Mob.Dhruva,
        Mob.Dullahan,
        Mob.Fool,
        Mob.Geshunpest,
        Mob.Ghost,
        Mob.Gourmand,
        Mob.Mimic,
        Mob.Mousse,
        Mob.Troubadour,
    ];

    public static string GetName(Mob mob)
    {
        if (NameCache.TryGetValue(mob, out var name))
        {
            return name;
        }

        if (Svc.Data.GetExcelSheet<BNpcName>().TryGetRow((uint)mob, out var row))
        {
            var titleCase = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(row.Singular.ToString().ToLower());
            NameCache[mob] = titleCase;
            return titleCase;
        }

        return mob.ToString();
    }
}
