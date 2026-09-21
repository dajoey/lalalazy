# LazyCrucible — design (phase 2, after 0.1.0.0)

> "I don't want LazyCrucible to play it. I want it to select the animals and the switch and the food and take the best
> items for the upcoming fights and be a repository of information you'll need to know for each fight.. I'm not looking
> for fully crucible automation. i can pair autoduty with it if I want that. I don't."
> — the owner's scope, 2026-09-21. It governs everything below.

LazyCrucible fills in the **selection screens the player opens** during a Crucible of the Unbroken run and carries a
**per-fight guide**. It never plays the run.

Evidence tags: **[obs]** seen in recorded plugin logs (ffxivdb `plugin_log_lines`, runs of 2026-09-17 to 2026-09-21;
replay fixtures in `tests/LazyCrucible.Harness/Fixtures`); **[pub]** read in someone else's source (facts only; licences
in §8); **[sheet]** game sheet data (7.56); **[inf]** inferred, unverified. Times are US Eastern.

## 1. What it does, what it only suggests, what it never does

| Screen (opened by the player) | LazyCrucible | Switch (default) | Grounded by |
|---|---|---|---|
| Run roster, Bentbranch entry menu | Writes the ten best-coverage familiars | `AutoRoster` (on) | 0.1.0.0, live-graded |
| Battlehorns before each fight (the "switch") | Writes the three familiars that answer that fight | `AutoHorns` (on) | 0.1.0.0, 20/20 correct |
| Beast Feed picker (opens when a feed is bought) | Picks who eats it, confirms on the prompt that names both | `AutoFeed` (on) | [obs] 3 recorded feeds: pick `kind 0 [1, slot]`, confirm, picker closes, satiety +1; [pub] |
| Shop | Buys the items, gear and feed the fights ahead call for | `AutoShop` (on) | [obs] 12 recorded feed purchases (tokens drop by the listed price); [pub] buy = callback `[2, index]` |
| Spoils after a battle | "Take all" when everything fits | `AutoSpoils` (on) | [pub] button node 46; [obs] AutoDuty's 12:56 take (tokens 201 → 412) |
| Campsite | Selects who rests; **Rest stays the player's button** | `AutoCamp` (on) | [obs] picks `kind 0 [1, index]` flip the rest flag (09-20 01:40) |
| Treasure coffer | **Suggestion only**: frames the screen, names the best choice and why | `AutoTreasure` (on) | click param ↔ choice mapping never recorded (§6) |
| Spoils that do not fit / repeat owned gear | **Suggestion only**: best items first | — | per-item take not needed yet |
| A feed that would hurt every familiar, or a feed LazyCrucible did not see bought | **Suggestion only** | — | — |
| Fight guide | `/lazycrucible guide`; opens on the upcoming fight | `GuideAutoOpen` (on) | §5 |

**Never:** walk, enter a board, pick a board, press challenge / commence / start battle, choose the path, press Rest,
leave a campsite / shop / coffer, sell, discard, use board items, flee, suspend or forfeit. The allow-list
(`Policy/ActuationGuard.cs`) is the complete list of inputs that can ever be sent: shop `[2, index]`, familiar pick
`kind 0 [1, index]`, "Take all" (node 46), a treasure choice button (param ≥ 2, not enabled), and Yes/No on a prompt this
plugin's own input opened. Everything else is refused before it is sent (harness-tested).

**Stand-down:** every screen is acted on once per open, one input at a time. Any input the player makes on the same
screen (a familiar pick not sent by LazyCrucible, a shop click seen by the FireCallback hook, an entry bought by hand), a
failed read-back, a prompt that does not match, AutoDuty running (`YieldToAutoDuty`, on) or an older GluttonyCombo with
its own writer hands the rest of that screen open to the player.

## 2. Policies — survival first (pure, `Policy/`, harness-tested)

Order of priorities: (1) keep the player and the familiars alive (the player's knock-out ends the run; a knocked-out
familiar is gone unless revived), (2) win, (3) score. Score bonuses are chased only by the `ScoreGoal` setting
(`Starve the Fever` = never feed; `Feed the Bold` = feed anything harmless). Every decision returns a short reason that
goes to the log (`SL|`), to chat (`AnnouncePicks`) and to the guide window.

