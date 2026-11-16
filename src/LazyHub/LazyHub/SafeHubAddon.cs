using System;
using System.Diagnostics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using LalaUiKit;
using LazyHub.Core;

namespace LazyHub;

/// <summary>
/// Safe-mode recovery window (`/lazy safe`). It deliberately retains the original no-panel, no-tint, no-hub-IPC
/// implementation: only Dalamud's plugin list and Gluttony's existing read-only probe are consulted. The approved
/// navigation, accent, headings, live counts and state vocabulary are applied with text and button nodes alone.
/// NativeAddon allocates on every Open, so asynchronous icons remain guarded by <see cref="_generation"/>.
/// </summary>
internal sealed unsafe class SafeHubAddon(PluginMonitor monitor, GluttonyProbe gluttony, IconLoader icons) : NativeAddon
{
    private const float TabWidth = 144f;
    private const float TabHeight = 28f;
    private const float TabGap = 8f;
    private const float BodyTop = 144f;
    private const float CellHeight = 60f;
    private const float ColumnGap = 12f;
    private const float IconSize = 44f;
    private const float OpenWidth = 64f;

    private sealed class Row
    {
        public required CatalogEntry Entry;
        public required TextNode Status;
        public required TextButtonNode Open;
    }

    private readonly List<Row> _rows = [];
    private readonly Stopwatch _sinceRefresh = Stopwatch.StartNew();
    private ResNode? _pluginsBody;
    private ResNode? _quickBody;
    private TextNode? _quickState;
    private TextButtonNode? _pluginsTab;
    private TextButtonNode? _quickTab;
    private TextNode? _pageTitle;
    private TextNode? _pagePurpose;
    private TextNode? _pageCount;
    private TextNode? _status;
    private bool _showPlugins = true;
    private int _generation;

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        // Safe mode uses the same native header Close path; no second content button and no change to the recovery lifecycle.
        DisableClose = false;
        if (WindowNode is KamiToolKit.Nodes.WindowNode nativeWindow)
            nativeWindow.ShowCloseButton = true;

        _generation++;
        _rows.Clear();
        _showPlugins = true;
        var origin = ContentStartPosition;
        var content = ContentSize;

        new TextNode
        {
            Position = origin + new Vector2(4f, 0f),
            Size = new Vector2(32f, 32f),
            FontSize = 24,
            FontType = FontType.Axis,
            TextColor = HubTheme.Accent,
            String = "◆",
        }.AttachNode(this);
        new TextNode
        {
            Position = origin + new Vector2(42f, 5f),
            Size = new Vector2(260f, 26f),
            FontSize = HubTheme.FontTitle,
            FontType = FontType.Axis,
            TextColor = HubTheme.White,
            String = "Lazy Hub · safe mode",
        }.AttachNode(this);
        _status = new TextNode
        {
            Position = origin + new Vector2(Math.Max(320f, content.X - 180f), 8f),
            Size = new Vector2(172f, 20f),
            FontSize = HubTheme.FontSecondary,
            FontType = FontType.Axis,
            TextColor = HubTheme.Grey,
            AlignmentType = AlignmentType.Right,
            TextFlags = TextFlags.Ellipsis,
            String = "",
        };
        _status.AttachNode(this);

        _pluginsTab = new TextButtonNode
        {
            Position = origin + new Vector2(0f, 44f),
            Size = new Vector2(TabWidth, TabHeight),
            String = "● Plugin library",
            OnClick = () => SelectTab(plugins: true),
        };
        _pluginsTab.LabelNode.TextColor = HubTheme.Accent;
        _pluginsTab.AttachNode(this);
        _quickTab = new TextButtonNode
        {
            Position = origin + new Vector2(TabWidth + TabGap, 44f),
            Size = new Vector2(TabWidth, TabHeight),
            String = "○ Quick state",
            OnClick = () => SelectTab(plugins: false),
        };
        _quickTab.LabelNode.TextColor = HubTheme.Grey;
        _quickTab.AttachNode(this);

