using System.Diagnostics;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;
using Lalalazy.Hub;
using LazyHub.Core;

namespace LazyHub;

/// <summary>
/// The lalalazy hub window, built from the game's own UI nodes (KamiToolKit). Three pages in one window:
/// Quick (the Gluttony tray with its Auto-Rotation switch and hotbar buttons, then one master switch per plugin that
/// offers one), Plugins (all fifteen, with Settings and Open), and a plugin's settings page.
///
/// Built to stay safe in a live game:
/// - Only node types already proven in game (text, button, image) are used; panels are stretched solid-colour images.
/// - Every node is created once in <see cref="OnSetup"/> from a fixed pool and afterwards only re-bound (text, colour,
///   position, visibility). Nothing is created or disposed while the window is used, and a click only QUEUES its effect,
///   which runs on the next update, never inside the click callback.
/// - Hub IPC is called from the update only, about once a second, and only while this window is open.
/// - Nodes are rebuilt on every Open, so asynchronous icon loads check a generation counter before touching a node.
/// </summary>
internal sealed unsafe class HubAddon(PluginMonitor monitor, HubClient client, IconLoader icons, ITextureProvider textures, IPluginLog log) : NativeAddon
{
    private enum Page { Quick, Plugins, Detail }

    private enum RowKind { Hidden, Plugin, Master, PluginHeader, Group, Toggle, Stepper, Choice, Button }

    private const string HotbarGroup = "Hotbar buttons";
    private const string GluttonyName = "GluttonyCombo";
    private const int PoolSize = 20;
    private const float RowH = 28f;
    private const float TabW = 110f;
    private const float TabH = 28f;
    private const float TrayH = 164f;
    private const float FlashH = 38f;
    private const long FlashMs = 5000;
    private const long PollMs = 1000;

    private sealed class Row
    {
        public required ImGuiImageNode Icon;
        public required TextNode Label;
        public required TextNode Info;
        public required TextButtonNode B1;
        public required TextNode Val;
        public required TextButtonNode B2;
        public RowKind Kind;
        public string? IconFile;
        public Action? On1;
        public Action? On2;

        public void Reset()
        {
            Kind = RowKind.Hidden;
            On1 = null;
            On2 = null;
            Icon.IsVisible = false;
            Label.IsVisible = false;
            Info.IsVisible = false;
            B1.IsVisible = false;
            Val.IsVisible = false;
            B2.IsVisible = false;

            // Pooled buttons are reused across pages: a toggle's green/grey/amber label must not leak into the next page's plain button.
            B1.LabelNode.TextColor = HubTheme.White;
            B2.LabelNode.TextColor = HubTheme.White;
        }
    }

    private sealed class Tray
    {
        public ImGuiImageNode Bg = null!;
        public TextNode Title = null!;
        public TextNode AutoLabel = null!;
        public TextButtonNode AutoToggle = null!;
        public TextNode AutoWhy = null!;
        public TextNode Hint = null!;
        public TextNode Missing = null!;
        public readonly List<TextButtonNode> Buttons = new();
        public PluginLink? Link;
        public ParsedControl? Master;
        public readonly List<ParsedControl> Picks = new();
        public Action? OnToggle;
        public readonly Action?[] OnPick = new Action?[8];

        public void SetVisible(bool v)
        {
            Bg.IsVisible = v;
            Title.IsVisible = v;
            if (!v)
            {
                AutoLabel.IsVisible = false;
                AutoToggle.IsVisible = false;
                AutoWhy.IsVisible = false;
                Hint.IsVisible = false;
                Missing.IsVisible = false;
                foreach (var b in Buttons) b.IsVisible = false;
            }
        }
    }

    private readonly List<Row> _rows = [];
    private readonly Queue<Action> _actions = new();
    private readonly ConfirmGate _confirm = new(5000);
    private readonly Stopwatch _sincePoll = Stopwatch.StartNew();

