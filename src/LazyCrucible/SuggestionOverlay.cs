using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace LazyCrucible;

/// <summary>
///     Suggestion-only screens: a framed note drawn just above the game screen it is about (the choice LazyCrucible
///     would make and why). Read-only; it disappears when the screen closes.
/// </summary>
internal static unsafe class SuggestionOverlay
{
    public static void Draw()
    {
        if (SelectionScreens.Suggestions.Count == 0)
            return;
        var draw = ImGui.GetForegroundDrawList();
        foreach (var (addonName, text) in SelectionScreens.Suggestions)
        {
            if (!ScreenReader.TryGet(addonName, out var addon) || addon->RootNode is null)
                continue;
            var scale = addon->Scale <= 0 ? 1f : addon->Scale;
            var pos = new Vector2(addon->X, addon->Y) + ImGui.GetMainViewport().Pos;
            var width = addon->RootNode->Width * scale;
            var height = addon->RootNode->Height * scale;
            var label = $"LazyCrucible: {text}";
            var wrap = Math.Max(240f, width);
            var size = ImGui.CalcTextSize(label, false, wrap);
            var boxMin = new Vector2(pos.X, pos.Y - size.Y - 10);
            var boxMax = new Vector2(pos.X + Math.Min(wrap, size.X) + 12, pos.Y - 2);
            draw.AddRectFilled(boxMin, boxMax, ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.1f, 0.92f)), 4f);
            draw.AddRect(boxMin, boxMax, ImGui.GetColorU32(new Vector4(1f, 0.8f, 0.3f, 1f)), 4f, ImDrawFlags.None, 2f);
            draw.AddRect(pos, pos + new Vector2(width, height), ImGui.GetColorU32(new Vector4(1f, 0.8f, 0.3f, 0.9f)), 6f, ImDrawFlags.None, 2f);
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(boxMin.X + 6, boxMin.Y + 5),
                ImGui.GetColorU32(new Vector4(1f, 0.95f, 0.8f, 1f)), label, wrap);
        }
    }
}
