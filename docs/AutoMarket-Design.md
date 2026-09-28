# Auto-Market decision core — design (0.2.0.0 / 0.2.1.0)

Status: **binding design for the 0.2.0.0 rebuild and 0.2.1.0 listing starvation fixes**,
written after the 2026-09-27 incident and expanded for the 2026-09-28 starvation fixes.
Git history is the proof of order. This document is mirrored to the
notebook (`Projects/FFXIV Mods`) through Helm.

## 0. The class of error this design exists to forbid

0.1.70.0 added decisions that compared one real number against one fantasy
number and then acted irreversibly on the result. Concretely, it trusted the
Item sheet's `PriceMid`/`PriceLow` as "what a vendor will pay" (observed live:
99,999 gil/unit sentinel prices on HQ gear and other items no vendor buys at
that rate; the plugin's own 0.1.30.0 note already documented the sheet diverging
from reality in the opposite direction, `PriceLow = 0` on Ice Crystals). Every
comparison of the form `marketNet < vendorTotal` built on that source fails
OPEN into vendoring: when the sheet number is fantasy-high, everything looks
"under vendor value" and gets vendored.

The 0.1.69.0 doctrine — *unconfirmed data means HOLD, never act in any
direction* — was only ever applied to the market side. This design applies it
to **every** price that any state-changing decision depends on, in both
directions, with no exceptions and no "reasonable default" escape hatches.

**Prime rule: a state-changing action (list, pull, vendor) may run only when
every price it depends on is CONFIRMED. Anything else is HOLD — no action in
any direction. HOLD is always safe: it leaves stock exactly where it is.**

## 1. Price sources and their confirmation contracts

| Source | Confirmed when | Failure modes | Safe default |
|---|---|---|---|
| **Market quote** (Universalis cheapest listing of the wanted quality) | `HasData`, `LastUploadUnixMs` fresh (< gate freshness hours), wanted-quality cheapest listing exists and is > 0 | request timeout, no data, stale upload, no listing of wanted quality, thin/single-listing price | **HOLD** — never list blind, never vendor, never pull |
| **Item sheet `PriceMid`/`PriceLow`** | **NEVER confirmed as a vendor payout.** It is a static data table, not a quote. | sentinel prices (99,999), NPC retail price ≠ vendor payout, HQ path fantasy, 0 on items NPCs do buy | Display estimate only. It may gate the *enablement* of the bounded junk path (§4) — it may NEVER decide that an item is worth vendoring |
| **Vendor offer from the game's own sell UI** | read for `(itemId, quality)` during this session from the retainer shop UI | UI not open, row unreadable | Vendor side unconfirmed ⇒ the item is **never** routed to vendoring by comparison (§4) |

**Consequence stated honestly:** the game currently exposes no headless way to
read a confirmed vendor offer. Therefore **no "vendor pays more than market"
decision exists in 0.2.0.0**. That comparison is undecidable without a
confirmed vendor price, and an undecidable comparison must not default to
acting. (Extension path, not in this release: a session cache seeded by a
UI-read vendor price. Until that exists and is verified in-game, the feature
does not exist.)

## 2. The decision flow (every path an item can take)

```
                  ┌────────────────────────────────────────────────────┐
                  │ Judge(item, quality) — ONE decision core, used by  │
                  │ the listing pass, the pull pass and the vendor leg │
                  └────────────────────────────────────────────────────┘
 stock (bags, retainer pages)  +  active market listings of same item+quality
        │
        ├─ market quote UNCONFIRMED ────────────────► HOLD (in place; named in log)
        │
        ├─ market net (5% fee, wanted quality, total sellable incl. listings)
        │     > threshold (default 100 gil) ───────► LIST (existing planner;
        │                                             slots, reserve, stack sizes)
        │
        ├─ market net ≤ threshold  (bounded junk path, §4)
        │     ├─ NQ, not equippable, sheet PriceLow > 0,
        │     │  keep floor not exhausted (§3)
        │     │        └───────────────────────────► VENDOR (VendorPlanner only)
        │     └─ anything else ────────────────────► HOLD (gear, HQ, unvendorable,
        │                                             or below the keep floor)
        │
        └─ dry-run ON (§6): every LIST/VENDOR/PULL decision is computed and
            LOGGED as `[AM][dry-run] would …` and NOT executed
```

