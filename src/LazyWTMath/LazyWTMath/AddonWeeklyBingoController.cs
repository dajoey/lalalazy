using System;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Controllers;
using KamiToolKit.Enums;
using KamiToolKit.Extensions;
using KamiToolKit.Nodes;

namespace WondrousTailsSolver;

/// <summary>
/// Bolt-on controller for the WeeklyBingo addon. Tip KamiToolKit turned AddonController's events into
/// init-only delegates and asserts the main game thread in Enable/Disable, so this is a thin wrapper
/// composing one AddonController instead of subclassing it; enable and teardown run on the framework thread.
/// </summary>
public unsafe class AddonWeeklyBingoController : IDisposable {
    private TextNode? probabilityTextNode;
    // Fork: remembers the inputs the overlay text was last rendered from (OnUpdate fires every frame; the prune and solve only need to run when they change).
    private readonly RefreshGate refreshGate = new();
    private readonly AddonController<AddonWeeklyBingo> controller;

    public AddonWeeklyBingoController() {
        controller = new AddonController<AddonWeeklyBingo> {
            AddonName = "WeeklyBingo",
            OnSetup = AttachNodes,
            OnRefresh = AddonRefresh,
            OnUpdate = AddonRefresh,
            OnFinalize = DetachNodes,
        };

        // EnableAsync registers the lifecycle listeners now and runs the first OnSetup on the
        // framework thread; the sync Enable() asserts the main thread, which the plugin load
        // thread is not.
        _ = controller.EnableAsync();
    }

    public void Dispose()
        // AddonController.Dispose disables on the framework thread (it asserts the main thread);
        // the plugin unloads on a threadpool thread.
        => WondrousTailsSolverPlugin.Framework.RunOnFrameworkThread(controller.Dispose);

    private void AttachNodes(AddonWeeklyBingo* addon) {
        // Fork: freshly attached nodes need one full refresh pass even when the cached inputs have not changed.
        refreshGate.Reset();

        var existingTextNode = addon->GetTextNodeById(34);
        if (existingTextNode is null) return;
        
        
        // Shrink existing node, the game doesn't need that space anyway.
        existingTextNode->SetHeight((ushort)(existingTextNode->GetHeight() * 2.0f / 3.0f));

        // Add new custom text node to ui
        probabilityTextNode = new TextNode {
            NodeFlags = NodeFlags.Enabled | NodeFlags.Visible,
            Size = new Vector2(existingTextNode->GetWidth(), existingTextNode->GetHeight()),
            Position = new Vector2(existingTextNode->GetXFloat(), existingTextNode->GetYFloat() + existingTextNode->GetHeight()),
            // Fork: Match the refreshed UI's primary text instead of inheriting the journal's dim label colour.
            TextColor = LazyWTMathOverlayStyle.TextColor,
            // Fork: Keep refreshed probabilities legible over the native journal surface.
            TextOutlineColor = LazyWTMathOverlayStyle.OutlineColor,
            FontSize = existingTextNode->FontSize,
            LineSpacing = existingTextNode->LineSpacing,
            CharSpacing = existingTextNode->CharSpacing,
            TextFlags = TextFlags.MultiLine | (TextFlags)existingTextNode->TextFlags,
            String = System.PerfectTails.SolveAndGetProbabilitySeString().TextValue,
        };

        // Fork: the api16 KamiToolKit refactor moved BackgroundColor off TextNode (it now lives
        // on BackgroundTextNode, a centered-only composite); set the journal-matched background
        // through the native node, exactly what the old managed setter did.
        probabilityTextNode.Node->BackgroundColor = existingTextNode->BackgroundColor;
        probabilityTextNode.AttachNode((AtkResNode*)existingTextNode, NodePosition.AfterTarget);
    }
    
    private void AddonRefresh(AddonWeeklyBingo* addon) {
        // Fork: PlayerState is null while logging in or out with the journal open; reading through a null native pointer is a hard crash, so bail out.
        var playerState = PlayerState.Instance();
        if (playerState == null) return;

        foreach (var index in Enumerable.Range(0, 16)) {
            // Fork: read through the null-checked pointer above instead of calling PlayerState.Instance() again.
            System.PerfectTails.GameState[index] = playerState->IsWeeklyBingoStickerPlaced(index);
        }

        // Fork: skip the SeString prune and the probability solve while the board and the placed-sticker count are unchanged.
        var boardMask = 0;
        for (var index = 0; index < 16; index++) {
            if (System.PerfectTails.GameState[index]) boardMask |= 1 << index;
        }

        int stickersPlaced = playerState->WeeklyBingoNumPlacedStickers;
        if (!refreshGate.ShouldRefresh(boardMask, stickersPlaced)) return;

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
            // Fork: record the inputs only after the text was actually updated, so a pass that returned early for a missing text node never suppresses the next real refresh.
            refreshGate.Commit(boardMask, stickersPlaced);
        }
    }
    
    private void DetachNodes(AddonWeeklyBingo* addon) {
        // Fork: the overlay node is going away; the next attach starts with a full pass.
        refreshGate.Reset();

        var existingTextNode = addon->GetTextNodeById(34);
        if (existingTextNode is not null) {
            existingTextNode->SetHeight((ushort)(existingTextNode->GetHeight() * 3.0f / 2.0f));
        }

        probabilityTextNode?.Dispose();
        probabilityTextNode = null;
    }
}
