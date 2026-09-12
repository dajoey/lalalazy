# Changelog

## v0.6.0.0 (2026-09-12)

### Added
- The "Apply outfit" button under the assembler now dresses the character for real: one press equips every planned piece - items in bags or the armoury chest move straight onto the equipped slots, pieces in the glamour dresser or armoire come out first and then equip, and each slot reports back what actually happened (files: new `Adapters/ApplyMover.cs`, `FashionService.cs ApplyOutfit`, `ReportWindow.cs DrawApply`)
- The dry-run staging is gone. The button the last two releases asked you to verify was a plan preview that moved nothing; you said you didn't see it and didn't want it, so this release ships the real thing instead - the same plan, executed (file: `ReportWindow.cs DrawApply`)
- Every move is checked before it happens: a piece that shifted bags since the plan was built is skipped and says so, a game-side rejection names its error code, and nothing is ever equipped into an unhinted "any item" slot (files: `Adapters/ApplyMover.cs`, `Core/ApplyPlan.cs`)
- Withdrawals from the glamour dresser and armoire resolve themselves: the withdraw is sent, and the moment the piece lands in the bags the plugin equips it - one bounded wait, no retry spam, and an honest timeout line if the server never answers (file: `Adapters/ApplyMover.cs PollPending`)
- Unhinted slots are still never touched, and dye is still never spent automatically: the result now lists "dye manually - apply X" for each planned dye, because this ClientStructs build has no verified dye-apply call to use (files: `Core/ApplyOutcome.cs`, `Adapters/ApplyMover.cs`)

### Notes
- The destination mapping (which FashionSlot lands on which equipped-container slot) follows the game's own container order with the retired belt slot skipped; it is pinned exactly by the offline harness and the apply log prints a per-slot before/after line so the first real apply confirms it live (files: `Core/ApplyOutcome.cs ApplyDestinations`, `Adapters/ApplyMover.cs`)
- Offline harness coverage: every plan rail kept from the dry-run release (untouched slots, missing pieces, dye stock reservation incl. shared-dye and pre-dyed-copy cases) plus the full destination table, the status-to-wording lines, dye reminders, and result counts (file: `tests/LazyFashionReport.Harness/Program.cs` sections 18-18b)

## v0.5.1.0 (2026-09-12)

### Added
- The dry-run readout now also logs the character's raw equipped-gear container layout - the exact container and slot numbers the future apply will move gear into - alongside the existing per-slot readout lines, so one in-game session can confirm both the plan and the physical gear addresses it will use (files: `Adapters/ApplyExecutor.cs Snapshot`, `FashionService.cs LogEquippedLayout`)

### Notes
- Diagnostic release: nothing is moved or consumed, exactly as in v0.5.0.0. The only change is the extra `raw-equipped` log lines under the `equipped-layout` block; they exist so the live apply's gear addresses can be verified against the game's own inventory containers before the apply button exists (file: `FashionService.cs LogEquippedLayout`)
- Offline harness coverage: unchanged - this release touches only the live-read adapter and the log formatting, which the offline harness does not compile (file: `tests/LazyFashionReport.Harness/Program.cs`)

## v0.5.0.0 (2026-09-12)

### Added
- A "Dry-run apply" button under the outfit assembler: it turns the assembled plan into a concrete per-slot course of action and shows exactly what applying it WOULD do - which pieces would be equipped from the bags, which would be withdrawn from the glamour dresser or the armoire first, which dyes would be consumed, and which slots stay untouched - one line per slot with the predicted score after the apply (files: new `Core/ApplyPlan.cs` `ApplyPlanBuilder.Build`, new `Core/ApplySimulator.cs` `ApplySimulator.Run`, `ReportWindow.cs DrawApplyDryRun`)
- Unhinted slots are never touched by an apply: any slot the planner left as "any item" already scores its base points, and the dry run says so explicitly rather than silently planning a gear swap there (file: `Core/ApplyPlan.cs`)
- A dye is only planned when the dye item is physically present in the character's bags, and one copy is reserved per consuming slot - when two slots want the same dye and only one bottle is owned, exactly one slot gets the dye step and the other is reported honestly. A copy that already carries the wanted dye is equipped as-is and consumes nothing (files: `Core/ApplyPlan.cs`, `Adapters/ApplyExecutor.cs Snapshot`)
- Every dry run is logged line-by-line to the plugin log, plus the character's current equipped layout, so an in-game verification can compare the readout against the character sheet slot by slot (files: `FashionService.cs DryRunApply`, `FashionService.cs LogEquippedLayout`)

