using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using Lumina.Excel.Sheets;

namespace LazyFateAutomation.Helpers.Extensions;

public static class IChatGuiExtensions {
    public static unsafe void ExecuteCommand(this IChatGui _, string command) {
        if (!command.StartsWith('/'))
            return;

        using var cmd = new Utf8String(command);
        RaptureShellModule.Instance()->ExecuteCommandInner(&cmd, UIModule.Instance());
    }

    public static void PrintMessage(this IChatGui chat, string message)
        => chat.Print(new XivChatEntry {
            Type = XivChatType.Echo,
            Message = $"[LazyFateAutomation] {message}"
        });

    public static void PrintError(this IChatGui chat, string message)
        => chat.Print(new XivChatEntry {
            Type = XivChatType.SystemError,
            Message = $"[LazyFateAutomation] {message}"
        });

    public static void PrintColor(this IChatGui chat, string message, UIColor color)
        => chat.Print(new XivChatEntry {
            Type = XivChatType.Echo,
            Message = new SeString(
                new UIForegroundPayload((ushort)color.RowId),
                new TextPayload($"[LazyFateAutomation] {message}"),
                UIForegroundPayload.UIForegroundOff)
        });
}
