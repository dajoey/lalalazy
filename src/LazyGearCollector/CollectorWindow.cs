using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace LazyGearCollector;

public sealed class CollectorWindow : Window
{
    private const float ControlHeight = 34f;

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

    private readonly Plugin _plugin;
    private CollectorGroup _group = CollectorGroup.Overview;
    private OverviewPage _overviewPage = OverviewPage.Summary;
    private string? _selectedRole;
    private string _searchText = "";

    public CollectorWindow(Plugin plugin) : base("Lazy Gear Collector##lazygear")
    {
        _plugin = plugin;
        Flags = ImGuiWindowFlags.NoTitleBar;
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

    private GearCollection? Current =>
        _plugin.Collections.FirstOrDefault(c => c.Id == _plugin.Config.LastCollectionId)
        ?? _plugin.Collections.FirstOrDefault();

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
            (allPlans, grandTotal, donePieces) = _plugin.Planner.PlanMany(collection.Pieces, target);
            foreach (var role in collection.Roles)
            {
                var chains = collection.ForRole(role).ToList();
                var (plans, remaining, done) = _plugin.Planner.PlanMany(chains, target);
                roles.Add(new RoleProgress(role, JobsFor(role), chains, plans, remaining, done));
            }
        }

        var state = OverallState(collection, donePieces);
        if (!SearchEnabled(collection))
            _searchText = "";

        DrawTopBar(collection, state);
        DrawSearchResults(collection);
        DrawFeatureRow(collection);
        DrawPageHeader(collection, roles, donePieces, grandTotal);

        var footerHeight = FooterHeight(ImGui.GetContentRegionAvail().X);
        ImGui.BeginChild("##gear_body", new Vector2(0, MathF.Max(40, ImGui.GetContentRegionAvail().Y - footerHeight)), false);
        if (collection == null && _group != CollectorGroup.Settings)
        {
            DrawEmptyCollection();
        }
        else
        {
            switch (_group)
            {
                case CollectorGroup.Overview when _overviewPage == OverviewPage.Summary:
                    DrawSummary(collection!, roles, allPlans, donePieces);
                    break;
                case CollectorGroup.Overview:
                    DrawMaterials(collection!, grandTotal);
                    break;
                case CollectorGroup.Roles:
                    DrawRoleDetail(collection!, roles.FirstOrDefault(r => r.Role == _selectedRole), target);
                    break;
                case CollectorGroup.Settings:
                    DrawSettingsPage(collection);
                    break;
            }
        }
        ImGui.EndChild();
        DrawFooter(collection, donePieces, target);
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

    private void DrawTopBar(GearCollection? collection, UiState state)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        var groups = Enum.GetValues<CollectorGroup>();
        var brandSize = new Vector2(MathF.Min(width, MathF.Max(220, (ImGui.CalcTextSize("Lazy Gear Collector").X * Theme.TitleScale) + 68)), 42);
        var groupSizes = groups.Select(group => new Vector2(MathF.Max(82, ImGui.CalcTextSize(GroupName(group)).X + 32), ControlHeight)).ToArray();
        var stateLabel = Copy.Word(state);
        var statusSize = new Vector2(MathF.Max(76, ImGui.CalcTextSize(stateLabel).X + 43), ControlHeight);
        var search = SearchEnabled(collection);
        const float minSearchWidth = 180;
        const float maxSearchWidth = 260;
        var leftWidth = brandSize.X + gap + groupSizes.Sum(size => size.X) + ((groupSizes.Length - 1) * gap);
        var singleRow = width >= leftWidth + statusSize.X + (2 * gap) + (search ? minSearchWidth + gap : 0);
        var groupPositions = new Vector2[groups.Length];
        var brandPos = new Vector2(0, 12);
        Vector2 statusPos;
        Vector2 searchPos = default;
        float searchWidth = 0;
        float barHeight;

