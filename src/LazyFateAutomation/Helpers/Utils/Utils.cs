using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LazyFateAutomation.Helpers.Utils;

public static class Utils {
    public static IDalamudTextureWrap? GetIcon(uint iconId) => iconId != 0 ? Svc.Texture?.GetFromGameIcon(iconId).GetWrapOrEmpty() : null;

    public static unsafe bool IsClickingInGameWorld()
        => !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow)
        && !ImGui.GetIO().WantCaptureMouse
        && AtkStage.Instance()->RaptureAtkUnitManager->AtkUnitManager.FocusedUnitsList.Count == 0
        && Framework.Instance()->Cursor->ActiveCursorType == 0;
}