### Notes
- This release is the executor's dry-run half: the button SIMULATES the apply against the live inventory and moves nothing. The live apply - physically equipping, withdrawing from the dresser and armoire, and consuming dye items - ships only after this readout is verified against the character sheet in game
- A piece the planner chose that is no longer findable in bags, dresser, or armoire is reported as SKIP for its slot - never guessed at (file: `Core/ApplyPlan.cs Missing`)
- Offline harness coverage: equip/withdraw/already-worn/missing per-slot plans, untouched-slot rails, dye stock reservation incl. shared-dye and pre-dyed-copy cases, and the full dry-run readout (file: `tests/LazyFashionReport.Harness/Program.cs` section 18)

## v0.4.0.0 (2026-09-12)

### Added
- The outfit assembler: a new "Assemble for 80+" section that composes the best outfit the character can field RIGHT NOW from owned pieces and owned dyes - one row per slot with the chosen item, where it sits if not in the bags, the exact dye instruction, and the points it contributes, totalling to the predicted score. When 80 is not reachable, the header says so and each blocking slot gets a note ("no owned candidate for the hint - any item still scores 2") (files: new `Core/OutfitPlan.cs` `OutfitAssembler.Build`, `ReportWindow.cs DrawAssembly`)
- Dye instructions are ownership-checked against the character's actual dye inventory: "apply Jet Black (+2)" only when that dye is owned; otherwise the best same-shade owned dye is named ("Metallic Silver not owned - Snow White owned (+1)"), and when nothing matches the slot says "no matching dye owned" instead of pretending (files: `Adapters/ClientReader.cs ReadOwnedStains`, `Adapters/SheetAdapter.cs` dye item map, `Core/OutfitPlan.cs`)
- The assembler respects the get-to notes: a chosen piece sitting in the glamour dresser or the armoire is marked in the plan exactly as in the Wear list (file: `Core/OutfitPlan.cs` `OutfitPiece.LocationNote`)

### Notes
- This release is the planner half of auto-dress: it says exactly what to wear and dye, and nothing is equipped or consumed automatically. The application half - physically equipping and dyeing - moves real gear and consumes real dye items, and ships only after its executor is verified live in-game
- Offline harness coverage: full 80+ assembly from the week-449 fixture, exact-dye and substitute-dye instructions, gap notes, and the any-item fallback (file: `tests/LazyFashionReport.Harness/Program.cs` section 17)

## v0.3.2.0 (2026-09-12)

### Added
- The judged-week feedback loop: after a Fashion Report judgement, the plugin records what Masked Rose awarded against what the predictor said at submit time, and shows it under the week header - "week 449: awarded 84, predicted 85 (off by -1) | within 1pt: 1/2 of recent weeks". The log carries the same line with an explicit verdict (files: new `Core/JudgedFeedback.cs`, `FashionService.cs HarvestJudged`, `ReportWindow.cs Draw`)
- The judged history is kept in the plugin config, bounded to the last 26 weeks, and de-duplicated per judged week - reopening the result screen never double-counts a judgement (file: `Core/JudgedFeedback.cs Next/Append`)

### Notes
- The within-1-point bar is the predictor's acceptance criterion: a judged week where the awarded score differs from the prediction by more than one point is called out, so drift in the scoring rules or the crowd data surfaces instead of hiding behind a total that looks plausible (file: `Core/JudgedFeedback.cs WithinOne`)
- A judgement is only recorded when a live prediction exists to diff against; with no prediction running the result screen read is skipped rather than recording a 0 (file: `FashionService.cs HarvestJudged`)
- Offline harness coverage: dedupe, bound, accuracy math, and the summary format (file: `tests/LazyFashionReport.Harness/Program.cs` section 16)

## v0.3.1.0 (2026-09-12)

### Added
- The get-to leg: an owned candidate that is NOT in the bags now says where it sits - "(in glamour dresser)", "(in armoire)", or "(in dresser or armoire)" - right next to the item in the Wear list, in yellow. A piece that is in the bags or already equipped needs no trip, so it carries no note (files: `Core/SlotPlan.cs` `OwnedCatalog`, `Adapters/ClientReader.cs ReadOwnedCatalog`, `ReportWindow.cs DrawSlotPlan`)
- The owned-items read now keeps per-location flags (bags / glamour dresser / armoire / equipped) instead of collapsing everything into one id set, while the owned-filter and missing-pieces views consume the same ids as before - no behavior change there, just a richer snapshot underneath (file: `Adapters/ClientReader.cs`)

### Notes
- "Owned" and "in bags" are different things: a dresser or armoire piece is glamour-usable at the Gold Saucer but requires standing at a glamour dresser first, which is exactly the trip the note is there to name (file: `Core/SlotPlan.cs LocationNote`)
- Retainer-held pieces stay out of this view - they are not glamour-usable without retrieving them first, and the crowd candidates are gear the character could wear now
- Offline harness coverage: per-location notes, combined locations, and the bags/equipped cases that must stay silent (file: `tests/LazyFashionReport.Harness/Program.cs` section 15)

