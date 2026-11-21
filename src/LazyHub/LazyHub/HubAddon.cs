using System;
using System.Diagnostics;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.BaseTypes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using Lalalazy.Hub;
using LalaUiKit;
using LazyHub.Core;

namespace LazyHub;

/// <summary>
/// The Lazy Hub window, built from native game UI nodes. Quick controls and the plugin library share one shell;
/// a detail page exposes the controls already supplied by each plugin's existing hub adapter.
///
/// Safety invariants:
/// - Nodes are created once in <see cref="OnSetup"/> from fixed pools and afterwards only re-bound. Clicks queue effects
///   for the next update; no hub IPC write runs inside a native click callback.
/// - Hub IPC is polled only while this window is open, and asynchronous icon loads check the setup generation.
/// - Rounded cards, selection outlines and track toggles use the same proven image, text and button node types. Each
///   panel's texture is drawn at the panel's own pixel size, so the image node never stretches it.
/// - The toolkit scrolling container clips overflow instead of hiding controls or covering the footer.
/// </summary>
internal sealed unsafe class HubAddon(PluginMonitor monitor, HubClient client, IconLoader icons, ITextureProvider textures, IPluginLog log) : NativeAddon
{
    private enum Page { Quick, Plugins, Detail }
    private enum RowKind { Hidden, Plugin, Master, Group, Toggle, Stepper, Choice, Button }

    private const string HotbarGroup = "Hotbar buttons";
    private const string GluttonyName = "GluttonyCombo";
    private const int PoolSize = 40;
    private const long FlashMs = 5000;
    private const long PollMs = 1000;
    private const float DisabledAlpha = 0.45f;
    private static readonly Vector2 ToggleSize = new(HubLayout.ToggleWidth, HubLayout.ToggleHeight);

    /// <summary>
    /// A rounded (or flat) rectangle drawn by one image node. The texture is rasterised at the panel's exact whole-pixel
    /// size and replaced only when that size changes, so corners and borders stay crisp and nothing is stretched.
    /// </summary>
    private sealed class Panel
    {
        private readonly ITextureProvider _textures;
        private readonly Vector4 _fill;
        private readonly Vector4? _border;
        private readonly float _radius;
        private int _width;
        private int _height;

        public readonly ImGuiImageNode Node;

        public Panel(ITextureProvider textures, Vector4 fill, Vector4? border, float radius)
        {
            _textures = textures;
            _fill = fill;
            _border = border;
            _radius = radius;
            Node = new ImGuiImageNode
            {
                Size = new Vector2(4f, 4f),
                TextureSize = new Vector2(4f, 4f),
                FitTexture = true,
            };
        }

        private bool Flat => _radius <= 0f && _border is null;

        public bool IsVisible
        {
            get => Node.IsVisible;
            set => Node.IsVisible = value;
        }

        public float Alpha
        {
            set => Node.Alpha = value;
        }

        public Vector2 Position
        {
            get => Node.Position;
            set => Node.Position = value;
        }

        public void Place(Vector2 position, Vector2 size)
        {
            Node.Position = position;
            Fit(size);
        }

        public void Fit(Vector2 size)
        {
            if (size.X < 1f || size.Y < 1f) return;
            var w = PanelRaster.Clamp(size.X);
            var h = PanelRaster.Clamp(size.Y);
            Node.Size = new Vector2(w, h);
            if (w == _width && h == _height) return;
            if (Flat && _width != 0)
            {
                _width = w;
                _height = h;
                return;
            }

            _width = w;
            _height = h;
            var tw = Flat ? 4 : w;
            var th = Flat ? 4 : h;
            var bgra = PanelRaster.Render(tw, th, _fill, _border, _radius);
            Node.TextureSize = new Vector2(tw, th);
            Node.LoadTexture(_textures.CreateFromRaw(RawImageSpecification.Bgra32(tw, th), bgra, "LazyHubPanel"));
        }
    }

    private sealed class ToggleVisual
    {
        public required Panel OnTrack;
        public required Panel OffTrack;
        public required Panel PendingTrack;
        public required Panel Knob;
        public required Panel KnobOn;

        public void HideAll()
        {
            OnTrack.IsVisible = false;
            OffTrack.IsVisible = false;
            PendingTrack.IsVisible = false;
            Knob.IsVisible = false;
            KnobOn.IsVisible = false;
        }

        public void Reset(TextButtonNode button)
        {
            button.BackgroundNode.IsVisible = false;
            HideAll();
        }

        public void Bind(bool pending, bool on, bool enabled, TextButtonNode button)
        {
            button.BackgroundNode.IsVisible = false;
            OnTrack.IsVisible = !pending && on;
            OffTrack.IsVisible = !pending && !on;
            PendingTrack.IsVisible = pending;
            var darkKnob = !pending && on;
            KnobOn.IsVisible = darkKnob;
            Knob.IsVisible = !darkKnob;
            var knobX = on ? HubLayout.ToggleWidth - HubLayout.KnobInset - HubLayout.KnobSize : HubLayout.KnobInset;
            var knobPosition = new Vector2(knobX, HubLayout.KnobInset);
            Knob.Position = knobPosition;
            KnobOn.Position = knobPosition;
            var alpha = enabled ? 1f : DisabledAlpha;
            OnTrack.Alpha = alpha;
            OffTrack.Alpha = alpha;
            PendingTrack.Alpha = alpha;
            Knob.Alpha = alpha;
            KnobOn.Alpha = alpha;
        }
    }

    private sealed class Row
    {
        public required Panel Background;
        public required Panel Skin1;
        public required Panel Skin2;
        public required ImGuiImageNode Icon;
        public required TextNode Label;
        public required TextNode Info;
        public required TextButtonNode B1;
        public required ToggleVisual Toggle1;
        public required TextNode Val;
        public required TextButtonNode B2;
        public RowKind Kind;
        public HubRect Text;
        public bool HasInfo;
        public bool UseSkin1;
        public bool UseSkin2;
        public string? IconFile;
        public Action? On1;
        public Action? On2;

