using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace LazyGearCollector;

public sealed class CollectorWindow : Window
{
    private const float ControlHeight = CollectorChrome.Control;

    private static readonly Dictionary<string, string> RoleJobs = new()
    {
        ["Fending"] = "PLD  WAR  DRK  GNB",
        ["Maiming"] = "DRG  RPR",
        ["Striking"] = "MNK  SAM  VPR",
        ["Scouting"] = "NIN",
        ["Aiming"] = "BRD  MCH  DNC",
        ["Healing"] = "WHM  SCH  AST  SGE",
        ["Casting"] = "BLM  SMN  RDM  PCT",
        ["Slaying"] = "Melee and physical ranged",
    };

    private static readonly Regex Parenthetical = new(@"\s*\([^)]*\)", RegexOptions.Compiled);

    private readonly Plugin _plugin;
    private CollectorGroup _group = CollectorGroup.Overview;
    private OverviewPage _overviewPage = OverviewPage.Summary;
    private string? _selectedRole;
    private string _searchText = "";
    private bool _bodyScrolls;

    public CollectorWindow(Plugin plugin) : base("Lazy Gear Collector##lazygear")
    {
        _plugin = plugin;
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        Size = new Vector2(1100, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 480),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    private enum CollectorGroup
    {
        Overview,
        Roles,
        Settings,
    }

    private enum OverviewPage
    {
        Summary,
        Materials,
    }

    private sealed record RoleProgress(
        string Role,
        string Jobs,
        List<PieceChain> Chains,
        List<PiecePlan> Plans,
        Dictionary<uint, long> Remaining,
        int Done);

    private sealed record SearchDestination(
        string Title,
        string Category,
        string Terms,
        CollectorGroup Group,
        OverviewPage Page,
        string? Role);

    private readonly record struct Stat(string Label, string Value, string? Cap, string Note, float? Fraction);

    private readonly record struct HeaderInfo(string Title, string Description, string Count, string Subtext);

    private GearCollection? Current =>
        _plugin.Collections.FirstOrDefault(c => c.Id == _plugin.Config.LastCollectionId)
        ?? _plugin.Collections.FirstOrDefault();

    // The window owns its full surface: bands are drawn full bleed and the body child carries the page gutter.
    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    public override void PostDraw() => ImGui.PopStyleVar();

    // Scrollbar track reserved at the right of the body child. It sits inside the right gutter: the child's right
    // padding is the gutter less the track and the content is indented by the track on the left, so the cards end
    // at the same edge as the header controls.
    private const float ScrollTrack = 10f;

    private static float Gutter(float width) => width >= 760 ? 24 : width >= 560 ? 16 : 12;

    public override void Draw()
    {
        Theme.PushFrame();
        try
        {
            DrawFrame();
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private readonly PlanCache _planCache = new();

    private void DrawFrame()
    {
        var collection = Current;
        var target = _plugin.Config.TargetTier;
        var allPlans = new List<PiecePlan>();
        var grandTotal = new Dictionary<uint, long>();
        var donePieces = 0;
        var roles = new List<RoleProgress>();

        if (collection != null)
        {
            // One cached plan of the whole collection; the role rows filter it instead of planning each role again.
            (allPlans, grandTotal, donePieces) = _planCache.Get(collection, target, _plugin.Config.IncludeCachedContainers, DateTime.UtcNow,
                () => _plugin.Planner.PlanMany(collection.Pieces, target));
            foreach (var role in collection.Roles)
            {
                var chains = collection.ForRole(role).ToList();
                var plans = allPlans.Where(p => p.Piece.Role == role).ToList();
                var (remaining, done) = Planner.Summarize(plans);
                roles.Add(new RoleProgress(role, JobsFor(role), chains, plans, remaining, done));
            }
        }

        var state = OverallState(collection, donePieces);
        if (!SearchEnabled(collection))
            _searchText = "";

        CollectorChrome.ResetLayout();
        var draw = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var gutter = Gutter(winSize.X);
        var header = HeaderFor(collection, roles, donePieces, grandTotal);
        var headerInBody = winSize.Y < 560;

        var y = DrawTopBar(draw, winPos, winSize.X, gutter, collection, state);
        y += DrawFeatureBar(draw, winPos, winSize.X, gutter, y, collection);
        if (!headerInBody)
            y += DrawPageHeader(draw, winPos, winSize.X, gutter, y, header);

        var footer = FooterFor(collection, donePieces, target);
        var footerHeight = FooterHeight(winSize.X, footer.Secondary);
        var bodyHeight = MathF.Max(80, winSize.Y - footerHeight - y);
        DrawBodyChild(winSize.X, gutter, y, bodyHeight, headerInBody, () =>
        {
            if (headerInBody)
            {
                // Short windows: the page header scrolls with the page so the body keeps the room.
                var at = ImGui.GetCursorScreenPos();
                var used = DrawPageHeader(ImGui.GetWindowDrawList(), new Vector2(ImGui.GetWindowPos().X, at.Y), winSize.X, gutter, -20, header);
                ImGui.Dummy(new Vector2(1, used - 20 - 8));
            }

            if (collection == null && _group != CollectorGroup.Settings)
            {
                DrawEmptyCollection();
                return;
            }

            switch (_group)
            {
                case CollectorGroup.Overview when _overviewPage == OverviewPage.Summary:
                    DrawSummary(collection!, roles, allPlans, donePieces, bodyHeight);
                    break;
                case CollectorGroup.Overview:
                    DrawMaterials(collection!, grandTotal, bodyHeight);
                    break;
                case CollectorGroup.Roles:
                    DrawRoleDetail(collection!, roles.FirstOrDefault(r => r.Role == _selectedRole), target, bodyHeight);
                    break;
                case CollectorGroup.Settings:
                    DrawSettingsPage(collection, bodyHeight);
                    break;
            }
        });

        DrawFooter(draw, winPos, winSize, gutter, footerHeight, footer, state);
    }

    private static UiState OverallState(GearCollection? collection, int donePieces)
    {
        if (collection == null || collection.Pieces.Count == 0)
            return UiState.Empty;
        if (!Plugin.ClientState.IsLoggedIn)
            return UiState.Idle;
        return UiState.Ready;
    }

    private static bool SearchEnabled(GearCollection? collection) =>
        collection != null && collection.Roles.Count() + 3 >= 8;

    // ---- top bar ----------------------------------------------------------------------------------------------

    // Band: 14 px top and bottom, 40 px brand, 36 px controls centred on it. Returns the band height plus its rule.
    private float DrawTopBar(ImDrawListPtr draw, Vector2 winPos, float width, float gutter, GearCollection? collection, UiState state)
    {
        const float pad = 14;
        const float gap = 8;
        const float wideGap = 16;
        const float brandHeight = 40;
        var minSearch = MathF.Max(180, CollectorChrome.TextWidth("Search roles and pages...") + 48);
        const float maxSearch = 320;
        var inner = width - (2 * gutter);
        var groups = Enum.GetValues<CollectorGroup>();
        var groupSizes = groups.Select(g => new Vector2(MathF.Max(72, CollectorChrome.TextWidth(GroupName(g)) + 32), ControlHeight)).ToArray();
        var groupsWidth = groupSizes.Sum(s => s.X) + ((groups.Length - 1) * gap);
        var pillSize = new Vector2(CollectorChrome.StatusPillWidth(), ControlHeight);
        var closeSize = new Vector2(ControlHeight);
        var rightWidth = pillSize.X + gap + closeSize.X;
        var search = SearchEnabled(collection);
        var brandScale = Theme.TitleScale;
        var brandNatural = CollectorChrome.BrandWidth(brandScale);

        var oneRow = inner >= brandNatural + wideGap + groupsWidth + (search ? wideGap + minSearch : 0) + wideGap + rightWidth;
        var groupPositions = new Vector2[groups.Length];
        var groupUsed = (Vector2[])groupSizes.Clone();
        Vector2 brandPos;
        Vector2 pillPos;
        Vector2 closePos;
        var searchPos = Vector2.Zero;
        float searchWidth = 0;
        float brandWidth;
        float barHeight;
        var controlY = pad + ((brandHeight - ControlHeight) / 2f);

        closePos = new Vector2(width - gutter - closeSize.X, controlY);
        pillPos = new Vector2(closePos.X - gap - pillSize.X, controlY);
        brandPos = new Vector2(gutter, pad);

        if (oneRow)
        {
            brandWidth = brandNatural;
            var x = gutter + brandWidth + wideGap;
            for (var i = 0; i < groups.Length; i++)
            {
                groupPositions[i] = new Vector2(x, controlY);
                x += groupSizes[i].X + gap;
            }

            if (search)
            {
                var room = pillPos.X - wideGap - (x - gap + wideGap);
                searchWidth = MathF.Min(maxSearch, room);
                searchPos = new Vector2(pillPos.X - wideGap - searchWidth, controlY);
            }

            barHeight = pad + brandHeight + pad;
        }
        else
        {
            // Compact: brand with status and close on the first row; navigation and search share the second.
            brandWidth = MathF.Min(brandNatural, inner - rightWidth - wideGap);
            var rowY = pad + brandHeight + gap;
            var x = gutter;
            if (groupsWidth > inner)
            {
                var each = (inner - ((groups.Length - 1) * gap)) / groups.Length;
                for (var i = 0; i < groups.Length; i++)
                {
                    groupUsed[i] = new Vector2(each, ControlHeight);
                    groupPositions[i] = new Vector2(gutter + (i * (each + gap)), rowY);
                }

                x = gutter + inner;
            }
            else
            {
                for (var i = 0; i < groups.Length; i++)
                {
                    groupPositions[i] = new Vector2(x, rowY);
                    x += groupSizes[i].X + gap;
                }
            }

            barHeight = rowY + ControlHeight + pad;
            if (search)
            {
                var room = gutter + inner - x;
                if (room >= 160)
                {
                    searchPos = new Vector2(x, rowY);
                    searchWidth = MathF.Min(inner, room);
                }
                else
                {
                    searchPos = new Vector2(gutter, rowY + ControlHeight + gap);
                    searchWidth = inner;
                    barHeight = searchPos.Y + ControlHeight + pad;
                }
            }
        }

        draw.PushClipRect(winPos, winPos + new Vector2(width, barHeight + 1), false);
        draw.AddRectFilled(winPos, winPos + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, barHeight), winPos + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        if (CollectorChrome.Brand(draw, winPos + brandPos, new Vector2(brandWidth, brandHeight), brandScale))
        {
            _group = CollectorGroup.Overview;
            _overviewPage = OverviewPage.Summary;
        }

        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            if (CollectorChrome.NavButton(draw, "group_" + group, GroupName(group), winPos + groupPositions[i], groupUsed[i], _group == group))
                NavigateGroup(group, collection);
        }

        if (search)
            DrawSearchInput(winPos + searchPos, searchWidth);
        CollectorChrome.StatusPill(draw, winPos + pillPos, pillSize, Copy.Word(state), state);
        if (CollectorChrome.CloseButton(draw, winPos + closePos, closeSize))
            IsOpen = false;
        if (search)
            DrawSearchResults(collection, winPos + searchPos, searchWidth, winPos.X + width - gutter);
        return barHeight + 1;
    }

    private void DrawSearchInput(Vector2 pos, float width)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.SetNextItemWidth(width);
        var framePadding = new Vector2(16, MathF.Max(Theme.Unit, (ControlHeight - ImGui.GetTextLineHeight()) / 2f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, framePadding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, CollectorChrome.Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.InputTextWithHint("##gear_search", "Search roles and pages...", ref _searchText, 256);
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(3);
    }

    /// <summary>Results float under the search box as a popup; opening it never moves the page.</summary>
    private void DrawSearchResults(GearCollection? collection, Vector2 searchPos, float searchWidth, float rightEdge)
    {
        const string popupId = "##gear_search_results";
        var term = _searchText.Trim();
        if (term.Length == 0 || collection == null)
            return;
        var matches = SearchDestinations(collection)
            .Where(d => (d.Title + " " + d.Category + " " + d.Terms).Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToList();
        // At least 300 wide, but never past the window's right gutter.
        var popupWidth = MathF.Max(searchWidth, 300);
        var popupX = MathF.Min(searchPos.X, rightEdge - popupWidth);
        ImGui.SetNextWindowPos(new Vector2(popupX, searchPos.Y + ControlHeight + Theme.Unit));
        ImGui.SetNextWindowSize(new Vector2(popupWidth, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, CollectorChrome.Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16, 12));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.20f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.14f });
        if (!ImGui.IsPopupOpen(popupId))
            ImGui.OpenPopup(popupId);
        if (ImGui.BeginPopup(popupId, ImGuiWindowFlags.NoFocusOnAppearing))
        {
            ImGui.SetWindowFontScale(Theme.SecondaryScale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted(matches.Count == 1 ? $"1 destination for ‘{term}’" : $"{matches.Count} destinations for ‘{term}’");
            ImGui.PopStyleColor();
            ImGui.SetWindowFontScale(1f);
            if (matches.Count == 0)
            {
                ImGui.SetWindowFontScale(Theme.SecondaryScale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + popupWidth - 32);
                ImGui.TextUnformatted("No matching role or page. Try a job, role, material, or counting term.");
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
                ImGui.SetWindowFontScale(1f);
            }

            foreach (var match in matches)
            {
                if (!ImGui.Selectable($"{match.Category} · {match.Title}##gear_result_{match.Category}_{match.Title}"))
                    continue;
                _group = match.Group;
                _overviewPage = match.Page;
                if (match.Role != null)
                    _selectedRole = match.Role;
                _searchText = "";
            }

            ImGui.EndPopup();
        }

        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(4);
    }

    private static IEnumerable<SearchDestination> SearchDestinations(GearCollection collection)
    {
        yield return new SearchDestination("Collection summary", "Overview", "home progress roles gear", CollectorGroup.Overview, OverviewPage.Summary, null);
        yield return new SearchDestination("Materials & wallet", "Overview", "materials costs currency wallet remaining", CollectorGroup.Overview, OverviewPage.Materials, null);
        yield return new SearchDestination("Counting & sources", "Settings", "settings counting target tier cached saddlebag retainers snapshots", CollectorGroup.Settings, OverviewPage.Summary, null);
        foreach (var role in collection.Roles)
            yield return new SearchDestination(role, "Role", JobsFor(role) + " gear pieces upgrades", CollectorGroup.Roles, OverviewPage.Summary, role);
    }

    // ---- sub-navigation ---------------------------------------------------------------------------------------

    private static void DrawBand(ImDrawListPtr draw, Vector2 winPos, float width, float top, float height)
    {
        draw.PushClipRect(winPos + new Vector2(0, top), winPos + new Vector2(width, top + height + 1), false);
        draw.AddRectFilled(winPos + new Vector2(0, top), winPos + new Vector2(width, top + height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, top + height), winPos + new Vector2(width, top + height), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
    }

    // Only when the group has two or more sections. Pills when they fit, otherwise one full-width field with a list.
    private float DrawFeatureBar(ImDrawListPtr draw, Vector2 winPos, float width, float gutter, float top, GearCollection? collection)
    {
        var features = Features(collection);
        if (features.Length < 2)
            return 0;
        var inner = width - (2 * gutter);
        const float gap = 12;
        const float padY = 12;
        const float pillHeight = 32;
        var sizes = features.Select(title => new Vector2(CollectorChrome.FeatureWidth(title), pillHeight)).ToArray();
        var total = sizes.Sum(size => size.X) + ((features.Length - 1) * gap);
        float barHeight;

        if (total > inner)
        {
            barHeight = padY + ControlHeight + padY;
            DrawBand(draw, winPos, width, top, barHeight);
            var fieldPos = winPos + new Vector2(gutter, top + padY);
            var fieldSize = new Vector2(inner, ControlHeight);
            ImGui.SetCursorScreenPos(fieldPos);
            var open = ImGui.InvisibleButton("##gear_feature_menu", fieldSize);
            var hovered = ImGui.IsItemHovered();
            draw.AddRectFilled(fieldPos, fieldPos + fieldSize, Theme.Pack(Theme.Accent with { W = 0.14f }), CollectorChrome.Radius);
            draw.AddRect(fieldPos, fieldPos + fieldSize, Theme.Pack(hovered ? Theme.Accent : Theme.Accent with { W = 0.6f }), CollectorChrome.Radius);
            var current = features.FirstOrDefault(FeatureSelected) ?? features[0];
            CollectorChrome.Text(draw, fieldPos + new Vector2(16, (ControlHeight - CollectorChrome.LineHeight()) / 2f),
                CollectorChrome.FitText(current, inner - 56), Theme.Accent);
            CollectorChrome.Chevron(draw, fieldPos + new Vector2(inner - 20, ControlHeight / 2f), Theme.Accent);
            if (open)
                ImGui.OpenPopup("##gear_feature_list");
            ImGui.SetNextWindowPos(fieldPos + new Vector2(0, ControlHeight + Theme.Unit));
            ImGui.SetNextWindowSize(new Vector2(inner, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, CollectorChrome.Radius);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 4));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
            ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
            ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.20f });
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.14f });
            if (ImGui.BeginPopup("##gear_feature_list"))
            {
                foreach (var feature in features)
                {
                    if (ImGui.Selectable(feature + "##gear_feature_" + feature, FeatureSelected(feature), ImGuiSelectableFlags.None, new Vector2(0, ControlHeight - 8)))
                        SelectFeature(feature, collection);
                }

                ImGui.EndPopup();
            }

            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar(4);
        }
        else
        {
            barHeight = padY + pillHeight + padY;
            DrawBand(draw, winPos, width, top, barHeight);
            var x = gutter;
            for (var i = 0; i < features.Length; i++)
            {
                if (CollectorChrome.FeatureButton(draw, "feature_" + features[i], features[i], winPos + new Vector2(x, top + padY), sizes[i], FeatureSelected(features[i])))
                    SelectFeature(features[i], collection);
                x += sizes[i].X + gap;
            }
        }

