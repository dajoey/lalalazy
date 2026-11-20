using ECommons.GameHelpers;
using Lumina.Excel.Sheets;

namespace RotationSolver.Basic.Actions;

internal class HpPotionItem : BaseItem
{
	private readonly float _percent;
	private readonly uint _maxHp;

	public uint MaxHp => !Player.Available || Player.Object == null ? 0 : Math.Min((uint)(Player.Object.MaxHp * _percent), _maxHp);

	protected override bool CanUseThis => Service.Config.UseHpPotions;

	public HpPotionItem(Item item) : base(item)
	{
		var action = _item.ItemAction.Value;
		Lumina.Excel.Collection<ushort> data = action.DataHQ[0] > 0 || action.DataHQ[1] > 0 ? action.DataHQ : action.Data;
		_percent = data[0] / 100f;
		_maxHp = data[1];
	}

	public override bool CanUse(out IAction item, bool clippingCheck)
	{
		item = this;

		if (!Player.Available || Player.Object == null || Player.Object.CurrentHp == 0)
		{
			return false;
		}

		if (ObjectHelper.GetPlayerHealthRatio() > Service.Config.UseHpPotionsPercent)
		{
			return false;
		}

		if (Player.Object.MaxHp - Player.Object.CurrentHp < MaxHp)
		{
			return false;
		}

		return base.CanUse(out item, clippingCheck);
	}
}
