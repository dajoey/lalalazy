using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using Lumina.Excel.Sheets;

namespace clib.Extensions;

public static class FateContextExtensions {
    public static unsafe Lumina.Excel.Sheets.Fate? GameData(this ref FateContext ctx)
        => Svc.Data.GetRow<Lumina.Excel.Sheets.Fate>(ctx.FateId);
}
