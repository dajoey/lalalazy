using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Config;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lalalazy.Hub;

namespace GluttonyCombo.Native;

/// <summary>
///     Fork (lalalazy hub, 2026-10): lets the hub window put one of Gluttony's custom action buttons on a hotbar
///     slot while Gluttony's own window is closed. Kept in its own fork-owned file; the Custom Actions tab keeps its
///     own (unchanged) overlay, so today's drag behaves exactly as before.
///
///     Interaction: a hub button calls <see cref="Begin"/>; the button's icon then follows the cursor; the NEXT
///     click on a hovered hotbar slot places it, a click anywhere else, Escape, logging out, or 60 seconds cancel.
///     It is click-to-pick then click-to-drop on purpose: a native mouse press could swallow the hotbar's hover
///     events, and with the button already released that cannot happen.
///
///     The overlay window, its flags, offset and scale, the slot write and WriteSavedSlot are copied from the tab
///     (hover delivery to the game depends on them). One state machine: the hotbar's "show empty slots" setting is
///     captured once on pick-up and restored exactly once on drop, cancel or dispose.
/// </summary>
internal sealed unsafe class CustomActionDragService : IDisposable
{
    private const UiControlOption HotbarSetting = UiControlOption.HotbarEmptyVisible;
    private const long TimeoutMs = 60_000;
    private const long IgnoreClicksMs = 250;

    private CustomAction? _selected;
    private uint _hiddenSlots;
    private long _startedAt;

    public CustomActionDragService()
    {
        Svc.PluginInterface.UiBuilder.Draw += Update;
    }

    public bool Active => _selected != null;

    public SetOutcome Begin(uint actionId)
    {
        try
        {
            if (!Player.Available) return SetOutcome.Refuse("Log in first.");
            if (Active) return SetOutcome.Refuse("A button is already picked up: click a hotbar slot, or press Escape.");

            var manager = P?.CustomActions?.Manager;
            if (manager == null) return SetOutcome.Refuse("Custom action buttons are not ready.");

            var act = manager.Actions.FirstOrDefault(a => a.Id == actionId);
            if (act == null) return SetOutcome.Refuse("That button does not exist.");
            if (!manager.IconTextures.ContainsKey(act.IconId)) return SetOutcome.Refuse("The button's icon is not loaded yet.");

            Svc.GameConfig.TryGet(HotbarSetting, out _hiddenSlots);
            Svc.GameConfig.Set(HotbarSetting, 1);   // show empty slots so there is something to drop onto
            _startedAt = Environment.TickCount64;
            _selected = act;
            Svc.Log.Information("[LalaHub] hotbar button picked up: {Name}", act.Name);
            return SetOutcome.Success;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[LalaHub] could not pick up a hotbar button");
            Cancel();
            return SetOutcome.Refuse("Could not pick up the button.");
        }
    }

    public void Cancel()
    {
        if (_selected == null) return;
        _selected = null;
        try { Svc.GameConfig.Set(HotbarSetting, _hiddenSlots); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[LalaHub] could not restore the hotbar setting"); }
    }

    private void Update()
    {
        var act = _selected;
        if (act == null) return;

        try
        {
            var age = Environment.TickCount64 - _startedAt;
            if (!Player.Available || age > TimeoutMs || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                Svc.Log.Information("[LalaHub] hotbar button drag cancelled");
                Cancel();
                return;
            }

            if (P?.CustomActions?.Manager.IconTextures.TryGetValue(act.IconId, out var icon) != true
                || icon == null || !icon.TryGetWrap(out var texture, out _))
            {
                Cancel();
                return;
            }

            var mousePos = ImGui.GetMousePos();
            mousePos.X -= 5;
            mousePos.Y -= 5;
            ImGui.SetNextWindowPos(mousePos);
            ImGui.SetNextWindowBgAlpha(0.5f);
            ImGui.Begin($"{act.Name}DragDrops", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize);
            try { ImGui.Image(texture.Handle, new Vector2(50f.Scale())); }
            finally { ImGui.End(); }

            // The click that picked the button up already happened; ignore the first moments anyway.
            if (age > IgnoreClicksMs && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                if (P.CustomActions.HoveredSlot is { } slot)
                {
                    var hotbar = RaptureHotbarModule.Instance();
                    hotbar->Hotbars[slot.Hotbar].Slots[slot.Slot].Set(RaptureHotbarModule.HotbarSlotType.Action, act.Id);
                    var actualSlot = hotbar->Hotbars[slot.Hotbar].Slots[slot.Slot];
                    hotbar->WriteSavedSlot(Player.ClassJob.RowId, (uint)slot.Hotbar, (uint)slot.Slot, &actualSlot, false, false);
                    Svc.Log.Information("[LalaHub] hotbar button placed: {Name} on hotbar {Hotbar} slot {Slot}", act.Name, slot.Hotbar, slot.Slot);
                }
                else
                {
                    Svc.Log.Information("[LalaHub] hotbar button drag cancelled (no hotbar slot under the cursor)");
                }
                Cancel();
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[LalaHub] hotbar button drag failed");
            Cancel();
        }
    }

    public void Dispose()
    {
        Svc.PluginInterface.UiBuilder.Draw -= Update;
        Cancel();
    }
}
