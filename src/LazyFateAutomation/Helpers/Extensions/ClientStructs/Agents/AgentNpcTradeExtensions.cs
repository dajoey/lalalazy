using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;

namespace LazyFateAutomation.Helpers.Extensions;

public static unsafe class AgentNpcTradeExtensions {
    extension(AgentNpcTrade) {
        public static bool IsTurnInRequestInProgress(uint itemId)
            => AgentNpcTrade.Instance()->IsAgentActive() && UIState.Instance()->NpcTrade.Requests.Count == 1 && UIState.Instance()->NpcTrade.Requests.Items[0].ItemId == itemId;

        public static void TurnInRequests() {
            var agent = AgentNpcTrade.Instance();
            if (!agent->IsAgentActive()) {
                Svc.Log.PrintError("Agent not active...");
                return;
            }

            if (agent->SelectedTurnInSlot >= 0) {
                Svc.Log.PrintError($"Turn-in already in progress for slot {agent->SelectedTurnInSlot}");
                return;
            }

            Span<AtkValue> param = stackalloc AtkValue[4];
            param[0].SetInt(2); // start turnin
            param[2].SetInt(0); // ???
            param[3].SetInt(0); // ???
            var res = new AtkValue();
            for (var i = 0; i < UIState.Instance()->NpcTrade.Requests.Count; i++) {
                param[1].SetInt(i); // slot
                agent->ReceiveEvent(&res, param.GetPointer(0), 4, 0);
            }
            //var res = new AtkValue();
            //Span<AtkValue> param = stackalloc AtkValue[4];
            //param[0].SetInt(2); // start turnin
            //param[1].SetInt(0); // slot
            //param[2].SetInt(0); // ???
            //param[3].SetInt(0); // ???
            //agent->ReceiveEvent(&res, param.GetPointer(0), 4, 0);

            if (agent->SelectedTurnInSlot != 0 || agent->SelectedTurnInSlotItemOptions <= 0) {
                Svc.Log.PrintError($"Failed to start turn-in: cur slot={agent->SelectedTurnInSlot}, count={agent->SelectedTurnInSlotItemOptions}");
                return;
            }

            param[0].SetInt(0); // confirm
            param[1].SetInt(0); // option #0
            agent->ReceiveEvent(&res, param.GetPointer(0), 4, 1);

            if (agent->SelectedTurnInSlot >= 0) {
                Svc.Log.PrintError($"Turn-in not confirmed: cur slot={agent->SelectedTurnInSlot}");
                return;
            }

            // commit
            var addonId = agent->AddonId;
            agent->ReceiveEvent(&res, param.GetPointer(0), 4, 0);
            var addon = RaptureAtkUnitManager.Instance()->GetAddonById((ushort)addonId);
            if (addon != null && addon->IsVisible)
                addon->Close(false);
        }
    }
}
