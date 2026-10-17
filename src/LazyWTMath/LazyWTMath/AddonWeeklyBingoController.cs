using System.Linq;
using System.Numerics;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Classes.Controllers;
using KamiToolKit.Extensions;
using KamiToolKit.Nodes;

namespace WondrousTailsSolver;

public unsafe class AddonWeeklyBingoController : AddonController<AddonWeeklyBingo> {
    private TextNode? probabilityTextNode;

    // Fork: cache of the inputs the overlay text was last rendered from; -1 forces a full pass (OnUpdate fires every frame, and the prune + solve only need to run when these change).
    private int lastRenderedBoardMask = -1;
    private int lastRenderedStickersPlaced = -1;

    public AddonWeeklyBingoController(IDalamudPluginInterface pluginInterface) : base("WeeklyBingo") {
        KamiToolKitLibrary.Initialize(pluginInterface);
        OnAttach += AttachNodes;
        OnRefresh += AddonRefresh;
        OnUpdate += AddonRefresh;
        OnDetach += DetachNodes;
        Enable();
    }

    private void AttachNodes(AddonWeeklyBingo* addon) {
        // Fork: freshly attached nodes need one full refresh pass (text prune + probability solve) even when the cached inputs have not changed.
        this.lastRenderedBoardMask = -1;
        this.lastRenderedStickersPlaced = -1;

        var existingTextNode = addon->GetTextNodeById(34);
        if (existingTextNode is null) return;
        
        
        // Shrink existing node, the game doesn't need that space anyway.
        existingTextNode->SetHeight((ushort)(existingTextNode->GetHeight() * 2.0f / 3.0f));

        // Add new custom text node to ui
        probabilityTextNode = new TextNode {
            NodeFlags = NodeFlags.Enabled | NodeFlags.Visible,
            Size = new Vector2(existingTextNode->GetWidth(), existingTextNode->GetHeight()),
            Position = new Vector2(existingTextNode->GetXFloat(), existingTextNode->GetYFloat() + existingTextNode->GetHeight()),
            TextColor = existingTextNode->TextColor.ToVector4(),
            TextOutlineColor = existingTextNode->EdgeColor.ToVector4(),
            BackgroundColor = existingTextNode->BackgroundColor.ToVector4(),
            FontSize = existingTextNode->FontSize,
            LineSpacing = existingTextNode->LineSpacing,
            CharSpacing = existingTextNode->CharSpacing,
            TextFlags = TextFlags.MultiLine | (TextFlags)existingTextNode->TextFlags,
            String = System.PerfectTails.SolveAndGetProbabilitySeString().TextValue,
        };

        probabilityTextNode.AttachNode((AtkResNode*)existingTextNode, NodePosition.AfterTarget);
    }
    
    private void AddonRefresh(AddonWeeklyBingo* addon) {
        foreach (var index in Enumerable.Range(0, 16)) {
            System.PerfectTails.GameState[index] = PlayerState.Instance()->IsWeeklyBingoStickerPlaced(index);
        }

        // Fork: skip the SeString prune and probability solve while both inputs they depend on are unchanged; the overlay text is a pure function of the board mask and the placed-sticker count.
        var boardMask = 0;
        for (var index = 0; index < 16; index++) {
            if (System.PerfectTails.GameState[index])
                boardMask |= 1 << index;
        }

        var stickersPlaced = PlayerState.Instance()->WeeklyBingoNumPlacedStickers;
        if (boardMask == this.lastRenderedBoardMask && stickersPlaced == this.lastRenderedStickersPlaced)
            return;

        if (probabilityTextNode is not null) {
            var existingTextNode = addon->GetTextNodeById(34);
            if (existingTextNode is null) return;
            var nodeText = SeString.Parse(existingTextNode->NodeText);

            var lineBreakIndex = -1;
            for (var index = 0; index < nodeText.Payloads.Count; index++)
            {
                if (index > 0)
                {
                    var previousPayload = nodeText.Payloads[index - 1];
                    var payload = nodeText.Payloads[index];

                    if (previousPayload.Type is PayloadType.NewLine && payload.Type is PayloadType.NewLine)
                    {
                        lineBreakIndex = index - 1;
                        break;
                    }
                }
            }

            if (lineBreakIndex is not -1)
            {
                var newString = new SeStringBuilder();

                for (var index = 0; index < lineBreakIndex; index++)
                {
                    newString.Add(nodeText.Payloads[index]);
                }
                existingTextNode->SetText(newString.Encode());
            }

            probabilityTextNode.String = System.PerfectTails.SolveAndGetProbabilitySeString().TextValue;

            // Fork: record the inputs only after the text actually updated, so a pass skipped for a missing text node never suppresses the next real refresh.
            this.lastRenderedBoardMask = boardMask;
            this.lastRenderedStickersPlaced = stickersPlaced;
        }
    }
    
    private void DetachNodes(AddonWeeklyBingo* addon) {
        var existingTextNode = addon->GetTextNodeById(34);
        if (existingTextNode is not null) {
            existingTextNode->SetHeight((ushort)(existingTextNode->GetHeight() * 3.0f / 2.0f));
        }

        probabilityTextNode?.Dispose();
        probabilityTextNode = null;
    }
}