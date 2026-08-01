using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace LazyOccultCrescent.Modules.Data;

public unsafe class Enemy(IGameObject obj)
{
    private BattleChara* _battleChar = null;

    private BattleChara* BattleChara
    {
        get
        {
            if (_battleChar == null)
            {
                _battleChar = (BattleChara*)obj.Address;
            }

            return _battleChar;
        }
    }

    public uint LayoutId
    {
        get => BattleChara->LayoutId;
    }

    public uint Rank
    {
        get => BattleChara->ForayInfo.Level;
    }

    public string Name
    {
        get => obj.Name.ToString();
    }

    public Position Position
    {
        get => Position.Create(obj.Position);
    }
}