        public void Reset()
        {
            Kind = RowKind.Hidden;
            On1 = null;
            On2 = null;
            HasInfo = false;
            UseSkin1 = false;
            UseSkin2 = false;
            Background.IsVisible = false;
            Skin1.IsVisible = false;
            Skin2.IsVisible = false;
            Icon.IsVisible = false;
            Label.IsVisible = false;
            Info.IsVisible = false;
            B1.IsVisible = false;
            Val.IsVisible = false;
            B2.IsVisible = false;
            Toggle1.Reset(B1);
            B1.String = "";
            B2.String = "";
            B1.LabelNode.TextColor = HubTheme.Grey;
            B2.LabelNode.TextColor = HubTheme.Grey;
            Val.AlignmentType = AlignmentType.Center;
        }
    }

    private sealed class Tray
    {
        public Panel Bg = null!;
        public Panel Rule = null!;
        public TextNode Title = null!;
        public TextNode AutoLabel = null!;
        public TextButtonNode AutoToggle = null!;
        public ToggleVisual AutoVisual = null!;
        public TextNode AutoValue = null!;
        public TextNode AutoWhy = null!;
        public TextNode Hint = null!;
        public TextNode Missing = null!;
        public readonly List<TextButtonNode> Buttons = new();
        public readonly List<Panel> Skins = new();
        public PluginLink? Link;
        public ParsedControl? Master;
        public readonly List<ParsedControl> Picks = new();
        public Action? OnToggle;
        public readonly Action?[] OnPick = new Action?[8];

        public void SetVisible(bool visible)
        {
            Bg.IsVisible = visible;
            Rule.IsVisible = visible;
            Title.IsVisible = visible;
            if (visible) return;
            AutoLabel.IsVisible = false;
            AutoToggle.IsVisible = false;
            AutoVisual.HideAll();
            AutoValue.IsVisible = false;
            AutoWhy.IsVisible = false;
            Hint.IsVisible = false;
            Missing.IsVisible = false;
            foreach (var button in Buttons) button.IsVisible = false;
        }
    }

    private readonly List<Row> _rows = [];
    private readonly Queue<Action> _actions = new();
    private readonly ConfirmGate _confirm = new(5000);
    private readonly Stopwatch _sincePoll = Stopwatch.StartNew();

    private ScrollingNode<ResNode>? _scroll;
    private Tray? _tray;
    private Panel? _backdrop;
    private Panel? _headerBand;
    private Panel? _headerRule;
    private Panel? _navBand;
    private Panel? _navRule;
    private Panel? _brandChip;
    private Panel? _statusBg;
    private TextNode? _brandMark;
    private TextNode? _brand;
    private TextNode? _status;
    private Panel? _quickBase;
    private Panel? _pluginsBase;
    private Panel? _quickSelected;
    private Panel? _pluginsSelected;
    private Panel? _backSkin;
    private TextButtonNode? _tabQuick;
    private TextButtonNode? _tabPlugins;
    private TextButtonNode? _back;
    private Panel? _pageTile;
    private TextNode? _pageMark;
    private TextNode? _pageTitle;
    private TextNode? _pagePurpose;
    private TextNode? _pageCount;
    private TextNode? _empty;
    private Panel? _footerBand;
    private Panel? _footerRule;
    private TextNode? _footerMark;
    private TextNode? _footerLine;
    private TextNode? _flash;

    private Vector2 _origin;
    private Vector2 _lastContentSize;
    private float _width;
    private float _contentHeight;
    private float _height;
    private int _generation;
    private Page _page = Page.Quick;
    private PluginLink? _detail;
    private Page? _nextPage;
    private PluginLink? _nextDetail;
    private long _flashUntil;
    private bool _tintFailed;

    private static long Now => Environment.TickCount64;
    private float Gutter => HubLayout.Gutter(_width);
    private float InnerWidth => HubLayout.InnerWidth(_width);

    // ===== lifecycle ====================================================================================

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        // The native header owns the only Close control; keep it enabled and visible instead of adding a duplicate content X.
        DisableClose = false;
        if (WindowNode is KamiToolKit.Nodes.WindowNode nativeWindow)
            nativeWindow.ShowCloseButton = true;

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
        _lastContentSize = c;
        _width = c.X;
        _contentHeight = c.Y;
        _height = HubLayout.BodyHeight(c.Y);
        _origin = o + new Vector2(0f, HubLayout.BodyTop);

        // Back to front: one opaque backdrop over the whole content area first, so the game's window skin never shows between panels.
        _backdrop = MakePanel(HubTheme.Shell, null, 0f);
        _headerBand = MakePanel(HubTheme.Shell, null, 0f);
        _navBand = MakePanel(HubTheme.Shell, null, 0f);
        _headerRule = MakePanel(HubTheme.Border, null, 0f);
        _navRule = MakePanel(HubTheme.Border, null, 0f);
        _brandChip = MakePanel(HubTheme.Tray, HubTheme.Border, HubLayout.Radius);
        _brandMark = MakeText(24, HubTheme.Accent, "◆", AlignmentType.Center);
        _brand = MakeText(HubTheme.FontTitle, HubTheme.White, "Lazy Hub", AlignmentType.Left);
        _statusBg = MakePanel(HubTheme.Tray, HubTheme.Border, HubLayout.Control / 2f);
        _status = MakeText(HubTheme.FontSecondary, HubTheme.Grey, "", AlignmentType.Center);

        _quickBase = MakePanel(HubTheme.Tray, HubTheme.BorderSoft, HubLayout.Radius);
        _pluginsBase = MakePanel(HubTheme.Tray, HubTheme.BorderSoft, HubLayout.Radius);
        _quickSelected = MakePanel(HubTheme.Selected, HubTheme.Accent, HubLayout.Radius);
        _pluginsSelected = MakePanel(HubTheme.Selected, HubTheme.Accent, HubLayout.Radius);
        _backSkin = MakePanel(HubTheme.Tray, HubTheme.Border, HubLayout.Radius);
        _tabQuick = MakeButton("Quick controls", () => _nextPage = Page.Quick);
        _tabPlugins = MakeButton("Plugin library", () => _nextPage = Page.Plugins);
        _back = MakeButton("< Plugin library", () => _nextPage = Page.Plugins);