        new TextNode
        {
            Position = origin + new Vector2(2f, 88f),
            Size = new Vector2(32f, 30f),
            FontSize = 22,
            FontType = FontType.Axis,
            TextColor = HubTheme.Accent,
            String = "◆",
        }.AttachNode(this);
        _pageTitle = new TextNode
        {
            Position = origin + new Vector2(42f, 86f),
            Size = new Vector2(content.X * 0.56f, 24f),
            FontSize = HubTheme.FontTitle,
            FontType = FontType.Axis,
            TextColor = HubTheme.White,
            TextFlags = TextFlags.Ellipsis,
            String = "Plugin library",
        };
        _pageTitle.AttachNode(this);
        _pagePurpose = new TextNode
        {
            Position = origin + new Vector2(42f, 114f),
            Size = new Vector2(content.X - 46f, 20f),
            FontSize = HubTheme.FontSecondary,
            FontType = FontType.Axis,
            TextColor = HubTheme.Grey,
            TextFlags = TextFlags.Ellipsis,
            String = "",
        };
        _pagePurpose.AttachNode(this);
        _pageCount = new TextNode
        {
            Position = origin + new Vector2(content.X * 0.64f, 91f),
            Size = new Vector2((content.X * 0.36f) - 4f, 20f),
            FontSize = HubTheme.FontSecondary,
            FontType = FontType.Axis,
            TextColor = HubTheme.Grey,
            AlignmentType = AlignmentType.Right,
            TextFlags = TextFlags.Ellipsis,
            String = "",
        };
        _pageCount.AttachNode(this);

        var bodyPosition = origin + new Vector2(0f, BodyTop);
        var bodySize = new Vector2(content.X, Math.Max(content.Y - BodyTop, 0f));
        _pluginsBody = new ResNode { Position = bodyPosition, Size = bodySize };
        _pluginsBody.AttachNode(this);
        _quickBody = new ResNode { Position = bodyPosition, Size = bodySize, IsVisible = false };
        _quickBody.AttachNode(this);