    private Tray? _tray;
    private TextButtonNode? _tabQuick;
    private TextButtonNode? _tabPlugins;
    private TextButtonNode? _back;
    private ImGuiImageNode? _underline;
    private TextNode? _empty;
    private TextNode? _flash;

    private Vector2 _origin;
    private float _width;
    private float _height;
    private int _generation;
    private Page _page = Page.Quick;
    private PluginLink? _detail;
    private Page? _nextPage;
    private PluginLink? _nextDetail;
    private long _flashUntil;
    private bool _tintFailed;

    private static long Now => Environment.TickCount64;

    // ===== lifecycle ====================================================================================

    protected override void OnSetup(AtkUnitBase* addon)
    {
        // This runs inside the game's native Setup callback: an exception that escapes here ends the game. A half-built window
        // is safe, because BindAll returns early while any of its pieces is missing, so log and leave it.
        try { BuildWindow(); }
        catch (Exception ex) { log.Error(ex, "Hub window setup failed"); }
    }

    private void BuildWindow()
    {
        _generation++;
        _rows.Clear();
        _actions.Clear();
        _confirm.Reset();
        _page = Page.Quick;
        _detail = null;
        _nextPage = null;
        _nextDetail = null;
        _flashUntil = 0;
        _tintFailed = false;

        TintChrome();

        var o = ContentStartPosition;
        var c = ContentSize;
        _origin = o + new Vector2(0f, TabH + 12f);
        _width = c.X;
        _height = Math.Max(c.Y - (TabH + 12f), 120f);

        // Backdrop first: everything else is drawn above it.
        MakePanel(_origin + new Vector2(-6f, -4f), new Vector2(_width + 12f, _height + 8f), HubTheme.Backdrop);

        _tabQuick = MakeButton(o, new Vector2(TabW, TabH), "Quick", () => _nextPage = Page.Quick);
        _tabPlugins = MakeButton(o + new Vector2(TabW + 8f, 0f), new Vector2(TabW, TabH), "Plugins", () => _nextPage = Page.Plugins);
        _back = MakeButton(o + new Vector2((TabW + 8f) * 2f, 0f), new Vector2(90f, TabH), "< Back", () => _nextPage = Page.Plugins);
        _underline = MakePanel(o + new Vector2(0f, TabH + 3f), new Vector2(TabW, 3f), HubTheme.GoldLine);

        _tray = BuildTray();
        for (var i = 0; i < PoolSize; i++) _rows.Add(MakeRow());
        _empty = MakeText(Vector2.Zero, new Vector2(_width - 16f, 20f), 13, HubTheme.Dim, "");

        // Two wrapped lines: a confirm text can be 200 characters, which must not run past the window.
        _flash = MakeText(_origin + new Vector2(8f, _height - FlashH), new Vector2(_width - 16f, FlashH - 2f), 12, HubTheme.Amber, "");
        _flash.TextFlags = TextFlags.WordWrap | TextFlags.MultiLine;

        // Nothing is polled while the window is closed, so a plugin may have been updated or reloaded since: describe afresh.
        client.ForgetAll();
        Poll();
        _sincePoll.Restart();
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        try
        {
            if (_nextPage is { } page)
            {
                _page = page;
                if (page == Page.Detail) _detail = _nextDetail;
                _nextPage = null;
                _nextDetail = null;
                _confirm.Reset();
                BindAll();
            }

            var ran = false;
            while (_actions.Count > 0)
            {
                _actions.Dequeue().Invoke();
                ran = true;
            }
            if (ran) BindAll();

            // The window's own focus animation resets the background tint: put it back.
            TintChrome();

            if (_confirm.ClearIfExpired(Now)) BindAll();

            if (_flashUntil != 0 && Now > _flashUntil)
            {
                _flashUntil = 0;
                if (_flash != null) _flash.String = "";
            }

            if (_sincePoll.ElapsedMilliseconds >= PollMs)
            {
                _sincePoll.Restart();
                Poll();
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Hub window update failed");
        }
    }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        // The nodes die with the addon. Forget them and invalidate pending icon loads.
        _generation++;
        _rows.Clear();
        _actions.Clear();
        _tray = null;
        _tabQuick = null;
        _tabPlugins = null;
        _back = null;
        _underline = null;
        _empty = null;
        _flash = null;
    }