Active listings enter the same judge through the **pull pass** (pre-0.1.70
mechanism, retained): a listing whose *confirmed* market net is ≤ threshold is
pulled back to stock, after which that stock faces the same judge above. A
listing with an unconfirmed quote is never touched. **The 0.1.70 delist pass
(withdraw-because-vendor-pays-more) is withdrawn**: it cannot be decided
without a confirmed vendor price (§1).

## 3. Keep-N reserve — one floor, all stock, exhausted last

The keep floor for an item is `max(KeepInBags, KeepInRetainer)` — one number,
counted across **bags and retainer pages together**. The vendor leg may only
vendor units beyond that floor, taken largest-stack-first (existing
sort). Listing may use stock above the floor freely; the floor itself is never
listed or vendored.

**Active market listings never reduce the floor.** A listing is a pending
SALE, not a reserve: stock the board is already selling is on its way out, so
it cannot stand in for units the player asked to keep. (Correction
2026-09-27, before implementation: an earlier draft of this section counted
listings toward the floor — "a listing is stock" — which would let 500
listings substitute for a keep-500 reserve and vendor 450 units of stock the
moment the board filled in. The reserve must survive the board selling out.)

Rationale: "keep 500" means *keep 500*, not "keep 500 only where the config
field happens to be checked". On 2026-09-27 a keep-500 item lost its whole
retainer stack (198 units) because the reserve was per-origin and the retainer
origin's keep was 0, while a second path (the delist hand-off) bypassed the
planner's reserve entirely.

**Invariant V2: `VendorOp` objects are constructed by `VendorPlanner` and by
nothing else.** Any code path that could hand a raw vendor op to the executor
is a defect by definition (0.1.70's `_delistedVendorOps` append was exactly
this and is deleted with the delist pass).

## 4. The bounded junk path (the only automated vendoring in 0.2.0.0)

Automated vendoring exists for one purpose: items whose **confirmed market
net is at or under the threshold** (default 100 gil) are not worth a market
slot. For those, and only those:

1. the market side is CONFIRMED (fresh quote, wanted quality, positive price);
2. the item is NQ (**HQ stock is never auto-vendored** — every large loss on
   2026-09-27 was HQ);
3. the item is **not equippable** (gear is never auto-vendored, regardless of
   value — belt and braces for the sheet-fantasy class);
4. the sheet gives `PriceLow > 0` (enablement check only; the sheet number is
   never compared against the market value);
5. the global keep floor (§3) is respected.

The worst possible damage of this path is bounded by the threshold: an item
whose real market value exceeds the threshold is never vendored, so a wrong
sheet price can cost at most one item's ≤threshold market value, not a
five-figure stack. Everything else — gear, HQ, unvendorable, unpriced,
below-floor — HOLDS.

## 5. Slot integrity — no removal without replacement or recorded hold

After any withdrawal pass (pull), the listing plan is rebuilt from a **fresh
market-container snapshot**. Every freed slot is then either filled by a
planned listing, or an explicit hold note is logged naming the slot and the
reason (`slot #N left empty: <why>`). The read-back after a withdrawal uses a
bounded retry (the container can lag the move): rc=0 followed by "slot still
occupied" at first read is retried for up to ~1.5 s before being declared
failed; a declared failure forces a fresh snapshot before any further
slot-dependent planning (the 2026-09-27 false-failure race planned listings
against stale slot data and left real slots empty).

**Invariant V4: no listing is removed without either a replacement listing
going up in the same plan or an explicit hold decision recorded in the run
log.**

## 6. Dry-run (offline harness only, since 0.2.4.0)

