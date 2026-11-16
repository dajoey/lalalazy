using CurrencySpender.Classes;
using CurrencySpender.Windows; // Fork: table cell helpers of the shared shell
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace CurrencySpender.Helpers;

internal static unsafe class UiHelper
{
    public static void RightAlign(string str, bool formatString = false)
    {
        if (formatString) str = StringHelper.FormatString(str);
        // Fork: inside a table every line is centred in the shared line slot and aligned to the cell's right edge.
        if (CurrencyChrome.InTable)
        {
            foreach (var line in str.Split('\n'))
                CurrencyChrome.TextCell(line, right: true);
            return;
        }
        float rowHeight = ImGui.GetFrameHeight(); // Height of the row
        float textHeight = ImGui.GetTextLineHeight(); // Height of the text
        float iconSizef = 20; // Assuming your icon is 20x20
        float maxHeight = Math.Max(textHeight, iconSizef); // Get the largest height (icon or text)
        float offset = (rowHeight - maxHeight) / 1.0f; // Calculate offset

        if (offset > 0)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset); // Adjust vertical position
        var posX = ImGui.GetCursorPosX() + ImGui.GetColumnWidth() - ImGui.CalcTextSize(str).X
            - ImGui.GetScrollX();
        if (posX > ImGui.GetCursorPosX())
            ImGui.SetCursorPosX(posX);
        ImGui.Text(str);
    }
    public static void RightAlignWithIcon(string text, ImTextureID icon, bool formatString = false)
    {
        if (formatString)
            text = StringHelper.FormatString(text);
        // Fork: inside a table the number and currency icon share one centred line slot, right aligned to the cell.
        if (CurrencyChrome.InTable)
        {
            CurrencyChrome.IconTextCell(text, icon);
            return;
        }

        // Get current column width
        float columnWidth = ImGui.GetColumnWidth();

        // Calculate the total width of text + icon + padding
        Vector2 iconSize = new Vector2(20, 20);
        float textWidth = ImGui.CalcTextSize(text).X;
        float totalWidth = textWidth + iconSize.X + 5; // 4px padding between text and icon

        // Calculate starting position for alignment
        float posX = ImGui.GetCursorPosX() + columnWidth - totalWidth - ImGui.GetScrollX();

        // Ensure position is within bounds
        if (posX > ImGui.GetCursorPosX())
            ImGui.SetCursorPosX(posX);

        float rowHeight = ImGui.GetFrameHeight(); // Height of the row
        float textHeight = ImGui.GetTextLineHeight(); // Height of the text
        float iconSizef = 20; // Assuming your icon is 20x20
        float maxHeight = Math.Max(textHeight, iconSizef); // Get the largest height (icon or text)
        float offset = (rowHeight - maxHeight) / 1.0f; // Calculate offset

        if(offset > 0)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset); // Adjust vertical position

        // Render text
        ImGui.Text(text);

        // Render icon next to the text
        ImGui.SameLine();
        ImGui.Image(icon, iconSize);
    }
    public static void LeftAlign(string str, bool formatString = false)
    {
        if (formatString) str = StringHelper.FormatString(str);
        // Fork: inside a table every line is centred in the shared line slot and elided to the cell (full text on hover).
        if (CurrencyChrome.InTable)
        {
            foreach (var line in str.Split('\n'))
                CurrencyChrome.TextCell(line, right: false);
            return;
        }
        float rowHeight = ImGui.GetFrameHeight(); // Height of the row
        float textHeight = ImGui.GetTextLineHeight(); // Height of the text
        float iconSizef = 20; // Assuming your icon is 20x20
        float maxHeight = Math.Max(textHeight, iconSizef); // Get the largest height (icon or text)
        float offset = (rowHeight - maxHeight) / 1.0f; // Calculate offset

        if (offset > 0)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset); // Adjust vertical position
        ImGui.Text(str);
    }

    public static void WarningText(string str)
    {
        // Fork: the same tinted notice as the main window's attention state instead of the thin kit info strip.
        CurrencyChrome.Notice(str, LalaUiKit.UiState.Blocked);
    }

    /// <summary>Fork: width of the Flag and TP button pair, for the shared Actions column.</summary>
    internal static float MapButtonsWidth() => CurrencyChrome.SmallButtonWidth("Flag") + 8 + CurrencyChrome.SmallButtonWidth("TP");

    private static readonly MissingLocationLog missingLocationLog = new();

    internal static void BuildMapButtons(ShopItem item)
    {
        Location backupLocation = new Location();
        if (item?.Shop?.Location == null) 
        {
            // Runs every frame for every row, and a shop NPC missing from the hand-kept Location.Locations
            // is an expected data gap: note each NPC once at Debug (was ERR per row per frame, 159,189
            // lines in one session on 2026-10-04) and draw no Flag/TP buttons for it.
            if (item?.Shop != null && missingLocationLog.Decide(item.Shop.NpcId) == MissingLocationLogLevel.Debug)
                PluginLog.Debug($"No map location for shop NPC {item.Shop.NpcId} ({item.Shop.NpcName}), shop {item.ShopId}; Flag/TP buttons hidden.");
            return;
        }
        if (item.Shop.Location.NeedsPresence && item.Shop.Location.BackupNpc != null)
        {
            PluginLog.Debug("Back up location triggered");
            backupLocation = Location.Locations.Where(loc => loc.NpcId == item.Shop.Location.BackupNpc).First();
        }
        var slotTop = CurrencyChrome.MapButtonsBegin(MapButtonsWidth()); // Fork: framed buttons, right aligned in one line slot
        if (CurrencyChrome.SmallButton($"flag-{item.Id}-{item.ShopId}-{item.Shop.NpcId}", "Flag"))
        {
            if(item.Shop.Location.NeedsPresence && AgentMap.Instance()->CurrentTerritoryId != item.Shop.Location.TerritoryId)
            {
                PluginLog.Debug($"Back up location triggered: {backupLocation}");
                Service.GameGui.OpenMapWithMapLink(backupLocation.GetMapMarker());
            }
            else
            {
                PluginLog.Debug($"Normal location triggered: {item.Shop.Location}");
                var loc = item.Shop.Location;
                PluginLog.Information($"DEBUG: NpcId={loc.NpcId}, Position={loc.Position?.X}, {loc.Position?.Y}, HasPosition={loc.Position != null}");
                Service.GameGui.OpenMapWithMapLink(item.Shop.Location.GetMapMarker());
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip(); // Fork: plain text; the table cell helpers would draw nothing in a tooltip window
            if(item.Shop.Location.NeedsPresence && AgentMap.Instance()->CurrentTerritoryId != item.Shop.Location.TerritoryId)
                ImGui.TextUnformatted($"The flag will only show up, if you are here: {item.Shop.Location.Zone}\nShowing teleport point instead: {backupLocation.Zone}");
            else
                ImGui.TextUnformatted($"{item.Shop.Location.Zone}");
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        if (CurrencyChrome.SmallButton($"tp-{item.Id}-{item.ShopId}-{item.Shop.NpcId}", "TP"))
        {
            item.Shop.Location.Teleport();
            if(item.Shop.Location.NeedsPresence && AgentMap.Instance()->CurrentTerritoryId != item.Shop.Location.TerritoryId)
                Service.GameGui.OpenMapWithMapLink(backupLocation.GetMapMarker());
            else
                Service.GameGui.OpenMapWithMapLink(item.Shop.Location.GetMapMarker());
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip(); // Fork: plain text; the table cell helpers would draw nothing in a tooltip window
            if(item.Shop.Location.NeedsPresence && AgentMap.Instance()->CurrentTerritoryId != item.Shop.Location.TerritoryId)
                ImGui.TextUnformatted($"You need to travel there first manually.\nWill teleport to nearest Aetheryte instead.");
            else
                ImGui.TextUnformatted($"{item.Shop.Location.Zone}");
            ImGui.EndTooltip();
        }
        CurrencyChrome.MapButtonsEnd(slotTop); // Fork: pairs with MapButtonsBegin
    }
    public static void Notification(string content, NotificationType type = NotificationType.Info, bool minimized = true)
    {
        Service.Notification.AddNotification(new Notification { Content = content, Type = type, Minimized = minimized });
    }
    public static void LinkItem(uint id)
    {
        Item item = Service.DataManager.GetExcelSheet<Item>().GetRow(id);
        var payloadList = new List<Payload> {
                new UIForegroundPayload((ushort) (0x223 + item.Rarity * 2)),
                new UIGlowPayload((ushort) (0x224 + item.Rarity * 2)),
                new ItemPayload(item.RowId, item.CanBeHq),
                new UIForegroundPayload(500),
                new UIGlowPayload(501),
                new TextPayload($"{(char) SeIconChar.LinkMarker}"),
                new UIForegroundPayload(0),
                new UIGlowPayload(0),
                new TextPayload(item.Name.ExtractText()),
                new RawPayload(new byte[] {0x02, 0x27, 0x07, 0xCF, 0x01, 0x01, 0x01, 0xFF, 0x01, 0x03}),
                new RawPayload(new byte[] {0x02, 0x13, 0x02, 0xEC, 0x03})
            };

        var payload = new SeString(payloadList);

        Service.Chat.Print(new XivChatEntry
        {
            Message = payload
        });
    }
}
