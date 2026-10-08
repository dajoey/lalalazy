# Knightshopper ground truth

Knightshopper (third-party plugin) refuses a whole import code when one item's
`(item, vendor, shop, sub-currency)` is not in its own catalog. This folder produces that
catalog from Knightshopper's own library, so ArmoireAutoFill's export can be tested against
Knightshopper's answer instead of against ArmoireAutoFill's own model.

| Tool | What it does | Output fixture |
|---|---|---|
| `NativeDump` | Loads Knightshopper 1.0.1.6's `Knightshopper.Native.dll` (from its installed `Native/` folder) and calls the same exports the plugin calls: ABI check, `discover(sqpackPath, language)`, then the six result arrays (vendors, locations, shops, listings, territories, item meta). `init` returns 0 outside the game; `discover` does not need it. Windows only (the library is a PE dll). | `tests/ArmoireAutoFill.CatalogHarness/fixtures/ks-native-catalog.json.gz` (trimmed to the fields the managed side uses; see `KnightshopperNativeCatalog.cs`) |
| `PlacedNpcs` | Runs `KnightshopperVendorPlacement.Scan` (the exact scan the plugin runs in-game) over a real install's `sqpack` and prints its cost. | `tests/ArmoireAutoFill.CatalogHarness/fixtures/ks-placed-npcs.json.gz` |

Run both against the same read-only game install (`.../game/sqpack`), then copy that install's
`ffxiv/0a0000.win32.*` files (the Excel sheets) into the sqpack folder the CatalogHarness reads
(`ARMOIRE_CATALOG_HARNESS_SQPACK`), so sheets, native catalog and placements are one patch.
The fixtures committed here come from game 2026.08.05.0000.0000, English, Knightshopper 1.0.1.6
(native dll sha256 0f0e561bee614da4fdad5ec5eba14f372338a31056e1ec671288dcde7219bb0a).

```
dotnet publish tools/KnightshopperGroundTruth/NativeDump -c Release -o out/nd
dotnet publish tools/KnightshopperGroundTruth/PlacedNpcs -c Release -o out/pn
dotnet out/nd/NativeDump.dll <Knightshopper.Native.dll> <game>/sqpack nd.json 2
dotnet out/pn/PlacedNpcs.dll <game>/sqpack ks-placed-npcs.json.gz
dotnet run --project tests/ArmoireAutoFill.CatalogHarness -c Release
```

`nd.json` is the raw dump; the committed fixture keeps `vendors` that have a location,
`shops` and `listings` (see the `meta.note` inside it). Re-generate and re-run the harness after
a Knightshopper update or when a new game patch matters: the harness fails if any exported code
or catalog entry is one Knightshopper's own catalog does not have.
