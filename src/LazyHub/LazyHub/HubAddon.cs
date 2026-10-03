using System.Diagnostics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;
using LazyHub.Core;

namespace LazyHub;

/// <summary>
/// The hub window: a native game-style window (KamiToolKit) with two tabs.
/// Plugins: a two-column grid, one cell per lalalazy plugin (icon, name, live status, Open button).
/// Quick: Gluttony Combo's auto-rotation state, read only.
///
/// NativeAddon allocates the addon on every Open() and finalizes it on every close, so all nodes are
/// created in <see cref="OnSetup"/> and forgotten in <see cref="OnFinalize"/>. Nothing may hold a node
/// across a close; <see cref="_generation"/> lets asynchronous icon loads tell.
/// </summary>
internal sealed unsafe class HubAddon(PluginMonitor monitor, GluttonyProbe gluttony, IconLoader icons) : NativeAddon
{
    private const float TabWidth = 120f;
    private const float TabHeight = 28f;
    private const float TabGap = 8f;
    private const float BodyGap = 10f;
    private const float CellHeight = 60f;
    private const float ColumnGap = 12f;
    private const float IconSize = 44f;
    private const float OpenWidth = 64f;

    private static readonly Vector4 White = new(0.95f, 0.97f, 0.95f, 1f);
    private static readonly Vector4 Green = new(0.435f, 0.851f, 0.498f, 1f);   // #6fd97f
    private static readonly Vector4 Amber = new(0.910f, 0.647f, 0.282f, 1f);   // #e8a548
    private static readonly Vector4 Grey = new(0.62f, 0.70f, 0.66f, 1f);
    private static readonly Vector4 Dim = new(0.42f, 0.48f, 0.45f, 1f);
    private static readonly Vector4 Red = new(0.91f, 0.54f, 0.54f, 1f);

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
    private int _generation;

    protected override void OnSetup(AtkUnitBase* addon)
    {
        _generation++;
        _rows.Clear();

        var origin = ContentStartPosition;
        var content = ContentSize;

        var pluginsTab = new TextButtonNode
        {
            Position = origin,
            Size = new Vector2(TabWidth, TabHeight),
            String = "Plugins",
            OnClick = () => SelectTab(plugins: true),
        };
        pluginsTab.AttachNode(this);

        var quickTab = new TextButtonNode
        {
            Position = origin + new Vector2(TabWidth + TabGap, 0f),
            Size = new Vector2(TabWidth, TabHeight),
            String = "Quick",
            OnClick = () => SelectTab(plugins: false),
        };
        quickTab.AttachNode(this);

        var bodyPosition = origin + new Vector2(0f, TabHeight + BodyGap);
        var bodySize = new Vector2(content.X, Math.Max(content.Y - TabHeight - BodyGap, 0f));

        _pluginsBody = new ResNode { Position = bodyPosition, Size = bodySize };
        _pluginsBody.AttachNode(this);

        _quickBody = new ResNode { Position = bodyPosition, Size = bodySize, IsVisible = false };
        _quickBody.AttachNode(this);

        BuildPluginGrid(_pluginsBody, bodySize.X);
        BuildQuickCard(_quickBody, bodySize.X);

        Refresh();
        _sinceRefresh.Restart();
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        // About once a second is plenty: plugin state changes rarely and every node write costs.
        if (_sinceRefresh.ElapsedMilliseconds < 1000) return;
        _sinceRefresh.Restart();
        Refresh();
    }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        // The nodes die with the addon. Forget them and invalidate pending icon loads.
        _generation++;
        _rows.Clear();
        _pluginsBody = null;
        _quickBody = null;
        _quickState = null;
    }

    private void SelectTab(bool plugins)
    {
        if (_pluginsBody != null) _pluginsBody.IsVisible = plugins;
        if (_quickBody != null) _quickBody.IsVisible = !plugins;
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
            };
            icon.AttachNode(body);
            icons.LoadInto(icon, entry.IconFile, () => generation == _generation && IsOpen);

            var name = new TextNode
            {
                Position = cell + new Vector2(IconSize + 8f, 6f),
                Size = new Vector2(textWidth, 22f),
                FontSize = 14,
                FontType = FontType.Axis,
                TextColor = White,
                String = entry.DisplayName,
            };
            name.AttachNode(body);

            var status = new TextNode
            {
                Position = cell + new Vector2(IconSize + 8f, 30f),
                Size = new Vector2(textWidth, 20f),
                FontSize = 12,
                FontType = FontType.Axis,
                TextColor = Grey,
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
            FontSize = 16,
            FontType = FontType.Axis,
            TextColor = White,
            String = "Auto-Rotation (Gluttony Combo)",
        }.AttachNode(body);

        _quickState = new TextNode
        {
            Position = new Vector2(0f, 30f),
            Size = new Vector2(width, 22f),
            FontSize = 14,
            FontType = FontType.Axis,
            TextColor = Grey,
            String = "",
        };
        _quickState.AttachNode(body);

        new TextNode
        {
            Position = new Vector2(0f, 72f),
            Size = new Vector2(width, 60f),
            FontSize = 12,
            FontType = FontType.Axis,
            TextFlags = TextFlags.WordWrap | TextFlags.MultiLine,
            TextColor = Dim,
            String = "This card only shows Gluttony Combo's current state. It does not change anything yet.",
        }.AttachNode(body);
    }

    private void Refresh()
    {
        var snapshot = monitor.Snapshot();
        var byName = new Dictionary<string, PluginState>(snapshot.Count, StringComparer.Ordinal);
        foreach (var r in snapshot) byName[r.Entry.InternalName] = r.State;

        foreach (var row in _rows)
        {
            var state = byName.GetValueOrDefault(row.Entry.InternalName, PluginState.NotInstalled);
            row.Status.String = PluginStatus.Label(state);
            row.Status.TextColor = ColorFor(state);
            row.Open.IsEnabled = PluginStatus.CanOpen(state);
        }

        if (_quickState != null)
        {
            var reading = gluttony.Read(monitor.IsLoaded("GluttonyCombo"));
            (_quickState.String, _quickState.TextColor) = reading switch
            {
                GluttonyProbe.Reading.On => ("On", Green),
                GluttonyProbe.Reading.Off => ("Off", Amber),
                GluttonyProbe.Reading.Unavailable => ("Running, state not readable yet", Red),
                _ => ("Gluttony Combo is not running", Dim),
            };
        }
    }

    private static Vector4 ColorFor(PluginState state) => state switch
    {
        PluginState.Loaded => Green,
        PluginState.LoadedTesting => Amber,
        PluginState.InstalledNotLoaded => Grey,
        _ => Dim,
    };
}
