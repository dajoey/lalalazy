# Changelog - ArmoireAutoFill

## v0.5.7.0 (2026-10-10)
### Fixed
- **The PvP shopping list now includes gear sold for Trophy Crystals and Wolf Collars.** The crystal quartermaster's Trophy Crystal Exchange (weapons, armour, accessories) and the collar quartermaster's exchange were missing from the list because their cost currencies were not mapped, so the PvP code came out empty. Both quartermasters are in the list now, checked against Knightshopper's own vendor catalog: every exported pair is one it accepts.
- Pieces no import code can name are now listed in the window by name with the reason (the shop has no vendor link in the game data, or Knightshopper has no vendor for its shop), instead of being dropped quietly. The Wolf Mark gear behind the mark quartermaster falls in this list: its shop opens through a dialogue handler, so no vendor pair can be written for it.
- The shopping summary line showed placeholder braces instead of the counts; it shows the numbers now.

## v0.5.6.0 (2026-10-08)
### Fixed
- **Import codes now carry only what can actually be bought today.** Some pieces are gated behind more than a quest: seasonal vendors also gate pieces on an achievement, and the previous filter only knew about quests, so pieces the game would not sell yet were still written into the codes, and the first one of them made Knightshopper refuse the whole import ("Item N is not available from its shared Gil vendor"). The shopping list now checks live quest and achievement progress: what can be bought now goes into the code, a piece whose cheapest listing is locked falls back to a listing that can be bought, and a piece with no buyable listing at all is left out and counted at the bottom of the window ("N missing piece(s) need quest or achievement progress you don't have yet - left out of the import"). The rule applies to every currency, not just gil.

## v0.5.5.0 (2026-10-08) [testing]
### Fixed
- **Import codes now name vendors that Knightshopper itself has.** The previous build still produced Gil codes that Knightshopper refused whole ("Item N is not available from its shared Gil vendor"): the rule it used to decide which vendors Knightshopper knows was a guess and was wrong for most Gil shops. The rule now follows how Knightshopper builds its own vendor list: a vendor is an NPC the game actually places in an area, and seasonal NPCs that are re-used under new ids with the same name and the same shops count once. It applies to every currency, not just gil. The Gil code now carries several times as many pieces, because shops the old rule dropped by mistake are back.
- Placeholder items with no name are no longer put in a code; Knightshopper has no entry for them.
- Every code and every catalog entry was checked offline against the vendor data produced by Knightshopper's own library (not against this plugin's own model): no refusals. This is not yet confirmed in the game, and newer game patches than the one checked are not covered.
### Changed
- The shopping-list section says what the codes were checked against and that it is not yet confirmed in-game; the copy message also explains that Knightshopper names only the first item it refuses.
- The vendor placement is read from the game's area files once per session in the background (about one second), with one log line.

## v0.5.4.0 (2026-10-08) [testing]
### Fixed
- **Import codes no longer contain items Knightshopper would refuse.** Knightshopper rejects a whole import when even one item is not in its own catalog, which the previous Gil code hit: shops whose vendors only spawn for an event never enter Knightshopper's catalog, but this plugin was still naming them. The catalog now only names shops with a permanently placed vendor, matching how Knightshopper derives its own list, and the same rule applies to every currency, not just gil.
- Pieces that this leaves out are no longer silent: the window shows "N items left out: Knightshopper cannot buy them", the build log counts the skipped shops, and the copy buttons never produce a code Knightshopper would answer with "not available from its shared vendor".
## v0.5.3.0 (2026-10-08) [testing]
### Fixed
- **The "Copy import code" instructions now name the exact controls.** After copying, the message says which currency window to open (the sidebar entry under "Currencies"), that the shopping-list dropdown sits at the top of the window next to "Buy All", and that the import is the clipboard icon labelled "Paste" at the right end of the "New list name..." row. It also names the list that will be created, quotes the confirmation line Knightshopper prints in chat ("Imported the shopping list from the clipboard."), and explains that each code only imports in its own currency's window - pasted elsewhere, Knightshopper refuses it and says which window to use.
- Currency names now match Knightshopper's own labels ("The Hunt" instead of "Hunt", "Tomestones" instead of "Tomestone", "Scrips" instead of "Scrip"), so the headings, the created list names and the instructions match what the sidebar shows.
- When Knightshopper is not loaded, the hint now points at the same controls instead of just saying "paste them".

## v0.5.2.0 (2026-10-08) [testing]
### Fixed
- **The shopping list now builds.** On the previous testing build the Knightshopper shopping list always showed "Shop catalog unavailable": one shop entry with an unusual data shape aborted the whole catalog. That entry is now skipped instead, the rest of the list still builds, and a failure is no longer retried every frame.
### Changed
- The section now says what actually happened: game data not ready yet, a build failure (with a Refresh button — nothing retries automatically), or a normal empty result ("nothing to shop for").
- One line per build in the plugin log reports how many shops were scanned, skipped and failed, and how many entries each currency produced.