        BuildPluginGrid(_pluginsBody, bodySize.X);
        BuildQuickCard(_quickBody, bodySize.X);
        UpdatePageCopy();
        Refresh();
        _sinceRefresh.Restart();
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        if (_sinceRefresh.ElapsedMilliseconds < 1000) return;
        _sinceRefresh.Restart();
        Refresh();
    }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        _generation++;
        _rows.Clear();
        _pluginsBody = null;
        _quickBody = null;
        _quickState = null;
        _pluginsTab = null;
        _quickTab = null;
        _pageTitle = null;
        _pagePurpose = null;
        _pageCount = null;
        _status = null;
    }

    private void SelectTab(bool plugins)
    {
        _showPlugins = plugins;
        if (_pluginsBody != null) _pluginsBody.IsVisible = plugins;
        if (_quickBody != null) _quickBody.IsVisible = !plugins;
        if (_pluginsTab != null)
        {
            _pluginsTab.String = plugins ? "● Plugin library" : "○ Plugin library";
            _pluginsTab.LabelNode.TextColor = plugins ? HubTheme.Accent : HubTheme.Grey;
        }
        if (_quickTab != null)
        {
            _quickTab.String = !plugins ? "● Quick state" : "○ Quick state";
            _quickTab.LabelNode.TextColor = !plugins ? HubTheme.Accent : HubTheme.Grey;
        }
        UpdatePageCopy();
    }

    private void UpdatePageCopy()
    {
        if (_pageTitle != null) _pageTitle.String = _showPlugins ? "Plugin library" : "Quick state";
        if (_pagePurpose != null)
            _pagePurpose.String = _showPlugins
                ? "Read-only recovery list; Open uses Dalamud's existing plugin window."
                : "Read-only Auto-Rotation state; no control writes are sent.";
        if (_pageCount != null) _pageCount.String = _showPlugins ? "15 plugins" : "Read only";
    }

    private void BuildPluginGrid(ResNode body, float width)
    {
        var cellWidth = (width - ColumnGap) / 2f;
        var textWidth = Math.Max(cellWidth - IconSize - 8f - OpenWidth - 8f, 40f);
        var generation = _generation;

        for (var i = 0; i < Catalog.Plugins.Count; i++)
        {
            var entry = Catalog.Plugins[i];
            var cell = new Vector2((i % 2) * (cellWidth + ColumnGap), (i / 2) * CellHeight);
            var icon = new ImGuiImageNode
            {
                Position = cell + new Vector2(0f, 6f),
                Size = new Vector2(IconSize, IconSize),
                TextureSize = new Vector2(64f, 64f),
                FitTexture = true,
            };
            icon.AttachNode(body);
            icons.LoadInto(icon, entry.IconFile, () => generation == _generation && IsOpen);

            var name = new TextNode
            {
                Position = cell + new Vector2(IconSize + 8f, 6f),
                Size = new Vector2(textWidth, 22f),
                FontSize = HubTheme.FontBody,
                FontType = FontType.Axis,
                TextColor = HubTheme.White,
                TextFlags = TextFlags.Ellipsis,
                String = entry.DisplayName,
            };
            name.AttachNode(body);
            var status = new TextNode
            {
                Position = cell + new Vector2(IconSize + 8f, 30f),
                Size = new Vector2(textWidth, 20f),
                FontSize = HubTheme.FontCaption,
                FontType = FontType.Axis,
                TextColor = HubTheme.Grey,
                TextFlags = TextFlags.Ellipsis,
                String = "",
            };
            status.AttachNode(body);
            var internalName = entry.InternalName;
            var open = new TextButtonNode
            {
                Position = cell + new Vector2(cellWidth - OpenWidth, 14f),
                Size = new Vector2(OpenWidth, 28f),
                String = "Open",
                OnClick = () => monitor.Open(internalName),
            };
            open.LabelNode.TextColor = HubTheme.White;
            open.AttachNode(body);
            _rows.Add(new Row { Entry = entry, Status = status, Open = open });
        }
    }

    private void BuildQuickCard(ResNode body, float width)
    {
        new TextNode
        {
            Position = new Vector2(0f, 0f),
            Size = new Vector2(width, 24f),
            FontSize = HubTheme.FontTitle,
            FontType = FontType.Axis,
            TextColor = HubTheme.White,
            String = "Auto-Rotation · Gluttony Combo",
        }.AttachNode(body);
        _quickState = new TextNode
        {
            Position = new Vector2(0f, 34f),
            Size = new Vector2(width, 22f),
            FontSize = HubTheme.FontBody,
            FontType = FontType.Axis,
            TextColor = HubTheme.Grey,
            String = "",
        };
        _quickState.AttachNode(body);
        new TextNode
        {
            Position = new Vector2(0f, 76f),
            Size = new Vector2(width, 60f),
            FontSize = HubTheme.FontCaption,
            FontType = FontType.Axis,
            TextFlags = TextFlags.WordWrap | TextFlags.MultiLine,
            TextColor = HubTheme.Dim,
            String = "This recovery page only reads Gluttony Combo's current state. It does not register a lease or change any setting.",
        }.AttachNode(body);
    }

    private void Refresh()
    {
        var snapshot = monitor.Snapshot();
        var byName = new Dictionary<string, PluginState>(snapshot.Count, StringComparer.Ordinal);
        foreach (var row in snapshot) byName[row.Entry.InternalName] = row.State;
        foreach (var row in _rows)
        {
            var state = byName.GetValueOrDefault(row.Entry.InternalName, PluginState.NotInstalled);
            row.Status.String = HubTheme.StatusText(state);
            row.Status.TextColor = HubTheme.StateColor(state);
            row.Open.IsEnabled = PluginStatus.CanOpen(state);
        }

        var loaded = snapshot.Count(row => PluginStatus.CanOpen(row.State));
        var stateWord = loaded > 0 ? Copy.Word(UiState.Ready) : snapshot.Count > 0 ? Copy.Word(UiState.NeedsSetup) : Copy.Word(UiState.Missing);
        if (_status != null)
        {
            _status.String = $"● {stateWord} · {loaded}/{snapshot.Count}";
            _status.TextColor = loaded > 0 ? Copy.MarkColor(UiState.Ready) : Copy.MarkColor(UiState.NeedsSetup);
        }
        if (_pageCount != null && _showPlugins) _pageCount.String = $"{loaded}/{snapshot.Count} loaded";

        if (_quickState != null)
        {
            var reading = gluttony.Read(monitor.IsLoaded("GluttonyCombo"));
            (_quickState.String, _quickState.TextColor) = HubTheme.GluttonyStatus(reading);
            if (_pageCount != null && !_showPlugins) _pageCount.String = _quickState.String;
        }
    }
}
