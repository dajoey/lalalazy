using System;

namespace LazyMarketCompanion.AutoMarket;

// 0.1.12.0: the command parameter of the retainer item command. Values are the game's own callback
// params (FCS AgentInventoryContext.InventoryContextEvent: AgentRetainer - Have Retainer Sell Items = 5),
// mirrored by AutoRetainer's RetainerItemCommand enum and SimpleTweaks' Addon sheet row probe (5480).
public enum RetainerItemCommand : long
{
  RetrieveFromRetainer = 0,
  EntrustToRetainer = 1,
  RetrieveQuantity = 3,
  EntrustQuantity = 4,
  HaveRetainerSellItem = 5,
}