    private void Poll()
    {
        client.Refresh(monitor.Snapshot(), pollValues: true, Now);
        BindAll();
    }

    /// <summary>
    /// Pulls the stock window's blue toward forest green. Called on open and every update: the stock window animates its
    /// background tint back to neutral whenever it gains or loses focus (the border only animates its alpha), so the tint
    /// is simply re-applied. Stops for good after one failure so a bad node cannot spam the log.
    /// </summary>
    private void TintChrome()
    {
        if (_tintFailed) return;
        try
        {
            if (WindowNode is KamiToolKit.Nodes.WindowNode w)
            {
                w.BackgroundNode.AddColor = HubTheme.ChromeTint;
                w.BorderNode.AddColor = HubTheme.ChromeTint;
            }
        }
        catch (Exception ex)
        {
            _tintFailed = true;
            log.Debug(ex, "Could not tint the window chrome");
        }
    }

    // ===== node factories ===============================================================================

    private ImGuiImageNode MakePanel(Vector2 position, Vector2 size, Vector4 color)
    {
        var node = new ImGuiImageNode
        {
            Position = position,
            Size = size,
            TextureSize = new Vector2(2f, 2f),
            // Without this a native image node draws the texture at its own size and ignores Size.
            FitTexture = true,
        };
        node.AttachNode(this);

        // BGRA, like the PNG icons that are proven in game: Dalamud maps B8G8R8A8 to a kernel texture format and has no case
        // for R8G8B8A8, so an RGBA texture would reach the game with an unset format.
        var bgra = new byte[2 * 2 * 4];
        for (var i = 0; i < 4; i++)
        {
            bgra[i * 4 + 0] = (byte)Math.Clamp((int)MathF.Round(color.Z * 255f), 0, 255);
            bgra[i * 4 + 1] = (byte)Math.Clamp((int)MathF.Round(color.Y * 255f), 0, 255);
            bgra[i * 4 + 2] = (byte)Math.Clamp((int)MathF.Round(color.X * 255f), 0, 255);
            bgra[i * 4 + 3] = (byte)Math.Clamp((int)MathF.Round(color.W * 255f), 0, 255);
        }
        node.LoadTexture(textures.CreateFromRaw(RawImageSpecification.Bgra32(2, 2), bgra, "LazyHubPanel"));
        return node;
    }

    private TextNode MakeText(Vector2 position, Vector2 size, uint fontSize, Vector4 color, string text)
    {
        var node = new TextNode
        {
            Position = position,
            Size = size,
            FontSize = fontSize,
            FontType = FontType.Axis,
            TextColor = color,
            String = text,
        };
        node.AttachNode(this);
        return node;
    }

    private TextButtonNode MakeButton(Vector2 position, Vector2 size, string text, Action onClick)
    {
        var node = new TextButtonNode
        {
            Position = position,
            Size = size,
            String = text,
            OnClick = onClick,
        };
        node.AttachNode(this);
        return node;
    }

    private Row MakeRow()
    {
        Row? row = null;
        var icon = new ImGuiImageNode
        {
            Size = new Vector2(22f, 22f),
            TextureSize = new Vector2(64f, 64f),
            FitTexture = true,
        };
        icon.AttachNode(this);

        var label = MakeText(Vector2.Zero, new Vector2(240f, 20f), 14, HubTheme.White, "");
        var info = MakeText(Vector2.Zero, new Vector2(200f, 18f), 12, HubTheme.Dim, "");
        var b1 = MakeButton(Vector2.Zero, new Vector2(100f, 24f), "", () => row?.On1?.Invoke());
        var val = MakeText(Vector2.Zero, new Vector2(120f, 18f), 14, HubTheme.White, "");
        val.AlignmentType = AlignmentType.Center;

        // A single line that is too long ends in "..." instead of running past its node (and the window).
        label.TextFlags = TextFlags.Ellipsis;
        info.TextFlags = TextFlags.Ellipsis;
        val.TextFlags = TextFlags.Ellipsis;
        var b2 = MakeButton(Vector2.Zero, new Vector2(100f, 24f), "", () => row?.On2?.Invoke());

        row = new Row { Icon = icon, Label = label, Info = info, B1 = b1, Val = val, B2 = b2 };
        row.Reset();
        return row;
    }

