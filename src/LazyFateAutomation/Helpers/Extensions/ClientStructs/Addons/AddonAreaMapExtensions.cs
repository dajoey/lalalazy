using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Text;
using System.Text.RegularExpressions;

namespace LazyFateAutomation.Helpers.Extensions;

public static unsafe partial class AddonAreaMapExtensions {
    public static Vector2? GetMouseWorldCoords(ref this AddonAreaMap areaMap) {
        var mapCoords = areaMap.GetMouseMapCoords();
        return mapCoords is null ? null : MapToWorld(mapCoords.Value, Svc.Data.GetRef<Sheets.Map>(AgentMap.Instance()->SelectedMapId).Value);
    }

    // TODO: see if this is in the agent
    public static Vector2? GetMouseMapCoords(ref this AddonAreaMap areaMap) {
        var node = areaMap.AtkUnitBase.GetNodeById<AtkTextNode>(46);
        if (node == null) return null;
        var text = node->NodeText.ToString();
        var match = ExtractCoords().Match(text);
        if (match.Success && match.Groups.Count >= 3) {
            var xStr = ConvertSubscriptToNumber(match.Groups[1].Value);
            var yStr = ConvertSubscriptToNumber(match.Groups[2].Value);

            if (xStr.Length > 0 && yStr.Length > 0 &&
                float.TryParse(xStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(yStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)) {
                return new Vector2(x, y);
            }
        }

        return null;
    }

    private static string ConvertSubscriptToNumber(string input) {
        var result = new StringBuilder();
        foreach (var c in input) {
            if (c is >= '\u2080' and <= '\u2089') {
                result.Append((char)('0' + (c - '\u2080'))); // subscripts to regular
            }
            else if (char.IsDigit(c) || c == '.') {
                result.Append(c); // keep digits and dots
            }
            else if (c == '\uE03F') {
                result.Append('.'); // convert special dot to dot
            }
        }
        return result.ToString();
    }

    private static Vector2 MapToWorld(Vector2 pos, Sheets.Map zone) {
        MapLinkPayload maplink = new(zone.TerritoryType.Value.RowId, zone.RowId, pos.X, pos.Y);
        return new(maplink.RawX / 1000f, maplink.RawY / 1000f);
    }

    [GeneratedRegex(@"X:\s*(.*?)\s+Y:\s*(.*)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex ExtractCoords();
}
