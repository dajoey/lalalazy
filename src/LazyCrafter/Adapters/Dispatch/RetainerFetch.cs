using System.Reflection;
using Dalamud.Plugin.Services;

namespace LazyCrafter.Adapters.Dispatch;

/// <summary>
/// Fetches materials out of the character's retainers and into the bags, so a craft that only lacked stock
/// <i>location</i> can actually run (card t_63b845ad; Joey twice: "stock the ingredients in my bag first").
///
/// <para><b>Why this drives Artisan and not AutoRetainer.</b> The card's first choice was AutoRetainer. Read from the
/// installed <c>AutoRetainer.dll</c> 4.6.1.34 (ILSpy 11.0.0.9375, not from strings): its 27 IPC names are all
/// venture / postprocess / multi-mode - none withdraws an item. The only reverse-direction code it has is
/// <c>AutoRetainer.Scheduler.Tasks.TaskEntrustDuplicates.EnqueueNewReverse(EntrustPlan)</c> (public static), and it is
/// the wrong shape for us three times over: (1) its first queued step is "Wait until addon SelectString ready" - it
/// assumes a retainer session is <b>already open</b> and contains no bell interaction and no retainer selection, so it
/// cannot start from a standing player; (2) it is driven by an <c>EntrustPlan</c> whose <c>EntrustItemsAmountToKeep</c>
/// is an amount-to-keep-at-the-far-end, not an amount-to-fetch, and it moves whole slots via
/// <c>MoveSlotFromToRetainerInventoryUnsafe</c> rather than a requested quantity; (3) <c>RetrieveFromRetainer</c> - the
/// name that looked promising in the string table - is a literal of the <c>RetainerItemCommand</c> enum, not a callable.
/// So the card's fallback applies: Artisan.</para>
///
/// <para><b>What Artisan gives us.</b> <c>Artisan.IPC.RetainerInfo.RestockFromRetainers(uint itemId, int howManyToGet)</c>
/// is <c>public static</c> and enqueues the entire loop on Artisan's own <c>TaskManager</c>: lock YesAlready, target and
/// interact with the nearest summoning bell, then per retainer holding the item - select it, "Entrust Items", open the
/// item's context menu, type the quantity into <c>InputNumeric</c>, close, quit - aborting the moment the bags hold
/// enough. It suppresses AutoRetainer for the duration (<c>AutoRetainer.SetSuppressed</c>), which is the correct
/// interaction with option B rather than a fight with it. Artisan's IPC surface cannot reach this (36 names, none of
/// them restock-related), so it is reflection, behind <see cref="ReflectionGuard"/> with a version pin.</para>
///
/// <para><b>Quantity semantics, verified in the decompiled body.</b> <c>howManyToGet</c> is used as a delta inside
/// <c>ExtractSingular</c> (<c>value = Min(howManyToGet, foundInThisStack)</c>, then <c>howManyToGet -= value</c>) but as
/// an absolute bag target in the per-retainer abort check (<c>NumberOfIngredient(item) &gt;= howManyToGet</c>). That
/// check runs <b>after</b> a retainer's withdrawals, so passing the delta withdraws the delta and then stops - correct
/// for us - but it can also stop early after a partial pull. <see cref="DispatchService"/> therefore never trusts the
/// call: it measures the bag-count delta and comes back for the remainder.</para>
///
/// <para><b>0.1.3.0 - one bell trip, not one per item.</b> Joey, on 0.1.2.0's live run: four materials from one
/// retainer became four full Artisan sessions (bell, select, entrust, quit - ~5.5 s each), back to back. The decompile
/// shows why: the per-item overload enqueues the whole bell cycle <i>per call</i>. It also shows the batch twin of that
/// method, <c>RestockFromRetainers(NewCraftingList)</c>: <b>one</b> <c>TM.EnqueueBell()</c>, then per retainer x per
/// required item, with the demand computed as the list's recipe expansion minus what the bags hold
/// (<c>CraftingListUI.NumberOfIngredient</c>) at session time, withdrawing <c>Min(required, firstFoundQuantity)</c> per
/// stack - so it converges exactly to the shortfall and never over-pulls, and its inventory-change pacing
/// (<c>_InventoryChanged</c>) is wired to Artisan's own item-added/removed subscribers (gated on the bell condition),
/// so it is live whenever a bell session is. LazyCrafter therefore feeds the cart's recipes (crafts <b>and</b> the
/// retrieval-deferred ones) through a hand-built <c>NewCraftingList</c> and gets the whole cart's stock in one session;
/// the per-item overload remains as the fallback for remainder pulls and items with no recipe row. Both overloads are
/// pinned (the list one via <see cref="Adapters.ReflectionGuardExtensions"/> alias, since a pin can hold only one
/// member per key) and proved by <c>tests/LazyCrafter.GuardProbe</c>.</para>
///
/// <para><b>Session preflight.</b> The batch path runs unattended for up to a couple of minutes, so before queueing it
/// the same blockers the per-item path checks are re-checked in one place - <see cref="SessionPreflight"/>: AllaganTools
/// gate, a reachable bell, no existing retainer task on <c>TM</c>. The demand is measured against the bags first, and a
/// demand that is already fully in the bags returns <c>null</c> demand with no session - Artisan's own loop would open
/// the bell and quit for nothing.</para>
///
/// Pinned against Artisan 4.0.5.19 - the build installed on the client - decompiled 2026-09-03 (SHA-256 of the
/// decompiled DLL matches omasky's installed copy: d7760c20...), batch members added 2026-09-04.
/// </summary>
public sealed class RetainerFetch
{
    public const string InternalName = "Artisan";