    private Tray BuildTray()
    {
        var tray = new Tray();
        tray.Bg = MakePanel(_origin, new Vector2(_width, TrayH), HubTheme.Tray);
        tray.Title = MakeText(_origin + new Vector2(10f, 8f), new Vector2(300f, 22f), 16, HubTheme.Gold, "Gluttony Combo");
        tray.AutoLabel = MakeText(_origin + new Vector2(10f, 42f), new Vector2(160f, 22f), 14, HubTheme.White, "Auto-Rotation");
        tray.AutoToggle = MakeButton(_origin + new Vector2(176f, 38f), new Vector2(100f, 26f), "", () => tray.OnToggle?.Invoke());
        tray.AutoWhy = MakeText(_origin + new Vector2(286f, 44f), new Vector2(_width - 296f, 18f), 12, HubTheme.Dim, "");
        tray.Hint = MakeText(_origin + new Vector2(10f, 76f), new Vector2(_width - 20f, 18f), 12, HubTheme.Dim,
            "Hotbar buttons: click one, then click a hotbar slot to place it. Escape cancels.");
        tray.Missing = MakeText(_origin + new Vector2(10f, 42f), new Vector2(_width - 20f, 40f), 13, HubTheme.Dim,
            "Gluttony Combo is not running, or does not offer controls yet.");

        var bw = (_width - 20f - 3f * 8f) / 4f;
        for (var i = 0; i < 8; i++)
        {
            var index = i;
            var x = 10f + (index % 4) * (bw + 8f);
            var y = 100f + (index / 4) * 32f;
            tray.Buttons.Add(MakeButton(_origin + new Vector2(x, y), new Vector2(bw, 26f), "", () => tray.OnPick[index]?.Invoke()));
        }
        tray.SetVisible(false);
        return tray;
    }

    // ===== layout =======================================================================================

    private void Put(NodeBase node, float x, float y, float w, float h)
    {
        node.Position = _origin + new Vector2(x, y);
        node.Size = new Vector2(w, h);
    }

