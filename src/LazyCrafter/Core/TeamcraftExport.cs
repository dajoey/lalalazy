namespace LazyCrafter.Core;

/// <summary>
/// Builds a TeamCraft list-import link for a set of final items (Plan §Phase 4 task 4).
/// <para>
/// Format, verified against the TeamCraft client source (<c>pages/import/import.component.ts</c>): the path
/// segment after <c>https://ffxivteamcraft.com/import/</c> is base64 of <c>itemId,recipeId,quantity</c> rows
/// joined by <c>;</c>, where <c>recipeId</c> is the literal <c>null</c> when unknown and <c>quantity</c> is the
/// number of <b>items</b> wanted (not crafts). TeamCraft's own test vector
/// <c>MjA1NDUsbnVsbCwzOzE3OTYyLDMyMzA4LDE7MjAyNDcsbnVsbCwx</c> = <c>20545,null,3;17962,32308,1;20247,null,1</c>.
/// Artisan's <c>Teamcraft.ExportSelectedListToTC</c> emits the same shape with <c>null</c> recipe ids.
/// </para>
/// </summary>
public static class TeamcraftExport
{
    public const string BaseUrl = "https://ffxivteamcraft.com/import/";

    public sealed record Line(uint ItemId, uint? RecipeId, int Quantity);

    /// <summary>The raw (pre-base64) payload. Lines with a non-positive quantity are dropped; duplicates of an item are summed.</summary>
    public static string Payload(IEnumerable<Line> lines)
    {
        var merged = new List<Line>();
        var index = new Dictionary<uint, int>();
        foreach (var l in lines)
        {
            if (l.Quantity <= 0) continue;
            if (index.TryGetValue(l.ItemId, out var i))
                merged[i] = merged[i] with { Quantity = checked(merged[i].Quantity + l.Quantity), RecipeId = merged[i].RecipeId ?? l.RecipeId };
            else
            {
                index[l.ItemId] = merged.Count;
                merged.Add(l);
            }
        }
        return string.Join(";", merged.Select(l => $"{l.ItemId},{(l.RecipeId is { } r ? r.ToString() : "null")},{l.Quantity}"));
    }

    /// <summary>Base64 of <see cref="Payload"/>; empty when there is nothing to export.</summary>
    public static string Encode(IEnumerable<Line> lines)
    {
        var payload = Payload(lines);
        return payload.Length == 0 ? "" : Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));
    }

    /// <summary>Full import URL, or <c>null</c> when the list is empty.</summary>
    public static string? Link(IEnumerable<Line> lines)
    {
        var b64 = Encode(lines);
        return b64.Length == 0 ? null : BaseUrl + b64;
    }
}
