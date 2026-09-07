using System;
using System.Linq;
using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace LazyMarketCompanion;

public static class Communicator
{
  private const string Prefix = "[LMC] ";
  private static readonly ExcelSheet<Item> ItemSheet = Svc.Data.GetExcelSheet<Item>();

  public static void PrintInfo(string message)
  {
    Svc.Chat.Print(Prefix + message);
  }

  public static void PrintPriceUpdate(string itemName, int? oldPrice, int? newPrice, float cutPercentage, bool priceFromUniversalis = false)
  {
    if (!Plugin.Configuration.ShowPriceAdjustmentsMessages)
      return;

    if (oldPrice == null || newPrice == null || oldPrice.Value == newPrice.Value)
      return;

    var dec = oldPrice.Value > newPrice.Value ? "cut" : "increase";
    var sourceText = priceFromUniversalis ? " (from Universalis data center)" : string.Empty;
    var itemPayload = RawItemNameToItemPayload(itemName);

    if (itemPayload != null)
    {
      Svc.Chat.Print(new SeStringBuilder()
          .AddText(Prefix)
          .AddItemLink(itemPayload.ItemId, itemPayload.IsHQ)
          .AddText($": matching from {oldPrice.Value:N0} to {newPrice.Value:N0} gil{sourceText}, a {dec} of {MathF.Abs(MathF.Round(cutPercentage, 2))}%")
          .Build());
    }
    else
      Svc.Chat.Print($"{Prefix}{itemName}: matching from {oldPrice.Value:N0} to {newPrice.Value:N0}{sourceText}, a {dec} of {MathF.Abs(MathF.Round(cutPercentage, 2))}%");
  }

  public static void PrintNewListingPriced(string itemName, int price, bool priceFromUniversalis)
  {
    if (!Plugin.Configuration.ShowAutoMarketMessages)
      return;

    var sourceText = priceFromUniversalis ? " (Universalis)" : string.Empty;
    var itemPayload = RawItemNameToItemPayload(itemName);
    if (itemPayload != null)
    {
      Svc.Chat.Print(new SeStringBuilder()
          .AddText(Prefix)
          .AddItemLink(itemPayload.ItemId, itemPayload.IsHQ)
          .AddText($": new listing priced at {price:N0} gil{sourceText}")
          .Build());
    }
    else
      Svc.Chat.Print($"{Prefix}{itemName}: new listing priced at {price:N0} gil{sourceText}");
  }

  public static void PrintListed(uint itemId, bool hq, int quantity)
  {
    if (!Plugin.Configuration.ShowAutoMarketMessages)
      return;

    Svc.Chat.Print(new SeStringBuilder()
        .AddText(Prefix + "listed ")
        .AddItemLink(itemId, hq)
        .AddText($" x{quantity}")
        .Build());
  }

  public static void PrintSweepDone(int listed, int failures, int vendored = 0, int heldBack = 0)
  {
    if (!Plugin.Configuration.ShowAutoMarketMessages && listed == 0 && failures == 0 && vendored == 0 && heldBack == 0)
      return;

    var text = listed == 0 && failures == 0 && vendored == 0 && heldBack == 0
      ? "done."
      : $"done: {listed} new listing(s){(failures > 0 ? $", {failures} skipped (stock moved)" : string.Empty)}{(vendored > 0 ? $", {vendored} vendored" : string.Empty)}{(heldBack > 0 ? $", {heldBack} held back by the value gate" : string.Empty)}.";
    Svc.Chat.Print(Prefix + text);
  }

