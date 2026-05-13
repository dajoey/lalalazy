using System.Numerics;
using ArmoireAutoFill.Data;
using ArmoireAutoFill.Logic;
using ArmoireAutoFill.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ArmoireAutoFill.Windows;

public class MainWindow : Window
{
    private static readonly Vector4 ColorMissing = new(1f, 0.5f, 0f, 1f);
    private static readonly Vector4 ColorInventory = new(0.4f, 0.85f, 0.4f, 1f);
    private static readonly Vector4 ColorArmoire = new(0.4f, 0.75f, 1f, 1f);
    private static readonly Vector4 ColorOk = new(0f, 1f, 0f, 1f);
    private static readonly Vector4 ColorMuted = new(0.65f, 0.65f, 0.65f, 1f);

    private readonly InventoryScanner _scanner;
    private readonly CabinetObserver _cabinet;
    private DateTime _lastScan = DateTime.MinValue;
    private static readonly TimeSpan ScanCooldown = TimeSpan.FromSeconds(5);

    public MainWindow(InventoryScanner scanner, CabinetObserver cabinet)
        : base("Armoire Auto-Fill###ArmoireAutoFillMain")
    {
        _scanner = scanner;
        _cabinet = cabinet;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 360),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        if (!ArmoireGearDatabase.IsLoaded)
        {
            ImGui.TextColored(ColorMissing, "Gear database is still loading. If this persists, check /xllog for errors.");
            return;
        }

        DrawSummary();
        ImGui.Separator();
        DrawScanButton();
        ImGui.Separator();
        DrawDungeonTable();
    }

    private void DrawSummary()
    {
        var owned = ArmoireGearDatabase.OwnedCount;
        var total = ArmoireGearDatabase.TotalItems;
        var progress = total > 0 ? (float)owned / total : 0f;

        ImGui.Text($"Armoire-eligible gear: {owned} / {total} owned");
        ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{progress * 100:F1}%");

        if (ArmoireGearDatabase.MissingCount > 0)
        {
            var incompleteDungeons = ArmoireGearDatabase.DungeonSets.Count(d => !d.IsComplete);
            ImGui.TextColored(ColorMissing, $"Missing: {ArmoireGearDatabase.MissingCount} entries across {incompleteDungeons} sources");
        }
        else if (owned > 0)
        {
            ImGui.TextColored(ColorOk, "All tracked armoire gear collected.");
        }

        if (_cabinet.LastSnapshot == DateTime.MinValue && _cabinet.CachedArmoireItemIds.Count == 0)
        {
            ImGui.TextColored(ColorMuted, "Armoire contents unknown. Open the armoire UI at an inn once to populate.");
        }
        else
        {
            var snapshotText = _cabinet.LastSnapshot == DateTime.MinValue
                ? "cached from previous session"
                : $"last snapshot {(DateTime.UtcNow - _cabinet.LastSnapshot).TotalMinutes:F0}m ago";
            ImGui.TextColored(ColorMuted, $"Armoire snapshot: {_cabinet.CachedArmoireItemIds.Count} items stored ({snapshotText}).");
        }
    }

    private void DrawScanButton()
    {
        if (ImGui.Button("Rescan inventory"))
        {
            _scanner.Scan();
            _lastScan = DateTime.UtcNow;
        }

        ImGui.SameLine();
        var cooldownRemaining = ScanCooldown - (DateTime.UtcNow - _lastScan);
        if (cooldownRemaining > TimeSpan.Zero)
        {
            ImGui.TextDisabled($"(cooldown: {cooldownRemaining.TotalSeconds:F0}s)");
        }
        else
        {
            ImGui.TextDisabled("(idle)");
        }
    }

    private static void DrawDungeonTable()
    {
        if (!ImGui.BeginTable("dungeon_table", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY))
            return;

        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Items", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Missing", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableHeadersRow();

        // Known dungeons first (alphabetical); "Source unknown" sinks to the bottom.
        var ordered = ArmoireGearDatabase.DungeonSets
            .OrderBy(d => d.ContentFinderConditionId == 0 ? 1 : 0)
            .ThenBy(d => d.Name);

        foreach (var dungeon in ordered)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            var open = ImGui.TreeNodeEx(dungeon.Name, ImGuiTreeNodeFlags.SpanFullWidth);

            ImGui.TableSetColumnIndex(1);
            ImGui.Text(dungeon.Items.Count.ToString());

            ImGui.TableSetColumnIndex(2);
            ImGui.Text(dungeon.OwnedCount.ToString());

            ImGui.TableSetColumnIndex(3);
            if (dungeon.MissingCount > 0)
                ImGui.TextColored(ColorMissing, dungeon.MissingCount.ToString());
            else
                ImGui.Text("0");

            ImGui.TableSetColumnIndex(4);
            if (dungeon.IsComplete)
                ImGui.TextColored(ColorOk, "Complete");
            else
                ImGui.TextColored(ColorMissing, $"{dungeon.MissingCount} left");

            if (open)
            {
                foreach (var item in dungeon.Items.OrderBy(i => i.Slot).ThenBy(i => i.Name))
                {
                    ImGui.Bullet();
                    ImGui.SameLine();

                    var (color, badge) = item.Owned switch
                    {
                        OwnershipStatus.InInventory => (ColorInventory, "Inventory"),
                        OwnershipStatus.InArmoire => (ColorArmoire, "Armoire"),
                        _ => (ColorMissing, "Missing"),
                    };

                    ImGui.TextColored(color, $"[{item.Slot}] {item.Name} — {badge}");

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"Item ID: {item.ItemId}");
                        ImGui.Text($"Slot: {item.Slot}");
                        ImGui.Text($"Source: {item.DungeonName ?? "Unknown"}");
                        ImGui.Text($"Status: {badge}");
                        ImGui.EndTooltip();
                    }
                }
                ImGui.TreePop();
            }
        }

        ImGui.EndTable();
    }
}
