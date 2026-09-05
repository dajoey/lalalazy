using System;

namespace LazyMarketCompanion
{
  internal sealed class NewPriceEventArgs(int newPrice) : EventArgs
  {
    public int NewPrice { get; } = newPrice;
  }
}