## v0.5.1.0 (2026-10-08) [testing]
### Added
- **The shopping list now shows its pieces.** Every currency section in the Knightshopper shopping list can be expanded into a table of the actual pieces: item, price, vendor, one needed of each. Gil prices are the standard vendor price from the item sheet (some vendors charge less with reputation).
### Changed
- Empty states say so in words: nothing missing at all, or none of the missing pieces sold by a vendor Knightshopper can reach.
- The section reports how many additional armoire pieces sit only in shops the list cannot safely name (scripted vendors and gil-priced SpecialShop entries), so the partial total is visible.

## v0.5.0.0 (2026-10-07) [testing]
### Added
- **Knightshopper shopping list.** New collapsible section in the main window: every armoire-eligible item that is missing, that is sold by a vendor Knightshopper can reach, and that is not already in the inventory, grouped by currency (gil, hunt seals, PvP wolf marks). One click per currency copies a Knightshopper import code to the clipboard; pasting it into Knightshopper's matching currency tab (paste button) creates a new shopping list and leaves existing lists untouched. No purchase is ever started by this feature — the clipboard is the only thing it touches.
- Missing pieces that are already in the inventory or armoury chest are reported instead of listed for purchase (auto-store handles those), and pieces no reachable vendor sells are counted so the total adds up.
### Notes
- Testing channel only; the production channel is unchanged.
- Knightshopper itself does not need to be loaded to copy the codes; when it is not detected the window says so. Import-time validation still applies inside Knightshopper, so every generated entry uses only shops Knightshopper's own catalog can reach.
- Quest-locked shop entries are included and counted, since the vendor only offers them after the quest is done.

## v0.4.4.1 (2026-10-03) [testing]
### Added
- **Quick controls for the Lazy Hub window.** The plugin now offers "Auto-store when the Armoire opens", scanning the inventory on login, showing owned items and hiding finished dungeons to Lazy Hub (`/lazy`) over Dalamud IPC, so they can be changed from one window next to the other lalalazy plugins. Each change does exactly what the matching setting in the plugin's own window does.
### Notes
- Testing channel only; the production channel is unchanged.
- Without Lazy Hub installed the plugin behaves exactly as before.

## v0.4.4.0 (2026-09-05)

- Added the in-game "What's new" popup. After Armoire Auto-Fill updates, its changelog now opens once inside the game so the changes are visible without a trip to GitHub. It waits until the character is logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read. Type `/armoire changelog` any time to reopen it.
- No change to armoire behaviour: the dungeon checklist, the auto-store on opening the armoire, and the gearset/armoury options are all unchanged.

## v0.4.3.0 (2026-07-02)

### Fixed
- **Auto-store on armoire open actually fires now.** v0.4.2.0 ran `StoreAll` directly from the Cabinet addon's PostSetup event, but cabinet contents load from the server asynchronously *after* the addon opens, so `UIState.Cabinet.IsCabinetLoaded()` was still false and the store bailed silently (the manual button worked because the data had loaded by then). PostSetup now just arms a pending flag; a Framework.Update poll fires `StoreAll` once the cabinet data is loaded (10s timeout, disarmed on PreFinalize if the UI closes first). File: `Logic/ArmoireAutoStore.cs`.

## v0.4.2.0 (2026-07-02)

### Changed
- **Auto-store is now ON by default** - the plugin finally lives up to its name. Opening the armoire UI at an inn automatically stores eligible gear from inventory. Config migration (v2 -> v3) flips `AutoStoreOnOpen` on for existing installs; it can still be turned off via the checkbox in the main window. Files: `Configuration.cs`, `Plugin.cs`.
- **Auto-store scope narrowed to the regular inventory (bags) by default.** The armoury chest is no longer scanned unless the new "Also store from armoury chest" option (`AutoStoreIncludeArmory`, off by default) is enabled. Gearset protection (`SkipGearsetItems`) remains on by default. Files: `Logic/ArmoireAutoStore.cs`, `Windows/MainWindow.cs`.

### Fixed
- **Eligibility check order in `StoreAll`.** Items with no Cabinet sheet entry were previously tested via `IsItemInCabinet(GetValueOrDefault(itemId, 0))`, probing cabinet row 0 before the real lookup ran. The `TryGetValue` lookup now runs first and `IsItemInCabinet` only ever sees a real cabinet row. File: `Logic/ArmoireAutoStore.cs`.

## v0.4.1.0 (2026-06-18)

### Added
- **"Skip gear that is in a gearset" option** (on by default). Auto-store now excludes any item that belongs to a saved gearset, so gear in active use is not deposited. Built from RaptureGearsetModule (same source as the in-game gearset UI), HQ flag stripped for matching. New `SkipGearsetItems` config + checkbox in the main window; result message reports how many items were kept. Files: `Logic/ArmoireAutoStore.cs`, `Configuration.cs`, `Windows/MainWindow.cs`.

## v0.4.0.0 (2026-06-18)

### Added
- Auto-store to armoire. New Logic/ArmoireAutoStore.cs stores eligible items into the armoire via the native Cabinet.StoreCabinetItem API. Adds an optional "Auto-store when armoire opens" toggle (off by default) that fires on the Cabinet addon PostSetup, plus a manual "Store all to armoire" button in the main window with a live result message. Deduplicates by item ID and skips items already in the armoire. Files: Logic/ArmoireAutoStore.cs, Configuration.cs (AutoStoreOnOpen), Plugin.cs, Windows/MainWindow.cs.