The in-game dry-run gate (0.2.0.0–0.2.3.0) is REMOVED — see §12. Dry-run simulation now
exists only in the offline harness: the recorded incident inventory states (§9) replay
through the decision core with zero execution and assert holds and zero planned actions —
that simulation is SC4's dry-run evidence. The `[AM][dry-run] would ...` strings
(`DryRunFormat`) remain as that harness's simulation formatter and are suite-pinned
(case 139); no in-game code path emits them.

## 7. The vendor buyback window is a protected resource

The game keeps a buyback list per retainer; closing the bell after vendoring
surfaces "Your retainer will be unable to process item buyback requests once
recalled." **No automation path in 0.2.0.0 clicks Yes on that dialog.** The
close gate parks the chain for that retainer and records it in the done line;
the player decides whether to buy back before closing. (With dry-run on and
the junk path as the only vendor source, the dialog should not appear from
automation at all; if vendoring did run, parking is the correct price of
protecting the window.) Recovery of incident losses depends on this window.

## 8. Defect-by-defect mapping (the table the reviewer must check)

| # | 2026-09-27 defect | Mechanism in 0.1.70.0 | Design element that forbids it | Suite proof |
|---|---|---|---|---|
| D1 | 999+ super-ethers (market value > vendor price) vendored | `BuildPlan` compared `marketNet < vendorTotal` with sheet-fantasy `vendorTotal` (e.g. 399,996 gil for a 4-stack) → routed to vendor leg | §1/§4: vendor price never decides anything above the bounded threshold; the comparison does not exist | Case 130: sentinel sheet price + confirmed live market quote → zero vendor ops, Hold |
| D2 | keep-500 reserve ignored | reserve was per-origin (retainer keep 0 ⇒ whole retainer stack vendored); delist hand-off appended raw ops bypassing the planner | §3: one global floor across bags + retainer + listings; §3 invariant: only VendorPlanner builds ops | Case 131: 100 in bags + 198 on retainer, keep 500 → 0 vendored; Case 132: listings never reduce the floor
| D3 | gear worth tens of thousands vendored | same fantasy comparison routed HQ gear to the vendor leg | §4: NQ-only and non-equippable-only junk path; HQ and gear always Hold | Case 133: HQ gear below threshold → Hold, zero vendor ops |
| D4 | marketboard listings removed, retainer sale slots left EMPTY | delist pass removed listings with no backfill contract; read-back race made the planner run on a stale container | §2: delist withdrawn; §5: rebuild from fresh snapshot, freed slot → listing or logged hold; bounded read-back retry | Case 134: pull frees a slot → same-plan backfill op or hold note; stale-snapshot probe replans from fresh data |

Plus: case 127 pins the buyback gate never confirming (§7); case 135 replays
the recorded incident fixtures (below) end-to-end through the decision core
and asserts zero vendor/pull actions (every listing untouched, stock held);
case 136 pins the pull pass (confirmed below-threshold only — an unconfirmed
quote keeps the listing on the board). Numbering corrected 2026-09-27 at
review (the pre-integration draft's 135/136 shifted when the buyback pin
landed in the existing case 127).

## 9. Recorded incident fixtures (from the 2026-09-27 telemetry)

Serialized into the harness as the acceptance set; every line is a real logged
decision from the implicated run:

| Fixture (item, quality, qty) | Confirmed market net | Sheet "vendor value" | 0.1.70.0 did | 0.2.0.0 must |
|---|---|---|---|---|
| 37832 HQ ×4 (board) | 15,086 gil | 399,996 gil | removed + vendored | listing untouched (Hold) |
| 36251 HQ ×1 (board) | 3,757 gil | 99,999 gil | removed + vendored | listing untouched |
| 43976 HQ ×20 (retainer) | (gear) | 1,999,980 gil est | vendored | Hold (gear + HQ) |
| 4854 HQ ×198 (retainer, keep 500, 302 in bags) | ~real market | 594 gil est | vendored all 198 | 0 vendored (global floor 500 > stock 500 ⇒ floor holds all) |
| 23168 HQ ×99 (board, ×4 slots) | 109,192 gil | 437,184 gil | 4 removals planned | listings untouched |
| 8146 ×15 (board) | 1,838 gil | 4,500 gil | removed + vendored | listing untouched (above threshold) |