**What is ahead** (`Data/CrucibleBoards`, [sheet]): the board graph (spaces, depths, edges, random outcomes, campsite
sizes). The run's position is the space of the last battle fought (`RunTracker`, from the hostile enemies' panel battle
in combat); everything reachable from there is ahead, both sides of an unchosen fork. A battle is *certain* when every
path to the boss passes it. The fights ahead give the context: threats (from the guide), panel needs, weaknesses, and
**horn demand** — for each familiar, how many fights ahead it is one of the three `PickSlots` picks (1 per certain fight,
0.5 per possible one). Feeding and resting use horn demand, so they favour the familiars the horn pass will field.

**Items** (`Data/CrucibleItems`, generated from the XBMItem tooltips, [sheet]): max HP, damage taken, vulnerabilities,
resistances, heals, kin suitability and drawbacks are parsed from the tooltip text; only an item's role is hand-assigned.
Value (`ItemValue`): gear = max HP + 2 × damage-taken cut + vulnerability cuts + 35 per resistance to a status a certain
fight ahead inflicts (18 possible) + a third of damage (half for familiar-affecting gear) − drawbacks; owned gear and
full slots are worth 0. Consumables: heals scale down as more are held; Reraise 55/70, Temporal Sand 60, a revive 45 when
a needed familiar is down, cures / serums 25–32 for a status ahead, fangs more with adds ahead (Fang of Water more with a
dispel need), score items 1, self-harming potions 0.