        _pageTile = MakePanel(HubTheme.Selected, null, HubLayout.Radius);
        _pageMark = MakeText(22, HubTheme.Accent, "◆", AlignmentType.Center);
        _pageTitle = MakeText(HubTheme.FontPage, HubTheme.White, "", AlignmentType.Left);
        _pagePurpose = MakeText(HubTheme.FontSecondary, HubTheme.Grey, "", AlignmentType.Left);
        _pageCount = MakeText(HubTheme.FontSecondary, HubTheme.Grey, "", AlignmentType.Right);
        _pageTitle.TextFlags = TextFlags.Ellipsis;
        _pagePurpose.TextFlags = TextFlags.Ellipsis;
        _pageCount.TextFlags = TextFlags.Ellipsis;
        _status.TextFlags = TextFlags.Ellipsis;
        _brand.TextFlags = TextFlags.Ellipsis;

        _scroll = new ScrollingNode<ResNode>
        {
            Position = _origin,
            Size = new Vector2(_width, _height),
            AutoHideScrollBar = true,
            ScrollSpeed = 48,
        };
        _scroll.AttachNode(this);
        _scroll.ContentNode.Size = new Vector2(_width, _height);

        _tray = BuildTray(_scroll.ContentNode);
        for (var i = 0; i < PoolSize; i++) _rows.Add(MakeRow(_scroll.ContentNode));
        _empty = MakeText(HubTheme.FontSecondary, HubTheme.Dim, "", AlignmentType.TopLeft, _scroll.ContentNode);
        _empty.TextFlags = TextFlags.WordWrap | TextFlags.MultiLine;

        // The footer sits above the scrolling body: a status line (a mark and a summary) and, for a few seconds after an action, its result.
        _footerBand = MakePanel(HubTheme.Shell, null, 0f);
        _footerRule = MakePanel(HubTheme.Border, null, 0f);
        _footerMark = MakeText(HubTheme.FontCaption, HubTheme.Grey, "●", AlignmentType.Left);
        _footerLine = MakeText(HubTheme.FontBody, HubTheme.White, "", AlignmentType.Left);
        _flash = MakeText(HubTheme.FontSecondary, HubTheme.Amber, "", AlignmentType.Left);
        _footerLine.TextFlags = TextFlags.Ellipsis;
        _flash.TextFlags = TextFlags.Ellipsis;