    private void Layout(Row r, RowKind kind, float y)
    {
        r.Kind = kind;
        r.Icon.IsVisible = false;
        r.Label.IsVisible = true;
        r.Info.IsVisible = false;
        r.B1.IsVisible = false;
        r.Val.IsVisible = false;
        r.B2.IsVisible = false;

        switch (kind)
        {
            case RowKind.Plugin:
                Put(r.Icon, 8, y + 3, 22, 22); r.Icon.IsVisible = true;
                Put(r.Label, 38, y + 5, 250, 20);
                Put(r.Info, 296, y + 7, 214, 18); r.Info.IsVisible = true;
                Put(r.B1, 520, y + 2, 110, 24); r.B1.IsVisible = true;
                Put(r.B2, 638, y + 2, 100, 24); r.B2.IsVisible = true;
                break;
            case RowKind.Master:
                Put(r.Icon, 8, y + 3, 22, 22); r.Icon.IsVisible = true;
                Put(r.Label, 38, y + 5, 250, 20);
                Put(r.B1, 300, y + 2, 100, 24); r.B1.IsVisible = true;
                Put(r.B2, 408, y + 2, 100, 24); r.B2.IsVisible = true;
                Put(r.Info, 520, y + 7, _width - 528f, 18); r.Info.IsVisible = true;
                break;
            case RowKind.PluginHeader:
                Put(r.Icon, 8, y + 3, 22, 22); r.Icon.IsVisible = true;
                Put(r.Label, 38, y + 5, 420, 20);
                Put(r.Info, 470, y + 7, _width - 478f, 18); r.Info.IsVisible = true;
                break;
            case RowKind.Group:
                Put(r.Label, 8, y + 5, 420, 20);
                break;
            case RowKind.Toggle:
                Put(r.Label, 8, y + 5, 340, 20);
                Put(r.B1, 360, y + 2, 110, 24); r.B1.IsVisible = true;
                Put(r.Info, 480, y + 7, _width - 488f, 18); r.Info.IsVisible = true;
                break;
            case RowKind.Stepper:
                Put(r.Label, 8, y + 5, 300, 20);
                Put(r.B1, 316, y + 2, 44, 24); r.B1.IsVisible = true;
                Put(r.Val, 364, y + 6, 130, 18); r.Val.IsVisible = true;
                Put(r.B2, 498, y + 2, 44, 24); r.B2.IsVisible = true;
                Put(r.Info, 552, y + 7, _width - 560f, 18); r.Info.IsVisible = true;
                break;
            case RowKind.Choice:
                Put(r.Label, 8, y + 5, 300, 20);
                Put(r.B1, 316, y + 2, 44, 24); r.B1.IsVisible = true;
                Put(r.Val, 364, y + 6, 190, 18); r.Val.IsVisible = true;
                Put(r.B2, 558, y + 2, 44, 24); r.B2.IsVisible = true;
                Put(r.Info, 612, y + 7, _width - 620f, 18); r.Info.IsVisible = true;
                break;
            case RowKind.Button:
                Put(r.Label, 8, y + 5, 340, 20);
                Put(r.B1, 360, y + 2, 110, 24); r.B1.IsVisible = true;
                Put(r.Info, 480, y + 7, _width - 488f, 18); r.Info.IsVisible = true;
                break;
        }
    }

    private void SetIcon(Row r, string file)
    {
        if (r.IconFile == file) return;
        r.IconFile = file;
        var generation = _generation;
        icons.LoadInto(r.Icon, file, () => generation == _generation && IsOpen && r.IconFile == file);
    }

    // ===== binding ======================================================================================

    private void BindAll()
    {
        if (_tray == null || _tabQuick == null || _tabPlugins == null || _back == null || _underline == null || _empty == null) return;

        foreach (var r in _rows) r.Reset();
        _empty.IsVisible = false;

        // Tabs: the gold bar and the brighter label say which page is open.
        var onQuick = _page == Page.Quick;
        _underline.Position = ContentStartPosition + new Vector2(onQuick ? 0f : TabW + 8f, TabH + 3f);
        _tabQuick.LabelNode.TextColor = onQuick ? HubTheme.White : HubTheme.Grey;
        _tabPlugins.LabelNode.TextColor = onQuick ? HubTheme.Grey : HubTheme.White;
        _back.IsVisible = _page == Page.Detail;
        _tray.SetVisible(onQuick);

        switch (_page)
        {
            case Page.Quick: BindQuick(); break;
            case Page.Plugins: BindPlugins(); break;
            default: BindDetail(); break;
        }
    }

    private bool IsUsable(PluginLink link, ParsedState? st) => link.IsRunning && st != null && st.Enabled && !st.Locked;

    private string Key(PluginLink link, ParsedControl c) => link.Entry.InternalName + "/" + c.Id;

