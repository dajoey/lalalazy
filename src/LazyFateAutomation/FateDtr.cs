using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace LazyFateAutomation;

/// <summary>
///     The "Lazy Fate Automation" server info bar (DTR) entry: the FATE bot's on/off
///     switch in the game's bar, which Umbra's DtrBar widget renders as a button. Same
///     shape as the GluttonyCombo sibling entries: registered once (nullable — a title
///     still held by a previous load is skipped rather than failing the load), text and
///     visibility refreshed per framework tick, removed in Dispose (Dalamud's own
///     per-plugin cleanup stops after the first entry, so every entry this load owns is
///     removed here). Click behavior is FateDtrLogic's decision: a click stops a running
///     bot (hard stop, vnavmesh included, like the window's Stop button); when stopped,
///     the first click arms the start and a second click within the confirm window starts
///     it. The entry hides with the "Show server info bar entry" setting (default on).
/// </summary>
internal sealed class FateDtr : IDisposable {
    public const string EntryTitle = "Lazy Fate Automation";

    private readonly FateToolKit _toolkit;
    private readonly IDtrBarEntry? _entry;
    private long _armedAtMs;
    private bool _confirmArmed;
    private bool _warnedOnce;

    public FateDtr(FateToolKit toolkit) {
        _toolkit = toolkit;
        _entry = TryGetEntry();
        if (_entry is not null) {
            _entry.OnClick += OnClick;
            _entry.Tooltip = new SeString(
                new TextPayload("Click to stop the FATE bot; Ctrl+click stops after the current FATE, once out of combat.\n"),
                new TextPayload("When stopped, click to arm the start, then click again to confirm.\n"),
                new TextPayload("Disable this icon in /xlsettings -> Server Info Bar"));
        }
    }

    /// <summary>
    ///     Server info bar entry, or null when the title is still held by a previous load
    ///     of this plugin. A missing entry must never fail the plugin load; it returns on
    ///     the next clean load (same handling as the GluttonyCombo entries).
    /// </summary>
    private static IDtrBarEntry? TryGetEntry() {
        try {
            return Svc.DtrBar.Get(EntryTitle);
        }
        catch (ArgumentException ex) {
            Svc.Log.PrintWarning($"Server info bar entry \"{EntryTitle}\" is still held by a previous load; it is skipped until the next clean load. ({ex.Message})");
            return null;
        }
    }

    /// <summary>Framework tick: expire an unconfirmed start, then repaint text and visibility.</summary>
    public void Update() {
        try {
            if (_entry is null)
                return;
            // The window repairs a Running flag whose task has ended on every draw; with
            // the window closed (and the hub closed) nothing else does, so do it here too.
            _toolkit.SyncRunningState();
            if (_confirmArmed && !FateDtrLogic.IsConfirmLive(true, _armedAtMs, Environment.TickCount64))
                _confirmArmed = false;
            _entry.Shown = Plugin.Config.ShowServerBarEntry;
            var running = _toolkit.Running && Service.Automation.Running;
            _entry.Text = new SeString(
                new IconPayload(FateDtrLogic.SwordUnsheathed(running) ? BitmapFontIcon.SwordUnsheathed : BitmapFontIcon.SwordSheathed),
                new TextPayload(FateDtrLogic.EntryText(running, _toolkit.PendingStopWhenSafe, _confirmArmed, _toolkit.CurrentState)));
        }
        catch (Exception ex) {
            // A per-frame repaint must not spam the log: warn once, then stay silent.
            if (_warnedOnce)
                return;
            _warnedOnce = true;
            Svc.Log.PrintWarning($"Server info bar entry update failed once; staying silent about further failures. ({ex.Message})");
        }
    }

    private void OnClick(DtrInteractionEvent args) {
        var running = _toolkit.Running && Service.Automation.Running;
        var action = FateDtrLogic.DecideClick(running, _confirmArmed, ECommons.GameHelpers.Player.Available,
            args.ModifierKeys.HasFlag(ClickModifierKeys.Ctrl));
        switch (action) {
            case FateDtrLogic.ClickAction.Stop:
                _confirmArmed = false;
                _toolkit.ToggleRunning();
                // The bot is already stopped at this point; a missing vnavmesh must not
                // turn that into a reported failure (same handling as the hub switch).
                try { Service.Navmesh.Stop(); } catch { }
                break;

            case FateDtrLogic.ClickAction.StopWhenSafe:
                _toolkit.PendingStopWhenSafe = true;
                break;

            case FateDtrLogic.ClickAction.ArmStart:
                _confirmArmed = true;
                _armedAtMs = Environment.TickCount64;
                break;

            case FateDtrLogic.ClickAction.StartNow:
                _confirmArmed = false;
                _toolkit.ToggleRunning();
                break;
        }
    }

    public void Dispose() {
        if (_entry is null)
            return;
        _entry.OnClick -= OnClick;
        _entry.Remove();
    }
}