        if (singleRow)
        {
            barHeight = 66;
            var x = brandSize.X + gap;
            var y = (barHeight - ControlHeight) / 2f;
            for (var i = 0; i < groups.Length; i++)
            {
                groupPositions[i] = new Vector2(x, y);
                x += groupSizes[i].X + gap;
            }
            statusPos = new Vector2(width - statusSize.X, y);
            if (search)
            {
                searchWidth = MathF.Min(maxSearchWidth, statusPos.X - gap - x);
                searchPos = new Vector2(statusPos.X - gap - searchWidth, y);
            }
        }
        else
        {
            statusPos = new Vector2(width - statusSize.X, 16);
            float x = 0;
            float y = 62;
            for (var i = 0; i < groups.Length; i++)
            {
                var buttonWidth = MathF.Min(groupSizes[i].X, width);
                if (x > 0 && x + buttonWidth > width)
                {
                    x = 0;
                    y += ControlHeight + gap;
                }
                groupPositions[i] = new Vector2(x, y);
                x += buttonWidth + gap;
            }
            var navigationBottom = y + ControlHeight;
            if (search)
            {
                searchWidth = MathF.Min(maxSearchWidth, width);
                searchPos = new Vector2(0, navigationBottom + gap);
                barHeight = searchPos.Y + ControlHeight + 12;
            }
            else
            {
                barHeight = navigationBottom + 12;
            }
        }

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        if (CollectorChrome.BrandButton(draw, origin + brandPos, brandSize))
        {
            _group = CollectorGroup.Overview;
            _overviewPage = OverviewPage.Summary;
        }
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            if (CollectorChrome.OutlineButton(draw, "group_" + group, GroupName(group), origin + groupPositions[i], groupSizes[i], _group == group))
                NavigateGroup(group, collection);
        }
        if (search)
            DrawSearchInput(origin + searchPos, searchWidth);
        CollectorChrome.StatusPill(draw, origin + statusPos, statusSize, stateLabel, state);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private void DrawSearchInput(Vector2 pos, float width)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.SetNextItemWidth(width);
        var framePadding = Theme.FramePadding;
        framePadding.Y = MathF.Max(framePadding.Y, (ControlHeight - ImGui.GetTextLineHeight()) / 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, framePadding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2 * Theme.Unit);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.SurfaceBorder);
        ImGui.InputTextWithHint("##gear_search", "Search roles and pages...", ref _searchText, 256);
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
    }

    private void DrawSearchResults(GearCollection? collection)
    {
        if (!SearchEnabled(collection) || _searchText.Trim().Length == 0)
            return;
        var term = _searchText.Trim();
        var matches = SearchDestinations(collection!)
            .Where(d => (d.Title + " " + d.Category + " " + d.Terms).Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToList();
        CollectorCard.Draw(() =>
        {
            Widgets.Caption($"{matches.Count} destinations for ‘{term}’");
            if (matches.Count == 0)
            {
                Widgets.SecondaryWrapped("No matching role or page. Try a job, role, material, or counting term.");
                return;
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
        });
    }

    private static IEnumerable<SearchDestination> SearchDestinations(GearCollection collection)
    {
        yield return new SearchDestination("Collection summary", "Overview", "home progress roles gear", CollectorGroup.Overview, OverviewPage.Summary, null);
        yield return new SearchDestination("Materials & wallet", "Overview", "materials costs currency wallet remaining", CollectorGroup.Overview, OverviewPage.Materials, null);
        yield return new SearchDestination("Counting & sources", "Settings", "settings counting target tier cached saddlebag retainers snapshots", CollectorGroup.Settings, OverviewPage.Summary, null);
        foreach (var role in collection.Roles)
            yield return new SearchDestination(role, "Role", JobsFor(role) + " gear pieces upgrades", CollectorGroup.Roles, OverviewPage.Summary, role);
    }

    private void DrawFeatureRow(GearCollection? collection)
    {
        var features = Features(collection);
        if (features.Length == 0)
            return;
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 3 * Theme.Unit;
        var sizes = features.Select(title => new Vector2(MathF.Min(width, MathF.Max(110, ImGui.CalcTextSize(title).X + 52)), ControlHeight)).ToArray();
        var positions = new Vector2[features.Length];
        float x = 0;
        float y = 8;
        for (var i = 0; i < features.Length; i++)
        {
            if (x > 0 && x + sizes[i].X > width)
            {
                x = 0;
                y += ControlHeight + (2 * Theme.Unit);
            }
            positions[i] = new Vector2(x, y);
            x += sizes[i].X + gap;
        }
        var barHeight = y + ControlHeight + 8;
        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        for (var i = 0; i < features.Length; i++)
        {
            if (CollectorChrome.FeatureButton(draw, "feature_" + features[i], features[i], origin + positions[i], sizes[i], FeatureSelected(features[i])))
                SelectFeature(features[i], collection);
        }
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private string[] Features(GearCollection? collection) => _group switch
    {
        CollectorGroup.Overview => ["Summary", "Materials"],
        CollectorGroup.Roles => collection?.Roles.ToArray() ?? [],
        _ => ["Counting & sources"],
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

    private void DrawPageHeader(GearCollection? collection, List<RoleProgress> roles, int done, Dictionary<uint, long> remaining)
    {
        var target = Math.Clamp(_plugin.Config.TargetTier, 0, 3);
        string title;
        string description;
        string count;
        string subtext;
        if (collection == null && _group != CollectorGroup.Settings)
        {
            title = "Collection unavailable";
            description = "No tracked gear sets could be built from the current game data.";
            count = "0 collections";
            subtext = Copy.Missing("collection data");
        }
        else if (_group == CollectorGroup.Settings)
        {
            title = "Counting & sources";
            description = "Choose the upgrade goal and which visible containers contribute to ownership.";
            count = $"{_plugin.Config.Snapshots.Count} remembered containers";
            subtext = $"Goal: {TierLabel(target)}";
        }
        else if (_group == CollectorGroup.Roles)
        {
            var role = roles.FirstOrDefault(r => r.Role == _selectedRole);
            title = role == null ? "Role gear" : $"{role.Role} gear";
            description = role == null ? "Select a role to inspect its pieces." : $"{role.Jobs} · Piece-by-piece ownership and acquisition costs.";
            count = role == null ? "0 roles selected" : $"{role.Done} / {role.Chains.Count} pieces at {TierLabel(target)}";
            subtext = role == null ? "Empty" : $"{role.Plans.Count(p => p.HasShortcut)} trade-up routes";
        }
        else if (_overviewPage == OverviewPage.Materials)
        {
            title = "Materials & wallet";
            description = "Compare aggregated acquisition costs with the currency and materials on hand.";
            count = $"{remaining.Count} remaining material types";
            subtext = $"{collection!.Currencies.Count} wallet currencies";
        }
        else
        {
            title = collection!.DisplayName;
            description = collection.SourceNote;
            count = $"{done} / {collection.Pieces.Count} pieces at {TierLabel(target)}";
            subtext = $"{collection.Roles.Count()} roles · {collection.Pieces.Sum(p => p.Tiers.Count)} tracked items";
        }

        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var tileSize = new Vector2(42);
        draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 2 * Theme.Unit);
        CollectorChrome.Diamond(draw, origin + (tileSize / 2f), 10, Theme.Pack(Theme.Accent));

        if (width >= 780)
        {
            var textX = origin.X + tileSize.X + 12;
            var countWidth = ImGui.CalcTextSize(count).X;
            var subtextWidth = ImGui.CalcTextSize(subtext).X;
            DrawText(draw, new Vector2(textX, origin.Y), FitText(title, width - tileSize.X - 12 - countWidth - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            DrawText(draw, new Vector2(textX, origin.Y + 29), FitText(description, width - tileSize.X - 12 - subtextWidth - 16), Theme.TextSecondary);
            DrawTextRight(draw, origin.X + width, origin.Y + 4, count, Theme.TextSecondary);
            DrawTextRight(draw, origin.X + width, origin.Y + 31, subtext, Theme.TextSecondary);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 58));
            ImGui.Separator();
            return;
        }

        DrawText(draw, origin + new Vector2(tileSize.X + 12, 4), FitText(title, width - tileSize.X - 12, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, tileSize.Y + 8));
        Widgets.SecondaryWrapped(description);
        Widgets.Caption(count);
        Widgets.Caption(subtext);
        ImGui.Separator();
    }

    private void DrawSummary(GearCollection collection, List<RoleProgress> roles, List<PiecePlan> allPlans, int done)
    {
        var target = Math.Clamp(_plugin.Config.TargetTier, 0, 3);
        var completeRoles = roles.Count(r => r.Chains.Count > 0 && r.Done == r.Chains.Count);
        TwoColumns(
            () =>
            {
                DrawStatCards(
                    new Stat("Pieces at goal", done.ToString(), collection.Pieces.Count.ToString(), $"Measured against {TierLabel(target)}", collection.Pieces.Count == 0 ? null : done / (float)collection.Pieces.Count),
                    new Stat("Roles complete", completeRoles.ToString(), roles.Count.ToString(), "Every piece at the selected goal", roles.Count == 0 ? null : completeRoles / (float)roles.Count),
                    new Stat("Trade-up hints", allPlans.Count(p => p.HasShortcut).ToString(), null, "Plans with a free or cheaper route", null));
                CollectorCard.Draw(() =>
                {
                    Section("Role progress", "Select a role for its piece-by-piece plan.", $"{roles.Count} roles");
                    if (roles.Count == 0)
                    {
                        Widgets.InfoBox("Empty — this collection has no role entries.", UiState.Empty);
                        return;
                    }
                    foreach (var role in roles)
                        DrawRoleProgressRow(role, target);
                });
            },
            () =>
            {
                DrawRequirementsCard(collection, done);
                DrawSettingsCard();
            });
    }

    private void DrawRoleProgressRow(RoleProgress role, int target)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        const float height = 84;
        ImGui.SetCursorScreenPos(origin);
        var clicked = ImGui.InvisibleButton("##gear_role_summary_" + role.Role, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar), Theme.Unit);
        if (clicked)
        {
            _selectedRole = role.Role;
            _group = CollectorGroup.Roles;
        }
        if (hovered)
            ImGui.SetTooltip($"Open the {role.Role} piece plan");

        var complete = role.Chains.Count > 0 && role.Done == role.Chains.Count;
        DrawText(draw, origin + new Vector2(8, 6), role.Role, Theme.TextPrimary);
        DrawTextRight(draw, origin.X + width - 8, origin.Y + 6, $"{role.Done} / {role.Chains.Count}", complete ? Theme.StateGreen : Theme.TextSecondary);
        DrawText(draw, origin + new Vector2(8, 28), FitText(role.Jobs, width - 16), Theme.TextSecondary);
        var remaining = RemainingSummary(role.Remaining);
        DrawText(draw, origin + new Vector2(8, 49), FitText(remaining, width - 16), complete ? Theme.StateGreen : Theme.TextSecondary);
        var min = origin + new Vector2(8, 72);
        var max = origin + new Vector2(width - 8, 76);
        draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
        var fraction = role.Chains.Count == 0 ? 0 : role.Done / (float)role.Chains.Count;
        if (fraction > 0)
            draw.AddRectFilled(min, min + new Vector2((max.X - min.X) * fraction, 0), Theme.Pack(complete ? Theme.StateGreen : Theme.StateAmber), 2f);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
    }

    private void DrawMaterials(GearCollection collection, Dictionary<uint, long> remaining)
    {
        TwoColumns(
            () =>
            {
                CollectorCard.Draw(() =>
                {
                    Section("Still needed overall", "Costs are aggregated across every piece at the selected goal.", $"{remaining.Count} material types");
                    if (remaining.Count == 0)
                    {
                        Widgets.InfoBox("Ready — collection complete at this goal. Nothing left to buy.", UiState.Ready);
                        return;
                    }
                    foreach (var pair in remaining.OrderByDescending(k => k.Value))
                        DrawMaterialRequirement(pair.Key, pair.Value);
                });
            },
            () =>
            {
                CollectorCard.Draw(() =>
                {
                    Section("Wallet", "Live ownership plus remembered containers when included.", $"{collection.Currencies.Count} currencies");
                    if (collection.Currencies.Count == 0)
                        Widgets.InfoBox("Empty — no currency costs were found for this collection.", UiState.Empty);
                    foreach (var currencyId in collection.Currencies)
                        DrawWalletRow(currencyId);
                });
                DrawSettingsCard();
            });
    }

    private void DrawRoleDetail(GearCollection collection, RoleProgress? role, int target)
    {
        if (role == null)
        {
            Widgets.InfoBox("Empty — select a role from the feature row to see its piece plan.", UiState.Empty);
            return;
        }

        TwoColumns(
            () =>
            {
                DrawStatCards(
                    new Stat("Pieces at goal", role.Done.ToString(), role.Chains.Count.ToString(), $"Measured against {TierLabel(target)}", role.Chains.Count == 0 ? null : role.Done / (float)role.Chains.Count),
                    new Stat("Owned pieces", role.Plans.Count(p => p.OwnedTier >= 0).ToString(), role.Chains.Count.ToString(), "Any tier visible to the scanner", role.Chains.Count == 0 ? null : role.Plans.Count(p => p.OwnedTier >= 0) / (float)role.Chains.Count),
                    new Stat("Trade-up hints", role.Plans.Count(p => p.HasShortcut).ToString(), null, "Free or cheaper acquisition routes", null));
                foreach (var plan in role.Plans)
                    CollectorCard.Draw(() => DrawPiecePlan(plan, target));
            },
            () =>
            {
                CollectorCard.Draw(() =>
                {
                    Section($"{role.Role} summary", role.Jobs, $"{role.Remaining.Count} material types");
                    if (role.Remaining.Count == 0)
                        Widgets.InfoBox("Ready — every piece in this role is at the selected goal.", UiState.Ready);
                    foreach (var pair in role.Remaining.OrderByDescending(k => k.Value))
                        DrawMaterialRequirement(pair.Key, pair.Value);
                });
                DrawRequirementsCard(collection, role.Done, role.Chains.Count);
                DrawSettingsCard();
            });
    }

    private void DrawPiecePlan(PiecePlan plan, int target)
    {
        Section($"{plan.Piece.SlotName} · {plan.Piece.PieceName}");
        var state = plan.Complete ? UiState.Ready : plan.OwnedTier < 0 ? UiState.Missing : UiState.Running;
        var stateLabel = plan.Complete ? "Ready" : plan.OwnedTier < 0 ? Copy.Missing("base piece") : "Running";
        Widgets.StatusDot(state);
        ImGui.SameLine();
        Widgets.StateText(stateLabel, state);
        var owned = plan.OwnedTier < 0 ? "none" : TierLabel(plan.OwnedTier);
        Widgets.Secondary($"Owned: {owned} · Goal: {TierLabel(target)}");
        if (plan.OwnedTier >= 0 && ImGui.IsItemHovered())
            DrawOwnershipTooltip(plan.Piece, plan.OwnedTier);

        if (plan.Complete)
        {
            Widgets.Body("No purchases needed at this goal.");
        }
        else
        {
            foreach (var pair in plan.Remaining.OrderByDescending(k => k.Value))
            {
                var have = _plugin.Ownership.TotalCount(pair.Key);
                var shortBy = Math.Max(0, pair.Value - have);
                Widgets.StatusDot(shortBy == 0 ? UiState.Ready : UiState.Running);
                ImGui.SameLine();
                ImGui.TextWrapped($"{pair.Value:N0} × {_plugin.Shops.ItemName(pair.Key)}");
                Widgets.SecondaryWrapped($"On hand {have:N0} · {(shortBy == 0 ? "covered" : $"short {shortBy:N0}")}");
            }
        }

        foreach (var note in plan.Notes)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
            ImGui.TextWrapped(note);
            ImGui.PopStyleColor();
        }
    }

    private void DrawOwnershipTooltip(PieceChain chain, int ownedTier)
    {
        var node = chain.Tier(ownedTier);
        if (node == null)
            return;
        var live = _plugin.Ownership.LiveCount(node.ItemId);
        var sources = _plugin.Ownership.CachedSources(node.ItemId).ToList();
        var text = live > 0 ? "In bags, armoury or equipped gear." : "";
        foreach (var (label, count, seen) in sources)
            text += $"\n{label}: {count} (seen {seen.ToLocalTime():yyyy-MM-dd HH:mm})";
        ImGui.SetTooltip(string.IsNullOrWhiteSpace(text) ? node.Name : $"{node.Name}\n{text}");
    }

    private void DrawMaterialRequirement(uint itemId, long required)
    {
        var have = _plugin.Ownership.TotalCount(itemId);
        var shortBy = Math.Max(0, required - have);
        Widgets.StatusDot(shortBy == 0 ? UiState.Ready : UiState.Running);
        ImGui.SameLine();
        ImGui.TextWrapped($"{required:N0} × {_plugin.Shops.ItemName(itemId)}");
        Widgets.SecondaryWrapped($"On hand {have:N0} · {(shortBy == 0 ? "Ready — enough on hand" : $"Running — short {shortBy:N0}")}");
        ImGui.Separator();
    }

    private void DrawWalletRow(uint currencyId)
    {
        var start = ImGui.GetCursorPos();
        var width = ImGui.GetContentRegionAvail().X;
        var name = _plugin.Shops.ItemName(currencyId);
        var value = $"{_plugin.Ownership.TotalCount(currencyId):N0} on hand";
        ImGui.TextWrapped(name);
        var nameWidth = ImGui.CalcTextSize(name).X;
        var valueWidth = ImGui.CalcTextSize(value).X;
        if (nameWidth + valueWidth + 24 <= width)
        {
            ImGui.SetCursorPos(new Vector2(start.X + width - valueWidth, start.Y));
            Widgets.Secondary(value);
        }
        else
        {
            Widgets.SecondaryWrapped(value);
        }
        ImGui.Separator();
    }

    private void DrawSettingsPage(GearCollection? collection)
    {
        TwoColumns(
            () =>
            {
                CollectorCard.Draw(() =>
                {
                    Section("Goal & ownership", "Both controls use the existing saved settings.");
                    DrawTargetTier();
                    DrawCachedToggle();
                });
                CollectorCard.Draw(() =>
                {
                    Section("What the scanner can see");
                    Widgets.SecondaryWrapped("Live: bags, armoury chest, equipped gear, and currency containers. Saddlebag and retainer contents are remembered only while the game has them open.");
                    Widgets.SecondaryWrapped("The glamour dresser cannot be read by plugins on this API, so pieces stored there show as missing.");
                });
            },
            () =>
            {
                if (collection != null)
                    DrawRequirementsCard(collection, null, collection.Pieces.Count);
                CollectorCard.Draw(() =>
                {
                    Section("Remembered containers", "Snapshots refresh every 5 seconds while logged in and readable.", $"{_plugin.Config.Snapshots.Count} containers");
                    if (_plugin.Config.Snapshots.Count == 0)
                    {
                        Widgets.InfoBox("Empty — open a saddlebag or retainer to record tracked items.", UiState.Empty);
                        return;
                    }
                    foreach (var snapshot in _plugin.Config.Snapshots.Values.OrderBy(s => s.Label, StringComparer.Ordinal))
                    {
                        Widgets.Body(snapshot.Label);
                        Widgets.SecondaryWrapped($"{snapshot.Counts.Values.Sum():N0} tracked items · seen {snapshot.SeenUtc.ToLocalTime():yyyy-MM-dd HH:mm}");
                        ImGui.Separator();
                    }
                });
            });
    }

    private void DrawRequirementsCard(GearCollection collection, int? done = null, int? total = null)
    {
        CollectorCard.Draw(() =>
        {
            Section("Requirements");
            Requirement("Collection data", collection.Pieces.Count == 0 ? Copy.Missing("gear pieces") : $"{collection.Pieces.Count} pieces", collection.Pieces.Count == 0 ? UiState.Missing : UiState.Ready);
            Requirement("Live ownership", Plugin.ClientState.IsLoggedIn ? "Ready" : "Idle", Plugin.ClientState.IsLoggedIn ? UiState.Ready : UiState.Idle);
            Requirement("Remembered containers", _plugin.Config.IncludeCachedContainers ? $"{_plugin.Config.Snapshots.Count} included" : "Excluded", _plugin.Config.IncludeCachedContainers ? UiState.Ready : UiState.Idle);
            Requirement("Target tier", TierLabel(Math.Clamp(_plugin.Config.TargetTier, 0, 3)), UiState.Ready);
            if (done != null && total != null)
                Requirement("At selected goal", $"{done} / {total}", done == total ? UiState.Ready : UiState.Running);
        });
    }

    private static void Requirement(string label, string value, UiState state)
    {
        var start = ImGui.GetCursorPos();
        var width = ImGui.GetContentRegionAvail().X;
        Widgets.StatusDot(state);
        ImGui.SameLine();
        Widgets.Secondary(label);
        var valueWidth = ImGui.CalcTextSize(value).X;
        var labelWidth = ImGui.CalcTextSize(label).X;
        if (labelWidth + valueWidth + 42 <= width)
        {
            ImGui.SetCursorPos(new Vector2(start.X + width - valueWidth, start.Y));
            Widgets.Caption(value);
        }
        else
        {
            ImGui.SetCursorPosX(start.X + 24);
            Widgets.Caption(value);
        }
    }

    private void DrawSettingsCard()
    {
        CollectorCard.Draw(() =>
        {
            Section("Settings");
            DrawTargetTier();
            DrawCachedToggle();
        });
    }

    private void DrawTargetTier()
    {
        var target = Math.Clamp(_plugin.Config.TargetTier, 0, 3);
        Widgets.Body("Target tier");
        Widgets.SecondaryWrapped("Progress and remaining costs are calculated against this upgrade tier.");
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        var columns = width >= 280 ? 4 : 2;
        var buttonWidth = (width - ((columns - 1) * gap)) / columns;
        for (var i = 0; i < 4; i++)
        {
            var row = i / columns;
            var column = i % columns;
            var pos = origin + new Vector2(column * (buttonWidth + gap), row * (ControlHeight + gap));
            if (!CollectorChrome.OutlineButton(draw, "target_" + i, TierLabel(i), pos, new Vector2(buttonWidth, ControlHeight), target == i) || target == i)
                continue;
            _plugin.Config.TargetTier = i;
            _plugin.Config.Save();
            target = i;
        }
        var rows = (4 + columns - 1) / columns;
        ImGui.SetCursorScreenPos(origin + new Vector2(0, (rows * ControlHeight) + ((rows - 1) * gap) + Theme.ItemSpacing.Y));
    }

    private void DrawCachedToggle()
    {
        var cached = _plugin.Config.IncludeCachedContainers;
        if (!CollectorChrome.Toggle("Include remembered saddlebag and retainers", ref cached,
                "Counts pieces remembered from saddlebag and retainer snapshots in addition to live bags, armoury, equipped gear, and currency."))
            return;
        _plugin.Config.IncludeCachedContainers = cached;
        _plugin.Config.Save();
    }

    private static void DrawEmptyCollection()
    {
        Widgets.InfoBox("Missing collection data — no tracked sets could be built from the current game data. This can happen when the current patch does not contain the tracked sets.", UiState.Missing);
        Widgets.SecondaryWrapped("Counting settings remain available. No collection totals or acquisition costs are invented while the data is unavailable.");
    }

    private void DrawFooter(GearCollection? collection, int done, int target)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = FooterHeight(width);
        var draw = ImGui.GetWindowDrawList();
        var state = OverallState(collection, done);
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));

        string primary;
        string secondary;
        if (collection == null || collection.Pieces.Count == 0)
        {
            primary = "Empty · no collection data is available";
            secondary = "Counting settings remain available; no gear action is taken.";
        }
        else if (!Plugin.ClientState.IsLoggedIn)
        {
            primary = "Idle · log in to refresh live ownership";
            secondary = "Remembered container snapshots remain visible according to the counting setting.";
        }
        else if (done == collection.Pieces.Count)
        {
            primary = $"Ready · collection complete at {TierLabel(target)}";
            secondary = "Change the goal to plan another tier; ownership snapshots continue to refresh.";
        }
        else
        {
            primary = $"Ready · {collection.Pieces.Count - done} pieces remain at {TierLabel(target)}";
            secondary = "Ownership snapshots refresh every 5 seconds; no purchases or gear actions are automated.";
        }

        draw.AddCircleFilled(origin + new Vector2(12, 19), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        DrawText(draw, origin + new Vector2(24, 9), FitText(primary, width - 32), Theme.TextPrimary);
        DrawText(draw, origin + new Vector2(24, 36), FitText(secondary, width - 32), Theme.TextSecondary);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
    }

    private static float FooterHeight(float width) => width < 620 ? 72 : 64;

    private static void TwoColumns(Action work, Action rail)
    {
        if (ImGui.GetContentRegionAvail().X >= 820)
        {
            using var columns = Layout.TwoColumns.Begin(0.72f);
            work();
            columns.Split();
            rail();
        }
        else
        {
            work();
            ImGui.Separator();
            rail();
        }
    }

    private static void Section(string title, string? description = null, string? count = null)
    {
        Widgets.Title(count == null ? title : $"{title} · {count}");
        if (!string.IsNullOrEmpty(description))
            Widgets.SecondaryWrapped(description);
    }

    private static void DrawStatCards(params Stat[] stats)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var stacked = available < 650;
        var gap = 3 * Theme.Unit;
        var width = stacked ? available : (available - (2 * gap)) / 3;
        const float height = 112;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
            CollectorChrome.Diamond(draw, pos + new Vector2(20, 20), 5, Theme.Pack(Theme.Accent));
            ImGui.SetCursorScreenPos(pos + new Vector2(38, 11));
            Widgets.Caption(stat.Label);
            ImGui.SetCursorScreenPos(pos + new Vector2(12, 34));
            Widgets.Title(stat.Cap == null ? stat.Value : $"{stat.Value} / {stat.Cap}");
            if (stat.Fraction is { } fraction)
            {
                var min = pos + new Vector2(12, 70);
                var max = pos + new Vector2(width - 12, 74);
                draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
                if (fraction > 0)
                    draw.AddRectFilled(min, min + new Vector2((max.X - min.X) * Math.Clamp(fraction, 0, 1), 0), Theme.Pack(Theme.StateGreen), 2f);
            }
            ImGui.SetCursorScreenPos(pos + new Vector2(12, 84));
            Widgets.Caption(FitText(stat.Note, width - 24));
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(0, stacked ? stats.Length * (height + gap) : height + gap));
    }

    private string RemainingSummary(Dictionary<uint, long> remaining)
    {
        if (remaining.Count == 0)
            return "Ready · every piece is at the selected goal";
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

    private static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
            return;
        if (scale == 1)
            draw.AddText(pos, Theme.Pack(color), text);
        else
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    private static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color)
    {
        if (text.Length == 0)
            return;
        draw.AddText(new Vector2(right - ImGui.CalcTextSize(text).X, y), Theme.Pack(color), text);
    }

    private static string FitText(string text, float width, float scale = 1)
    {
        if (width <= 0)
            return "";
        if (ImGui.CalcTextSize(text).X * scale <= width)
            return text;
        const string ellipsis = "…";
        var length = text.Length;
        while (length > 0 && ImGui.CalcTextSize(text[..length] + ellipsis).X * scale > width)
            length--;
        return length == 0 ? "" : text[..length].TrimEnd() + ellipsis;
    }

    /// <summary>Shorten long currency names so summary rows stay readable.</summary>
    private static string Abbreviate(string name) => name
        .Replace("Enlightenment ", "")
        .Replace("Final Final ", "")
        .Replace(" Obol", " obol");
}