    private void BindTray()
    {
        var t = _tray!;
        var link = client.Find(GluttonyName);
        var master = link?.Master;
        t.Link = link;
        t.Master = master;
        t.Picks.Clear();
        for (var i = 0; i < t.OnPick.Length; i++) t.OnPick[i] = null;

        if (link == null || link.Descriptor == null || master == null)
        {
            t.AutoLabel.IsVisible = false;
            t.AutoToggle.IsVisible = false;
            t.AutoWhy.IsVisible = false;
            t.Hint.IsVisible = false;
            t.Missing.IsVisible = true;
            foreach (var b in t.Buttons) b.IsVisible = false;
            return;
        }

        t.Missing.IsVisible = false;
        t.AutoLabel.IsVisible = true;
        t.AutoToggle.IsVisible = true;
        t.AutoWhy.IsVisible = true;

        var st = link.ValueOf(master.Id);
        BindToggleButton(t.AutoToggle, link, master, st);
        t.AutoWhy.String = WhyText(link, master, st);
        t.AutoWhy.TextColor = st?.Locked == true || _confirm.IsPending(Key(link, master), Now) ? HubTheme.Amber : HubTheme.Dim;
        t.OnToggle = () => _actions.Enqueue(() => ActToggle(link, master));

        foreach (var c in link.Descriptor.Controls)
            if (c.Kind == ControlKind.Button && string.Equals(c.Group, HotbarGroup, StringComparison.Ordinal) && t.Picks.Count < t.Buttons.Count)
                t.Picks.Add(c);

        t.Hint.IsVisible = t.Picks.Count > 0;
        for (var i = 0; i < t.Buttons.Count; i++)
        {
            var b = t.Buttons[i];
            if (i >= t.Picks.Count) { b.IsVisible = false; continue; }

            var c = t.Picks[i];
            b.IsVisible = true;
            b.String = c.Label;
            b.IsEnabled = IsUsable(link, link.ValueOf(c.Id));
            t.OnPick[i] = () => _actions.Enqueue(() => ActInvoke(link, c, "Click a hotbar slot to place it. Escape cancels."));
        }
    }

    private void BindToggleButton(TextButtonNode button, PluginLink link, ParsedControl c, ParsedState? st)
    {
        var pending = _confirm.IsPending(Key(link, c), Now);
        var on = st?.Bool == true;
        button.String = pending ? "Confirm?" : ControlFormat.ValueText(c, st);
        button.LabelNode.TextColor = pending ? HubTheme.Amber : on ? HubTheme.Green : HubTheme.Grey;
        button.IsEnabled = IsUsable(link, st);
    }

    private string WhyText(PluginLink link, ParsedControl c, ParsedState? st)
    {
        if (_confirm.IsPending(Key(link, c), Now) && !string.IsNullOrEmpty(c.Confirm)) return "Click again to confirm";
        return st?.Why ?? "";
    }

    private void BindQuick()
    {
        BindTray();

        var y = TrayH + 10f;
        var n = 0;
        foreach (var link in client.Links)
        {
            if (string.Equals(link.Entry.InternalName, GluttonyName, StringComparison.Ordinal)) continue;
            var m = link.Master;
            if (m == null || n >= _rows.Count || y + RowH > _height - FlashH) continue;

            var r = _rows[n++];
            var st = link.ValueOf(m.Id);
            Layout(r, RowKind.Master, y);
            y += RowH;

            SetIcon(r, link.Entry.IconFile);
            r.Label.String = link.Entry.DisplayName;
            r.Label.TextColor = HubTheme.White;
            BindToggleButton(r.B1, link, m, st);
            r.B2.String = "Settings";
            r.B2.IsEnabled = link.HasSettings(HotbarGroup);
            r.Info.String = WhyText(link, m, st);
            r.Info.TextColor = st?.Locked == true || _confirm.IsPending(Key(link, m), Now) ? HubTheme.Amber : HubTheme.Dim;
            r.On1 = () => _actions.Enqueue(() => ActToggle(link, m));
            r.On2 = () => GoDetail(link);
        }

        if (n == 0 && _empty != null)
        {
            _empty.Position = _origin + new Vector2(8f, y + 4f);
            _empty.String = "No other plugin offers quick switches yet. Use the Plugins tab to open them.";
            _empty.IsVisible = true;
        }
    }

