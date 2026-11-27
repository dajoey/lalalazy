using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace AutoPotion;

/// <summary>Dalamud/game boundary for <see cref="ChocoboControl"/>.</summary>
internal sealed unsafe class ChocoboService
{
    private static readonly TimeSpan CommandInterval = TimeSpan.FromSeconds(1);

    private readonly Plugin _plugin;
    private readonly ChocoboControl _control = new();
    private DateTime _nextCommand = DateTime.MinValue;
    private int _attackerCount;
    private ChocoboStance _currentStance;

    public ChocoboService(Plugin plugin) => _plugin = plugin;

    public void Tick()
    {
        var local = Plugin.Objects.LocalPlayer;
        var livingPlayer = local != null && !local.IsDead && local.MaxHp > 0;
        var companionPresent = livingPlayer && Plugin.Buddies.CompanionBuddy?.GameObject != null;
        _currentStance = ReadStance();
        _attackerCount = livingPlayer ? CountAttackers(local!.GameObjectId) : 0;

        var wanted = _control.Update(
            enabled: _plugin.Config.MasterEnable && _plugin.Config.ChocoboControl,
            companionPresent: companionPresent,
            livingPlayer: livingPlayer,
            attackerCount: _attackerCount,
            fullyHealed: local != null && ChocoboControl.IsFullyHealed(local.CurrentHp, local.MaxHp),
            currentStance: _currentStance);

        if (wanted == ChocoboStance.None || DateTime.UtcNow < _nextCommand)
            return;

        var stanceName = wanted switch
        {
            ChocoboStance.Free => "Free Stance",
            ChocoboStance.Defender => "Defender Stance",
            ChocoboStance.Attacker => "Attacker Stance",
            ChocoboStance.Healer => "Healer Stance",
            _ => null,
        };
        if (stanceName == null)
            return;

        // /cac is a built-in game command, not a Dalamud plugin command. Submit only
        // these fixed stance strings through the same UI path as the chat box.
        _nextCommand = DateTime.UtcNow + CommandInterval;
        SendStanceCommand(stanceName);
        Plugin.Log.Information(
            $"AutoPotion: requested chocobo {stanceName} (attackers={_attackerCount}, previous={_control.PreviousStance})");
    }

    private static void SendStanceCommand(string stanceName)
    {
        var bytes = Encoding.UTF8.GetBytes($"/cac \"{stanceName}\"");
        var message = Utf8String.FromSequence(bytes);
        try
        {
            UIModule.Instance()->ProcessChatBoxEntry(message);
        }
        finally
        {
            message->Dtor(true);
        }
    }

    private static int CountAttackers(ulong playerId)
    {
        var count = 0;
        foreach (var obj in Plugin.Objects)
        {
            if (obj is not IBattleNpc npc)
                continue;
            if (ChocoboControl.CountsAsAttacker(
                    obj.IsTargetable,
                    npc.BattleNpcKind == BattleNpcSubKind.Combatant,
                    obj.TargetObjectId == playerId))
                count++;
        }
        return count;
    }

    private static ChocoboStance ReadStance()
    {
        var ui = UIState.Instance();
        return ui == null ? ChocoboStance.None : (ChocoboStance)ui->Buddy.CompanionInfo.ActiveCommand;
    }

    public string StatusLine =>
        $"Chocobo: master={_plugin.Config.MasterEnable}; feature={_plugin.Config.ChocoboControl}; " +
        $"stance={_currentStance}; attackers={_attackerCount}; " +
        $"engaged={_control.IsEngaged}; previous={_control.PreviousStance}";
}