## 10. Non-goals

- "Vendor when the vendor pays more than the market" — undecidable without a
  confirmed vendor price (§1); the feature does not exist in this release.
- Re-pricing, pinch pre-flight, routing mover, category routing — unchanged;
  they already follow their own verified contracts.
- Any change to the production channel. Testing releases (0.2.0.0, 0.2.1.0) ship to the **testing**
  channel only; production stays 0.1.68.0.

## 11. 0.2.1.0 additions: Gate resilience, routing priority, and dry-run visibility

Observed in game on 0.2.0.0: transient network drops during Universalis multi-chunk fetch
held 49 of 52 items unpriced, leaving 7 market slots empty across the pass. Furthermore,
dry-run simulation mode was on by default but lacked in-game visibility, and category routing
deposited market-destined stock into retainer storage pages ahead of the listing pass.
0.2.1.0 introduces the following targeted mechanisms:

1. **Gate resilience & bounded-TTL price cache:**
   - Multi-chunk Universalis fetch (`GateChunkFetch.cs`) now retries failed chunks up to 3
     attempts with linear backoff delay (`(attempt + 1) * 250ms`).
   - If all retries for a chunk fail, `GatePriceCache` provides confirmed quotes from prior
     successful lookups within the gate quote-freshness window (configurable 1–168 h, default 6 h) before declaring items unpriced.
   - The unconfirmed => hold rule remains strictly intact: unpriced items without cached or
     live data are never listed or vendored blind.

2. **Empty-slot backfill retry:**
   - If market slots remain unfilled after an initial listing pass because stocked items
     were held unpriced by the gate, `MarketAutomation` triggers a targeted retry lookup
     for those unpriced items within the session before concluding the pass.

3. **Routing deposit priority over storage:**
   - Category routing (`RoutingMove.cs`, `AutoMarketService.cs`) now computes market-destined
     items via `AutoMarketPlanner` before planning bag deposits into retainer storage.
   - Any stack needed to fill open market slots on the current retainer is excluded from
     storage deposit moves, ensuring stock remains available in bags for the listing pass.

4. **Dry-run visibility and 1-click toggle:**
   - Dry-run mode remains ON by default per SC4 doctrine.
   - A visible indicator and one-click toggle button (`Dry-Run: ON` / `Dry-Run: OFF`) are
     rendered directly on the retainer bell and sell list overlays (`MarketAutomation.cs`),
     making simulation state obvious and easily toggled in-game.