**Feed** (`FeedPolicy`). Hard rules: the feed's kin list [sheet] and the picker's own "cannot eat" mark (they agreed in
every recorded picker [obs]); satiety below the familiar's cap (XBMPet.SatietyMax [sheet], +1 per feed [obs]); the same
feed never twice ([obs] every recorded refusal was a repeat); never a knocked-out familiar. Value: max HP, heals (up to
the missing HP), damage-taken and vulnerability cuts, resistance to a status ahead, Lily Simular (feed survives a rest)
and Lassi Simular (full heal at a campsite) when a campsite is ahead; damage counts a little; drawbacks (max HP -80%,
Slow +200%, self damage over time, self-inflicted statuses) cost more than most feeds give. Weighted by horn demand; a
hurt familiar that may rest at a campsite ahead is discounted 25% (resting wipes feed, Addon#17657 [sheet]) unless it ate
Lily. In the picker: the best eligible familiar; a feed that is harmful to everyone is left to the player. A feed is paid
for only when the familiar is confirmed (Addon#17653 "Purchase X and feed it to your Y?").

**Shop** (`ItemPolicies.NextPurchase`, one purchase at a time, re-read after each): (1) healing reserve — while fewer
heals are held than fights ahead (min 2, max 4), the best affordable heal; (2) otherwise the highest value among items,
gear, and feed (feed on the item scale: its value × 0.5–2 by horn demand, so one familiar's buff does not outbid gear that
protects the player); (3) stop below value 12, when nothing is affordable, or after 8 purchases. No token reserve: what is
bought now also serves later fights, and a later shop's stock is unknown (shop stock has no client sheet [sheet §7.5]).

**Treasure** (`TreasurePick`): the highest-value choice; if nothing is worth anything (e.g. items full and only items
offered) the choice is left to the player. **Spoils** (`Spoils`): Take all when every item fits (≤ 10 items, gear not
already owned, ≤ 10 gear); otherwise the best items first, for the player.

**Campsite** (`CampPolicy`): heal per rest for the player and each resting familiar: boards 1–2 90 / 45 / 30 % (0 / 1 /
2 familiars), board 3 100 / 60 / 40 / 30 % [guides, two sources; [obs] +30% each with two familiars on board 1]; boards
4–5 publish no numbers, board 3's table is used and the chat line says so [inf]. For 0 up to the campsite's limit
(nearest campsite ahead on the board graph; the screen does not show it [obs]) it adds the player's HP gained (× 2) and
each familiar's HP gained × (0.5 + horn demand) minus 12 per feed it would lose (0 if it ate Lily), and picks the best
count, fewer on a tie. Knocked-out familiars cannot rest.

## 3. Screens (layouts verified against recorded snapshots [obs])

- **Shop `XBMContentsItemShop` (n=269):** `[1]` tokens text; `[2]` stock count (16: always 8 feed, 4 items, 4 gear);
  entry k at `3+5k`: listed, XBMItem row, price text (`" (-50%)"` when discounted; unaffordable prices are wrapped in
  colour payloads, stripped before reading), discount flag, bought flag. Carried items `154+5s`, gear `205+5s` (row at
  +3). Buy = callback `[2, k]` → Yes/No Addon#17641 "Purchase X?" (items/gear) → bought flag + tokens drop. A **feed**
  entry opens the Beast Feed picker instead; the prompt comes after the familiar is picked.
- **Beast Feed picker `XBMPetParty`, mode 3 (n=1188):** familiar k block `B=6+77k`: +1 `242000+XBMPet row`, +2 cannot eat
  the offered feed, +3 name, +5/+6 HP, +7..+16 feeds eaten as (icon, row) pairs, +72/+73 satiety used/max, +75 rest flag,
  +76 XBMPet row. **The offered feed is not shown anywhere [obs]**: LazyCrucible knows it from its own purchase or from the
  player's shop click (FireCallback hook, `[2, k]`), and cross-checks it against the picker's cannot-eat marks before
  feeding. Pick = agent event `kind 0 [1, k]` (the same live-proven route as the horn pass) → Yes/No Addon#17653 → the
  picker closes and the entry is bought. The picker fires `kind 0 [1, slot]` by itself when it opens [obs]; events in its
  first 1.2 s are not treated as player edits.
- **Campsite, `XBMPetParty` mode 4:** same blocks; pick = `kind 0 [1, k]`, read back from +75. Rest (`kind 0 [3]`) and its
  prompt (Addon#17660) are the player's.
- **Treasure `XBMContentsTreasure` (n=144):** choice k present `3+5k`, row `6+5k`; carried `24+5s` / `75+5s`. Choice
  buttons are ButtonClick events with param ≥ 2 [pub]; prompt Addon#17626 "Choose X?".
- **Spoils `XBMContentsBooty` (n=147):** `[2]` tokens, `[4]` tokens offered; loot k present `6+5k`, row `9+5k`, taken flag
  `129+k`; carried `27+5s` / `78+5s`. Take all = node 46 → prompt Addon#17674 "You will receive: … Proceed?" (every loot
  name must be in it).
- **HUD `XBMContentsMainHUD` (n=111):** carried items `9+5s`, gear `60+5s` (used as the treasure read-back).

**Prompt guard** (`Policy/PromptGuard.cs`): Yes is pressed only on a prompt that appears within 3 s of LazyCrucible's own
input, contains the expected Addon row's fixed text in order (read from the running client, English fallback) and every
name LazyCrucible chose (item, familiar), and matches none of the refused rows (forfeit 17618, suspend 17619, challenge
17787, commence 17789, leave shop 17648 / campsite 17656 / coffer 17633, discard 17610/17632/17647/17684, sell 17663,
rest 17660/17662, leave loot 17676, flee 17894/17895, revive 17697, team presets). A mismatch answers No and stands down.
Names are matched in English; on another client language the check fails closed (the player chooses).

**Read-back** after every input: shop — bought flag or tokens drop (or the picker opened, for feed); feed — satiety or the
eaten list, or the picker closing after the confirmed Yes; campsite — the rest flag; spoils — every taken flag, or the
screen closing after the confirmed Yes. A miss stands down with a chat line.

## 4. What 0.1.0.0 already records that 0.1.1.0 relies on

`XB|` (every XBM screen + generic prompts in Bentbranch/boards, 2000 values), `XC|` (callbacks), `XR|` (plain-button
clicks with param/node), `XE|`/`PSP|` (agent events), `XA|`, `XO|`, `XK|`. New in 0.1.1.0: `SL|` (every selection
decision, input, prompt answer and read-back) and `RT|` (run start, battles fought).

## 5. The fight guide (information repository)

`Guide/CrucibleGuide.json` (embedded): all 45 battles on the five boards. Per fight: title, where it sits, a one-line
summary, kill order, mechanics and dangerous hits (**tell → what to do**, action ids, cast time), counters (interrupt /
dispel / cleanse and what they answer), threats (statuses, room-wide hits, big single hits, adds, counter stances,
invulnerable phases, do-not-attack targets), what to bring, and what no source covers. Every line carries source tags
(`panel` = game sheets; guides IV, NT, AS, G8, LSB, CGW; public automation data MRB, CHR, BMR; R, YT). Conflicts between
sources are listed under "not covered", with the safer instruction in the text; nothing is invented.

Coverage: 37 fights have two or more strategy sources; 8 are partial — 2:6 (panel data only), 3:7, 4:4, 4:6, 4:9 (the
only guide reconstructed them from the panels), 5:11, 5:12, 5:13 (one guide, no BossmodReborn module).

**The guide and the picks never disagree:** the window's horn picks are the live `PickSlots` ranking (run roster and HP
on the board, else every captured familiar) with its reasons; the guide names no familiar team of its own (harness
check). Every enemy-panel need the ranking scores is a guide counter tagged `panel`, and no counter claims the panel for
a need it does not have (`CrucibleGuide.Validate`, harness check); the window marks each counter covered or not by the
picks. The threats that drive the shop, feed and treasure values are the guide's own threat lists, and "Bring" adds the
items, gear and feed whose tooltips resist or cure what the fight inflicts.

**Window** (`/lazycrucible guide`): follows the fight the board layout or the Battlehorn screen is focused on (the horn
pass's identification) and opens on it; board and battle selectors to browse; short lines, source tags at line end; the
session's selection decisions and their reasons at the bottom.

## 6. What the 0.1.0.0 recorder still needs to confirm

1. **Treasure click param ↔ choice.** One manual treasure pick with 0.1.0.0: its `XR|…XBMContentsTreasure|type=25|param=P`
   line and the next HUD `XB|` (which item was gained) give the mapping. Then `TreasureActuation.Grounded` becomes true
   (the input path, prompt guard and read-back are already in place). The AutoDuty take at 12:59:08 (Green Beret) was
   too fast to snapshot.
2. **The Yes/No prompts in play.** `XB|SelectYesno` on a shop item purchase, a feed confirm, a Take all, a treasure pick:
   the exact text the guard matches (the Addon rows are known; that each flow shows one is inferred from event timing).
3. **Knocked-out familiars in the picker / campsite.** No recorded block had 0 HP (the old collector stopped at value
   399, party slots 6–9); 0.1.0.0 records 2000 values. The reader treats 0 current HP as knocked out [inf].
4. **Campsite limit and heal on boards 3–5.** Not on the screen; the board graph's campsite size and board 3's table are
   used. One campsite on board 3+ with `XB|` before/after Rest confirms both.
5. **Item and gear purchases.** Only feed purchases were ever recorded; the first item purchase's `XC|`/`XB|` confirms
   the bought flag / tokens read-back for items.
6. **Full inventory.** Never recorded (most held: 7). The discard prompts are refused; the spoils fall back to a
   suggestion.
7. **Stage focus on boards 2–5** (`_entrySelection`, carried over from 0.1.0.0): the guide's auto-follow uses it too.

## 7. Residual assumptions (each beside its guard)

- The run's position is the last battle fought — guard: everything reachable counts as ahead (over-inclusive).
- A suspended and resumed run starts over — guard: same (the whole board counts as ahead).
- The campsite limit is the smallest campsite ahead — guard: a pick the game refuses fails its read-back and stands down.
- Item names in prompts are English — guard: fails closed.
- The feed the picker offers is the last one bought / clicked — guard: the picker's cannot-eat marks must match that
  feed's kin list exactly, else the choice is the player's.

## 8. Upstream sources and licences

| Source | Licence | Use here |
|---|---|---|
| FFXIVClientStructs + PR #1952 | MIT | offsets, signatures, agent/addon ids |
| ECommons | MIT | callback firing (`Callback.Fire`), addon helpers |
| Dalamud, Lumina | AGPL-3.0 / MIT | runtime API; the client's Addon sheet text for the prompt guard |
| BossmodReborn | BSD-3-Clause | facts for the guide: mechanic names, action ids, cast times, shapes (no code) — see NOTICE.md |
| MagitekRoutine PR #325 data, CherryXIV Beastmaster-Reactions | GPL-3.0 / none | facts for the guide (ids, panel reads, reaction triggers) |
| Icy Veins, consolegameswiki, nettoge, asellog, Game8, Lodestone blog | web guides | facts for the guide, tagged per line |
| erdelf/AutoDuty | none ("Unlicensed") | facts only: callback values, node ids (shop `[2, k]`, Take all 46, treasure param ≥ 2) |
| DailyRoutines ModulesPublic, BeastHelper, BOCXIV | AGPL-3.0 / none | facts only (0.1.0.0) |