        LayoutChrome();
        client.ForgetAll();
        Poll();
        _sincePoll.Restart();
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        try
        {
            var content = ContentSize;
            if (Vector2.DistanceSquared(content, _lastContentSize) > 1f)
            {
                _lastContentSize = content;
                LayoutChrome();
                BindAll();
            }

            if (_nextPage is { } page)
            {
                _page = page;
                if (page == Page.Detail) _detail = _nextDetail;
                _nextPage = null;
                _nextDetail = null;
                _confirm.Reset();
                BindAll();
                _scroll?.ScrollToStart();
            }

            var ran = false;
            while (_actions.Count > 0)
            {
                _actions.Dequeue().Invoke();
                ran = true;
            }
            if (ran) BindAll();

            TintChrome();
            if (_confirm.ClearIfExpired(Now)) BindAll();
            if (_flashUntil != 0 && Now > _flashUntil)
            {
                _flashUntil = 0;
                if (_flash != null) _flash.String = "";
                PlaceFooter();
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
        _generation++;
        _rows.Clear();
        _actions.Clear();
        _scroll = null;
        _tray = null;
        _backdrop = null;
        _headerBand = null;
        _headerRule = null;
        _navBand = null;
        _navRule = null;
        _brandChip = null;
        _statusBg = null;
        _brandMark = null;
        _brand = null;
        _status = null;
        _quickBase = null;
        _pluginsBase = null;
        _quickSelected = null;
        _pluginsSelected = null;
        _backSkin = null;
        _tabQuick = null;
        _tabPlugins = null;
        _back = null;
        _pageTile = null;
        _pageMark = null;
        _pageTitle = null;
        _pagePurpose = null;
        _pageCount = null;
        _empty = null;
        _footerBand = null;
        _footerRule = null;
        _footerMark = null;
        _footerLine = null;
        _flash = null;
    }

    private void Poll()
    {
        client.Refresh(monitor.Snapshot(), pollValues: true, Now);
        BindAll();
    }

    private void TintChrome()
    {
        if (_tintFailed) return;
        try
        {
            if (WindowNode is KamiToolKit.Nodes.WindowNode w)
            {
                w.BackgroundTextureNode.AddColor = HubTheme.ChromeTint;
                w.BorderTextureNode.AddColor = HubTheme.ChromeTint;
            }
        }
        catch (Exception ex)
        {
            _tintFailed = true;
            log.Debug(ex, "Could not tint the window chrome");
        }
    }

    // ===== node factories ===============================================================================

    private Panel MakePanel(Vector4 color, Vector4? border, float radius, NodeBase? parent = null, NodePosition? relative = null)
    {
        var panel = new Panel(textures, color, border, radius);
        if (parent is null) panel.Node.AttachNode(this);
        else if (relative is { } relation) panel.Node.AttachNode(parent, relation);
        else panel.Node.AttachNode(parent);
        return panel;
    }

    private TextNode MakeText(uint fontSize, Vector4 color, string text, AlignmentType alignment, NodeBase? parent = null)
    {
        var node = new TextNode
        {
            Size = new Vector2(40f, 20f),
            FontSize = fontSize,
            FontType = FontType.Axis,
            TextColor = color,
            AlignmentType = alignment,
            String = text,
        };
        if (parent is null) node.AttachNode(this);
        else node.AttachNode(parent);
        return node;
    }

    /// <summary>
    /// A native text button whose own blue skin is hidden (the same way the switches hide it): the rounded panel under it
    /// supplies the look, the native node keeps the click, the pressed label and the disabled dimming.
    /// </summary>
    private TextButtonNode MakeButton(string text, Action onClick, NodeBase? parent = null)
    {
        var node = new TextButtonNode
        {
            Size = new Vector2(HubLayout.ButtonMinWidth, HubLayout.ButtonHeight),
            String = text,
            OnClick = onClick,
        };
        node.BackgroundNode.IsVisible = false;
        node.LabelNode.TextColor = HubTheme.Grey;
        node.LabelNode.TextFlags = TextFlags.Ellipsis;
        if (parent is null) node.AttachNode(this);
        else node.AttachNode(parent);
        return node;
    }

    private ToggleVisual MakeToggleVisual(TextButtonNode button)
    {
        button.BackgroundNode.IsVisible = false;
        var radius = HubLayout.ToggleHeight / 2f;
        var on = MakePanel(HubTheme.Accent, HubTheme.Accent, radius, button.BackgroundNode, NodePosition.AfterTarget);
        var off = MakePanel(HubTheme.TrackOff, HubTheme.Border, radius, on.Node, NodePosition.AfterTarget);
        var pending = MakePanel(HubTheme.Pending, HubTheme.Amber, radius, off.Node, NodePosition.AfterTarget);
        var knob = MakePanel(HubTheme.Grey, null, HubLayout.KnobSize / 2f, pending.Node, NodePosition.AfterTarget);
        var knobOn = MakePanel(HubTheme.Shell, null, HubLayout.KnobSize / 2f, knob.Node, NodePosition.AfterTarget);
        foreach (var track in new[] { on, off, pending }) track.Place(Vector2.Zero, ToggleSize);
        foreach (var dot in new[] { knob, knobOn }) dot.Place(new Vector2(HubLayout.KnobInset, HubLayout.KnobInset), new Vector2(HubLayout.KnobSize, HubLayout.KnobSize));
        var visual = new ToggleVisual { OnTrack = on, OffTrack = off, PendingTrack = pending, Knob = knob, KnobOn = knobOn };
        visual.Reset(button);
        return visual;
    }

    private Row MakeRow(ResNode parent)
    {
        Row? row = null;
        var background = MakePanel(HubTheme.Tray, HubTheme.Border, HubLayout.Radius, parent);
        var skin1 = MakePanel(HubTheme.Tray, HubTheme.Border, HubLayout.Radius, parent);
        var skin2 = MakePanel(HubTheme.Tray, HubTheme.Border, HubLayout.Radius, parent);
        var icon = new ImGuiImageNode
        {
            Size = new Vector2(HubLayout.IconSize, HubLayout.IconSize),
            TextureSize = new Vector2(64f, 64f),
            FitTexture = true,
        };
        icon.AttachNode(parent);
        var label = MakeText(HubTheme.FontBody, HubTheme.White, "", AlignmentType.Left, parent);
        var info = MakeText(HubTheme.FontCaption, HubTheme.Dim, "", AlignmentType.Left, parent);
        var b1 = MakeButton("", () => row?.On1?.Invoke(), parent);
        var toggle1 = MakeToggleVisual(b1);
        var val = MakeText(HubTheme.FontBody, HubTheme.White, "", AlignmentType.Center, parent);
        var b2 = MakeButton("", () => row?.On2?.Invoke(), parent);
        label.TextFlags = TextFlags.Ellipsis;
        info.TextFlags = TextFlags.Ellipsis;
        val.TextFlags = TextFlags.Ellipsis;
        row = new Row { Background = background, Skin1 = skin1, Skin2 = skin2, Icon = icon, Label = label, Info = info, B1 = b1, Toggle1 = toggle1, Val = val, B2 = b2 };
        row.Reset();
        return row;
    }

    private Tray BuildTray(ResNode parent)
    {
        var tray = new Tray();
        tray.Bg = MakePanel(HubTheme.Tray, HubTheme.Border, HubLayout.Radius, parent);
        tray.Rule = MakePanel(HubTheme.Border, null, 0f, parent);
        tray.Title = MakeText(HubTheme.FontBody, HubTheme.White, "Gluttony Combo", AlignmentType.Left, parent);
        tray.AutoLabel = MakeText(HubTheme.FontBody, HubTheme.White, "Auto-Rotation", AlignmentType.Left, parent);
        tray.AutoToggle = MakeButton("", () => tray.OnToggle?.Invoke(), parent);
        tray.AutoVisual = MakeToggleVisual(tray.AutoToggle);
        tray.AutoValue = MakeText(HubTheme.FontBody, HubTheme.White, "", AlignmentType.Right, parent);
        tray.AutoWhy = MakeText(HubTheme.FontCaption, HubTheme.Dim, "", AlignmentType.Left, parent);
        tray.AutoWhy.TextFlags = TextFlags.Ellipsis;
        tray.Hint = MakeText(HubTheme.FontCaption, HubTheme.Dim,
            "Hotbar buttons: click one, then click a hotbar slot to place it. Escape cancels.", AlignmentType.Left, parent);
        tray.Hint.TextFlags = TextFlags.Ellipsis;
        tray.Missing = MakeText(HubTheme.FontSecondary, HubTheme.Dim,
            "Missing Gluttony Combo controls. Start Gluttony Combo, then reopen Quick controls after it finishes loading.", AlignmentType.TopLeft, parent);
        tray.Missing.TextFlags = TextFlags.WordWrap | TextFlags.MultiLine;
        for (var i = 0; i < 8; i++)
        {
            var index = i;
            tray.Skins.Add(MakePanel(HubTheme.Tray, HubTheme.Border, HubLayout.Radius, parent));
            tray.Buttons.Add(MakeButton("", () => tray.OnPick[index]?.Invoke(), parent));
        }
        LayoutTray(tray, hasWhy: false);
        tray.SetVisible(false);
        return tray;
    }

    // ===== layout =======================================================================================

    private static void Put(NodeBase node, float x, float y, float w, float h)
    {
        node.Position = new Vector2(x, y);
        node.Size = new Vector2(w, h);
    }

    private static void Put(NodeBase node, HubRect r) => Put(node, r.X, r.Y, r.Width, r.Height);

    private static void Put(Panel panel, HubRect r) => panel.Place(new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height));

    /// <summary>Puts a node into a rectangle given relative to the content area.</summary>
    private void PutAt(NodeBase node, HubRect r) => Put(node, r.X + _contentOrigin.X, r.Y + _contentOrigin.Y, r.Width, r.Height);

    private void PutAt(Panel panel, HubRect r) => panel.Place(new Vector2(r.X + _contentOrigin.X, r.Y + _contentOrigin.Y), new Vector2(r.Width, r.Height));

    private Vector2 _contentOrigin;

