using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using LazyFashionReport.Core;

namespace LazyFashionReport.Adapters;

/// <summary>
/// The live executor (P4 executor live half, v0.6.0.0). Everything here runs on the framework
/// thread and MUTATES: gear moves onto equipped slots, pieces come out of the glamour dresser
/// and the armoire. Member pins were enumerated from omasky's INSTALLED FFXIVClientStructs
/// (Hooks/15.0.3.4, 2026-09-12, dnfile probe with a negative control) BEFORE this file was
/// written:
/// - InventoryManager: i32 MoveItemSlot(InventoryType, u16, InventoryType, u16, bool) - the
///   equip primitive, WITH a result code (non-zero = rejected, reported, never retried).
/// - MirageManager: bool RestorePrismBoxItem(u32) - glamour-dresser withdraw by item id.
/// - Cabinet (UIState->Cabinet): bool WithdrawCabinetItem(u32) - armoire withdraw by item id;
///   like the store direction (ArmoireAutoFill's lesson), the cabinet data only loads while
///   the armoire UI has been opened, so an unloaded cabinet is reported, never blind-called.
///
/// Hard rules encoded here:
/// - An unhinted "any item" slot is NEVER touched (it already scores base).
/// - Dye is NEVER auto-applied: no verified dye-apply call exists in this ClientStructs
///   build (the plate-editor pending-stain path needs its own in-game probe first), so a
///   planned dye becomes a manual reminder on the result, not a silent skip.
/// - One pass, no retry loops: a withdraw that lands late resolves exactly once via a bounded
///   framework-tick continuation, and a source that changed since the plan was built is
///   skipped, never forced.
/// </summary>
internal static unsafe class ApplyMover
{
    /// <summary>Destination slot inside InventoryType.EquippedItems for a FashionSlot; the
    /// table itself lives in Core (ApplyDestinations) so the offline harness pins it.</summary>
    public static int EquippedDest(FashionSlot slot) => ApplyDestinations.EquippedDest(slot);

    /// <summary>Withdraws waiting for their piece to land in bags (bounded, one equip attempt each).</summary>
    private sealed record PendingWithdraw
    {
        public required ApplyStep Step { get; init; }
        public int TicksLeft { get; set; }
    }

    private const int PendingTicksBudget = 180; // ~3s at 60fps: server round-trip with headroom.
    private static readonly List<PendingWithdraw> Pending = new();
    private static ApplyLiveResult? _live;      // the in-flight result continuations update in place

    public static bool HasPending => Pending.Count > 0;

    /// <summary>Execute the plan, one framework-thread pass. Returns the live result; its
    /// WithdrawPending entries resolve later via <see cref="PollPending"/> (call it every tick).</summary>
    public static ApplyLiveResult Apply(IReadOnlyList<ApplyStep> steps, int predictedTotal)
    {
        Pending.Clear();
        var results = new List<ApplyLiveStep>(steps.Count);
        var inv = InventoryManager.Instance();

        foreach (var step in steps)
        {
            switch (step.Method)
            {
                case ObtainMethod.LeaveUntouched:
                    results.Add(ApplyLiveStep.For(step, ApplyStepStatus.Untouched));
                    break;

                case ObtainMethod.Missing:
                    results.Add(ApplyLiveStep.For(step, ApplyStepStatus.Missing));
                    break;

                case ObtainMethod.AlreadyWorn:
                    results.Add(ApplyLiveStep.For(step, ApplyStepStatus.AlreadyWorn));
                    break;

                case ObtainMethod.EquipFromBags:
                    results.Add(EquipFromSource(inv, step));
                    break;

                case ObtainMethod.WithdrawFromDresser:
                {
                    var mirage = MirageManager.Instance();
                    if (mirage == null || !mirage->PrismBoxLoaded)
                    {
                        results.Add(Fail(step, "glamour dresser data not loaded - open a glamour dresser once, then Apply again"));
                        break;
                    }
                    if (!mirage->RestorePrismBoxItem(step.ItemId))
                    {
                        results.Add(Fail(step, "the glamour dresser rejected the withdraw"));
                        break;
                    }
                    Pending.Add(new PendingWithdraw { Step = step, TicksLeft = PendingTicksBudget });
                    results.Add(ApplyLiveStep.For(step, ApplyStepStatus.WithdrawPending));
                    break;
                }

                case ObtainMethod.WithdrawFromArmoire:
                {
                    var ui = UIState.Instance();
                    if (ui == null || !ui->Cabinet.IsCabinetLoaded())
                    {
                        results.Add(Fail(step, "armoire data not loaded - open the armoire once, then Apply again"));
                        break;
                    }
                    if (!ui->Cabinet.WithdrawCabinetItem(step.ItemId))
                    {
                        results.Add(Fail(step, "the armoire rejected the withdraw"));
                        break;
                    }
                    Pending.Add(new PendingWithdraw { Step = step, TicksLeft = PendingTicksBudget });
                    results.Add(ApplyLiveStep.For(step, ApplyStepStatus.WithdrawPending));
                    break;
                }

                default:
                    results.Add(ApplyLiveStep.For(step, ApplyStepStatus.Missing));
                    break;
            }
        }

        _live = new ApplyLiveResult { Steps = results, PredictedTotal = predictedTotal };
        return _live;
    }