    private void BindPlugins()
    {
        var y = 0f;
        var n = 0;
        foreach (var link in client.Links)
        {
            if (n >= _rows.Count) break;
            var r = _rows[n++];
            Layout(r, RowKind.Plugin, y);
            y += RowH;

            SetIcon(r, link.Entry.IconFile);
            r.Label.String = link.Entry.DisplayName;
            r.Label.TextColor = HubTheme.White;
            r.Info.String = PluginStatus.Label(link.State);
            r.Info.TextColor = link.State switch
            {
                PluginState.Loaded => HubTheme.Green,
                PluginState.LoadedTesting => HubTheme.Amber,
                PluginState.InstalledNotLoaded => HubTheme.Grey,
                _ => HubTheme.Dim,
            };
            r.B1.String = "Settings";
            r.B1.IsEnabled = link.HasSettings(HotbarGroup);
            r.B2.String = "Open";
            r.B2.IsEnabled = link.IsRunning;
            var name = link.Entry.InternalName;
            r.On1 = () => GoDetail(link);
            r.On2 = () => _actions.Enqueue(() => monitor.Open(name));
        }
    }

    private void BindDetail()
    {
        var link = _detail;
        if (link == null || link.Descriptor == null || !link.IsRunning)
        {
            // The plugin stopped or went away while its page was open: back to the list.
            _nextPage = Page.Plugins;
            return;
        }

        var n = 0;
        var y = 0f;

        var head = _rows[n++];
        Layout(head, RowKind.PluginHeader, y);
        y += RowH;
        SetIcon(head, link.Entry.IconFile);
        head.Label.String = link.Entry.DisplayName + "   v" + link.Descriptor.Version;
        head.Label.TextColor = HubTheme.Gold;
        head.Info.String = PluginStatus.Label(link.State);
        head.Info.TextColor = link.State == PluginState.LoadedTesting ? HubTheme.Amber : HubTheme.Green;

        // Keep the rows (and the "and N more" line) clear of the message line at the bottom.
        // The header row and a 24 px "and N more" line come out of the space above the message line.
        var capacity = Math.Max(1, Math.Min(_rows.Count - 1, (int)((_height - FlashH - RowH - 24f) / RowH)));
        var rows = DetailRows.Build(link.Descriptor, capacity, HotbarGroup, out var hidden);
        foreach (var dr in rows)
        {
            if (n >= _rows.Count) break;
            var r = _rows[n++];

            if (dr.IsHeader)
            {
                Layout(r, RowKind.Group, y);
                r.Label.String = dr.Header.ToUpperInvariant();
                r.Label.TextColor = HubTheme.Gold;
                y += RowH;
                continue;
            }

            var c = dr.Control!;
            var st = link.ValueOf(c.Id);
            var usable = IsUsable(link, st);
            var kind = c.Kind switch
            {
                ControlKind.Toggle => RowKind.Toggle,
                ControlKind.Stepper => RowKind.Stepper,
                ControlKind.Choice => RowKind.Choice,
                _ => RowKind.Button,
            };
            Layout(r, kind, y);
            y += RowH;

            r.Label.String = c.Label;
            r.Label.TextColor = HubTheme.White;
            r.Info.String = WhyText(link, c, st);
            r.Info.TextColor = st?.Locked == true || _confirm.IsPending(Key(link, c), Now) ? HubTheme.Amber : HubTheme.Dim;

            switch (kind)
            {
                case RowKind.Toggle:
                    BindToggleButton(r.B1, link, c, st);
                    r.On1 = () => _actions.Enqueue(() => ActToggle(link, c));
                    break;
                case RowKind.Stepper:
                    r.B1.String = "-";
                    r.B2.String = "+";
                    r.B1.LabelNode.TextColor = HubTheme.White;
                    r.B2.LabelNode.TextColor = HubTheme.White;
                    r.B1.IsEnabled = usable;
                    r.B2.IsEnabled = usable;
                    r.Val.String = ControlFormat.ValueText(c, st);
                    r.Val.TextColor = usable ? HubTheme.White : HubTheme.Dim;
                    r.On1 = () => _actions.Enqueue(() => ActStepper(link, c, -1));
                    r.On2 = () => _actions.Enqueue(() => ActStepper(link, c, +1));
                    break;
                case RowKind.Choice:
                    r.B1.String = "<";
                    r.B2.String = ">";
                    r.B1.LabelNode.TextColor = HubTheme.White;
                    r.B2.LabelNode.TextColor = HubTheme.White;
                    r.B1.IsEnabled = usable;
                    r.B2.IsEnabled = usable;
                    r.Val.String = ControlFormat.ValueText(c, st);
                    r.Val.TextColor = usable ? HubTheme.White : HubTheme.Dim;
                    r.On1 = () => _actions.Enqueue(() => ActChoice(link, c, -1));
                    r.On2 = () => _actions.Enqueue(() => ActChoice(link, c, +1));
                    break;
                default:
                    r.B1.String = _confirm.IsPending(Key(link, c), Now) ? "Confirm?" : "Run";
                    r.B1.LabelNode.TextColor = _confirm.IsPending(Key(link, c), Now) ? HubTheme.Amber : HubTheme.White;
                    r.B1.IsEnabled = usable;
                    r.On1 = () => _actions.Enqueue(() => ActInvoke(link, c, ""));
                    break;
            }
        }

        if (hidden > 0 && _empty != null)
        {
            _empty.Position = _origin + new Vector2(8f, y + 4f);
            _empty.String = "...and " + hidden + " more in the plugin's own window.";
            _empty.IsVisible = true;
        }
    }