    private void LayoutChrome()
    {
        if (_scroll is null || _backdrop is null || _headerBand is null || _headerRule is null || _navBand is null || _navRule is null ||
            _brandChip is null || _statusBg is null || _brandMark is null || _brand is null || _status is null ||
            _quickBase is null || _pluginsBase is null || _quickSelected is null || _pluginsSelected is null || _backSkin is null ||
            _tabQuick is null || _tabPlugins is null || _back is null || _pageTile is null || _pageMark is null || _pageTitle is null ||
            _pagePurpose is null || _pageCount is null || _footerBand is null || _footerRule is null || _footerMark is null ||
            _footerLine is null || _flash is null) return;

        var o = ContentStartPosition;
        var c = ContentSize;
        _contentOrigin = o;
        _width = c.X;
        _contentHeight = c.Y;
        _height = HubLayout.BodyHeight(c.Y);
        _origin = o + new Vector2(0f, HubLayout.BodyTop);

        PutAt(_backdrop, new HubRect(0f, 0f, _width, c.Y));
        PutAt(_headerBand, new HubRect(0f, 0f, _width, HubLayout.HeaderHeight));
        PutAt(_headerRule, new HubRect(0f, HubLayout.HeaderHeight - 1f, _width, 1f));
        PutAt(_navBand, new HubRect(0f, HubLayout.HeaderHeight, _width, HubLayout.NavHeight));
        PutAt(_navRule, new HubRect(0f, HubLayout.HeaderHeight + HubLayout.NavHeight - 1f, _width, 1f));

        var chip = HubLayout.Brand(_width);
        PutAt(_brandChip, chip);
        PutAt(_brandMark, new HubRect(chip.X + 12f, chip.Y, 24f, chip.Height));
        PutAt(_brand, new HubRect(chip.X + 48f, chip.Y, chip.Width - 48f - 16f, chip.Height));

        var pill = HubLayout.StatusPill(_width);
        PutAt(_statusBg, pill);
        PutAt(_status, new HubRect(pill.X + 12f, pill.Y, pill.Width - 24f, pill.Height));

        var quick = HubLayout.NavPill(0, _width);
        var plugins = HubLayout.NavPill(1, _width);
        PutAt(_quickBase, quick);
        PutAt(_pluginsBase, plugins);
        PutAt(_quickSelected, quick);
        PutAt(_pluginsSelected, plugins);
        PutAt(_tabQuick, quick);
        PutAt(_tabPlugins, plugins);
        PutAt(_back, HubLayout.NavBack(_width));

        var tile = HubLayout.PageTile(_width);
        PutAt(_pageTile, tile);
        PutAt(_pageMark, tile);
        PutAt(_pageTitle, HubLayout.PageTitle(_width));
        PutAt(_pagePurpose, HubLayout.PageDescription(_width));
        PutAt(_pageCount, HubLayout.PageMeta(_width));

        _scroll.Position = _origin;
        _scroll.Size = new Vector2(_width, _height);

        var footer = HubLayout.Footer(_width, c.Y);
        PutAt(_footerBand, footer);
        PutAt(_footerRule, new HubRect(footer.X, footer.Y, footer.Width, 1f));
        PlaceFooter();

        if (_tray is not null) LayoutTray(_tray, hasWhy: false);
    }

    private void PlaceFooter()
    {
        if (_footerBand is null || _footerMark is null || _footerLine is null || _flash is null) return;
        var hasSecond = _flashUntil != 0;
        var footer = HubLayout.Footer(_width, _contentHeight);
        var (mark, first, second) = HubLayout.FooterLines(footer, _width, hasSecond);
        PutAt(_footerMark, mark);
        PutAt(_footerLine, first);
        PutAt(_flash, second);
        _flash.IsVisible = hasSecond;
    }

    private void LayoutTray(Tray tray, bool hasWhy)
    {
        var g = Gutter;
        var inner = InnerWidth;
        var textX = g + HubLayout.CardPad;
        var textWidth = Math.Max(120f, inner - (2f * HubLayout.CardPad));
        Put(tray.Bg, new HubRect(g, 0f, inner, HubLayout.TrayHeight));
        Put(tray.Rule, new HubRect(textX, 92f, textWidth, 1f));
        Put(tray.Title, textX, 16f, textWidth, 24f);
        var toggleX = g + inner - HubLayout.CardPad - HubLayout.ToggleWidth;
        Put(tray.AutoToggle, toggleX, 52f, HubLayout.ToggleWidth, HubLayout.ToggleHeight);
        Put(tray.AutoLabel, textX, 44f, Math.Max(80f, toggleX - 12f - textX), hasWhy ? 20f : 40f);
        Put(tray.AutoValue, toggleX - 80f, 52f, 72f, 20f);
        Put(tray.AutoWhy, textX, 64f, Math.Max(80f, toggleX - 12f - textX), 18f);
        Put(tray.Hint, textX, 100f, textWidth, 18f);
        Put(tray.Missing, textX, 48f, textWidth, 44f);
        for (var i = 0; i < tray.Buttons.Count; i++)
        {
            var r = HubLayout.TrayButton(i, _width);
            Put(tray.Buttons[i], r);
        }
    }

    private void Layout(Row row, RowKind kind, HubCell cell)
    {
        row.Kind = kind;
        row.Background.IsVisible = false;
        if (kind != RowKind.Group) Put(row.Background, new HubRect(cell.X, cell.Y, cell.Width, cell.Height));
        row.Background.IsVisible = kind != RowKind.Group;
        row.Label.IsVisible = true;
        row.Val.IsVisible = false;

        switch (kind)
        {
            case RowKind.Plugin:
            {
                var p = HubLayout.LibraryRow(cell);
                Put(row.Icon, p.Icon); row.Icon.IsVisible = true;
                row.Text = p.Text;
                Put(row.B1, p.ButtonA); row.B1.IsVisible = true; row.UseSkin1 = true;
                Put(row.B2, p.ButtonB); row.B2.IsVisible = true; row.UseSkin2 = true;
                break;
            }
            case RowKind.Master:
            {
                var p = HubLayout.MasterRow(cell);
                Put(row.Icon, p.Icon); row.Icon.IsVisible = true;
                row.Text = p.Text;
                Put(row.B2, p.ButtonA); row.B2.IsVisible = true; row.UseSkin2 = true;
                Put(row.B1, p.ButtonB); row.B1.IsVisible = true;
                break;
            }
            case RowKind.Group:
                Put(row.Label, cell.X + 4f, cell.Y + 5f, Math.Max(40f, cell.Width - 8f), 22f);
                break;
            case RowKind.Toggle:
            {
                var p = HubLayout.ToggleRow(cell);
                row.Text = p.Text;
                Put(row.B1, p.ButtonB); row.B1.IsVisible = true;
                break;
            }
            case RowKind.Stepper:
            case RowKind.Choice:
            {
                var p = HubLayout.StepRow(cell);
                row.Text = p.Text;
                Put(row.B1, p.ButtonA); row.B1.IsVisible = true; row.UseSkin1 = true;
                Put(row.Val, p.Value); row.Val.IsVisible = true; row.Val.AlignmentType = AlignmentType.Center;
                Put(row.B2, p.ButtonB); row.B2.IsVisible = true; row.UseSkin2 = true;
                break;
            }
            case RowKind.Button:
            {
                var p = HubLayout.ActionRow(cell);
                row.Text = p.Text;
                Put(row.B1, p.ButtonA); row.B1.IsVisible = true; row.UseSkin1 = true;
                break;
            }
        }
    }

