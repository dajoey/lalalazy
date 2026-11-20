using ECommons.GameHelpers;
using Lumina.Excel.Sheets;

namespace RotationSolver.Basic.Actions;

internal class MpPotionItem : BaseItem
{
	public uint MaxMp { get; }

	protected override bool CanUseThis => Service.Config.UseMpPotions;

	public MpPotionItem(Item item) : base(item)
	{
		var action = _item.ItemAction.Value;
		Lumina.Excel.Collection<ushort> data = action.DataHQ[1] > 0 ? action.DataHQ : action.Data;
		MaxMp = data[1];
	}

	public override bool CanUse(out IAction item, bool clippingCheck)
	{
		item = this;

		if (!Player.Available || Player.Object == null || Player.Object.CurrentHp == 0)
		{
			return false;
		}
		return ObjectHelper.GetPlayerMPRatio() <= Service.Config.UseMpPotionsPercent && Player.Object.MaxMp - DataCenter.CurrentMp >= MaxMp && base.CanUse(out item, clippingCheck);
	}
}