## v0.3.0.0 (2026-09-12)

### Added
- The buy leg of fetch-missing: every missing piece now names where it comes from, in priority order - craftable (with the existing Craft via Artisan button), a gil vendor with its exact gil price and location, a currency shop such as a Grand Company or beast-tribe vendor with its full cost ("1,500 Storm Seals"), or the market board with a recent median price. A piece with no resolvable source says "not craftable (no vendor or market source found)" instead of vanishing (files: new `Core/BuySource.cs` `BuyResolver.Resolve`, `ReportWindow.cs DrawBuySource`)
- A Shop button next to every placed vendor piece: one click flags that vendor on the map with exact coordinates. The click is the only thing that happens - nothing is bought, no teleport is fired (file: `ReportWindow.cs DrawShopButton`)
- Gil vendors are resolved through the game's shop sheets plus the ENpcShop/ENpcPlace datasets (same source LazyCrafter uses), with a Level-sheet fallback, and the best vendor is the one nearest a teleportable aetheryte. An unplaced gil vendor still shows its price, it just gets no Shop button (file: new `Adapters/VendorIndex.cs`)
- Currency-shop offers show the full cost phrase using the game's own plurals ("7 Ixali Oaknots", "1,500 Storm Seals"), and only shops that resolve to a placed, named NPC are shown - an unplaced currency vendor is a dead end, so such pieces fall through to the market board instead (file: `Adapters/VendorIndex.cs SpecialShopFor`)
- Market-board prices are per-item Universalis lookups on the character's own world: the median of recent sales within 30 days, never the outlier-poisoned average, and no number at all when the board is empty or stale - the label then just says "market board" (file: new `Adapters/MarketQuotes.cs`)

### Changed
- The "not craftable (vendor/market leg comes later)" label is gone: with the buy leg shipped, a piece that resolves to nothing now says so explicitly (file: `ReportWindow.cs DrawBuySource`)
- The plugin now carries the LuminaSupplemental.Excel package (plus its CSV reader) inside its own zip, exactly as LazyCrafter ships it - that is where the vendor placements live (file: `LazyFashionReport.csproj`)

### Notes
- Offline harness coverage for the resolver: craft beats vendors, an unplaced gil vendor keeps its price label without a map flag, a special-shop offer carries its costs and plural phrase, and market/none fallbacks hold (file: `tests/LazyFashionReport.Harness/Program.cs` section 14)
- Vendor picks are informational: the plugin flags the map and names the price; walking there and buying stays with the player (also why there is no auto-teleport in this release)

## v0.2.0.0 (2026-09-12)

### Changed
- The report window is rebuilt as one flat, readable layout: the scoring table on top, then "The week's pieces" - a block per hinted slot showing Wear (owned candidates) and Missing (every not-owned candidate) side by side, all visible on open with nothing hidden behind collapsed headers. The window's default first-open size widens from 560x640 to 780x700 to fit it (files: `ReportWindow.cs`, new `Core/SlotPlan.cs`)
- Every missing piece is now listed with its source - "craftable (<job> lv N)" when a recipe exists, "not craftable (vendor/market leg comes later)" when one does not. Previously a missing piece with no recipe was silently dropped from the list entirely, so uncraftable crowd items were invisible (files: `Core/SlotPlan.cs` `SlotPlanner.Compose`, `ReportWindow.cs DrawSlotPlan`)
- The owned-items snapshot is now always taken when the prediction rebuilds, not only when the owned-only candidate filter is on, so the missing-pieces view exists regardless of the wear filter (file: `FashionService.cs ReadGame / RebuildPrediction`)

### Fixed
- The missing-pieces list no longer depends on the Artisan section being enabled: the list of what is missing renders for every hinted slot even when crafting is toggled off or Artisan is not installed - the Craft via Artisan button is what stays gated on the "Craft a missing piece via Artisan" setting (files: `ReportWindow.cs DrawSlotPlan / DrawCraftButton`)
- A hinted slot with an empty Wear or Missing list now says which it is ("nothing owned fits this hint yet", "nothing missing for this hint", "ownership not read yet") instead of rendering nothing (file: `ReportWindow.cs DrawSlotPlan`)

### Notes
- Regression coverage in the offline harness: a Recipe==null crowd item must stay visible in the plan with a NotCraftable source, every hinted slot must yield a plan, and unknown ownership must be flagged rather than silently treated as an unfiltered list (file: `tests/LazyFashionReport.Harness/Program.cs` §13)
- The shop/vendor and market-board legs of fetch-missing are still upcoming; "not craftable" labels those pieces honestly in the meantime

## v0.1.2.0 (2026-09-07)