    private static void SetInfo(Row row, string text, Vector4 color)
    {
        row.Info.String = text;
        row.Info.TextColor = color;
        row.HasInfo = !string.IsNullOrEmpty(text);
    }

    /// <summary>Places the name and note once the note is known: a lone name is centred in the row.</summary>
    private static void FinishRow(Row row)
    {
        if (row.Kind is RowKind.Hidden or RowKind.Group) return;
        var (label, info) = HubLayout.TextLines(row.Text, row.HasInfo);
        Put(row.Label, label);
        Put(row.Info, info);
        row.Info.IsVisible = row.HasInfo;
    }

    private static void SyncSkin(Panel skin, TextButtonNode button, bool wanted)
    {
        var show = wanted && button.IsVisible;
        if (show)
        {
            skin.Place(button.Position, button.Size);
            skin.Alpha = button.IsEnabled ? 1f : DisabledAlpha;
        }
        skin.IsVisible = show;
    }

    /// <summary>Keeps every button's rounded skin under it: same place, same size, hidden with it, dimmed with it.</summary>
    private void SyncSkins()
    {
        foreach (var row in _rows)
        {
            SyncSkin(row.Skin1, row.B1, row.UseSkin1);
            SyncSkin(row.Skin2, row.B2, row.UseSkin2);
        }
        if (_tray is not null)
            for (var i = 0; i < _tray.Buttons.Count; i++)
                SyncSkin(_tray.Skins[i], _tray.Buttons[i], true);
        if (_backSkin is not null && _back is not null) SyncSkin(_backSkin, _back, true);
    }

    private void SetIcon(Row row, string file)
    {
        if (row.IconFile == file) return;
        row.IconFile = file;
        var generation = _generation;
        icons.LoadInto(row.Icon, file, () => generation == _generation && IsOpen && row.IconFile == file);
    }

    private void SetContentHeight(float contentHeight)
    {
        if (_scroll is null) return;
        _scroll.ContentNode.Size = new Vector2(_width, Math.Max(_height, contentHeight + HubLayout.ScrollPadBottom));
        _scroll.UpdateLayout();
    }

    // ===== binding ======================================================================================

    private void BindAll()
    {
        if (_scroll is null || _tray is null || _tabQuick is null || _tabPlugins is null || _back is null ||
            _quickSelected is null || _pluginsSelected is null || _empty is null) return;

        foreach (var row in _rows) row.Reset();
        _empty.IsVisible = false;
        var onQuick = _page == Page.Quick;
        _quickSelected.IsVisible = onQuick;
        _pluginsSelected.IsVisible = !onQuick;
        _tabQuick.String = onQuick ? "● Quick controls" : "○ Quick controls";
        _tabPlugins.String = !onQuick ? "● Plugin library" : "○ Plugin library";
        _tabQuick.LabelNode.TextColor = onQuick ? HubTheme.Accent : HubTheme.Grey;
        _tabPlugins.LabelNode.TextColor = !onQuick ? HubTheme.Accent : HubTheme.Grey;
        _back.IsVisible = _page == Page.Detail;
        _tray.SetVisible(onQuick);
        BindShell();

        switch (_page)
        {
            case Page.Quick: BindQuick(); break;
            case Page.Plugins: BindPlugins(); break;
            default: BindDetail(); break;
        }

        foreach (var row in _rows) FinishRow(row);
        SyncSkins();
    }

    private void BindShell()
    {
        if (_pageTitle is null || _pagePurpose is null || _pageCount is null || _status is null || _footerLine is null || _footerMark is null) return;
        var loaded = client.Links.Count(link => link.IsRunning);
        var installed = client.Links.Count(link => link.State != PluginState.NotInstalled);
        var quick = client.Links.Count(link => link.Master is not null);
        var state = OverallState(loaded, installed);
        _status.String = state == UiState.Missing ? "● Missing plugins" : "● " + Copy.Word(state);
        _status.TextColor = Copy.MarkColor(state);
        _footerMark.TextColor = Copy.MarkColor(state);
        _footerLine.String = $"{loaded} of {client.Links.Count} plugins loaded";

        if (_page == Page.Detail && _detail is { Descriptor: { } descriptor } link)
        {
            _pageTitle.String = link.Entry.DisplayName;
            _pagePurpose.String = "Common controls supplied through the existing Lazy Hub connection.";
            _pageCount.String = descriptor.Controls.Count(c => !string.Equals(c.Group, HotbarGroup, StringComparison.Ordinal)) + " controls · v" + descriptor.Version;
        }
        else if (_page == Page.Plugins)
        {
            _pageTitle.String = "Plugin library";
            _pagePurpose.String = "Open a plugin or jump to the controls it offers Lazy Hub.";
            _pageCount.String = $"{loaded}/{client.Links.Count} loaded";
        }
        else
        {
            _pageTitle.String = "Quick controls";
            _pagePurpose.String = "Live switches and hotbar actions offered by running plugins.";
            _pageCount.String = $"{quick} switches · {loaded}/{client.Links.Count} loaded";
        }
    }

    private UiState OverallState(int loaded, int installed)
    {
        if (_page == Page.Quick && client.Find(GluttonyName) is { Master: { } master } link)
        {
            var state = link.ValueOf(master.Id);
            if (state?.Locked == true) return UiState.Blocked;
            if (state?.Bool == true) return UiState.Running;
        }
        if (loaded > 0) return UiState.Ready;
        return installed > 0 ? UiState.NeedsSetup : UiState.Missing;
    }

