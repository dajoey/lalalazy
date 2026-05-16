# lalalazy logo kit

Everything needed to produce a new plugin logo that **reproduces the canon** — same chibi lala face, same blanket, same `zZz`, same thought bubble, only the dream content changes.

The kit uses Venice AI's `/image/edit` endpoint (img2img) against a frozen canonical base — NOT text-to-image generation. This is the difference between every logo being subtly different versus pixel-faithful reproductions.

## Files

| File | What it is |
|---|---|
| `assets/canonical-base.png` | Frozen copy of the original `pvpsolver-icon.png`. The img2img source for every plugin logo. **Don't edit in place.** |
| `../STYLE.md` | The locked style spec — canvas, palette, composition, what's allowed. |
| `prompt-template.txt` | The exact img2img prompt with the `{DREAM}` placeholder, plus the dream-icon authoring guide. |
| `generate_logo.ps1` | Windows PowerShell — calls Venice `/image/edit`, writes `LalaImages/<mod>-icon.png`. |
| `generate_logo.sh` | Bash wrapper for Cowork VM sessions — drives the PowerShell over SSH to DAJOEYROG. |

## Workflow — adding a new mod logo

### Option A: from DAJOEYROG directly

```powershell
cd <repo>\LalaImages\kit
.\generate_logo.ps1 -ModName tradecraft `
  -Dream "a tiny stack of three pixel-art crafting tools — a hammer, a saw, and a screwdriver — in earthy tones"
```

Result: `LalaImages/tradecraft-icon.png`, 500×500 PNG, pixel-near-identical to the canon outside the bubble.

### Option B: from a Cowork VM session

```bash
cd <repo>/LalaImages/kit
./generate_logo.sh tradecraft \
  "a tiny stack of three pixel-art crafting tools — a hammer, a saw, and a screwdriver — in earthy tones"
```

Same result. The bash wrapper SCPs both the PowerShell helper and the canonical base to DAJOEYROG, runs the edit, then pulls the PNG back.

## Authoring a good `{DREAM}` description

See the **{DREAM} authoring guide** section in `prompt-template.txt`. Short version:

- One sentence, one icon.
- Lead with the dominant color (gold, green, cyan, etc.).
- Use FFXIV vocabulary where it helps ("gil coin", "linkshell pearl", "aetheryte shard").
- Use "tiny" or "small" if you don't want the icon to fill the bubble.
- Don't mention the lalafell, the bubble, the green background, the blanket, or "zZz" — those come from the canonical base and the preserve clause.
- Keep it to ~3 distinct colors so the icon reads at 64 px.

## How the img2img approach holds the canvas stable

The prompt is split in two parts:

1. **The variable part** — what changes per mod: `"Replace the crossed swords inside the white thought bubble with {DREAM}."`
2. **The preserve clause** — locks the rest: `"Keep the sleeping lalafell face, brown bob hair, white duvet, dark green background, the zZz letters, and the white thought bubble outline EXACTLY the same as the original. Maintain the chunky retro pixel-art aesthetic."`

The preserve clause is what stops the edit model from re-interpreting the lala or smoothing the chunky pixels. Don't shorten it.

## Cost & speed

- `qwen-edit` (default) — ~$0.04 per edit, ~7 seconds wall-clock.
- `seedream-v5-lite-edit` — ~$0.05, slightly higher fidelity.
- `flux-2-max-edit` — ~$0.19, top quality (overkill here).

## Re-generating an existing logo

Run the same command again — it overwrites `LalaImages/<mod>-icon.png` in place. The seed is not load-bearing here (the base image dominates the result), so the same prompt produces a similar-but-not-identical reroll.

## When to refresh the canonical base

If you want to change the underlying lala (different hair, different blanket, etc.), replace `assets/canonical-base.png` with a new render that satisfies `STYLE.md`, then re-run `generate_logo.ps1` for every existing plugin logo so they all inherit the new base. The repo icon (`repo-icon.png`) is the bubble-less counterpart — refresh it separately.

## Adding a logo to `pluginmaster.json`

```json
{
  "IconUrl": "https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/<mod>-icon.png"
}
```

(See existing entries for the exact format.)