    // ===== actions (run from OnUpdate, never inside a click callback) ===================================

    private void GoDetail(PluginLink link)
    {
        _nextPage = Page.Detail;
        _nextDetail = link;
    }

    private void ActToggle(PluginLink link, ParsedControl c)
    {
        var st = link.ValueOf(c.Id);
        if (st == null) return;

        var target = !st.Bool;
        var needsConfirm = target && !string.IsNullOrEmpty(c.Confirm);
        if (!_confirm.ShouldProceed(Key(link, c), needsConfirm, Now))
        {
            Flash(ConfirmText(c), HubTheme.Amber);
            return;
        }
        Report(link, client.Set(link, c.Id, target), "");
    }

    private void ActStepper(PluginLink link, ParsedControl c, int direction)
    {
        var st = link.ValueOf(c.Id);
        if (st == null || st.Value is not double current) return;
        Report(link, client.Set(link, c.Id, ControlFormat.StepperNext(c, current, direction)), "");
    }

    private void ActChoice(PluginLink link, ParsedControl c, int direction)
    {
        var st = link.ValueOf(c.Id);
        if (st == null || st.Value is not double current) return;
        Report(link, client.Set(link, c.Id, ControlFormat.ChoiceNext(c, current, direction)), "");
    }

    private void ActInvoke(PluginLink link, ParsedControl c, string successNote)
    {
        if (!_confirm.ShouldProceed(Key(link, c), !string.IsNullOrEmpty(c.Confirm), Now))
        {
            Flash(ConfirmText(c), HubTheme.Amber);
            return;
        }
        Report(link, client.Invoke(link, c.Id), successNote);
    }

    private static string ConfirmText(ParsedControl c)
        => string.IsNullOrEmpty(c.Confirm)
            ? "Click again within a few seconds to confirm."
            : c.Confirm + " Click again within a few seconds to confirm.";

    private void Report(PluginLink link, SetOutcome outcome, string successNote)
    {
        if (outcome.Ok) Flash(successNote, HubTheme.Green);
        else Flash(link.Entry.DisplayName + ": " + outcome.Why, HubTheme.Red);
    }

    private void Flash(string text, Vector4 color)
    {
        if (_flash == null) return;
        _flash.String = text;
        _flash.TextColor = color;
        _flashUntil = string.IsNullOrEmpty(text) ? 0 : Now + FlashMs;
    }
}