        return barHeight + 1;
    }

    private string[] Features(GearCollection? collection) => _group switch
    {
        CollectorGroup.Overview => ["Summary", "Materials"],
        CollectorGroup.Roles => collection?.Roles.ToArray() ?? [],
        _ => [],
    };

    private bool FeatureSelected(string feature) => _group switch
    {
        CollectorGroup.Overview => feature == (_overviewPage == OverviewPage.Summary ? "Summary" : "Materials"),
        CollectorGroup.Roles => feature == _selectedRole,
        _ => true,
    };

    private void SelectFeature(string feature, GearCollection? collection)
    {
        if (_group == CollectorGroup.Overview)
            _overviewPage = feature == "Summary" ? OverviewPage.Summary : OverviewPage.Materials;
        else if (_group == CollectorGroup.Roles && collection?.Roles.Contains(feature) == true)
            _selectedRole = feature;
    }

    private void NavigateGroup(CollectorGroup group, GearCollection? collection)
    {
        _group = group;
        if (group == CollectorGroup.Roles)
            _selectedRole ??= collection?.Roles.FirstOrDefault();
    }

    // ---- page header ------------------------------------------------------------------------------------------

    private HeaderInfo HeaderFor(GearCollection? collection, List<RoleProgress> roles, int done, Dictionary<uint, long> remaining)
    {
        var target = Math.Clamp(_plugin.Config.TargetTier, 0, 3);
        if (collection == null && _group != CollectorGroup.Settings)
            return new HeaderInfo("Collection unavailable", "No tracked gear sets could be built from the current game data.", "", "");
        if (_group == CollectorGroup.Settings)
            return new HeaderInfo(
                "Counting & sources",
                "Choose the upgrade goal and which visible containers contribute to ownership.",
                $"{_plugin.Config.Snapshots.Count} remembered containers",
                $"Goal: {TierLabel(target)}");
        if (_group == CollectorGroup.Roles)
        {
            var role = roles.FirstOrDefault(r => r.Role == _selectedRole);
            return new HeaderInfo(
                role == null ? "Role gear" : $"{role.Role} gear",
                role == null ? "Select a role to inspect its pieces." : $"{role.Jobs} · Piece-by-piece ownership and acquisition costs.",
                role == null ? "0 roles selected" : $"{role.Done} / {role.Chains.Count} pieces at {TierLabel(target)}",
                role == null ? "Empty" : $"{role.Plans.Count(p => p.HasShortcut)} trade-up routes");
        }

        if (_overviewPage == OverviewPage.Materials)
            return new HeaderInfo(
                "Materials & wallet",
                "Compare aggregated acquisition costs with the currency and materials on hand.",
                $"{remaining.Count} remaining material types",
                $"{collection!.Currencies.Count} wallet currencies");
        return new HeaderInfo(
            collection!.DisplayName,
            collection.SourceNote,
            $"{done} / {collection.Pieces.Count} pieces at {TierLabel(target)}",
            $"{collection.Roles.Count()} roles · {collection.Pieces.Sum(p => p.Tiers.Count)} tracked items");
    }

    // Tile 44, text column at gutter + 60; no divider below. Returns the header height (20 above, 8 below).
    private static float DrawPageHeader(ImDrawListPtr draw, Vector2 winPos, float width, float gutter, float top, HeaderInfo info)
    {
        const float titleScale = 1.35f;
        const float tile = 44;
        var sm = Theme.SecondaryScale;
        var wide = width >= 760;
        var textX = gutter + 60;
        var left = winPos + new Vector2(textX, top + 20);
        var titleH = CollectorChrome.LineHeight(titleScale);
        var smH = CollectorChrome.LineHeight(sm);
        float block;

        draw.AddRectFilled(winPos + new Vector2(gutter, top + 20), winPos + new Vector2(gutter + tile, top + 20 + tile), Theme.Pack(Theme.Accent with { W = 0.14f }), CollectorChrome.Radius);
        CollectorChrome.Diamond(draw, winPos + new Vector2(gutter + (tile / 2f), top + 20 + (tile / 2f)), 10, Theme.Pack(Theme.Accent));

        if (wide)
        {
            var metaWidth = MathF.Min(
                MathF.Max(CollectorChrome.TextWidth(info.Count, sm), CollectorChrome.TextWidth(info.Subtext, sm)),
                width * 0.4f);
            // Content, meta and the cards below all end at the window width less the gutter.
            var contentRight = width - gutter;
            var textWidth = contentRight - textX - metaWidth - 16;
            var shownTitle = CollectorChrome.FitText(info.Title, textWidth, titleScale);
            var shownDescription = CollectorChrome.ClampLines(info.Description, textWidth, sm, 2);
            var descriptionH = CollectorChrome.WrappedHeight(shownDescription, textWidth, sm);
            CollectorChrome.Text(draw, left, shownTitle, Theme.TextPrimary, titleScale);
            CollectorChrome.Text(draw, left + new Vector2(0, titleH + 4), shownDescription, Theme.TextSecondary, sm, textWidth);
            block = titleH + 4 + MathF.Max(smH, descriptionH);

            // Meta: first line centred on the title line, second on the description line.
            var metaRight = winPos.X + contentRight;
            var shownCount = CollectorChrome.FitText(info.Count, metaWidth, sm);
            var shownSubtext = CollectorChrome.FitText(info.Subtext, metaWidth, sm);
            CollectorChrome.TextRight(draw, metaRight, left.Y + ((titleH - smH) / 2f), shownCount, Theme.TextSecondary, sm);
            CollectorChrome.TextRight(draw, metaRight, left.Y + titleH + 4, shownSubtext, Theme.TextSecondary, sm);
            TipIfElided(
                shownTitle != info.Title || shownDescription != info.Description || shownCount != info.Count || shownSubtext != info.Subtext,
                winPos, top, width, block, $"{info.Title}\n{info.Description}\n{info.Count}\n{info.Subtext}".TrimEnd());
        }
        else
        {
            var textWidth = width - gutter - textX;
            var shownTitle = CollectorChrome.FitText(info.Title, textWidth, titleScale);
            CollectorChrome.Text(draw, left, shownTitle, Theme.TextPrimary, titleScale);
            var y = left.Y + titleH + 4;
            var shownDescription = CollectorChrome.ClampLines(info.Description, textWidth, sm, 3);
            var descriptionH = CollectorChrome.WrappedHeight(shownDescription, textWidth, sm);
            CollectorChrome.Text(draw, new Vector2(left.X, y), shownDescription, Theme.TextSecondary, sm, textWidth);
            y += descriptionH;
            var meta = string.Join(" · ", new[] { info.Count, info.Subtext }.Where(s => s.Length > 0));
            var shownMeta = CollectorChrome.FitText(meta, textWidth, sm);
            if (meta.Length > 0)
            {
                y += 4;
                CollectorChrome.Text(draw, new Vector2(left.X, y), shownMeta, Theme.TextSecondary, sm);
                y += smH;
            }

            block = y - left.Y;
            TipIfElided(
                shownDescription != info.Description || shownMeta != meta,
                winPos, top, width, block, $"{info.Title}\n{info.Description}\n{meta}".TrimEnd());
        }

        return 20 + MathF.Max(tile, block) + 8;
    }

    private static void TipIfElided(bool elided, Vector2 winPos, float top, float width, float block, string full)
    {
        if (!elided)
            return;
        var min = winPos + new Vector2(0, top + 20);
        var max = winPos + new Vector2(width, top + 20 + block);
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max))
            CollectorChrome.Tip(full);
    }

    // ---- body -------------------------------------------------------------------------------------------------

    private void DrawBodyChild(float width, float gutter, float top, float height, bool headerInBody, Action page)
    {
        // A one pixel transparent border makes the child honour its window padding: every page gets the same gutters
        // and a bottom margin. One scroll region for the whole body; the scrollbar track is always reserved so the
        // column width does not jump between pages, and the grab only shows when there is something to scroll.
        ImGui.SetCursorPos(new Vector2(0, top));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(MathF.Max(0, gutter - ScrollTrack), 16));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, ScrollTrack);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f);
        ImGui.PushStyleColor(ImGuiCol.Border, 0u);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, _bodyScrolls ? Theme.Pack(Theme.SurfaceBorder) : 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, _bodyScrolls ? Theme.Pack(Theme.TextSecondary with { W = 0.45f }) : 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, _bodyScrolls ? Theme.Pack(Theme.Accent with { W = 0.6f }) : 0u);
        ImGui.BeginChild("##gear_body", new Vector2(width, height), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);
        ImGui.Indent(gutter - MathF.Max(0, gutter - ScrollTrack));
        // The child has applied its own padding; tooltips opened from page content keep the theme padding.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.WindowPadding);
        page();
        ImGui.PopStyleVar();
        _bodyScrolls = ImGui.GetScrollMaxY() > 0;
        // Fade where more content scrolls in below, so nothing looks cut by the footer.
        if (ImGui.GetScrollMaxY() > 0 && ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1)
        {
            var bodyDraw = ImGui.GetWindowDrawList();
            var min = ImGui.GetWindowPos();
            var bottom = min.Y + height - 1;
            var right = min.X + width - 11;
            var window = ImGui.GetColorU32(ImGuiCol.WindowBg);
            bodyDraw.AddRectFilledMultiColor(
                new Vector2(min.X, bottom - 28),
                new Vector2(right, bottom),
                window & 0x00FFFFFFu,
                window & 0x00FFFFFFu,
                window | 0xFF000000u,
                window | 0xFF000000u);
        }

        // Pinned page header: content that scrolled under it fades out instead of ending in a hard edge.
        if (!headerInBody && ImGui.GetScrollY() > 1)
        {
            var bodyDraw = ImGui.GetWindowDrawList();
            var min = ImGui.GetWindowPos();
            var window = ImGui.GetColorU32(ImGuiCol.WindowBg);
            bodyDraw.AddRectFilledMultiColor(
                min,
                new Vector2(min.X + width - 11, min.Y + 16),
                window | 0xFF000000u,
                window | 0xFF000000u,
                window & 0x00FFFFFFu,
                window & 0x00FFFFFFu);
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(6);
        ImGui.PopStyleVar(5);
    }

    private void DrawSummary(GearCollection collection, List<RoleProgress> roles, List<PiecePlan> allPlans, int done, float bodyHeight)
    {
        var target = Math.Clamp(_plugin.Config.TargetTier, 0, 3);
        var completeRoles = roles.Count(r => r.Chains.Count > 0 && r.Done == r.Chains.Count);
        CollectorChrome.Columns(
            "summary",
            bodyHeight,
            () =>
            {
                DrawStatCards(
                    new Stat("Pieces at goal", done.ToString(), collection.Pieces.Count.ToString(), $"Measured against {TierLabel(target)}", collection.Pieces.Count == 0 ? null : done / (float)collection.Pieces.Count),
                    new Stat("Roles complete", completeRoles.ToString(), roles.Count.ToString(), "Every piece at the selected goal", roles.Count == 0 ? null : completeRoles / (float)roles.Count),
                    new Stat("Trade-up hints", allPlans.Count(p => p.HasShortcut).ToString(), null, "Plans with a free or cheaper route", null));
                CollectorChrome.Card(
                    () =>
                    {
                        CollectorChrome.Section("Role progress", "Select a role for its piece-by-piece plan.", $"{roles.Count} roles");
                        if (roles.Count == 0)
                        {
                            CollectorChrome.Notice("Empty", "This collection has no role entries.", UiState.Empty);
                            return;
                        }

                        for (var i = 0; i < roles.Count; i++)
                            DrawRoleProgressRow(roles[i], i == 0, i == roles.Count - 1);
                    },
                    fill: true,
                    gapAfter: false);
            },
            () =>
            {
                DrawRequirementsCard(collection, done, collection.Pieces.Count);
                DrawSettingsCard(fill: true);
            });
    }

    private void DrawRoleProgressRow(RoleProgress role, bool first, bool last)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = CollectorChrome.Avail();
        var sm = Theme.SecondaryScale;
        var lh = CollectorChrome.LineHeight();
        var smH = CollectorChrome.LineHeight(sm);
        const float pad = 12;
        var rowH = pad + lh + 2 + smH + 2 + smH + 8 + Theme.BarHeight + pad;
        var hitH = last ? rowH - pad : rowH;

        var clicked = ImGui.InvisibleButton("##gear_role_summary_" + role.Role, new Vector2(width, hitH));
        var hovered = ImGui.IsItemHovered();
        if (!first)
            draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        if (hovered)
            draw.AddRectFilled(origin + new Vector2(-8, 2), origin + new Vector2(width + 8, hitH - 2), Theme.Pack(Theme.ShellBar), CollectorChrome.Radius);
        if (clicked)
        {
            _selectedRole = role.Role;
            _group = CollectorGroup.Roles;
        }

        if (hovered)
            CollectorChrome.Tip($"Open the {role.Role} piece plan");

        var complete = role.Chains.Count > 0 && role.Done == role.Chains.Count;
        var y = origin.Y + pad;
        var count = $"{role.Done} / {role.Chains.Count}";
        var countWidth = CollectorChrome.TextWidth(count);
        CollectorChrome.Text(draw, new Vector2(origin.X, y), CollectorChrome.FitText(role.Role, width - countWidth - 12), Theme.TextPrimary);
        CollectorChrome.TextRight(draw, origin.X + width, y, count, complete ? Theme.StateGreen : Theme.TextSecondary);
        y += lh + 2;
        CollectorChrome.Text(draw, new Vector2(origin.X, y), CollectorChrome.FitText(role.Jobs, width, sm), Theme.TextSecondary, sm);
        y += smH + 2;
        CollectorChrome.Text(draw, new Vector2(origin.X, y), CollectorChrome.FitText(RemainingSummary(role.Remaining), width, sm), complete ? Theme.StateGreen : Theme.TextSecondary, sm);
        y += smH + 8;
        var min = new Vector2(origin.X, y);
        var max = new Vector2(origin.X + width, y + Theme.BarHeight);
        draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
        var fraction = role.Chains.Count == 0 ? 0 : role.Done / (float)role.Chains.Count;
        if (fraction > 0)
            draw.AddRectFilled(min, new Vector2(min.X + ((max.X - min.X) * fraction), max.Y), Theme.Pack(Theme.StateGreen), 2f);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, hitH));
    }

    private void DrawMaterials(GearCollection collection, Dictionary<uint, long> remaining, float bodyHeight)
    {
        CollectorChrome.Columns(
            "materials",
            bodyHeight,
            () =>
            {
                CollectorChrome.Card(
                    () =>
                    {
                        CollectorChrome.Section("Still needed overall", "Costs are aggregated across every piece at the selected goal.", $"{remaining.Count} material types");
                        if (remaining.Count == 0)
                        {
                            CollectorChrome.Notice("Ready", "Collection complete at this goal. Nothing left to buy.", UiState.Ready);
                            return;
                        }

                        var rows = remaining.OrderByDescending(k => k.Value).ToList();
                        for (var i = 0; i < rows.Count; i++)
                            DrawMaterialRequirement(rows[i].Key, rows[i].Value, i == 0, i == rows.Count - 1);
                    },
                    fill: true,
                    gapAfter: false);
            },
            () =>
            {
                CollectorChrome.Card(() =>
                {
                    CollectorChrome.Section("Wallet", "Live ownership plus remembered containers when included.", $"{collection.Currencies.Count} currencies");
                    if (collection.Currencies.Count == 0)
                    {
                        CollectorChrome.Notice("Empty", "No currency costs were found for this collection.", UiState.Empty);
                        return;
                    }

                    for (var i = 0; i < collection.Currencies.Count; i++)
                    {
                        var id = collection.Currencies[i];
                        CollectorChrome.ValueRow(_plugin.Shops.ItemName(id), _plugin.Ownership.TotalCount(id).ToString("N0"), i == 0, i == collection.Currencies.Count - 1);
                    }
                });
                DrawSettingsCard(fill: true);
            });
    }

    private void DrawRoleDetail(GearCollection collection, RoleProgress? role, int target, float bodyHeight)
    {
        if (role == null)
        {
            CollectorChrome.Card(
                () => CollectorChrome.Notice("Empty", "Select a role from the section row to see its piece plan.", UiState.Empty),
                gapAfter: false);
            return;
        }

        CollectorChrome.Columns(
            "role",
            bodyHeight,
            () =>
            {
                DrawStatCards(
                    new Stat("Pieces at goal", role.Done.ToString(), role.Chains.Count.ToString(), $"Measured against {TierLabel(target)}", role.Chains.Count == 0 ? null : role.Done / (float)role.Chains.Count),
                    new Stat("Owned pieces", role.Plans.Count(p => p.OwnedTier >= 0).ToString(), role.Chains.Count.ToString(), "Any tier visible to the scanner", role.Chains.Count == 0 ? null : role.Plans.Count(p => p.OwnedTier >= 0) / (float)role.Chains.Count),
                    new Stat("Trade-up hints", role.Plans.Count(p => p.HasShortcut).ToString(), null, "Free or cheaper acquisition routes", null));
                for (var i = 0; i < role.Plans.Count; i++)
                {
                    var plan = role.Plans[i];
                    var lastPlan = i == role.Plans.Count - 1;
                    CollectorChrome.Card(() => DrawPiecePlan(plan, target), fill: lastPlan, gapAfter: !lastPlan);
                }
            },
            () =>
            {
                CollectorChrome.Card(() =>
                {
                    CollectorChrome.Section($"{role.Role} summary", role.Jobs, $"{role.Remaining.Count} material types");
                    if (role.Remaining.Count == 0)
                    {
                        CollectorChrome.Notice("Ready", "Every piece in this role is at the selected goal.", UiState.Ready);
                        return;
                    }

                    var rows = role.Remaining.OrderByDescending(k => k.Value).ToList();
                    for (var i = 0; i < rows.Count; i++)
                        DrawMaterialRequirement(rows[i].Key, rows[i].Value, i == 0, i == rows.Count - 1);
                });
                DrawRequirementsCard(collection, role.Done, role.Chains.Count);
                DrawSettingsCard(fill: true);
            });
    }

    private void DrawPiecePlan(PiecePlan plan, int target)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = CollectorChrome.Avail();
        var sm = Theme.SecondaryScale;
        var titleH = CollectorChrome.LineHeight(Theme.TitleScale);
        var smH = CollectorChrome.LineHeight(sm);

        // Header row: piece on the left, its state on the right.
        var state = plan.Complete ? UiState.Ready : plan.OwnedTier < 0 ? UiState.Missing : UiState.Running;
        var stateLabel = plan.Complete ? "Ready" : plan.OwnedTier < 0 ? Copy.Missing("base piece") : "Running";
        var mark = Copy.MarkColor(state);
        var stateWidth = (2 * Theme.DotRadius) + 8 + CollectorChrome.TextWidth(stateLabel, sm);
        var title = $"{plan.Piece.SlotName} · {plan.Piece.PieceName}";
        var shownTitle = CollectorChrome.FitText(title, width - stateWidth - 12, Theme.TitleScale);
        CollectorChrome.Text(draw, origin, shownTitle, Theme.TextPrimary, Theme.TitleScale);
        var stateX = origin.X + width - stateWidth;
        draw.AddCircleFilled(new Vector2(stateX + Theme.DotRadius, origin.Y + (titleH / 2f)), Theme.DotRadius, Theme.Pack(mark));
        CollectorChrome.Text(draw, new Vector2(stateX + (2 * Theme.DotRadius) + 8, origin.Y + ((titleH - smH) / 2f)), stateLabel, mark, sm);
        if (shownTitle != title)
            CollectorChrome.HoverTip(origin, origin + new Vector2(width - stateWidth - 12, titleH), title);
        ImGui.Dummy(new Vector2(width, titleH));

        // Ownership line; hovering it explains where the item was seen.
        var owned = plan.OwnedTier < 0 ? "none" : TierLabel(plan.OwnedTier);
        var ownedText = $"Owned: {owned} · Goal: {TierLabel(target)}";
        var ownedPos = origin + new Vector2(0, titleH + 4);
        var ownedSize = new Vector2(CollectorChrome.TextWidth(ownedText, sm), smH);
        CollectorChrome.Text(draw, ownedPos, ownedText, Theme.TextSecondary, sm);
        if (plan.OwnedTier >= 0 && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(ownedPos, ownedPos + ownedSize))
            DrawOwnershipTooltip(plan.Piece, plan.OwnedTier, ownedPos + new Vector2(ownedSize.X + 12, ownedSize.Y));
        ImGui.Dummy(new Vector2(width, 4 + smH));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, titleH + 4 + smH + 12));

        if (plan.Complete)
        {
            CollectorChrome.Para("No purchases needed at this goal.", Theme.SecondaryScale, Theme.TextSecondary, 0);
        }
        else
        {
            CollectorChrome.Hairline();
            var rows = plan.Remaining.OrderByDescending(k => k.Value).ToList();
            for (var i = 0; i < rows.Count; i++)
            {
                var have = _plugin.Ownership.TotalCount(rows[i].Key);
                CollectorChrome.CostRow(_plugin.Shops.ItemName(rows[i].Key), rows[i].Value, have, true, i == rows.Count - 1);
            }
        }

        foreach (var note in plan.Notes)
        {
            CollectorChrome.Space(12);
            var split = note.IndexOf(": ", StringComparison.Ordinal);
            var lead = split > 0 ? note[..split] : note;
            var body = split > 0 ? Capitalize(note[(split + 2)..]) : "";
            CollectorChrome.Notice(lead, body, note.StartsWith("Free", StringComparison.Ordinal) ? UiState.Ready : UiState.Idle);
        }
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void DrawOwnershipTooltip(PieceChain chain, int ownedTier, Vector2 anchor)
    {
        var node = chain.Tier(ownedTier);
        if (node == null)
            return;
        var live = _plugin.Ownership.LiveCount(node.ItemId);
        var details = new List<string>();
        if (live > 0)
            details.Add("In bags, armoury or equipped gear.");
        foreach (var (label, count, seen) in _plugin.Ownership.CachedSources(node.ItemId))
            details.Add($"{label}: {count} (seen {seen.ToLocalTime():yyyy-MM-dd HH:mm})");
        // Bottom left of the anchor: the panel rises to the right of the hovered line, clear of the rows below it.
        CollectorChrome.RichTip(anchor, new Vector2(0, 1), node.Name, details);
    }

    private void DrawMaterialRequirement(uint itemId, long required, bool first, bool last)
        => CollectorChrome.CostRow(_plugin.Shops.ItemName(itemId), required, _plugin.Ownership.TotalCount(itemId), first, last);

    private void DrawSettingsPage(GearCollection? collection, float bodyHeight)
    {
        CollectorChrome.Columns(
            "settings",
            bodyHeight,
            () =>
            {
                CollectorChrome.Card(() =>
                {
                    CollectorChrome.Section("Goal & ownership", "Both controls use the existing saved settings.");
                    DrawTargetTier();
                    CollectorChrome.Space(12);
                    DrawCachedToggle(separator: true);
                });
                CollectorChrome.Card(
                    () =>
                    {
                        CollectorChrome.Section("What the scanner can see");
                        CollectorChrome.Para("Live: bags, armoury chest, equipped gear, and currency containers. Saddlebag and retainer contents are remembered only while the game has them open.", Theme.SecondaryScale, Theme.TextSecondary, 12);
                        CollectorChrome.Para("The glamour dresser cannot be read by plugins on this API, so pieces stored there show as missing.", Theme.SecondaryScale, Theme.TextSecondary, 0);
                    },
                    fill: true,
                    gapAfter: false);
            },
            () =>
            {
                if (collection != null)
                    DrawRequirementsCard(collection, null, collection.Pieces.Count);
                CollectorChrome.Card(
                    () =>
                    {
                        var snapshots = _plugin.Config.Snapshots.Values.OrderBy(s => s.Label, StringComparer.Ordinal).ToList();
                        CollectorChrome.Section("Remembered containers", "Snapshots refresh every 5 seconds while logged in and readable.", snapshots.Count.ToString());
                        if (snapshots.Count == 0)
                        {
                            CollectorChrome.Notice("Empty", "Open a saddlebag or retainer to record tracked items.", UiState.Empty);
                            return;
                        }

                        for (var i = 0; i < snapshots.Count; i++)
                        {
                            var snapshot = snapshots[i];
                            CollectorChrome.DetailRow(
                                snapshot.Label,
                                $"{snapshot.Counts.Values.Sum():N0} tracked items · seen {snapshot.SeenUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
                                i == 0,
                                i == snapshots.Count - 1);
                        }
                    },
                    fill: true,
                    gapAfter: false);
            });
    }

    private void DrawRequirementsCard(GearCollection collection, int? done = null, int? total = null)
    {
        CollectorChrome.Card(() =>
        {
            CollectorChrome.Section("Requirements");
            var loggedIn = Plugin.ClientState.IsLoggedIn;
            var cached = _plugin.Config.IncludeCachedContainers;
            CollectorChrome.StatusRow("Collection data", collection.Pieces.Count == 0 ? Copy.Missing("gear pieces") : $"{collection.Pieces.Count} pieces", collection.Pieces.Count == 0 ? UiState.Missing : UiState.Ready);
            CollectorChrome.StatusRow("Live ownership", loggedIn ? "Ready" : "Idle", loggedIn ? UiState.Ready : UiState.Idle);
            CollectorChrome.StatusRow("Containers", cached ? $"{_plugin.Config.Snapshots.Count} included" : "Excluded", cached ? UiState.Ready : UiState.Idle);
            CollectorChrome.StatusRow("Target tier", TierLabel(Math.Clamp(_plugin.Config.TargetTier, 0, 3)), UiState.Ready);
            if (done != null && total != null)
                CollectorChrome.StatusRow("At selected goal", $"{done} / {total}", done == total ? UiState.Ready : UiState.Running);
        });
    }

    private void DrawSettingsCard(bool fill = false)
    {
        CollectorChrome.Card(
            () =>
            {
                CollectorChrome.Section("Settings");
                DrawTargetTier();
                CollectorChrome.Space(12);
                DrawCachedToggle(separator: true);
            },
            fill: fill,
            gapAfter: !fill);
    }

    private void DrawTargetTier()
    {
        var target = Math.Clamp(_plugin.Config.TargetTier, 0, 3);
        CollectorChrome.Para("Target tier", 1f, Theme.TextPrimary, 4);
        CollectorChrome.Para("Progress and remaining costs are calculated against this upgrade tier.", Theme.SecondaryScale, Theme.TextSecondary, 12);
        var picked = CollectorChrome.Segmented("target", [TierLabel(0), TierLabel(1), TierLabel(2), TierLabel(3)], target);
        if (picked < 0 || picked == target)
            return;
        _plugin.Config.TargetTier = picked;
        _plugin.Config.Save();
    }

    private void DrawCachedToggle(bool separator)
    {
        var cached = _plugin.Config.IncludeCachedContainers;
        if (!CollectorChrome.Toggle("Include remembered saddlebag and retainers", ref cached,
                "Counts pieces remembered from saddlebag and retainer snapshots in addition to live bags, armoury, equipped gear, and currency.",
                separator))
            return;
        _plugin.Config.IncludeCachedContainers = cached;
        _plugin.Config.Save();
    }

    private static void DrawEmptyCollection()
    {
        CollectorChrome.Card(
            () =>
            {
                var draw = ImGui.GetWindowDrawList();
                var origin = ImGui.GetCursorScreenPos();
                var width = CollectorChrome.Avail();
                var titleH = CollectorChrome.LineHeight(Theme.TitleScale);
                draw.AddCircleFilled(new Vector2(origin.X + 4, origin.Y + (titleH / 2f)), 4, Theme.Pack(Copy.MarkColor(UiState.Missing)));
                CollectorChrome.Text(draw, origin + new Vector2(16, 0), "Missing collection data", Theme.TextPrimary, Theme.TitleScale);
                ImGui.Dummy(new Vector2(width, titleH));
                ImGui.SetCursorScreenPos(origin + new Vector2(0, titleH + 8));
                CollectorChrome.Para(
                    "No tracked sets could be built from the current game data. This can happen when the current patch does not contain the tracked sets.",
                    1f,
                    Theme.TextSecondary,
                    12);
                CollectorChrome.Para(
                    "Counting settings remain available. No collection totals or acquisition costs are invented while the data is unavailable.",
                    Theme.SecondaryScale,
                    Theme.TextSecondary,
                    0);
            },
            gapAfter: false);
    }

    // ---- footer -----------------------------------------------------------------------------------------------

    private readonly record struct FooterText(string Primary, string Secondary);

    private static FooterText FooterFor(GearCollection? collection, int done, int target)
    {
        if (collection == null || collection.Pieces.Count == 0)
            return new FooterText("Empty · no collection data is available", "Counting settings remain available; no gear action is taken.");
        if (!Plugin.ClientState.IsLoggedIn)
            return new FooterText("Idle · log in to refresh live ownership", "Remembered container snapshots remain visible according to the counting setting.");
        if (done == collection.Pieces.Count)
            return new FooterText($"Ready · collection complete at {TierLabel(target)}", "Change the goal to plan another tier; ownership snapshots continue to refresh.");
        return new FooterText($"Ready · {collection.Pieces.Count - done} pieces remain at {TierLabel(target)}", "Ownership snapshots refresh every 5 seconds; no purchases or gear actions are automated.");
    }

    // Wide: one line each. Narrow: the second line wraps (two lines at most) rather than eliding the safety note.
    private static float FooterHeight(float width, string secondary)
    {
        var gutter = Gutter(width);
        var lh = CollectorChrome.LineHeight();
        var secondaryHeight = CollectorChrome.LineHeight(Theme.SecondaryScale);
        if (width < 620)
        {
            var wrapped = CollectorChrome.WrappedHeight(secondary, width - gutter - 16 - gutter, Theme.SecondaryScale);
            secondaryHeight = MathF.Min(wrapped, 2 * secondaryHeight);
        }

        return 12 + MathF.Max(lh + 4 + secondaryHeight, ControlHeight) + 12;
    }

    private static void DrawFooter(ImDrawListPtr draw, Vector2 winPos, Vector2 winSize, float gutter, float height, FooterText text, UiState state)
    {
        var width = winSize.X;
        var top = winSize.Y - height;
        var lh = CollectorChrome.LineHeight();
        var smH = CollectorChrome.LineHeight(Theme.SecondaryScale);
        var narrow = width < 620;
        var textX = gutter + 16;
        var textWidth = width - gutter - textX;

        draw.PushClipRect(winPos + new Vector2(0, top), winPos + winSize, false);
        draw.AddRectFilled(winPos + new Vector2(0, top), winPos + winSize, Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, top), winPos + new Vector2(width, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        var primary = Parenthetical.Replace(text.Primary, "");
        var shownPrimary = CollectorChrome.FitText(primary, textWidth);
        var shownSecondary = narrow
            ? CollectorChrome.ClampLines(text.Secondary, textWidth, Theme.SecondaryScale, 2)
            : CollectorChrome.FitText(text.Secondary, textWidth, Theme.SecondaryScale);
        var secondaryHeight = narrow ? CollectorChrome.WrappedHeight(shownSecondary, textWidth, Theme.SecondaryScale) : smH;
        var block = lh + 4 + secondaryHeight;
        var textTop = top + ((height - block) / 2f);

        draw.AddCircleFilled(winPos + new Vector2(gutter + 4, textTop + (lh / 2f)), 4, Theme.Pack(Copy.MarkColor(state)));
        CollectorChrome.Text(draw, winPos + new Vector2(textX, textTop), shownPrimary, Theme.TextPrimary);
        CollectorChrome.Text(draw, winPos + new Vector2(textX, textTop + lh + 4), shownSecondary, Theme.TextSecondary, Theme.SecondaryScale, narrow ? textWidth : 0f);
        if (shownPrimary != primary || shownSecondary != text.Secondary)
        {
            var min = winPos + new Vector2(textX, textTop);
            var max = winPos + new Vector2(width - gutter, textTop + block);
            if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max))
                CollectorChrome.Tip($"{text.Primary}\n{text.Secondary}");
        }
    }

    // ---- stat cards -------------------------------------------------------------------------------------------

    private static void DrawStatCards(params Stat[] stats)
    {
        const float gap = 16;
        var available = CollectorChrome.Avail();
        var wide = available >= (3 * 220) + (2 * gap);
        var width = wide ? (available - (2 * gap)) / 3 : available;
        var noteLines = wide ? stats.Max(st => CollectorChrome.StatNoteLines(st.Note, width)) : 1;
        var height = CollectorChrome.StatCardHeight(!wide, noteLines);
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        for (var i = 0; i < stats.Length; i++)
        {
            var pos = origin + (wide ? new Vector2(i * (width + gap), 0) : new Vector2(0, i * (height + gap)));
            var stat = stats[i];
            CollectorChrome.StatCard(draw, pos, width, !wide, stat.Label, stat.Value, stat.Cap, stat.Note, stat.Fraction, noteLines);
        }

        var total = wide ? height : (stats.Length * height) + ((stats.Length - 1) * gap);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(available, total));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, total));
        ImGui.Dummy(new Vector2(available, gap - Theme.ItemSpacing.Y));
    }

    private string RemainingSummary(Dictionary<uint, long> remaining)
    {
        if (remaining.Count == 0)
            return "Complete";
        var parts = remaining.OrderByDescending(k => k.Value).Take(2)
            .Select(kv => $"{kv.Value:N0} {Abbreviate(_plugin.Shops.ItemName(kv.Key))}").ToList();
        if (remaining.Count > 2)
            parts.Add($"+{remaining.Count - 2} more");
        return string.Join(" · ", parts);
    }

    private static string JobsFor(string role) => RoleJobs.TryGetValue(role, out var jobs) ? jobs : "Jobs are read from the gear family";

    private static string TierLabel(int tier) => tier <= 0 ? "Base" : $"+{tier}";

    private static string GroupName(CollectorGroup group) => group switch
    {
        CollectorGroup.Overview => "Overview",
        CollectorGroup.Roles => "Roles",
        _ => "Settings",
    };

    /// <summary>Shorten long currency names so summary rows stay readable.</summary>
    private static string Abbreviate(string name) => name
        .Replace("Enlightenment ", "")
        .Replace("Final Final ", "")
        .Replace(" Obol", " obol");
}
