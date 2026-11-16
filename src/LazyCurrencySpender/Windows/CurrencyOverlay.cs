using CurrencySpender.Classes;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CurrencySpender.Windows;

internal unsafe class CurrencyOverlay : Window
{
    private float height;
    private bool MainIsOpen = false;

    public CurrencyOverlay() : base("Currency overlay", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings, true)
    {
        P.ws.AddWindow(this);
        IsOpen = true;
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Currency", OnCurrencyOpen);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "Currency", OnCurrencyClose);
    }

    private void OnCurrencyOpen(AddonEvent type, AddonArgs args)
    {
        if (C.OpenAutomatically && !MainIsOpen)
        {
            P.mainTabWindow.IsOpen = true;
            MainIsOpen = true;
            PluginLog.Debug("Auto-opened main window on Currency addon open.");
        }
    }

    private void OnCurrencyClose(AddonEvent type, AddonArgs args)
    {
        MainIsOpen = false;
        PluginLog.Debug("Currency addon closed — reset auto-open flag.");
    }

    public override bool DrawConditions()
    {
        return GenericHelpers.TryGetAddonByName<AtkUnitBase>("Currency", out var addon) &&
               GenericHelpers.IsAddonReady(addon);
    }

    public override void Draw()
    {
        CurrencyChrome.PushFrame();
        try
        {
            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("Currency", out var addon) && GenericHelpers.IsAddonReady(addon))
            {
                if (addon->X != 0 || addon->Y != 0)
                    Position = new(addon->X, addon->Y - height);
                var label = "Open Currency Spender";
                var size = new Vector2(ImGui.CalcTextSize(label).X + 34, 34);
                var origin = ImGui.GetCursorScreenPos();
                var draw = ImGui.GetWindowDrawList();
                if (CurrencyChrome.OutlineButton(draw, "overlay_open", label, origin, size, true))
                    P.mainTabWindow.IsOpen = true;
                var closePos = origin + new Vector2(size.X + (2 * LalaUiKit.Theme.Unit), 0);
                if (CurrencyChrome.OutlineButton(draw, "overlay_close", "×", closePos, new Vector2(30, size.Y)))
                    IsOpen = false;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Close");
                if (IsOpen && C.OpenAutomatically && !MainIsOpen)
                {
                    P.mainTabWindow.IsOpen = true;
                    MainIsOpen = true;
                }
            }
            else
            {
                MainIsOpen = false;
            }
            height = ImGui.GetWindowSize().Y;
        }
        finally
        {
            CurrencyChrome.PopFrame();
        }
    }

    public override void OnClose()
    {
        MainIsOpen = false;
        base.OnClose();
    }
}
