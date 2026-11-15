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
/// - Rounded cards, selection outlines and track toggles use the same proven image, text and button node types.
/// - The toolkit scrolling container clips overflow instead of hiding controls or covering the footer.
/// </summary>
internal sealed unsafe class HubAddon(PluginMonitor monitor, HubClient client, IconLoader icons, ITextureProvider textures, IPluginLog log) : NativeAddon
{
    private enum Page { Quick, Plugins, Detail }
    private enum RowKind { Hidden, Plugin, Master, Group, Toggle, Stepper, Choice, Button }

    private const string HotbarGroup = "Hotbar buttons";
    private const string GluttonyName = "GluttonyCombo";
    private const int PoolSize = 40;
    private const float ChromeH = 152f;
    private const float FlashH = 44f;
    private const float RowH = 60f;
    private const float GroupH = 32f;
    private const float TrayH = 188f;
    private const long FlashMs = 5000;
    private const long PollMs = 1000;
    private static readonly Vector2 ToggleSize = new(52f, 26f);

    private sealed class ToggleVisual
    {
        public required ImGuiImageNode OnTrack;
        public required ImGuiImageNode OffTrack;
        public required ImGuiImageNode PendingTrack;
        public required ImGuiImageNode Knob;

        public void Reset(TextButtonNode button)
        {
            button.BackgroundNode.IsVisible = true;
            OnTrack.IsVisible = false;
            OffTrack.IsVisible = false;
            PendingTrack.IsVisible = false;
            Knob.IsVisible = false;
        }

        public void Bind(bool pending, bool on, bool enabled, TextButtonNode button)
        {
            button.BackgroundNode.IsVisible = false;
            OnTrack.IsVisible = !pending && on;
            OffTrack.IsVisible = !pending && !on;
            PendingTrack.IsVisible = pending;
            Knob.IsVisible = true;
            Knob.Position = new Vector2(on ? 32f : 4f, 5f);
            var alpha = enabled ? 1f : 0.45f;
            OnTrack.Alpha = alpha;
            OffTrack.Alpha = alpha;
            PendingTrack.Alpha = alpha;
            Knob.Alpha = alpha;
        }
    }

