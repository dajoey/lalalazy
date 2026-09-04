using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace LazyCrafter.Adapters.Dispatch;

/// <summary>
/// Post-craft pricing hand-off (Plan §Phase 5 task 5, Scope §0 item 6 - optional, never forced). Dagobert has no IPC for
/// its sell list, so v1 prints instructions after Artisan finishes a cart: what was made and how many, and the
/// <c>/pricematch</c> command that opens DagobertPriceMatcher. Only when <c>Configuration.DagobertAfterCraft</c> is on.
/// </summary>
public sealed class DagobertDispatch
{
    public const string InternalName = "DagobertPriceMatcher";

    private readonly IDalamudPluginInterface _pi;
    private readonly IChatGui _chat;

    public DagobertDispatch(IDalamudPluginInterface pi, IChatGui chat)
    {
        _pi = pi;
        _chat = chat;
    }

    public bool Installed => _pi.InstalledPlugins.Any(p => p.InternalName == InternalName && p.IsLoaded);

    public void AfterCraft(IReadOnlyList<(uint ItemId, int Quantity)> made, Func<uint, string> itemName, Func<uint, bool> marketable)
    {
        var sellable = made.Where(m => m.Quantity > 0 && marketable(m.ItemId)).ToList();
        if (sellable.Count == 0) return;
        var list = string.Join(", ", sellable.Take(8).Select(m => $"{itemName(m.ItemId)} x{m.Quantity}")) + (sellable.Count > 8 ? $" +{sellable.Count - 8}" : "");
        _chat.Print($"[LazyCrafter] Crafted and marketable: {list}.");
        _chat.Print(Installed
            ? "[LazyCrafter] To list them: summon a retainer, put the items up for sale, then use Dagobert (/pricematch) to match the board price."
            : "[LazyCrafter] DagobertPriceMatcher is not installed; list them at a retainer manually.");
    }
}