5. **Dry-run log-line format pinned:**
   - Deterministic formatting for `[AM][dry-run] would list ...`, `would pull ...`, and
     `would vendor ...` is extracted into `DryRunFormat.cs` and pinned in the offline harness
     (Case 139). (Superseded by §12: the in-game gate is gone since 0.2.4.0; the formatter
     remains the offline harness's simulation formatter.)

## 11a. 0.2.5.0: world-scope staleness fallback and held-set visibility

Diagnosed 2026-09-28 from a live 0.2.4.0 session plus an offline reproduction of the exact
gate request: sellable stock sat held while market slots were free NOT because of transport
failures (zero chunk failures in the session) but because **Universalis' per-world
`lastUploadTime` went stale for a large fraction of the configured items** (342 of 597 ids
on the live install were older than the 6 h freshness window at world scope, while the
data-center aggregate carried fresh data for 191 of them - other worlds of the data center
upload those items). The gate correctly held (unconfirmed means hold); the stock starved.
The round-3 timeout diagnosis is retired for this failure mode.

1. **Data-center-scope fallback (`GateDcFallback.cs`, suite cases 148/148a/148b):** when the
   primary scope is the home world and part of the fetch comes back with no source-usable
   quote (missing, `hasData=false`, stale upload, no positive listing), exactly those ids
   are re-asked at the data-center scope in small no-history chunks (10 ids,
   `entries=0`: the DC aggregate is expensive - 50-id DC chunks 504, 10-id no-history
   chunks answer in ~0.3 s). A DC quote replaces the stale world quote only when it is
   ITSELF source-usable; a stale DC answer overwrites nothing and the item stays held.
   DC fallback quotes carry zero sale velocity, so they rank at the end of the
   fastest-selling-first order while still taking free slots. **The invariant is
   unchanged**: every quote acted on is real Universalis data inside the freshness window;
   no heuristic price exists anywhere in the path. Items with no fresh data at either scope
   stay held - the correct outcome for genuinely cold items.
2. **Held-set visibility (cases 149/150):** the single per-pass chat line names up to three
   held item names plus an overflow count (bounded by construction, no chat spam), and the
   plugin log records the FULL held set with ids and best-effort names
   (`HeldSetAnnounce`). The count the line reports is the CURRENT pass's snapshot
   (`LastPassHeldUnpriced`), not the sweep-accumulated list, which had later retainers'
   lines naming earlier retainers' held stock as their own.
3. **Reprice execution telemetry (case 151):** every price the pinch walk writes emits one
   unconditional Information line (`PinchRepriceLog`) naming the item, the price replaced,
   the price set and the source - placeholder-landing is answerable from the plugin log
   without the optional decision-tap flag. Field evidence from the 0.2.4.0 session: every
   placeholder listing was walked to a market price (the walk's decision taps show
   placeholder → market transitions, and the following pass reported zero placeholder rows),
   so the pre-existing pinch flow was verified landing, not changed.

## 12. 0.2.4.0: the in-game dry-run gate is removed — testing builds list live

Observed across three builds (0.2.1.0–0.2.3.0): the gate — not the machinery — was the thing
the player experienced. Automated passes planned correctly every time while the default-ON
interception turned each pass into a silent simulation that had to be discovered, understood
and disabled before anything would list; the reachable controls added in 0.2.3.0 were never
reached before patience ran out. The default-ON interception layer was an implementation
choice beyond SC4's criterion, and it is ordered out: **testing builds list live by default.**

1. **Live by default.** There is no persisted in-game dry-run toggle, no `/lmc dryrun`
   command, no settings checkbox, no bell-overlay toggle, and no interception branch in any
   execution path (listing, pulling, vendoring). A planned pass with confirmed-priced stock
   executes. A stale `AutoMarketDryRun` key in an existing config file is an ignored dead
   key (config deserialization is by name; unmapped members are dropped — no migration).
2. **Dry-run lives ONLY in the offline harness.** The recorded incident fixtures (§9) replay
   through the decision core with zero execution; `DryRunFormat` stays as that harness's
   simulation formatter (suite-pinned, case 139). No in-game code calls it.
3. **Per-pass feedback (the visibility contract).** When a pass executes ≥1 listing/pull op
   and/or holds ≥1 item unpriced, exactly ONE chat line names BOTH the executed count and the
   held-unpriced count with the hold reason ("no confirmed market price; held in place").
   Idle passes emit zero such lines. `AutoMarketExecution.FormatPassFeedback` builds it;
   executed pulls thread into the final line via the post-pull continuation. Since 0.2.5.0
   (§11a) the line also names up to three held item names, and the count it reports is the
   current pass's own held snapshot.
4. **What did NOT change:** the four value invariants (§1–§5 pins — unconfirmed means hold in
   any direction, net-higher-never-vendored, keep-N honored, no removal without replacement
   or recorded hold) and the rollback doctrine (production pin + rehearsed feed-pin drill).
   The value gate protects real money decisions; the removed dry-run gate only delayed them.

## 13. 0.2.6.0: op-result semantics, the DC wall bound, run-scoped counters (the bag filler: removed in 0.2.7.0)

Diagnosed 2026-09-28 from the live 0.2.5.0 session plus a code walk of f1360f8c. The player's
report: "there are still items laying around in both my inventory and the inventory of
retainers that could be marketed and isn't." Both halves are real, and both have named
mechanisms now.

1. **The bag filler (0.2.7.0: REMOVED).** 0.2.6.0 shipped an off-list selling stage - marketable
   bag stock NOT on the Auto-Market list filling free market slots - ON by default. The owner
   rejected the surface itself (2026-09-28): the enrollment list is the boundary of what
   Auto-Market may sell, and marketable-but-unenrolled stock is manual-review territory (the
   standing rule since the 0.1.6x sweep-to-bags design). 0.2.7.0 removes the stage entirely:
   BagFillerPlanner and its service wiring are gone, the gate asks about configured ids only,
   and the v3 -> v4 config migration (Plugin.MigrateIfNeeded) forces the retired switch false
   once for any config that picked up the ON default. Free market slots stay free unless a
   CONFIGURED listing fills them - the planner iterates enrolled rules only (suite case 158
   pins the removal and the boundary). No bag-filler listing is known to have executed in the
   field (0.2.6.0's only loaded session ended at its price-gate line).

2. **Pull result semantics (rc=0 is acceptance).** A pull whose market-slot read-back still
   showed the item after the bounded ~1.5 s window was declared FAILED with "leaving the
   listing on the board" - for a withdrawal the server had accepted (rc=0) and that landed
   (the same pass vendored the stack out of retainer inventory). §5's declared-failure path is
   amended: rc=0 is the server's acceptance; a lagging read-back is reported as
   accepted-with-lag (`PullOutcome`, case 154) and the NEXT pass's fresh board snapshot is the
   corrective. A nonzero rc keeps the old FAILED semantics. The read-back itself is unchanged
   (bounded retry, best-effort destination hint).

3. **The market-destined guard mirrors the gated plan.** The 0.2.1.0 deposit guard computed
   "which bags stacks will the listing pass take" WITHOUT the value gate, so unpriced rules
   crowded the free slots and a priced later rule kept a storage-deposit op; the listing pass
   (which runs ahead of the routing moves) then emptied that source slot and the mover logged a
   false "FAILED rc=-1; leaving the stack where it is" for stock already listed. The destined
   set is now computed from the same gated, sorted rule list the real plan uses
   (`MarketDestined`, case 153), and a deposit whose source slot no longer holds the stack is
   an honest SKIP (`RoutingMove.VacatedLine`, case 155) - counted as neither move nor failure.
   Listing/stock integrity: a listing cannot go up for stock that never moved - the game moves
   the item into the market slot when it accepts the listing; the false-failure line was the
   defect, not the listing.

4. **The DC fallback is wall-time bounded.** The 0.2.5.0 fallback re-asked every
   source-unusable id with no deadline; a full stale set starved the task chain behind the gate
   (GateWait overrun, 42 cleared tasks, a CloseRetainer timeout, a lost sweep tail). The DC
   pass now stops issuing new chunks past a 10 s budget (`GateChunkFetch` deadline, case 157);
   never-asked ids stay held this pass - unconfirmed means hold - and the next pass asks again.
   The bound is soft by at most one in-flight chunk.

5. **Run-scoped counters.** The done line's counters cover ONE run - the state-reset-to-done
   window, whatever its scope (a sweep, a manual run, an automated postprocess session) - and a
   game session can contain several runs; the 0.2.5.0 session ran two sweeps and its surviving
   done line reconciled with the second run only (5 listed vs 8 executed across both). Every
   run now logs a numbered start line and a tagged done line (`DoneLine.RunTag`/`RunDoneLogLine`,
   case 156); the untagged chat shape is unchanged (case 40). A run whose chain dies in a
   cascade is visible as a start with no done line. The pull undercount that fed the same
   grading confusion is closed by (2).