    private bool IsUsable(PluginLink link, ParsedState? state) => link.IsRunning && state != null && state.Enabled && !state.Locked;
    private string Key(PluginLink link, ParsedControl control) => link.Entry.InternalName + "/" + control.Id;

    private void BindTray()
    {
        var tray = _tray!;
        var link = client.Find(GluttonyName);
        var master = link?.Master;
        tray.Link = link;
        tray.Master = master;
        tray.Picks.Clear();
        for (var i = 0; i < tray.OnPick.Length; i++) tray.OnPick[i] = null;
        tray.AutoValue.IsVisible = false;

        if (link == null || link.Descriptor == null || master == null)
        {
            tray.AutoLabel.IsVisible = false;
            tray.AutoToggle.IsVisible = false;
            tray.AutoVisual.HideAll();
            tray.AutoWhy.IsVisible = false;
            tray.Hint.IsVisible = false;
            tray.Missing.IsVisible = true;
            foreach (var button in tray.Buttons) button.IsVisible = false;
            return;
        }

        tray.Missing.IsVisible = false;
        tray.AutoLabel.IsVisible = true;
        tray.AutoToggle.IsVisible = true;
        var state = link.ValueOf(master.Id);
        BindToggleButton(tray.AutoToggle, tray.AutoVisual, tray.AutoValue, link, master, state);
        var why = WhyText(link, master, state);
        tray.AutoWhy.String = why;
        tray.AutoWhy.IsVisible = why.Length > 0;
        tray.AutoWhy.TextColor = state?.Locked == true || _confirm.IsPending(Key(link, master), Now) ? HubTheme.Amber : HubTheme.Dim;
        LayoutTray(tray, hasWhy: why.Length > 0);
        tray.OnToggle = () => _actions.Enqueue(() => ActToggle(link, master));

        foreach (var control in link.Descriptor.Controls)
            if (control.Kind == ControlKind.Button && string.Equals(control.Group, HotbarGroup, StringComparison.Ordinal) && tray.Picks.Count < tray.Buttons.Count)
                tray.Picks.Add(control);

        tray.Hint.IsVisible = tray.Picks.Count > 0;
        for (var i = 0; i < tray.Buttons.Count; i++)
        {
            var button = tray.Buttons[i];
            if (i >= tray.Picks.Count) { button.IsVisible = false; continue; }
            var control = tray.Picks[i];
            button.IsVisible = true;
            button.String = control.Label;
            button.IsEnabled = IsUsable(link, link.ValueOf(control.Id));
            tray.OnPick[i] = () => _actions.Enqueue(() => ActInvoke(link, control, "Click a hotbar slot to place it. Escape cancels."));
        }
    }

    private void BindToggleButton(TextButtonNode button, ToggleVisual visual, TextNode value, PluginLink link, ParsedControl control, ParsedState? state)
    {
        var pending = _confirm.IsPending(Key(link, control), Now);
        var on = state?.Bool == true;
        var usable = IsUsable(link, state);
        button.String = "";
        button.LabelNode.TextColor = HubTheme.Grey;
        button.IsEnabled = usable;
        visual.Bind(pending, on, usable, button);
        value.String = pending ? "Confirm?" : ControlFormat.ValueText(control, state);
        value.TextColor = pending ? HubTheme.Amber : on ? HubTheme.Accent : HubTheme.Grey;
    }

    private string WhyText(PluginLink link, ParsedControl control, ParsedState? state)
    {
        if (_confirm.IsPending(Key(link, control), Now) && !string.IsNullOrEmpty(control.Confirm)) return "Click again to confirm";
        return state?.Why ?? "";
    }

    private void BindQuick()
    {
        BindTray();
        var n = 0;
        var columns = HubLayout.Columns(InnerWidth);
        foreach (var link in client.Links)
        {
            if (string.Equals(link.Entry.InternalName, GluttonyName, StringComparison.Ordinal)) continue;
            var master = link.Master;
            if (master == null || n >= _rows.Count) continue;
            var state = link.ValueOf(master.Id);
            var row = _rows[n];
            Layout(row, RowKind.Master, HubLayout.Cell(n, InnerWidth, columns, HubLayout.RowHeight, HubLayout.ColumnGap, HubLayout.TrayLeading, Gutter, HubLayout.RowGap));
            SetIcon(row, link.Entry.IconFile);
            row.Label.String = link.Entry.DisplayName;
            row.Label.TextColor = HubTheme.White;
            BindToggleButton(row.B1, row.Toggle1, row.Val, link, master, state);
            row.B2.String = "Settings";
            row.B2.IsEnabled = link.HasSettings(HotbarGroup);
            SetInfo(row, WhyText(link, master, state),
                state?.Locked == true || _confirm.IsPending(Key(link, master), Now) ? HubTheme.Amber : HubTheme.Dim);
            row.On1 = () => _actions.Enqueue(() => ActToggle(link, master));
            row.On2 = () => GoDetail(link);
            n++;
        }

        var bottom = HubLayout.RowsHeight(n, columns, HubLayout.RowHeight, HubLayout.ColumnGap, HubLayout.TrayLeading, HubLayout.RowGap);
        if (n == 0 && _empty != null)
        {
            Put(_empty, Gutter, HubLayout.TrayLeading, InnerWidth, 48f);
            _empty.String = "No other running plugin offers quick switches. Open Plugin library to see all 15 plugins and their live state.";
            _empty.IsVisible = true;
            bottom = HubLayout.TrayLeading + 56f;
        }
        SetContentHeight(bottom);
    }

