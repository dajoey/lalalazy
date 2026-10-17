using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace LazyCrucible;

/// <summary>
///     Suggestion-only screens: a framed note drawn next to the game screen it is about, above it when there is
///     room and below it otherwise (the choice LazyCrucible would make and why). Read-only; it disappears when the
///     screen closes.
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
            var viewport = ImGui.GetMainViewport();
            var pos = new Vector2(addon->X, addon->Y) + viewport.Pos;
            var width = addon->RootNode->Width * scale;
            var height = addon->RootNode->Height * scale;
            var label = $"LazyCrucible: {text}";
            var wrap = Math.Max(240f, width);
            var size = ImGui.CalcTextSize(label, false, wrap);
            var boxWidth = Math.Min(wrap, size.X) + 12;
            var boxHeight = size.Y + 8;
            // Keep the whole box inside the viewport work area: above the addon when it fits, below it otherwise.
            var workMax = viewport.WorkPos + viewport.WorkSize;
            var boxX = Math.Clamp(pos.X, viewport.WorkPos.X, Math.Max(viewport.WorkPos.X, workMax.X - boxWidth));
            var boxY = pos.Y - boxHeight - 2 >= viewport.WorkPos.Y ? pos.Y - boxHeight - 2 : pos.Y + height + 2;
            boxY = Math.Clamp(boxY, viewport.WorkPos.Y, Math.Max(viewport.WorkPos.Y, workMax.Y - boxHeight));
            var boxMin = new Vector2(boxX, boxY);
            var boxMax = new Vector2(boxX + boxWidth, boxY + boxHeight);
            draw.AddRectFilled(boxMin, boxMax, ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.1f, 0.92f)), 4f);
            draw.AddRect(boxMin, boxMax, ImGui.GetColorU32(new Vector4(1f, 0.8f, 0.3f, 1f)), 4f, ImDrawFlags.None, 2f);
            draw.AddRect(pos, pos + new Vector2(width, height), ImGui.GetColorU32(new Vector4(1f, 0.8f, 0.3f, 0.9f)), 6f, ImDrawFlags.None, 2f);
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(boxMin.X + 6, boxMin.Y + 5),
                ImGui.GetColorU32(new Vector4(1f, 0.95f, 0.8f, 1f)), label, wrap);
        }
    }
}
