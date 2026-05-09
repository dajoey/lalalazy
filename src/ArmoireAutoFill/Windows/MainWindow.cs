using System.Numerics;
using ArmoireAutoFill.Data;
using ArmoireAutoFill.Logic;
using ArmoireAutoFill.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace ArmoireAutoFill.Windows;

public class MainWindow : Window
{
    private readonly InventoryScanner _scanner;
    private DateTime _lastScan = DateTime.MinValue;
    private static readonly TimeSpan ScanCooldown = TimeSpan.FromSeconds(5);

    public MainWindow(InventoryScanner scanner)
        : base("Armoire Auto-Fill###ArmoireAutoFillMain")
    {
        _scanner = scanner;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(550, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        if (ArmoireGearDatabase.AllItems.Count == 0)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Gear database is empty. Check that armoire_gear.json is embedded correctly.");
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
        var progress = (float)owned / total;

        ImGui.Text($"Armoire Dungeon Gear: {owned} / {total} owned");
        ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{progress * 100:F1}%");

        if (ArmoireGearDatabase.MissingCount > 0)
        {
            var incompleteDungeons = ArmoireGearDatabase.DungeonSets.Count(d => !d.IsComplete);
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1),
                $"Missing: {ArmoireGearDatabase.MissingCount} pieces across {incompleteDungeons} dungeons");
        }
        else if (owned > 0)
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "All tracked dungeon gear collected!");
        }
    }

    private void DrawScanButton()
    {
        if (ImGui.Button("Rescan Inventory"))
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

        ImGui.TableSetupColumn("Dungeon", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Items", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Missing", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableHeadersRow();

        foreach (var dungeon in ArmoireGearDatabase.DungeonSets.OrderBy(d => d.Name))
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
                ImGui.TextColored(new Vector4(1, 0.3f, 0, 1), dungeon.MissingCount.ToString());
            else
                ImGui.Text("0");

            ImGui.TableSetColumnIndex(4);
            if (dungeon.IsComplete)
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Complete");
            else
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), $"{dungeon.MissingCount} left");

            if (open)
            {
                foreach (var item in dungeon.Items.OrderBy(i => i.Slot).ThenBy(i => i.Name))
                {
                    ImGui.Bullet();
                    ImGui.SameLine();

                    var owned = item.Owned != OwnershipStatus.NotOwned;
                    if (owned)
                    {
                        ImGui.TextColored(new Vector4(0.4f, 0.8f, 0.4f, 1),
                            $"[{item.Slot}] {item.Name} - Owned");
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(1, 0.5f, 0, 1),
                            $"[{item.Slot}] {item.Name} - Missing");
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"Item ID: {item.ItemId}");
                        ImGui.Text($"Slot: {item.Slot}");
                        ImGui.Text($"Dungeon: {item.DungeonName ?? "Unknown"}");
                        ImGui.Text($"Status: {(owned ? "Owned" : "Not owned")}");
                        ImGui.EndTooltip();
                    }
                }
                ImGui.TreePop();
            }
        }

        ImGui.EndTable();
    }
}
