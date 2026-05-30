using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;

namespace LazyFateAutomation.Helpers.Extensions;

public static class ConfigKeyExtensions {
    public static bool TryGetInputId(this ConfigKey key, out InputId inputId) {
        inputId = Enum.GetValues<InputId>().FirstOrDefault(i => Enum.GetName(i) == key.Label.ToString(), InputId.NotFound);
        return inputId != InputId.NotFound;
    }

    public static unsafe bool IsDown(this ConfigKey key) => key.TryGetInputId(out var inputId) && UIInputData.Instance()->IsInputIdDown(inputId);
    public static unsafe bool IsHeld(this ConfigKey key) => key.TryGetInputId(out var inputId) && UIInputData.Instance()->IsInputIdHeld(inputId);
    public static unsafe bool IsPressed(this ConfigKey key) => key.TryGetInputId(out var inputId) && UIInputData.Instance()->IsInputIdPressed(inputId);
    public static unsafe bool IsReleased(this ConfigKey key) => key.TryGetInputId(out var inputId) && UIInputData.Instance()->IsInputIdReleased(inputId);
}