    /// <summary>Framework tick: resolve withdraw continuations. Each pending step gets ONE equip
    /// attempt the tick its piece shows up in bags; after the budget it reports FAILED honestly.
    /// This is a bounded wait for a server round-trip, not a retry loop.</summary>
    public static void PollPending()
    {
        if (Pending.Count == 0 || _live is null) return;
        var inv = InventoryManager.Instance();

        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            var p = Pending[i];
            var idx = _live.Steps.FindIndex(s => ReferenceEquals(s.Step, p.Step));
            if (idx < 0) { Pending.RemoveAt(i); continue; }

            var coord = FindInBags(inv, p.Step.ItemId);
            if (coord is { } c)
            {
                var moved = MoveToEquipped(inv, c, p.Step);
                _live.Steps[idx] = moved;
                Pending.RemoveAt(i);
                continue;
            }

            if (--p.TicksLeft <= 0)
            {
                _live.Steps[idx] = ApplyLiveStep.For(p.Step, ApplyStepStatus.Failed, -1)
                    with { Line = $"{p.Step.Slot.DisplayName()}: FAILED - {p.Step.ItemName} never landed in bags after the withdraw (server timeout)" };
                Pending.RemoveAt(i);
            }
        }
    }

    private static ApplyLiveStep EquipFromSource(InventoryManager* inv, ApplyStep step)
    {
        if (step.Source is not { } src)
            return Fail(step, "no source recorded");
        if (!StillThere(inv, src, step.ItemId))
            return ApplyLiveStep.For(step, ApplyStepStatus.SkippedChanged);
        return MoveToEquipped(inv, src, step);
    }

    private static ApplyLiveStep MoveToEquipped(InventoryManager* inv, InventoryCoord src, ApplyStep step)
    {
        var dest = EquippedDest(step.Slot);
        if (dest < 0 || inv == null)
            return Fail(step, "no equipped destination for this slot");
        var rc = inv->MoveItemSlot((InventoryType)src.Container, (ushort)src.Slot,
            InventoryType.EquippedItems, (ushort)dest, false);
        if (rc != 0)
            return ApplyLiveStep.For(step, ApplyStepStatus.Failed, rc);
        return ApplyLiveStep.For(step, ApplyStepStatus.Equipped);
    }

    private static ApplyLiveStep Fail(ApplyStep step, string why)
    {
        var line = $"{step.Slot.DisplayName()}: FAILED - {why}";
        return new ApplyLiveStep { Step = step, Status = ApplyStepStatus.Failed, Line = line };
    }

    /// <summary>The plan's source coord must still hold the planned item - the snapshot and the
    /// move are one framework pass apart, but a withdraw continuation can lag ~seconds.</summary>
    private static bool StillThere(InventoryManager* inv, InventoryCoord coord, uint itemId)
    {
        if (inv == null) return false;
        var cont = inv->GetInventoryContainer((InventoryType)coord.Container);
        if (cont == null || !cont->IsLoaded || coord.Slot >= cont->Size) return false;
        var item = cont->GetInventorySlot(coord.Slot);
        return item != null && (item->ItemId == itemId || item->GlamourId == itemId);
    }

    /// <summary>First bags/armoury coord holding the item (or a glamour carrier of it).</summary>
    private static InventoryCoord? FindInBags(InventoryManager* inv, uint itemId)
    {
        if (inv == null) return null;
        foreach (InventoryType type in Enum.GetValues<InventoryType>())
        {
            if (!IsBagContainer(type)) continue;
            var cont = inv->GetInventoryContainer(type);
            if (cont == null || !cont->IsLoaded) continue;
            for (var i = 0; i < cont->Size; i++)
            {
                var item = cont->GetInventorySlot(i);
                if (item != null && item->ItemId != 0 && (item->ItemId == itemId || item->GlamourId == itemId))
                    return new InventoryCoord { Container = (int)type, Slot = i };
            }
        }
        return null;
    }

    private static bool IsBagContainer(InventoryType type) => type switch
    {
        InventoryType.Inventory1 or InventoryType.Inventory2 or InventoryType.Inventory3
            or InventoryType.Inventory4 or InventoryType.ArmoryMainHand or InventoryType.ArmoryOffHand
            or InventoryType.ArmoryHead or InventoryType.ArmoryBody or InventoryType.ArmoryHands
            or InventoryType.ArmoryLegs or InventoryType.ArmoryFeets or InventoryType.ArmoryEar
            or InventoryType.ArmoryNeck or InventoryType.ArmoryWrist or InventoryType.ArmoryRings => true,
        _ => false,
    };
}
