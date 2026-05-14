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
    private static readonly TimeSpan ScanCooldown = TimeSpan.FromSeconds(2);

    public MainWindow(InventoryScanner scanner, CabinetObserver cabinet)
        : base("Armoire Auto-Fill###ArmoireAutoFillMain")
    {
        _scanner = scanner;
        _cabinet = cabinet;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620, 380),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void OnOpen()
    {
        // Always refresh ownership when the window opens so the user doesn't
        // wonder whether the data is stale.
        _scanner.Scan();
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
        DrawActions();
        ImGui.Separator();
        DrawDungeonTable();
    }

    private void DrawSummary()
    {
        var total = ArmoireGearDatabase.TotalItems;
        var inInv = _scanner.LastInventoryHits;
        var inArm = _scanner.LastArmoireHits;
        var owned = inInv + inArm;
        var missing = total - owned;
        var progress = total > 0 ? (float)owned / total : 0f;

        ImGui.Text($"Armoire-eligible gear: {owned} / {total} owned");
        ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{progress * 100:F1}%");

        ImGui.TextColored(ColorInventory, $"  Inventory / armory chest: {inInv}");
        ImGui.SameLine();
        ImGui.TextColored(ColorArmoire, $"   Stored in armoire: {inArm}");
        ImGui.SameLine();
        ImGui.TextColored(ColorMissing, $"   Missing: {missing}");

        ImGui.Spacing();

        var scanText = _scanner.LastScan == DateTime.MinValue
            ? "inventory not yet scanned"
            : $"inventory scanned {Ago(_scanner.LastScan)} ago — found {_scanner.LastInventoryItemsSeen} items";
        ImGui.TextColored(ColorMuted, scanText);

        if (!_cabinet.CabinetDataAvailable && _cabinet.CachedArmoireItemIds.Count == 0)
        {
            ImGui.TextColored(ColorMuted, "Armoire: still waiting for the server to send cabinet data (usually a few seconds after login).");
        }
        else
        {
            var snapshotText = _cabinet.LastSnapshot == DateTime.MinValue
                ? "cached from previous session"
                : $"last update {Ago(_cabinet.LastSnapshot)} ago";
            var availability = _cabinet.CabinetDataAvailable ? "live" : "cached";
            ImGui.TextColored(ColorMuted,
                $"Armoire snapshot ({availability}): {_cabinet.CachedArmoireItemIds.Count} items currently stored — {snapshotText}.");
        }
    }

    private void DrawActions()
    {
        if (ImGui.Button("Rescan inventory"))
            _scanner.Scan();
        ImGui.SameLine();
        if (ImGui.Button("Snapshot armoire now"))
            _cabinet.ForceSnapshot();

        ImGui.SameLine();
        var sinceScan = DateTime.UtcNow - _scanner.LastScan;
        if (_scanner.LastScan != DateTime.MinValue && sinceScan < ScanCooldown)
            ImGui.TextDisabled($"(scan cooldown {ScanCooldown.TotalSeconds - sinceScan.TotalSeconds:F0}s)");
        else
            ImGui.TextDisabled("(idle)");
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

    private static string Ago(DateTime utc)
    {
        var elapsed = DateTime.UtcNow - utc;
        if (elapsed.TotalSeconds < 60)
            return $"{elapsed.TotalSeconds:F0}s";
        if (elapsed.TotalMinutes < 60)
            return $"{elapsed.TotalMinutes:F0}m";
        return $"{elapsed.TotalHours:F1}h";
    }
}
