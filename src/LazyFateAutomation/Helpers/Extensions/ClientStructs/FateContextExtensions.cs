using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using Lumina.Excel.Sheets;

namespace LazyFateAutomation.Helpers.Extensions;

public static class FateContextExtensions {
    extension(ref FateContext ctx) {
        public Fate GameData => Fate.GetRow(ctx.FateId);
    }
}