    private void BindPlugins()
    {
        var n = 0;
        var columns = HubLayout.Columns(InnerWidth);
        foreach (var link in client.Links)
        {
            if (n >= _rows.Count) break;
            var row = _rows[n];
            Layout(row, RowKind.Plugin, HubLayout.Cell(n, InnerWidth, columns, HubLayout.RowHeight, HubLayout.ColumnGap, 0f, Gutter, HubLayout.RowGap));
            SetIcon(row, link.Entry.IconFile);
            row.Label.String = link.Entry.DisplayName;
            row.Label.TextColor = HubTheme.White;
            SetInfo(row, HubTheme.StatusText(link.State), HubTheme.StateColor(link.State));
            row.B1.String = "Settings";
            row.B1.IsEnabled = link.HasSettings(HotbarGroup);
            row.B2.String = "Open";
            row.B2.IsEnabled = link.IsRunning;
            var name = link.Entry.InternalName;
            row.On1 = () => GoDetail(link);
            row.On2 = () => _actions.Enqueue(() => monitor.Open(name));
            n++;
        }
        SetContentHeight(HubLayout.RowsHeight(n, columns, HubLayout.RowHeight, HubLayout.ColumnGap, 0f, HubLayout.RowGap));
    }

    private void BindDetail()
    {
        var link = _detail;
        if (link == null || link.Descriptor == null || !link.IsRunning)
        {
            _nextPage = Page.Plugins;
            return;
        }

        var n = 0;
        var y = 0f;
        var rows = DetailRows.Build(link.Descriptor, _rows.Count, HotbarGroup, out var hidden);
        foreach (var detailRow in rows)
        {
            if (n >= _rows.Count) break;
            var row = _rows[n++];
            if (detailRow.IsHeader)
            {
                Layout(row, RowKind.Group, new HubCell(Gutter, y, InnerWidth, HubLayout.GroupHeight));
                row.Label.String = "◆ " + detailRow.Header.ToUpperInvariant();
                row.Label.TextColor = HubTheme.Accent;
                y += HubLayout.GroupHeight + 4f;
                continue;
            }

            var control = detailRow.Control!;
            var state = link.ValueOf(control.Id);
            var usable = IsUsable(link, state);
            var kind = control.Kind switch
            {
                ControlKind.Toggle => RowKind.Toggle,
                ControlKind.Stepper => RowKind.Stepper,
                ControlKind.Choice => RowKind.Choice,
                _ => RowKind.Button,
            };
            Layout(row, kind, new HubCell(Gutter, y, InnerWidth, HubLayout.RowHeight));
            y += HubLayout.RowHeight + HubLayout.RowGap;
            row.Label.String = control.Label;
            row.Label.TextColor = HubTheme.White;
            SetInfo(row, WhyText(link, control, state),
                state?.Locked == true || _confirm.IsPending(Key(link, control), Now) ? HubTheme.Amber : HubTheme.Dim);

            switch (kind)
            {
                case RowKind.Toggle:
                    BindToggleButton(row.B1, row.Toggle1, row.Val, link, control, state);
                    row.On1 = () => _actions.Enqueue(() => ActToggle(link, control));
                    break;
                case RowKind.Stepper:
                    row.B1.String = "-";
                    row.B2.String = "+";
                    row.B1.IsEnabled = usable;
                    row.B2.IsEnabled = usable;
                    row.Val.String = ControlFormat.ValueText(control, state);
                    row.Val.TextColor = usable ? HubTheme.White : HubTheme.Dim;
                    row.On1 = () => _actions.Enqueue(() => ActStepper(link, control, -1));
                    row.On2 = () => _actions.Enqueue(() => ActStepper(link, control, +1));
                    break;
                case RowKind.Choice:
                    row.B1.String = "<";
                    row.B2.String = ">";
                    row.B1.IsEnabled = usable;
                    row.B2.IsEnabled = usable;
                    row.Val.String = ControlFormat.ValueText(control, state);
                    row.Val.TextColor = usable ? HubTheme.White : HubTheme.Dim;
                    row.On1 = () => _actions.Enqueue(() => ActChoice(link, control, -1));
                    row.On2 = () => _actions.Enqueue(() => ActChoice(link, control, +1));
                    break;
                default:
                    row.B1.String = _confirm.IsPending(Key(link, control), Now) ? "Confirm?" : "Run";
                    row.B1.LabelNode.TextColor = _confirm.IsPending(Key(link, control), Now) ? HubTheme.Amber : HubTheme.Grey;
                    row.B1.IsEnabled = usable;
                    row.On1 = () => _actions.Enqueue(() => ActInvoke(link, control, ""));
                    break;
            }
        }

        if (hidden > 0 && _empty != null)
        {
            Put(_empty, Gutter, y + 4f, InnerWidth, 48f);
            _empty.String = $"{hidden} more controls remain in the plugin's own window. Lazy Hub does not invent replacements for them.";
            _empty.IsVisible = true;
            y += 56f;
        }
        SetContentHeight(y);
    }

    // ===== actions (run from OnUpdate, never inside a click callback) ===================================

    private void GoDetail(PluginLink link)
    {
        _nextPage = Page.Detail;
        _nextDetail = link;
    }

    private void ActToggle(PluginLink link, ParsedControl control)
    {
        var state = link.ValueOf(control.Id);
        if (state == null) return;
        var target = !state.Bool;
        var needsConfirm = target && !string.IsNullOrEmpty(control.Confirm);
        if (!_confirm.ShouldProceed(Key(link, control), needsConfirm, Now))
        {
            Flash(ConfirmText(control), HubTheme.Amber);
            return;
        }
        Report(link, client.Set(link, control.Id, target), "");
    }

    private void ActStepper(PluginLink link, ParsedControl control, int direction)
    {
        var state = link.ValueOf(control.Id);
        if (state == null || state.Value is not double current) return;
        Report(link, client.Set(link, control.Id, ControlFormat.StepperNext(control, current, direction)), "");
    }

    private void ActChoice(PluginLink link, ParsedControl control, int direction)
    {
        var state = link.ValueOf(control.Id);
        if (state == null || state.Value is not double current) return;
        Report(link, client.Set(link, control.Id, ControlFormat.ChoiceNext(control, current, direction)), "");
    }

    private void ActInvoke(PluginLink link, ParsedControl control, string successNote)
    {
        if (!_confirm.ShouldProceed(Key(link, control), !string.IsNullOrEmpty(control.Confirm), Now))
        {
            Flash(ConfirmText(control), HubTheme.Amber);
            return;
        }
        Report(link, client.Invoke(link, control.Id), successNote);
    }

    private static string ConfirmText(ParsedControl control)
        => string.IsNullOrEmpty(control.Confirm)
            ? "Click again within a few seconds to confirm."
            : control.Confirm + " Click again within a few seconds to confirm.";

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
        PlaceFooter();
    }
}