    private sealed class Row
    {
        public required ImGuiImageNode Background;
        public required ImGuiImageNode Icon;
        public required TextNode Label;
        public required TextNode Info;
        public required TextButtonNode B1;
        public required ToggleVisual Toggle1;
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
            Background.IsVisible = false;
            Icon.IsVisible = false;
            Label.IsVisible = false;
            Info.IsVisible = false;
            B1.IsVisible = false;
            Val.IsVisible = false;
            B2.IsVisible = false;
            Toggle1.Reset(B1);
            B1.String = "";
            B2.String = "";
            B1.LabelNode.TextColor = HubTheme.White;
            B2.LabelNode.TextColor = HubTheme.White;
            Val.AlignmentType = AlignmentType.Center;
        }
    }

    private sealed class Tray
    {
        public ImGuiImageNode Bg = null!;
        public TextNode Title = null!;
        public TextNode AutoLabel = null!;
        public TextButtonNode AutoToggle = null!;
        public ToggleVisual AutoVisual = null!;
        public TextNode AutoValue = null!;
        public TextNode AutoWhy = null!;
        public TextNode Hint = null!;
        public TextNode Missing = null!;
        public readonly List<TextButtonNode> Buttons = new();
        public PluginLink? Link;
        public ParsedControl? Master;
        public readonly List<ParsedControl> Picks = new();
        public Action? OnToggle;
        public readonly Action?[] OnPick = new Action?[8];

        public void SetVisible(bool visible)
        {
            Bg.IsVisible = visible;
            Title.IsVisible = visible;
            if (visible) return;
            AutoLabel.IsVisible = false;
            AutoToggle.IsVisible = false;
            AutoVisual.OnTrack.IsVisible = false;
            AutoVisual.OffTrack.IsVisible = false;
            AutoVisual.PendingTrack.IsVisible = false;
            AutoVisual.Knob.IsVisible = false;
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
    private ImGuiImageNode? _topBar;
    private ImGuiImageNode? _navBar;
    private ImGuiImageNode? _statusBg;
    private TextNode? _brandMark;
    private TextNode? _brand;
    private TextNode? _status;
    private ImGuiImageNode? _quickBase;
    private ImGuiImageNode? _pluginsBase;
    private ImGuiImageNode? _quickSelected;
    private ImGuiImageNode? _pluginsSelected;
    private TextButtonNode? _tabQuick;
    private TextButtonNode? _tabPlugins;
    private TextButtonNode? _back;
    private TextNode? _pageMark;
    private TextNode? _pageTitle;
    private TextNode? _pagePurpose;
    private TextNode? _pageCount;
    private TextNode? _empty;
    private TextNode? _flash;

    private Vector2 _origin;
    private Vector2 _lastContentSize;
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
    private float ContentWidth => MathF.Max(120f, _width - 12f);

    // ===== lifecycle ====================================================================================

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
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
        _height = Math.Max(c.Y - ChromeH - FlashH, 120f);
        _origin = o + new Vector2(0f, ChromeH);

        _topBar = MakePanel(o, new Vector2(_width, 40f), HubTheme.Backdrop, HubTheme.Border, true);
        _brandMark = MakeText(o + new Vector2(12f, 5f), new Vector2(30f, 30f), 24, HubTheme.Accent, "◆");
        _brand = MakeText(o + new Vector2(46f, 9f), new Vector2(210f, 24f), HubTheme.FontTitle, HubTheme.White, "Lazy Hub");
        _statusBg = MakePanel(o + new Vector2(_width - 174f, 8f), new Vector2(162f, 24f), HubTheme.Tray, HubTheme.Border, true);
        _status = MakeText(o + new Vector2(_width - 164f, 11f), new Vector2(142f, 18f), HubTheme.FontSecondary, HubTheme.Grey, "");

        _navBar = MakePanel(o + new Vector2(0f, 48f), new Vector2(_width, 36f), HubTheme.Backdrop);
        _quickBase = MakePanel(o + new Vector2(0f, 52f), new Vector2(160f, 28f), HubTheme.Tray, HubTheme.Border, true);
        _pluginsBase = MakePanel(o + new Vector2(168f, 52f), new Vector2(160f, 28f), HubTheme.Tray, HubTheme.Border, true);
        _quickSelected = MakePanel(o + new Vector2(0f, 52f), new Vector2(160f, 28f), HubTheme.Selected, HubTheme.Accent, true);
        _pluginsSelected = MakePanel(o + new Vector2(168f, 52f), new Vector2(160f, 28f), HubTheme.Selected, HubTheme.Accent, true);
        _tabQuick = MakeButton(o + new Vector2(0f, 52f), new Vector2(160f, 28f), "Quick controls", () => _nextPage = Page.Quick);
        _tabPlugins = MakeButton(o + new Vector2(168f, 52f), new Vector2(160f, 28f), "Plugin library", () => _nextPage = Page.Plugins);
        _tabQuick.BackgroundNode.IsVisible = false;
        _tabPlugins.BackgroundNode.IsVisible = false;
        _back = MakeButton(o + new Vector2(_width - 120f, 52f), new Vector2(120f, 28f), "< Plugin library", () => _nextPage = Page.Plugins);

        _pageMark = MakeText(o + new Vector2(4f, 92f), new Vector2(34f, 32f), 22, HubTheme.Accent, "◆");
        _pageTitle = MakeText(o + new Vector2(44f, 90f), new Vector2(_width * 0.56f, 26f), HubTheme.FontTitle, HubTheme.White, "");
        _pagePurpose = MakeText(o + new Vector2(44f, 118f), new Vector2(_width * 0.58f, 20f), HubTheme.FontSecondary, HubTheme.Grey, "");
        _pageCount = MakeText(o + new Vector2(_width * 0.62f, 99f), new Vector2((_width * 0.38f) - 8f, 20f), HubTheme.FontSecondary, HubTheme.Grey, "");
        _pageCount.AlignmentType = AlignmentType.Right;
        _pageTitle.TextFlags = TextFlags.Ellipsis;
        _pagePurpose.TextFlags = TextFlags.Ellipsis;
        _pageCount.TextFlags = TextFlags.Ellipsis;
        _status.TextFlags = TextFlags.Ellipsis;

        _scroll = new ScrollingNode<ResNode>
        {
            Position = _origin,
            Size = new Vector2(_width, _height),
            AutoHideScrollBar = true,
            ScrollSpeed = 48,
        };
        _scroll.AttachNode(this);
        _scroll.ContentNode.Size = new Vector2(ContentWidth, _height);

        _tray = BuildTray(_scroll.ContentNode);
        for (var i = 0; i < PoolSize; i++) _rows.Add(MakeRow(_scroll.ContentNode));
        _empty = MakeText(Vector2.Zero, new Vector2(ContentWidth - 16f, 48f), HubTheme.FontSecondary, HubTheme.Dim, "", _scroll.ContentNode);
        _empty.TextFlags = TextFlags.WordWrap | TextFlags.MultiLine;

        _flash = MakeText(_origin + new Vector2(4f, _height + 6f), new Vector2(_width - 8f, FlashH - 8f), HubTheme.FontCaption, HubTheme.Amber, "");
        _flash.TextFlags = TextFlags.WordWrap | TextFlags.MultiLine;

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
        _topBar = null;
        _navBar = null;
        _statusBg = null;
        _brandMark = null;
        _brand = null;
        _status = null;
        _quickBase = null;
        _pluginsBase = null;
        _quickSelected = null;
        _pluginsSelected = null;
        _tabQuick = null;
        _tabPlugins = null;
        _back = null;
        _pageMark = null;
        _pageTitle = null;
        _pagePurpose = null;
        _pageCount = null;
        _empty = null;
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

    private static bool RoundedContains(int x, int y, int size, int inset, int radius)
    {
        if (x < inset || y < inset || x >= size - inset || y >= size - inset) return false;
        if (radius <= 0) return true;
        var left = inset + radius;
        var right = size - inset - radius - 1;
        var top = inset + radius;
        var bottom = size - inset - radius - 1;
        var cx = x < left ? left : x > right ? right : x;
        var cy = y < top ? top : y > bottom ? bottom : y;
        var dx = x - cx;
        var dy = y - cy;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    private ImGuiImageNode MakePanel(Vector2 position, Vector2 size, Vector4 color, Vector4? border = null, bool rounded = false,
        NodeBase? parent = null, NodePosition? relative = null)
    {
        const int textureSize = 32;
        var node = new ImGuiImageNode
        {
            Position = position,
            Size = size,
            TextureSize = new Vector2(textureSize, textureSize),
            FitTexture = true,
        };
        if (parent is null) node.AttachNode(this);
        else if (relative is { } relation) node.AttachNode(parent, relation);
        else node.AttachNode(parent);

        var bgra = new byte[textureSize * textureSize * 4];
        for (var y = 0; y < textureSize; y++)
        for (var x = 0; x < textureSize; x++)
        {
            if (!RoundedContains(x, y, textureSize, 0, rounded ? 7 : 0)) continue;
            var isBorder = border.HasValue && !RoundedContains(x, y, textureSize, 1, rounded ? 6 : 0);
            var c = isBorder ? border!.Value : color;
            var i = ((y * textureSize) + x) * 4;
            bgra[i + 0] = (byte)Math.Clamp((int)MathF.Round(c.Z * 255f), 0, 255);
            bgra[i + 1] = (byte)Math.Clamp((int)MathF.Round(c.Y * 255f), 0, 255);
            bgra[i + 2] = (byte)Math.Clamp((int)MathF.Round(c.X * 255f), 0, 255);
            bgra[i + 3] = (byte)Math.Clamp((int)MathF.Round(c.W * 255f), 0, 255);
        }
        node.LoadTexture(textures.CreateFromRaw(RawImageSpecification.Bgra32(textureSize, textureSize), bgra, "LazyHubPanel"));
        return node;
    }

    private TextNode MakeText(Vector2 position, Vector2 size, uint fontSize, Vector4 color, string text, NodeBase? parent = null)
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
        if (parent is null) node.AttachNode(this);
        else node.AttachNode(parent);
        return node;
    }

    private TextButtonNode MakeButton(Vector2 position, Vector2 size, string text, Action onClick, NodeBase? parent = null)
    {
        var node = new TextButtonNode
        {
            Position = position,
            Size = size,
            String = text,
            OnClick = onClick,
        };
        node.LabelNode.TextColor = HubTheme.White;
        node.LabelNode.TextFlags = TextFlags.Ellipsis;
        if (parent is null) node.AttachNode(this);
        else node.AttachNode(parent);
        return node;
    }

    private ToggleVisual MakeToggleVisual(TextButtonNode button)
    {
        button.BackgroundNode.IsVisible = false;
        var on = MakePanel(Vector2.Zero, ToggleSize, HubTheme.Selected, HubTheme.Accent, true, button.BackgroundNode, NodePosition.AfterTarget);
        var off = MakePanel(Vector2.Zero, ToggleSize, HubTheme.Tray, HubTheme.Border, true, on, NodePosition.AfterTarget);
        var pending = MakePanel(Vector2.Zero, ToggleSize, HubTheme.Pending, HubTheme.Amber, true, off, NodePosition.AfterTarget);
        var knob = MakePanel(new Vector2(4f, 5f), new Vector2(16f, 16f), HubTheme.White, null, true, pending, NodePosition.AfterTarget);
        var visual = new ToggleVisual { OnTrack = on, OffTrack = off, PendingTrack = pending, Knob = knob };
        visual.Reset(button);
        return visual;
    }

    private Row MakeRow(ResNode parent)
    {
        Row? row = null;
        var background = MakePanel(Vector2.Zero, new Vector2(300f, RowH), HubTheme.Tray, HubTheme.Border, true, parent);
        var icon = new ImGuiImageNode
        {
            Size = new Vector2(28f, 28f),
            TextureSize = new Vector2(64f, 64f),
            FitTexture = true,
        };
        icon.AttachNode(parent);
        var label = MakeText(Vector2.Zero, new Vector2(240f, 20f), HubTheme.FontBody, HubTheme.White, "", parent);
        var info = MakeText(Vector2.Zero, new Vector2(200f, 18f), HubTheme.FontCaption, HubTheme.Dim, "", parent);
        var b1 = MakeButton(Vector2.Zero, new Vector2(100f, 24f), "", () => row?.On1?.Invoke(), parent);
        var toggle1 = MakeToggleVisual(b1);
        var val = MakeText(Vector2.Zero, new Vector2(120f, 18f), HubTheme.FontBody, HubTheme.White, "", parent);
        val.AlignmentType = AlignmentType.Center;
        var b2 = MakeButton(Vector2.Zero, new Vector2(100f, 24f), "", () => row?.On2?.Invoke(), parent);
        label.TextFlags = TextFlags.Ellipsis;
        info.TextFlags = TextFlags.Ellipsis;
        val.TextFlags = TextFlags.Ellipsis;
        row = new Row { Background = background, Icon = icon, Label = label, Info = info, B1 = b1, Toggle1 = toggle1, Val = val, B2 = b2 };
        row.Reset();
        return row;
    }

    private Tray BuildTray(ResNode parent)
    {
        var tray = new Tray();
        tray.Bg = MakePanel(Vector2.Zero, new Vector2(ContentWidth, TrayH), HubTheme.Tray, HubTheme.Border, true, parent);
        tray.Title = MakeText(new Vector2(14f, 10f), new Vector2(300f, 24f), HubTheme.FontTitle, HubTheme.White, "◆ Gluttony Combo", parent);
        tray.AutoLabel = MakeText(new Vector2(14f, 42f), new Vector2(180f, 22f), HubTheme.FontBody, HubTheme.White, "Auto-Rotation", parent);
        tray.AutoToggle = MakeButton(Vector2.Zero, ToggleSize, "", () => tray.OnToggle?.Invoke(), parent);
        tray.AutoVisual = MakeToggleVisual(tray.AutoToggle);
        tray.AutoValue = MakeText(Vector2.Zero, new Vector2(64f, 20f), HubTheme.FontBody, HubTheme.White, "", parent);
        tray.AutoValue.AlignmentType = AlignmentType.Right;
        tray.AutoWhy = MakeText(new Vector2(14f, 68f), new Vector2(ContentWidth - 28f, 18f), HubTheme.FontCaption, HubTheme.Dim, "", parent);
        tray.AutoWhy.TextFlags = TextFlags.Ellipsis;
        tray.Hint = MakeText(new Vector2(14f, 92f), new Vector2(ContentWidth - 28f, 18f), HubTheme.FontCaption, HubTheme.Dim,
            "Hotbar buttons: click one, then click a hotbar slot to place it. Escape cancels.", parent);
        tray.Hint.TextFlags = TextFlags.Ellipsis;
        tray.Missing = MakeText(new Vector2(14f, 44f), new Vector2(ContentWidth - 28f, 44f), HubTheme.FontSecondary, HubTheme.Dim,
            "Missing Gluttony Combo controls. Start Gluttony Combo, then reopen Quick controls after it finishes loading.", parent);
        tray.Missing.TextFlags = TextFlags.WordWrap | TextFlags.MultiLine;
        for (var i = 0; i < 8; i++)
        {
            var index = i;
            tray.Buttons.Add(MakeButton(Vector2.Zero, new Vector2(100f, 26f), "", () => tray.OnPick[index]?.Invoke(), parent));
        }
        LayoutTray(tray);
        tray.SetVisible(false);
        return tray;
    }

    // ===== layout =======================================================================================

    private void LayoutChrome()
    {
        if (_scroll is null || _topBar is null || _navBar is null || _statusBg is null || _brandMark is null || _brand is null ||
            _status is null || _quickBase is null || _pluginsBase is null || _quickSelected is null || _pluginsSelected is null ||
            _tabQuick is null || _tabPlugins is null || _back is null || _pageMark is null || _pageTitle is null ||
            _pagePurpose is null || _pageCount is null || _flash is null) return;

        var o = ContentStartPosition;
        var c = ContentSize;
        _width = c.X;
        _height = Math.Max(c.Y - ChromeH - FlashH, 120f);
        _origin = o + new Vector2(0f, ChromeH);

        _topBar.Position = o;
        _topBar.Size = new Vector2(_width, 40f);
        _brandMark.Position = o + new Vector2(12f, 5f);
        _brand.Position = o + new Vector2(46f, 9f);
        _statusBg.Position = o + new Vector2(_width - 174f, 8f);
        _status.Position = o + new Vector2(_width - 164f, 11f);
        _navBar.Position = o + new Vector2(0f, 48f);
        _navBar.Size = new Vector2(_width, 36f);
        _quickBase.Position = o + new Vector2(0f, 52f);
        _pluginsBase.Position = o + new Vector2(168f, 52f);
        _quickSelected.Position = _quickBase.Position;
        _pluginsSelected.Position = _pluginsBase.Position;
        _tabQuick.Position = _quickBase.Position;
        _tabPlugins.Position = _pluginsBase.Position;
        _back.Position = o + new Vector2(_width - 120f, 52f);
        _pageMark.Position = o + new Vector2(4f, 92f);
        _pageTitle.Position = o + new Vector2(44f, 90f);
        _pageTitle.Size = new Vector2(Math.Max(80f, _width * 0.56f), 26f);
        _pagePurpose.Position = o + new Vector2(44f, 118f);
        _pagePurpose.Size = new Vector2(Math.Max(80f, _width * 0.58f), 20f);
        _pageCount.Position = o + new Vector2(_width * 0.62f, 99f);
        _pageCount.Size = new Vector2(Math.Max(80f, (_width * 0.38f) - 8f), 20f);
        _scroll.Position = _origin;
        _scroll.Size = new Vector2(_width, _height);
        _flash.Position = _origin + new Vector2(4f, _height + 6f);
        _flash.Size = new Vector2(_width - 8f, FlashH - 8f);
        if (_tray is not null) LayoutTray(_tray);
    }

    private void LayoutTray(Tray tray)
    {
        var width = ContentWidth;
        tray.Bg.Position = Vector2.Zero;
        tray.Bg.Size = new Vector2(width, TrayH);
        tray.Title.Size = new Vector2(Math.Max(120f, width - 28f), 24f);
        tray.AutoToggle.Position = new Vector2(width - 66f, 38f);
        tray.AutoToggle.Size = ToggleSize;
        tray.AutoValue.Position = new Vector2(width - 140f, 42f);
        tray.AutoWhy.Size = new Vector2(width - 28f, 18f);
        tray.Hint.Size = new Vector2(width - 28f, 18f);
        tray.Missing.Size = new Vector2(width - 28f, 44f);
        var bw = (width - 28f - (3f * 8f)) / 4f;
        for (var i = 0; i < tray.Buttons.Count; i++)
        {
            var x = 14f + ((i % 4) * (bw + 8f));
            var y = 118f + ((i / 4) * 32f);
            tray.Buttons[i].Position = new Vector2(x, y);
            tray.Buttons[i].Size = new Vector2(bw, 26f);
        }
    }

    private static void Put(NodeBase node, float x, float y, float w, float h)
    {
        node.Position = new Vector2(x, y);
        node.Size = new Vector2(w, h);
    }

    private void Layout(Row row, RowKind kind, HubCell cell)
    {
        row.Kind = kind;
        row.Background.IsVisible = kind != RowKind.Group;
        row.Background.Position = new Vector2(cell.X, cell.Y);
        row.Background.Size = new Vector2(cell.Width, cell.Height);
        row.Label.IsVisible = true;
        var x = cell.X;
        var y = cell.Y;
        var w = cell.Width;

        switch (kind)
        {
            case RowKind.Plugin:
                Put(row.Icon, x + 12, y + 16, 28, 28); row.Icon.IsVisible = true;
                Put(row.Label, x + 50, y + 9, Math.Max(40, w - 212), 20);
                Put(row.Info, x + 50, y + 33, Math.Max(40, w - 212), 18); row.Info.IsVisible = true;
                Put(row.B1, x + w - 152, y + 17, 76, 26); row.B1.IsVisible = true;
                Put(row.B2, x + w - 68, y + 17, 56, 26); row.B2.IsVisible = true;
                break;
            case RowKind.Master:
                Put(row.Icon, x + 12, y + 16, 28, 28); row.Icon.IsVisible = true;
                Put(row.Label, x + 50, y + 9, Math.Max(40, w - 272), 20);
                Put(row.Info, x + 50, y + 33, Math.Max(40, w - 272), 18); row.Info.IsVisible = true;
                Put(row.B2, x + w - 220, y + 17, 72, 26); row.B2.IsVisible = true;
                Put(row.Val, x + w - 140, y + 20, 64, 20); row.Val.IsVisible = true; row.Val.AlignmentType = AlignmentType.Right;
                Put(row.B1, x + w - 66, y + 17, ToggleSize.X, ToggleSize.Y); row.B1.IsVisible = true;
                break;
            case RowKind.Group:
                Put(row.Label, x + 4, y + 5, Math.Max(40, w - 8), 22);
                break;
            case RowKind.Toggle:
                Put(row.Label, x + 14, y + 9, Math.Max(40, w - 176), 20);
                Put(row.Info, x + 14, y + 33, Math.Max(40, w - 176), 18); row.Info.IsVisible = true;
                Put(row.Val, x + w - 140, y + 20, 64, 20); row.Val.IsVisible = true; row.Val.AlignmentType = AlignmentType.Right;
                Put(row.B1, x + w - 66, y + 17, ToggleSize.X, ToggleSize.Y); row.B1.IsVisible = true;
                break;
            case RowKind.Stepper:
            case RowKind.Choice:
                Put(row.Label, x + 14, y + 9, Math.Max(40, w - 234), 20);
                Put(row.Info, x + 14, y + 33, Math.Max(40, w - 234), 18); row.Info.IsVisible = true;
                Put(row.B1, x + w - 208, y + 17, 32, 26); row.B1.IsVisible = true;
                Put(row.Val, x + w - 168, y + 20, 124, 20); row.Val.IsVisible = true;
                Put(row.B2, x + w - 40, y + 17, 32, 26); row.B2.IsVisible = true;
                break;
            case RowKind.Button:
                Put(row.Label, x + 14, y + 9, Math.Max(40, w - 112), 20);
                Put(row.Info, x + 14, y + 33, Math.Max(40, w - 112), 18); row.Info.IsVisible = true;
                Put(row.B1, x + w - 86, y + 17, 72, 26); row.B1.IsVisible = true;
                break;
        }
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
        _scroll.ContentNode.Size = new Vector2(ContentWidth, Math.Max(_height, contentHeight + 8f));
        _scroll.RecalculateSizes();
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
    }

    private void BindShell()
    {
        if (_pageTitle is null || _pagePurpose is null || _pageCount is null || _status is null) return;
        var loaded = client.Links.Count(link => link.IsRunning);
        var installed = client.Links.Count(link => link.State != PluginState.NotInstalled);
        var quick = client.Links.Count(link => link.Master is not null);
        var state = OverallState(loaded, installed);
        _status.String = state == UiState.Missing ? "● Missing plugins" : "● " + Copy.Word(state);
        _status.TextColor = Copy.MarkColor(state);

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

        if (link == null || link.Descriptor == null || master == null)
        {
            tray.AutoLabel.IsVisible = false;
            tray.AutoToggle.IsVisible = false;
            tray.AutoVisual.OnTrack.IsVisible = false;
            tray.AutoVisual.OffTrack.IsVisible = false;
            tray.AutoVisual.PendingTrack.IsVisible = false;
            tray.AutoVisual.Knob.IsVisible = false;
            tray.AutoValue.IsVisible = false;
            tray.AutoWhy.IsVisible = false;
            tray.Hint.IsVisible = false;
            tray.Missing.IsVisible = true;
            foreach (var button in tray.Buttons) button.IsVisible = false;
            return;
        }

        tray.Missing.IsVisible = false;
        tray.AutoLabel.IsVisible = true;
        tray.AutoToggle.IsVisible = true;
        tray.AutoValue.IsVisible = true;
        tray.AutoWhy.IsVisible = true;
        var state = link.ValueOf(master.Id);
        BindToggleButton(tray.AutoToggle, tray.AutoVisual, tray.AutoValue, link, master, state);
        tray.AutoWhy.String = WhyText(link, master, state);
        tray.AutoWhy.TextColor = state?.Locked == true || _confirm.IsPending(Key(link, master), Now) ? HubTheme.Amber : HubTheme.Dim;
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
        button.LabelNode.TextColor = HubTheme.White;
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
        foreach (var link in client.Links)
        {
            if (string.Equals(link.Entry.InternalName, GluttonyName, StringComparison.Ordinal)) continue;
            var master = link.Master;
            if (master == null || n >= _rows.Count) continue;
            var state = link.ValueOf(master.Id);
            var row = _rows[n];
            var columns = HubLayout.Columns(ContentWidth);
            Layout(row, RowKind.Master, HubLayout.Cell(n, ContentWidth, columns, RowH, leading: TrayH + 12f));
            SetIcon(row, link.Entry.IconFile);
            row.Label.String = link.Entry.DisplayName;
            row.Label.TextColor = HubTheme.White;
            BindToggleButton(row.B1, row.Toggle1, row.Val, link, master, state);
            row.B2.String = "Settings";
            row.B2.IsEnabled = link.HasSettings(HotbarGroup);
            row.Info.String = WhyText(link, master, state);
            row.Info.TextColor = state?.Locked == true || _confirm.IsPending(Key(link, master), Now) ? HubTheme.Amber : HubTheme.Dim;
            row.On1 = () => _actions.Enqueue(() => ActToggle(link, master));
            row.On2 = () => GoDetail(link);
            n++;
        }

        var columnsUsed = HubLayout.Columns(ContentWidth);
        var bottom = HubLayout.RowsHeight(n, columnsUsed, RowH, leading: TrayH + 12f);
        if (n == 0 && _empty != null)
        {
            _empty.Position = new Vector2(8f, TrayH + 16f);
            _empty.Size = new Vector2(ContentWidth - 16f, 48f);
            _empty.String = "No other running plugin offers quick switches. Open Plugin library to see all 15 plugins and their live state.";
            _empty.IsVisible = true;
            bottom = TrayH + 72f;
        }
        SetContentHeight(bottom);
    }

    private void BindPlugins()
    {
        var n = 0;
        var columns = HubLayout.Columns(ContentWidth);
        foreach (var link in client.Links)
        {
            if (n >= _rows.Count) break;
            var row = _rows[n];
            Layout(row, RowKind.Plugin, HubLayout.Cell(n, ContentWidth, columns, RowH));
            SetIcon(row, link.Entry.IconFile);
            row.Label.String = link.Entry.DisplayName;
            row.Label.TextColor = HubTheme.White;
            row.Info.String = HubTheme.StatusText(link.State);
            row.Info.TextColor = HubTheme.StateColor(link.State);
            row.B1.String = "Settings";
            row.B1.IsEnabled = link.HasSettings(HotbarGroup);
            row.B2.String = "Open";
            row.B2.IsEnabled = link.IsRunning;
            var name = link.Entry.InternalName;
            row.On1 = () => GoDetail(link);
            row.On2 = () => _actions.Enqueue(() => monitor.Open(name));
            n++;
        }
        SetContentHeight(HubLayout.RowsHeight(n, columns, RowH));
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
                Layout(row, RowKind.Group, new HubCell(0, y, ContentWidth, GroupH));
                row.Label.String = "◆ " + detailRow.Header.ToUpperInvariant();
                row.Label.TextColor = HubTheme.Accent;
                y += GroupH + 4f;
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
            Layout(row, kind, new HubCell(0, y, ContentWidth, RowH));
            y += RowH + 8f;
            row.Label.String = control.Label;
            row.Label.TextColor = HubTheme.White;
            row.Info.String = WhyText(link, control, state);
            row.Info.TextColor = state?.Locked == true || _confirm.IsPending(Key(link, control), Now) ? HubTheme.Amber : HubTheme.Dim;

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
                    row.B1.LabelNode.TextColor = _confirm.IsPending(Key(link, control), Now) ? HubTheme.Amber : HubTheme.White;
                    row.B1.IsEnabled = usable;
                    row.On1 = () => _actions.Enqueue(() => ActInvoke(link, control, ""));
                    break;
            }
        }

        if (hidden > 0 && _empty != null)
        {
            _empty.Position = new Vector2(8f, y + 4f);
            _empty.Size = new Vector2(ContentWidth - 16f, 48f);
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
    }
}
