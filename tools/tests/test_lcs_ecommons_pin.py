"""LazyCurrencySpender must bundle an ECommons with the Api15 Callback fix.

Background (Helm t-plugerr-ff1e67c59015, fingerprint ff1e67c59015):
every game shutdown logged
"The type initializer for 'ECommons.Automation.Callback' threw an exception"
at ECommons.Automation.Callback.Dispose(), caught by GenericHelpers.Safe, so
unload still completed. Fleet logs showed the throw ONLY from plugins bundling
pre-Api15 ECommons (LazyCurrencySpender 3.1.0.5, third-party NecroLens) and
never from any sibling on 3.2.0.9 across mass-unload events. Upstream renamed
FFXIVClientStructs `ValueType` -> `AtkValueType` in the Apr-2026 Api15 update
(ECommons commit d435e12, shipped in 3.2.0.9, published 2026-04-30); the old
Callback static initializer (`ZeroAtkValue`) binds the stale layout and its
type initializer throws on first touch, which for this plugin is Dispose
during teardown (the plugin never calls Callback while running).

This test fails while the csproj pins a pre-fix ECommons and passes once the
pin is at or above the fixed version, so a future merge can never silently
re-strand this plugin on the old pin again.
"""

import re
import xml.etree.ElementTree as ET
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
LCS_CSPROJ = REPO / "src" / "LazyCurrencySpender" / "LazyCurrencySpender.csproj"

# First ECommons release containing upstream commit d435e12 ("Api15 update"
# of ECommons/Automation/Callback.cs, 2026-04-28; NuGet publish 2026-04-30).
MIN_FIXED_ECOMMONS = (3, 2, 0, 9)


def _ecommons_pin(csproj: Path) -> tuple:
    tree = ET.parse(csproj)
    for ref in tree.getroot().iter("PackageReference"):
        if ref.get("Include") == "ECommons":
            return tuple(int(p) for p in ref.get("Version").split("."))
    raise AssertionError(f"no ECommons PackageReference in {csproj}")


def test_lcs_ecommons_has_callback_fix():
    pin = _ecommons_pin(LCS_CSPROJ)
    assert pin >= MIN_FIXED_ECOMMONS, (
        f"LazyCurrencySpender pins ECommons "
        f"{'.'.join(map(str, pin))} < {'.'.join(map(str, MIN_FIXED_ECOMMONS))}: "
        f"pre-Api15 Callback type initializer throws at every game shutdown "
        f"(fingerprint ff1e67c59015)"
    )


def test_lcs_ecommons_pin_matches_fleet():
    """Sibling plugins share one ECommons pin; LCS must not drift from it."""
    pins = {}
    for csproj in sorted((REPO / "src").rglob("*.csproj")):
        if "GluttonyCombo" in csproj.parts:
            continue  # vendors ECommons as a ProjectReference, not a NuGet pin
        try:
            pins[str(csproj.relative_to(REPO))] = _ecommons_pin(csproj)
        except AssertionError:
            continue
    lcs = pins[str(LCS_CSPROJ.relative_to(REPO))]
    mode = max(set(pins.values()), key=list(pins.values()).count)
    assert lcs == mode, (
        f"LazyCurrencySpender ECommons {'.'.join(map(str, lcs))} != "
        f"fleet pin {'.'.join(map(str, mode))}"
    )