    private const string RetainerInfo = "Artisan.IPC.RetainerInfo";
    private const string ItemInfo = "Artisan.IPC.RetainerInfo.ItemInfo";
    private const string TaskManager = "ECommons.Automation.LegacyTaskManager.TaskManager";
    private const string ListType = "Artisan.CraftingLists.NewCraftingList";
    private const string ListItemType = "Artisan.CraftingLists.ListItem";
    private const string ListOptionsType = "Artisan.CraftingLists.ListItemOptions";

    public static readonly ReflectionGuard.Pin Pin = new(
        InternalName,
        MinVersion: new Version(4, 0, 5),
        MaxVerified: new Version(4, 1),
        VerifiedAgainst: "Artisan 4.0.5.19 (installed build, decompiled 2026-09-03; batch overload 2026-09-04)",
        Members:
        [
            // The gate: Artisan's retainer features are a no-op without AllaganTools ("Please enable Allagan Tools
            // for retainer features"), and ATools folds in its own DisableAllaganTools setting.
            new(RetainerInfo, "ATools", ReflectionGuard.MemberKind.StaticProperty),
            // RestockFromRetainers does nothing at all unless RetainerData already knows the item is on a retainer;
            // GetRetainerItemCount is what fills it (and returns the total).
            new(RetainerInfo, "RetainerData", ReflectionGuard.MemberKind.Field),
            new(RetainerInfo, "GetRetainerItemCount", ReflectionGuard.MemberKind.StaticMethod, [typeof(uint), typeof(bool), typeof(bool)]),
            // The overload that takes an item id - the other one takes Artisan's own NewCraftingList and is pinned
            // through the NewCraftingList type below.
            new(RetainerInfo, "RestockFromRetainers", ReflectionGuard.MemberKind.StaticMethod, [typeof(uint), typeof(int)]),
            // Pre-flight: no bell in interaction range means the whole queue would sit there silently.
            new(RetainerInfo, "GetReachableRetainerBell", ReflectionGuard.MemberKind.StaticMethod),
            new(ItemInfo, "ItemId", ReflectionGuard.MemberKind.Property),
            new(ItemInfo, "Quantity", ReflectionGuard.MemberKind.Property),
            // Progress: Artisan queues the whole session onto this one TaskManager.
            new(RetainerInfo, "TM", ReflectionGuard.MemberKind.Field),
            new(TaskManager, "IsBusy", ReflectionGuard.MemberKind.Property),
            new(TaskManager, "Abort", ReflectionGuard.MemberKind.Method),
            // 0.1.3.0 batch path: the list-shaped overload's data shapes. NewCraftingList is a plain data class
            // (Recipes, OnlyRestockNonCrafted); ListItem is (ID = recipe row, Quantity, ListItemOptions); its
            // ListMaterials() extension multiplies each recipe's ingredient amounts by the list quantity.
            new(ListType, "", ReflectionGuard.MemberKind.TypeOnly),
            new(ListItemType, "", ReflectionGuard.MemberKind.TypeOnly),
            new(ListOptionsType, "", ReflectionGuard.MemberKind.TypeOnly),
        ]);

    /// <summary>The list-shaped overload - same name as the per-item pin member, different parameter list, so it is
    /// resolved as an alias (a pin can carry only one member per key). The parameter is named as a string: the type
    /// lives in Artisan and cannot be referenced at compile time.</summary>
    public static readonly ReflectionGuardExtensions.AliasMember BatchOverload =
        new(RetainerInfo, "RestockFromRetainers", "batch", [ListType]);

    private readonly ReflectionGuard _guard;
    private readonly IPluginLog _log;
    private MethodInfo? _batchOverload;