### Added
- Fetch-missing, step one: a settings toggle (off by default) called "Craft a missing piece via Artisan". When on, the report window gains a "Missing pieces (craftable via Artisan)" section listing the best-voted crowd candidate for each hinted slot that is not owned, with a Craft via Artisan button for each piece that has a recipe (files: `Core/FetchPlan.cs`, `Adapters/RecipeIndex.cs`, `ReportWindow.cs`)
- The craft action calls Artisan's public IPC (`Artisan.CraftItem(recipeId, 1)`) directly - the same interface LazyCrafter drives - so one click hands one recipe to Artisan and nothing crafts until the button is clicked. Artisan not installed means the buttons stay hidden and the section says so (files: `Adapters/ArtisanCraft.cs`, `FashionService.cs CraftViaArtisan`)
- A recipe index built once from the game's Recipe sheet maps each crowd item to the recipe that produces it; when several recipes make the same item the highest-level one is picked, since master-book recipes are the ones current crafters know (file: `Adapters/RecipeIndex.cs`)
- While Artisan is mid-craft the section says "Artisan is busy"; a failed craft request shows the reason in red with a dismiss button instead of failing silently (file: `ReportWindow.cs DrawMissingPieces`)

### Notes
- Buying is not wired in this version - the shop-exchange leg of fetch-missing comes later. The toggle only ever offers a craft
- The missing-pieces plan is built from the same owned-items snapshot the candidates filter uses, so the two views always agree (file: `FashionService.cs RebuildPrediction`)

## v0.1.1.0 (2026-09-07)

### Fixed
- The window no longer shows a bare list of slots with "no hint" under every row. The weekly hint/theme/dye data comes from fashionreportxiv.com, whose payload is formatted differently than the plugin assumed; the data downloaded successfully but every field silently failed to be read, so the window looked half-built while the log said everything was fine. All fields now bind correctly, and when a data source fails to load the window says which one and why instead of showing an empty table (files: `Core/RemoteDataSource.cs`, `FashionService.cs`, `ReportWindow.cs`)
- A single unknown item id in the crowdsourced data no longer aborts the whole weekly rebuild. One junk id (1010533) threw while names were being looked up and took every hint, dye and candidate down with it; unknown ids now fall back to showing the raw item number (file: `Adapters/SheetAdapter.cs`, `WarmItemNames`)
- The settings window's data-status line now names each source separately instead of one combined "data loaded" that stayed green while the hint source had failed (file: `ConfigWindow.cs`)

### Notes
- Verified offline against week 449's real downloaded payload (theme "Hunter from the Far East": four hints, six dye slots, base 70, the published easy-100 set scores exactly 100). The regression fixture replays the actual bytes from the live site through the same parsing path the plugin uses (file: `tests/LazyFashionReport.Harness`)
- If the window still says "MISSING" for a source, /lfr refresh re-downloads; cached data from earlier sessions is used when a download fails (file: `Core/RemoteDataSource.cs`)

## v0.1.0.0 (2026-09-06)

### Added
- First testing release: a Fashion Report assistant for the Gold Saucer. Open the in-game Fashion Report window (or type /lfr) and LazyFashionReport shows, for every gear slot: the week's hint, the exact +2 dye and the +1 shade family for left-side slots, and the top candidate items from the crowdsourced database (xivstats) filtered down to items actually owned - bags, glamour dresser and armoire included (files: Core/ScoreMath.cs, Core/Predictor.cs, Adapters/CrowdDataAdapter.cs)
- A live score predictor: as the worn outfit changes, the window shows "scores N - needs +X for 80" per slot and in total, computed from the verified scoring rules (10/8 base per unhinted slot, hinted slots 2 + 8/6 for a correct item, exact dye +2 / same shade +1 on left-side slots) (file: Core/ScoreMath.cs SlotScore / WeeklyBase)
- The weekly base is computed from where the hints landed, never hardcoded: 68 when all four hints are main gear slots, 70 when one is an accessory (verified against week 449's live data) (file: Core/ScoreMath.cs WeeklyBase)
- /lfr opens the assistant window, /lfr refresh re-downloads the crowd data, /lfr changelog shows what's new (file: Plugin.cs OnCommand)
- Settings: auto-open with the Fashion Report window, owned-only candidate filter (on by default), candidates-per-slot limit (file: Configuration.cs, ConfigWindow.cs)

### Notes
- Item candidates come from xivstats.com's crowdsourced database and exact weekly dyes from fashionreportxiv.com; both are cached locally and the plugin degrades gracefully to whatever loaded last when a fetch fails (file: Core/RemoteDataSource.cs)
- The predictor is offline-verified against week 449's published results: base 70, easy100 = the four gold items, easy80 = Brand-new Gloves + Abyssal Blue on the head slot (file: tests/LazyFashionReport.Harness)
- v1 does not change gear, apply dyes or submit anything: it only reads and advises (file: Plugin.cs)
