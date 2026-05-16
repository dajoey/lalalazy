# lalalazy — Logo Style Spec

The brand mark for the `lalalazy` repo is a **pixel-art sleeping lalafell** dreaming of the thing the mod does. Every plugin logo reuses the same sleeping-lala canvas; only the dream content changes per mod.

This is achieved by **img2img-editing a frozen canonical base** — not by re-generating from a text prompt. The base lala, bubble, `zZz`, blanket, and dark green background carry over byte-similar across every logo, and only what's *inside* the thought bubble varies.

This document is the contract every logo must satisfy.

---

## Canvas

| Property | Value |
|---|---|
| Dimensions | **500 × 500 px**, RGBA |
| Format | PNG, opaque |
| Corners | Rounded square, ~10 px corner radius (baked into the canonical base) |
| Background | Dark forest green `#244A3A` |

## The canonical base

The sleeping lala, hair, blanket, `zZz`, and an empty(-ish) thought bubble are baked into one image:

> `LalaImages/kit/assets/canonical-base.png`

This is a frozen copy of the original `pvpsolver-icon.png` from before the redesign — it's the source of truth for every plugin logo going forward. **Do not edit it in place.** When you need to refresh it, replace it with a new render that meets the spec below, and re-run the kit for the existing plugin logos so they match.

## What's in the canonical base

- Tight head-and-shoulders chibi sleeping lalafell, face fills the center of the canvas
- Chocolate-brown bob haircut, side-parted, covering the forehead
- Eyes closed in soft thin arcs, two soft pink blush circles on warm tan cheeks, tiny dot nose, faint smile
- One pointy elf ear visible on the left
- White duvet pulled up to the chin in the bottom ~25% of the canvas
- "zZz" in white with a thin dark outline in the upper-right (small `z`, capital `Z`, larger capital `Z`, cascading left-to-right)
- White cloud-shaped thought bubble with a thin dark outline in the upper-left, with two trailing puff dots leading toward the lala's head
- Chunky 16-bit pixel-art aesthetic, visible square pixels, no anti-aliasing

The thought bubble starts holding **two crossed swords** in the canonical base (because it began life as the PvP Solver logo). The img2img workflow swaps out only the bubble contents per mod.

## The repo icon

`LalaImages/repo-icon.png` is the same chibi sleeping lala **without a thought bubble**. It's the bare base, used as the repo's identity mark. Don't add a bubble to the repo icon — it intentionally stays clean.

## Dream content — the variable

For each plugin logo, the only thing that changes is the icon inside the thought bubble. Keep it:

- One pixel-art icon, ~80 px wide when rendered, centered in the bubble
- Clear at 64 px
- 3–4 dominant colors max
- Chunky pixels matching the rest of the canvas

## Current canon

| Logo | Dream subject | Source |
|---|---|---|
| `repo-icon.png` | *(none — bare base, no bubble)* | Original |
| `pvpsolver-icon.png` | Two crossed steel swords with golden hilts | Original |
| `dagobert-icon.png` | Gold gil coin with a star on its face | Original |
| `autopotion-icon.png` | Green-filled flask with red heart symbol | img2img from canonical-base |
| `armoire-icon.png` | Folded pastel-blue glamour shirt | img2img from canonical-base |

## Palette (sampled)

| Role | Hex (approx) |
|---|---|
| Background field | `#244A3A` |
| Lalafell skin | `#E8B589` |
| Lalafell skin shadow | `#B7825C` |
| Hair (dark) | `#3A1E14` |
| Hair (mid) | `#6A3B23` |
| Cheek blush | `#E08A8A` |
| Duvet white | `#F4F1E3` |
| Duvet shadow | `#B7BCC4` |
| zZz / bubble white | `#FFFFFF` |
| Outline (universal) | `#1A1A1A` |
| Gil-coin gold | `#E8C148` |
| Potion green | `#5FB458` |

## What is NOT allowed

- ❌ Anti-aliased smooth shapes — must be visible chunky pixels
- ❌ Photoreal / illustrated / cartoon styles
- ❌ Different background color
- ❌ Different lalafell pose, hair, or crop
- ❌ Multiple dream icons in one bubble
- ❌ Text inside the bubble
- ❌ Adding a bubble to the repo icon
- ❌ Glow effects, gradients, drop shadows beyond the 1-pixel outline

## Workflow for a new mod logo

See `kit/README.md`. Short version: `./generate_logo.sh <slug> "<dream description>"` runs img2img against the canonical base on DAJOEYROG and drops `LalaImages/<slug>-icon.png` (500×500 PNG) into this directory.
