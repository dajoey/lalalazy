# Changelog

## v0.1.3.0 (2026-09-04)

### Added

- Batch retainer fetch: when a dispatch needs retainer stock, ONE Artisan session (`RestockFromRetainers(NewCraftingList)`, decompiled + pinned) walks the retainers once for the whole cart instead of one bell cycle per material (file: `Adapters/Dispatch/RetainerFetch.cs`, function: `BeginBatch`; queue selection: `Core/RetainerBatch.Queue`)
- Fallback preserved: items with no recipe row, and any remainder after the batch pass, still go through the 0.1.2.0 per-item path (file: `Adapters/DispatchService.cs`, phases: `BatchRetrieve`/`BatchWait` before `Retrieve`)

### Fixed

- `RetrieveFromRetainers` setting was a no-op - a nested `if` in `WhyNoFetch` made the config check dead, so switching retrieval off still fetched; the toggle now gates every fetch path (file: `Adapters/DispatchService.cs`, function: `WhyNoFetch`)

### Notes

- The batch session is measured, not assumed: bag counts per demanded item are snapshotted at queue time and compared when Artisan goes idle, and anything still short stays in the per-item queue (trimmed to the remainder).
- Both overloads are proved offline by `tests/LazyCrafter.GuardProbe` (the list overload rides on the pin as an alias - `Adapters/ReflectionGuardExtensions.cs`) against the installed Artisan 4.0.5.19 (SHA-256 of the decompiled DLL matches omasky's installed copy).
- Queue decision tests: `tests/LazyCrafter.Harness/RetainerBatchQueueTests.cs` (deferred-because-of-retrieval crafts queue their rows; mixed-reason deferrals queue; non-retrieval deferrals stay out; unknown rows are dropped).
- ARC reflection pin ceiling raised 8.7 -> 8.8: omasky ships ARControl 8.7 and the exclusive ceiling flagged the installed build as unverified even though every pinned member resolves on it (GuardProbe against omasky's installed DLLs, 2026-09-04).

## v0.1.2.0 (2026-09-03)


### Added


### Changed


### Notes

- The guard-style refusal from 0.1.1.0 is untouched and still the safety net: a craft whose materials are not in the bags at the instant of hand-off is never sent to Artisan.
- Bullets in this entry are deliberately unwrapped single lines. `tools/Package-Plugin.ps1`'s CHANGELOG parser only keeps lines starting with `-`, `**` or a digit, so a hard-wrapped bullet loses everything after its first line in the user-facing `Changelog` field (visible in older entries above).

## v0.1.1.0 (2026-09-03)

Testing-channel fix build (production pointer stays 0.0.0.0). **"Owned" is not "in your bags."** Fixes the defect
a retainer, Artisan could not start, and LazyCrafter reported `1/1 craft finished` 1.25 s later. His verdict, verbatim:
*"needs to grab stock before attempting craft"*.

### Fixed