    public RetainerFetch(ReflectionGuard guard, IPluginLog log)
    {
        _guard = guard;
        _log = log;
    }

    public bool Installed => _guard.InstalledVersion(InternalName, out var loaded) is not null && loaded;

    /// <summary>Resolve the pin, reporting once through the guard on failure. <c>null</c> when unavailable.</summary>
    private ReflectionGuard.Resolved? Resolve()
    {
        var r = _guard.Require(Pin, "retainer fetch");
        if (r is null) return null;
        if (_batchOverload is null)
        {
            // The list overload rides on the same pin; verify it once per session (reflection resolution order is
            // deterministic, so once is enough - and a member renamed between runs is a version problem, not a
            // per-call one). Failure here only disables the batch path; the per-item fallback still works.
            var failure = ReflectionGuardExtensions.VerifyAlias(Pin, r.Plugin.GetType(), BatchOverload, out var mi);
            if (failure is not null)
                _log.Warning("Artisan batch retainer fetch unavailable: {Failure}", failure);
            else
                _batchOverload = mi;
        }
        return r;
    }

    /// <summary>
    /// Why a fetch cannot run <b>right now</b>, phrased as something the player can act on, or <c>null</c> when it can.
    /// Framework thread: reads the object table for the bell.
    /// </summary>
    public string? Blocker()
    {
        var r = Resolve();
        if (r is null) return "Artisan's retainer hand-off is unavailable (see the error above)";
        try
        {
            if (r.Property(ReflectionGuard.Key(RetainerInfo, "ATools")).GetValue(null) is not true)
                return "Artisan's retainer features are off - install/enable AllaganTools (InventoryTools) and make sure Artisan's \"Disable Allagan Tools\" setting is unchecked";
            if (r.Method(ReflectionGuard.Key(RetainerInfo, "GetReachableRetainerBell")).Invoke(null, null) is null)
                return "you are not standing next to a summoning bell - walk to one (inn, your house/apartment, or any city's markets) and press Dispatch again";
            if (Busy())
                return "Artisan is already running a retainer task; let it finish and press Dispatch again";
            return null;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "RetainerFetch.Blocker() threw");
            return $"could not inspect Artisan's retainer state ({ex.GetType().Name})";
        }
    }

    /// <summary>
    /// Units of <paramref name="itemId"/> Artisan can see on the retainers, refreshing its cache first
    /// (<c>tryCache: false</c>) - which is also what makes <see cref="Begin"/> able to do anything at all, because
    /// <c>RestockFromRetainers</c> returns immediately when <c>RetainerData</c> has no entry for the item.
    /// Framework thread: walks <c>RetainerManager</c>. Returns 0 on any failure.
    /// </summary>
    public int Available(uint itemId)
    {
        var r = Resolve();
        if (r is null) return 0;
        try
        {
            var n = r.Method(ReflectionGuard.Key(RetainerInfo, "GetRetainerItemCount")).Invoke(null, [itemId, false, false]);
            return n is int i ? Math.Max(0, i) : 0;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Artisan RetainerInfo.GetRetainerItemCount({Item}) failed", itemId);
            return 0;
        }
    }

    /// <summary>
    /// Queue the withdraw of <paramref name="quantity"/> units of <paramref name="itemId"/> from whichever retainers
    /// hold it. Framework thread. Returns an error string, or <c>null</c> when the queue was accepted - which is
    /// <b>not</b> proof anything moved; poll <see cref="Busy"/> and then measure the bags.
    /// </summary>
    public string? Begin(uint itemId, int quantity)
    {
        if (quantity <= 0) return "nothing to fetch";
        var r = Resolve();
        if (r is null) return "Artisan's retainer hand-off is unavailable";
        try
        {
            r.Method(ReflectionGuard.Key(RetainerInfo, "RestockFromRetainers")).Invoke(null, [itemId, quantity]);
            if (!Busy()) return "Artisan accepted the request but queued nothing (it may not see the item on any retainer)";
            return null;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Artisan RetainerInfo.RestockFromRetainers({Item}, {Qty}) failed", itemId, quantity);
            return ex.InnerException?.Message ?? ex.Message;
        }
    }

    /// <summary>
    /// One shared preflight for both fetch paths, checked at <b>queue</b> time: Artisan's retainer features are on
    /// (AllaganTools gate), a bell is reachable, and Artisan's retainer task queue is idle. The per-item path used to
    /// run these from <see cref="Blocker"/>; the batch session runs unattended for a couple of minutes, so the same
    /// checks run immediately before queueing it. Framework thread.
    /// </summary>
    public string? SessionPreflight()
    {
        var r = Resolve();
        if (r is null) return "Artisan's retainer hand-off is unavailable";
        try
        {
            if (r.Property(ReflectionGuard.Key(RetainerInfo, "ATools")).GetValue(null) is not true)
                return "Artisan's retainer features are off - install/enable AllaganTools (InventoryTools) and make sure Artisan's \"Disable Allagan Tools\" setting is unchecked";
            if (r.Method(ReflectionGuard.Key(RetainerInfo, "GetReachableRetainerBell")).Invoke(null, null) is null)
                return "you are not standing next to a summoning bell - walk to one and press Dispatch again";
            if (Busy())
                return "Artisan is already running a retainer task; let it finish and press Dispatch again";
            return null;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "RetainerFetch.SessionPreflight() threw");
            return $"could not inspect Artisan's retainer state ({ex.GetType().Name})";
        }
    }

    /// <summary>
    /// Queue ONE Artisan session that walks the retainers and withdraws every listed recipe's missing materials into
    /// the bags (the list-shaped <c>RestockFromRetainers(NewCraftingList)</c>). <paramref name="recipeIds"/> are the
    /// cart's recipe rows - the crafts, and deferred crafts whose blockers included a retrieval; Artisan expands them
    /// to ingredients itself (<c>ListMaterials</c> x the list quantity), primes its own retainer cache with
    /// <c>GetRetainerItemCount</c> per material, subtracts what the bags hold at session time, and withdraws exactly
    /// the difference. Returns an error string, or null when the session was accepted - which is <b>not</b> proof
    /// anything moved; poll <see cref="Busy"/> and then measure the bags. Framework thread.
    /// </summary>
    public string? BeginBatch(IReadOnlyList<uint> recipeIds)
    {
        if (recipeIds.Count == 0) return "nothing to fetch";
        var r = Resolve();
        if (r is null) return "Artisan's retainer hand-off is unavailable";
        if (_batchOverload is null) return "Artisan's batch retainer fetch is unavailable (see the warning in the log); the per-item fetch still works";
        try
        {
            var listType = r.Type(ListType);
            var listItemType = r.Type(ListItemType);
            var optionsType = r.Type(ListOptionsType);
            var list = Activator.CreateInstance(listType)!;
            var recipes = (System.Collections.IList)listType.GetProperty("Recipes")!.GetValue(list)!;
            foreach (var id in recipeIds.Distinct())
            {
                var item = Activator.CreateInstance(listItemType)!;
                listItemType.GetProperty("ID")!.SetValue(item, id);
                listItemType.GetProperty("Quantity")!.SetValue(item, 1);
                var opts = Activator.CreateInstance(optionsType);
                optionsType.GetProperty("Skipping")!.SetValue(opts, false);
                listItemType.GetProperty("ListItemOptions")!.SetValue(item, opts);
                recipes.Add(item);
            }
            if (recipes.Count == 0) return "no usable recipe rows for the batch fetch";
            _batchOverload.Invoke(null, [list]);
            if (!Busy()) return "Artisan accepted the batch request but queued nothing (it may not see any of the items on a retainer)";
            _log.Information("Artisan batch fetch queued for {Count} recipe row(s): [{Ids}]", recipes.Count, string.Join(",", recipeIds.Distinct()));
            return null;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Artisan RetainerInfo.RestockFromRetainers(NewCraftingList) failed");
            return ex.InnerException?.Message ?? ex.Message;
        }
    }

    /// <summary><c>true</c> while Artisan's retainer task queue has anything left to do.</summary>
    public bool Busy()
    {
        var r = Resolve();
        if (r is null) return false;
        try
        {
            var tm = r.Field(ReflectionGuard.Key(RetainerInfo, "TM")).GetValue(null);
            if (tm is null) return false;
            return r.Property(ReflectionGuard.Key(TaskManager, "IsBusy")).GetValue(tm) is true;
        }
        catch (Exception ex)
        {
            _log.Debug("Artisan RetainerInfo.TM.IsBusy unavailable: {Msg}", ex.Message);
            return false;
        }
    }

    /// <summary>Drop everything still queued on Artisan's retainer task manager. Safe when Artisan is absent.</summary>
    public void Abort()
    {
        var r = Resolve();
        if (r is null) return;
        try
        {
            var tm = r.Field(ReflectionGuard.Key(RetainerInfo, "TM")).GetValue(null);
            if (tm is not null) r.Method(ReflectionGuard.Key(TaskManager, "Abort")).Invoke(tm, null);
        }
        catch (Exception ex) { _log.Debug("Artisan RetainerInfo.TM.Abort() failed: {Msg}", ex.Message); }
    }
}