  private static ItemPayload? RawItemNameToItemPayload(string itemName)
  {
    var seString = SeString.Parse(Encoding.UTF8.GetBytes(itemName));
    var textPayloads = seString.Payloads.OfType<TextPayload>().ToList();
    if (textPayloads.Count == 0)
      return null;

    var cleanedName = "";
    var isHq = false;

    if (textPayloads.Count == 1)
    {
      cleanedName = textPayloads[0].Text?.Trim() ?? string.Empty;
    }
    else
    {
      var nameParts = new StringBuilder();
      for (int i = 1; i < textPayloads.Count; i++)
      {
        var text = textPayloads[i].Text;
        if (i == 1 && text?.Length >= 2 && text[1] == '\u0003')
          text = text[2..];
        nameParts.Append(text);
      }

      cleanedName = nameParts.ToString();
      if (cleanedName.Length >= 1 && cleanedName[^1] == '\uE03C')
      {
        isHq = true;
        cleanedName = cleanedName[..^1].TrimEnd();
      }
      else
        cleanedName = cleanedName.TrimEnd();
    }

    var item = ItemSheet.FirstOrDefault(i => i.Name.ToString().Equals(cleanedName, StringComparison.OrdinalIgnoreCase));
    return item.RowId > 0 ? new ItemPayload(item.RowId, isHq) : null;
  }

  public static void PrintAboveMaxCutError(string itemName)
  {
    if (!Plugin.Configuration.ShowErrorsInChat)
      return;

    var itemPayload = RawItemNameToItemPayload(itemName);
    if (itemPayload != null)
    {
      Svc.Chat.PrintError(new SeStringBuilder()
          .AddText(Prefix)
          .AddItemLink(itemPayload.ItemId, itemPayload.IsHQ)
          .AddText($": ignored because it would cut the price by more than {Plugin.Configuration.MaxUndercutPercentage}%")
          .Build());
    }
    else
      Svc.Chat.PrintError($"{Prefix}{itemName}: ignored because it would cut the price by more than {Plugin.Configuration.MaxUndercutPercentage}%");
  }

  public static void PrintRetainerName(string name)
  {
    if (!Plugin.Configuration.ShowRetainerNames)
      return;

    Svc.Chat.Print(new SeStringBuilder()
        .AddText(Prefix + "retainer ")
        .AddUiForeground(name, 561)
        .Build());
  }

  public static void PrintNoPriceToSetError(string itemName, bool placeholderListing = false, string? universalisFailureReason = null)
  {
    if (!Plugin.Configuration.ShowErrorsInChat)
      return;

    // Universalis normally prices an empty board from its recent sales (0.1.8.0), so reaching this
    // message means the fallback itself could not answer - say why, so "set it manually" only ever
    // means Universalis had nothing usable, not that the plugin declined to look.
    var suffix = placeholderListing
      ? ": no board price found - the new listing is still at the placeholder price, set it manually"
      : ": no price to set, please set price manually";
    if (!string.IsNullOrEmpty(universalisFailureReason))
      suffix += $" ({universalisFailureReason})";
    var itemPayload = RawItemNameToItemPayload(itemName);
    if (itemPayload != null)
    {
      Svc.Chat.PrintError(new SeStringBuilder()
          .AddText(Prefix)
          .AddItemLink(itemPayload.ItemId, itemPayload.IsHQ)
          .AddText(suffix)
          .Build());
    }
    else
      Svc.Chat.PrintError($"{Prefix}{itemName}{suffix}");
  }

  public static void PrintUsingDefaultAmountWarning(string itemName, int amount)
  {
    if (!Plugin.Configuration.ShowErrorsInChat)
      return;

    var itemPayload = RawItemNameToItemPayload(itemName);
    if (itemPayload != null)
    {
      Svc.Chat.PrintError(new SeStringBuilder()
          .AddText(Prefix)
          .AddItemLink(itemPayload.ItemId, itemPayload.IsHQ)
          .AddText($": using default amount {amount}")
          .Build());
    }
    else
      Svc.Chat.PrintError($"{Prefix}{itemName}: using default amount {amount}");
  }

  public static void PrintAllRetainersDisabled()
  {
    Svc.Chat.PrintError(new SeStringBuilder()
        .AddText(Prefix + "All retainers are disabled. Open configuration with ")
        .Add(Plugin.ConfigLinkPayload)
        .AddUiForeground("/lmc", 31)
        .Build());
  }
}
