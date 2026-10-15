# GluttonyCombo.CrucibleReplay

Runs windows of Joey's recorded Beastmaster Crucible telemetry (`CR|`, `BT|`, `DS|` lines from ffxivdb) through the REAL
`BST_RotationLogic.Decide` (compiled unchanged from `src/`, same three files as `GluttonyCombo.BSTRotationHarness`) and prints the
decision the code makes next to the decision the plugin recorded. Offline: no Dalamud, no game. No plugin change, no release.

```
dotnet build tests\GluttonyCombo.CrucibleReplay -c Release
dotnet tests\GluttonyCombo.CrucibleReplay\bin\Release\net10.0\GluttonyCombo.CrucibleReplay.dll --corpus tests\GluttonyCombo.CrucibleReplay\corpus
   --window <name>     only that window            -v          every tick (recorded | replay | other completions)
   --file <w.tsv>      one window file             --fields [--markdown]   the BstState field table below
   --blank-state       replay with an empty state (shows the harness can fail: ~1% reproduced)
   --freeze            write the current reproduced count into each window's #expect.<label> header
```

The last line is `fail N`: N counts windows whose reproduced count differs from the window's `#expect.<label>` header, windows below
their `#min_reproduced_pct`, lines that did not parse, and canaries that did not flip. Exit code 1 when N > 0.

## What the replay can and cannot prove (read this first)

The lines are a lossy summary of the state `Decide` reads. Of the 122 fields of `BstState`, **56 are rebuildable, 17 derivable and
49 are missing** (table below; the table is checked against the struct by reflection, so a new field fails the run until it is classified).
Missing fields include the ones most rules read: GCD/weave state, every cooldown/readiness flag, the last-press timers, the combo state,
damage-immune target, absolute HP, heal-potion stock. So **no tick has every decision-critical field rebuilt (0.0%)**.

The replay therefore does not hand `Decide` a guess and call it proof. For every tick it
1. sets each gap field to a fixed baseline, runs `Decide`, and compares with the recorded decision (rule name; the action id of a combo step is
   not compared, it follows the last combo action which is not logged);
2. tries every other candidate value of every gap field one at a time, and 64 random completions of all gap fields at once;
3. calls the tick **insensitive** when no completion changes the decision. Only an *insensitive* tick that matches is a claim about the code:
   none of the unlogged state could have changed that decision (within the candidate values listed in `FieldTable.cs` and the sample).

Replayed tick classes: `Reproduced` (matches, insensitive) / `ReproducedGap` (matches under the baseline, other completions differ - not a claim) /
`GapDiffers` (differs, but some completion of the gap fields reaches the recorded decision: a rebuilt-state gap) / `Unreached` (no sampled
completion reaches it: a joint setting the sample missed, a rebuilt-field error, or a code change since the recorded build).
`InCombat` is only a heuristic (positive evidence of a fight, else unknown): it is not logged.

**Settings are not logged either.** Every window is replayed with `BstSettings.Defaults()` (Shadow aggro when the window shows an `sh=aggro:` pick).

**The state collector is outside the replay.** `Decide` is replayed; the code that fills `BstState` from the game (`BST_Crucible.ReadCrucible`,
`BST.ReadState`) is not. A flag the log carries (for example `X`, TargetDoNotAttack) is an *input* here. So the 1.0.4.305 Abaddon fix
(`ConditionallyForbidden` gaining `othersUp`, computed by the collector) is invisible: the Abaddon window replays the same hold on the 1.0.4.304
source and on head, and every window gives identical numbers on both. Replaying that fix needs the inputs of `ConditionallyForbidden`
(`morphoAlive`, `othersUp`) in the log - see the telemetry proposal. What the Abaddon window does prove: given the recorded state, the 304 rules hold
(91 of 91 ticks) and clearing the do-not-attack condition flips the decision (canary below).

## Results (corpus 13 windows, 264 KB; head source = origin/main aa2afa015, 1.0.4.305)

```
corpus: 13 windows, 594 ticks | reproduced 472 (79.5%) | reproduced AND insensitive to every gap field 80 (13.5%) | all decision-critical fields rebuilt 0 (0.0%) | not replayable (some critical field missing) 594 (100.0%)
decisive ticks (recorded decision is a rule, not the gcdchain floor): 212, reproduced 111 (52.4%), reproduced and insensitive to every gap field 75
```

| window | recorded build | piece | covers | ticks | reproduced | + insensitive | decisive ticks reproduced | why the rest differs |
|---|---|---|---|---|---|---|---|---|
| `abaddon-idle-hold` | 1.0.4.304 | Abaddon (board 5, battle 5) | hold,do-not-attack,known-bad | 91 | 91 (100.0%) | 73 | 90/90 | - |
| `cleanse-answer-b5-bt1` | 1.0.4.304 | board 5 battle 1 (target 14631) | cleanse,answer-swap | 25 | 20 (80.0%) | 0 | 0/3 | 2x InCombat is only a heuristic (not logged); 1x needs ReadyHorn3; 1x needs ReadyTempered |
| `cleanse-invulnerable-b5-bt13` | 1.0.4.304 | board 5 battle 13 (target 14688/14691) | cleanse,invulnerable-hold | 53 | 45 (84.9%) | 0 | 0/8 | 2x unreached: joint gap setting missed, rebuilt-field error, or code change since the recorded build; 1x needs TargetInvulnerable; 1x needs ReadyAxe |
| `cleanse-ultrasonics-b5-bt2` | 1.0.4.301 | board 5 battle 2 (target 14633) | cleanse,answer-swap | 30 | 19 (63.3%) | 0 | 1/9 | 3x needs a joint setting of several gap fields; 3x needs CanWeave/SinceHornPress/SincePartingBlow; 3x needs ReadyTempered |
| `death-petsave-b3-bt2` | 1.0.4.291 | board 3 battle 2 (Nameless target 14571) | death,petsave,tempered | 20 | 15 (75.0%) | 0 | 0/2 | 3x needs CanWeave/SinceHornPress/SincePartingBlow; 1x needs ReadyHorn2/ReadyHorn3; 1x unreached: joint gap setting missed, rebuilt-field error, or code change since the recorded build |
| `dispel-b4-bt1` | 1.0.4.245 | board 4 battle 1 (target 14596) | dispel,answer-swap | 27 | 23 (85.2%) | 0 | 0/4 | 4x unreached: joint gap setting missed, rebuilt-field error, or code change since the recorded build |
| `interrupt-b5-bt10` | 1.0.4.304 | board 5 battle 10 (target 14673) | interrupt,soulcrush | 46 | 30 (65.2%) | 3 | 0/16 | 9x needs a joint setting of several gap fields; 3x unreached: joint gap setting missed, rebuilt-field error, or code change since the recorded build; 2x needs ReadyBeastMode |
| `invulnerable-b5-bt9` | 1.0.4.301 | board 5 battle 9 (target 14665) | invulnerable-hold | 19 | 10 (52.6%) | 0 | 0/9 | 9x needs TargetInvulnerable |
| `petsave-pack-b4-bt8` | 1.0.4.302 | board 4 battle 8 (target 14620/14622) | petsave,pack-release | 70 | 55 (78.6%) | 0 | 2/11 | 8x unreached: joint gap setting missed, rebuilt-field error, or code change since the recorded build; 6x needs CanWeave/SinceHornPress/SincePartingBlow; 1x needs ReadyHorn2 |
| `reflect-stance-b3-bt2` | 1.0.4.291 | board 3 battle 2 (target 14571) | reflect-stance-hold | 52 | 40 (76.9%) | 0 | 7/19 | 8x unreached: joint gap setting missed, rebuilt-field error, or code change since the recorded build; 2x needs ActiveSlot/PetObjectPresent/SinceHornPress; 1x needs ReadyHorn3 |
| `rotation-petsave-b4-bt3` | 1.0.4.302 | board 4 battle 3 (target 14603/14605) | rotation,petsave | 76 | 60 (78.9%) | 0 | 4/15 | 6x unreached: joint gap setting missed, rebuilt-field error, or code change since the recorded build; 5x needs a joint setting of several gap fields; 4x needs CanWeave/SinceHornPress/SincePartingBlow |
| `stance-death-gapclose-b3-bt2` | 1.0.4.235 | board 3 battle 2 (target 14571) | reflect-stance-hold,death,shield-charge | 34 | 18 (52.9%) | 1 | 2/18 | 7x unreached: joint gap setting missed, rebuilt-field error, or code change since the recorded build; 6x needs ActiveSlot/PetObjectPresent/SinceHornPress; 3x needs SinceHornPress/TargetInvulnerable |
| `stance-dispel-pack-b5-bt5` | 1.0.4.304 | board 5 battle 5 (target 14651) | reflect-stance-hold,dispel,pack-release | 51 | 46 (90.2%) | 3 | 5/8 | 4x unreached: joint gap setting missed, rebuilt-field error, or code change since the recorded build; 1x needs ReadyBeastMode |

* "reproduced" counts ticks whose replayed rule equals the recorded rule; most of them are the `gcdchain` floor (the decision when nothing else fires), so the
  **decisive** column counts only ticks whose recorded decision is a real rule (hold, swap, cleanse, interrupt, pet-save...).
* The Abaddon window (recorded on 1.0.4.304) is reproduced on the 304 source and on head: `91/91`, 73 of them insensitive to every gap field, decisive 90/90.
  Its header carries `#min_reproduced_pct=95`.
* Pieces covered (board/battle): Abaddon (board 5, battle 5), board 3 battle 2, board 4 battle 1, board 4 battle 3, board 4 battle 8, board 5 battle 1, board 5 battle 10, board 5 battle 13, board 5 battle 2, board 5 battle 5, board 5 battle 9. Needs covered: interrupt (`interrupt-b5-bt10`), cleanse (`cleanse-*`), dispel (`dispel-b4-bt1`,
  `stance-dispel-pack-b5-bt5`), reflect-stance hold (`reflect-stance-b3-bt2`, `stance-*`), damage-immune hold (`invulnerable-b5-bt9`, `cleanse-invulnerable-b5-bt13`),
  death (`death-petsave-b3-bt2`: HP 3 to 0 at 13:22:03Z; `stance-death-gapclose-b3-bt2`), do-not-attack hold (Abaddon), pet-save, pack release.
* **Directional-parry hold (Forward Guard, 2026-10-05) is NOT in the corpus**: ffxivdb keeps these lines from 2026-10-07 and the archived captures under
  `~/work/bst-crucible-research/xb-captures` hold no `P` flag or `hold-parry` decision at all. The reflect-stance hold stands in for "a parry or reflect hold".
* Count the shares yourself: the `corpus:` line above comes from the command at the top; "all decision-critical fields rebuilt" is the number of ticks whose gap set holds
  no critical field (`FieldRow.Critical`, `Replayer.Run`); "insensitive" is `TickClass.Reproduced`.

Canary (Abaddon window, first hold tick): `TargetDoNotAttack` true to false changes the decision:

```
canary abaddon-idle-hold tick 1792025697132: TargetDoNotAttack true -> false
  - decision 1000004:crucible:hold-do-not-attack
  + decision 44879:gcdchain
  canary: decision flipped, as designed
```

## First Degree windows and the degree comparison (round 4)

Ten recorded First Master's Board windows (`corpus/firstdegree-*.tsv`: encounters 3241-3257 on 2026-10-14 from ffxivdb, build 1.0.4.302, `dg=1` on every `CR|` line;
encounter 2472 from the 2026-10-06 capture, build 1.0.4.288, `dg=x`: the plugin had not read the degree, kept as the unread-degree control). The replayer
rebuilds `CrucibleDegreeKnown` / `CrucibleDegreeLevel` from the `dg=` letter (`Rebuilder.Rebuild`), so the real `Decide` sees the degree the plugin saw.
The window line prints the reproduced share and the insensitive share (`exact = insensitive`: reproduced AND no sampled completion of the gap fields changes it).

After each window with a degree letter the replay decides every tick twice, with the degree from its `dg=` letter and with the degree forced to Standard
(`DegreeCompare.cs`). The guard and the cover rule read absolute HP, which the lines do not carry, so the comparison reports two things: the *realistic*
completion (character max HP 4,060 and a 3,492 HP familiar, scaled by the logged percentages, every other gap field at its baseline: a claim about the code)
and *any sampled completion* (an HP grid plus 64 random completions of every gap field: shows the scale can matter on that tick, not that it did).
The `degree what-if` line forces Second and Third on the same recorded states and counts what the Second / Third guard scales change.

| window | piece | ticks | reproduced | insensitive | `dg=` presses a different action than Standard (realistic HP / any sampled completion) |
|---|---|---|---|---|---|
| `firstdegree-2472-sapling` | Sapling (dg=x) | 417 | 379 (90.9%) | 2 (0.5%) | not run: degree unread |
| `firstdegree-3241-sapling` | Sapling | 277 | 242 (87.4%) | 2 (0.7%) | 0 / 0 |
| `firstdegree-3245-gargoyle` | Gargoyle | 245 | 204 (83.3%) | 0 (0.0%) | 2 / 2 |
| `firstdegree-3247-treant` | Treant | 216 | 195 (90.3%) | 1 (0.5%) | 0 / 0 |
| `firstdegree-3249-corpse-flower` | Corpse Flower | 254 | 212 (83.5%) | 0 (0.0%) | 0 / 5 |
| `firstdegree-3251-treant` | Treant | 251 | 208 (82.9%) | 3 (1.2%) | 0 / 0 |
| `firstdegree-3252-treant` | Treant | 230 | 210 (91.3%) | 2 (0.9%) | 0 / 0 |
| `firstdegree-3254-queen-hawk` | Queen Hawk | 242 | 213 (88.0%) | 0 (0.0%) | 0 / 4 |
| `firstdegree-3255-corpse-flower` | Corpse Flower | 202 | 181 (89.6%) | 0 (0.0%) | 0 / 4 |
| `firstdegree-3257-corpse-flower` | Corpse Flower | 323 | 260 (80.5%) | 0 (0.0%) | 0 / 4 |

The insensitive share is low because no tick has every decision-critical field rebuilt (above): these windows show the recorded decision is *reachable*, not that
nothing else was. "Presses a different action" counts a tick only when the two degrees press different actions; the same potion under another rule name (panic heal
at Standard, raidwide guard at First) is counted apart (`+N` in the window line). The one place the degree changes what is pressed on the realistic completion is
Sweeping Evisceration (cast 48717) in the Gargoyle window: with `dg=1` the replay does not press the cover Snarl (the familiar cannot take 2 x the recorded 2,021
with the 1.25 margin), as the plugin recorded; forced to Standard the same state presses it (`aggro:snarl-tankbuster`). Every other difference is Rotten Stench
(cast 48690, Corpse Flower and Queen Hawk windows) and appears only on a sampled completion: the character was between 7% and 56% HP on those ticks; under 25% the
panic heal presses the same potion at every degree, between 25% and 33% HP the First scale presses a potion that Standard would not, and whether a potion was held
is not logged (`ReadyHealPotion`).

## Adding a window

```
python3 tests/GluttonyCombo.CrucibleReplay/tools/fetch_window.py --from "2026-10-14 20:54:57-04" --to "2026-10-14 20:55:59-04" \
    --name my-window --piece "board 5 battle 5" --tags "hold,..."          # on jobunthree: docker exec ffxivdb psql, SELECT only
```

It writes `corpus/my-window.tsv` (`#key=value` header, then `UTC ts TAB message` rows of `CR|`/`BT|`/`DS|` lines, 3 s of lead-in for the first tick's `BT|`) and
`corpus/my-window.query.sql` (the exact SELECT). `--from-tsv <archive.tsv>` reads an archived plugin-lines capture instead of the database. `#build=` comes from the
newest `ER|start|p=GluttonyCombo|v=` line before the window. Copy the file to the build host, run the replay, then `--freeze` (head) and, for windows recorded on
1.0.4.304, the 304 run (below) to write `#expect.<label>`. The whole corpus is capped at 2 MB.

Replay a window on the source it was recorded on: check that revision out in a second worktree (1.0.4.304 is `ce67410e4`) and build with
`-p:SrcRoot=<worktree>\src\ -p:SrcLabel=304 -o <dir>`; `SrcLabel` names the revision in the output and selects the `#expect.304` header.

When a fix changes decisions on purpose, re-run `--freeze` for the labels you ran and commit the new numbers with the fix.

## BstState field table

`rebuildable` = a logged value maps straight on. `derivable` = computed from logged values by a rule (approximate, noted). `missing` = not logged.
A rebuildable field is also a gap on a tick that has no `BT|` line within 2 s (or whose status list was cut).

| BstState field | class | source / how |
|---|---|---|
| `Level` | rebuildable | BT lv= |
| `InCombat` | derivable | CR - true when the tick shows a fight (player/target HP below full, an enemy aimed at the character or familiar, intake > 0); otherwise unknown |
| `HasHostileTarget` | rebuildable | CR t=nameId (non-zero) |
| `TargetDistance` | derivable | CR d= - d= is the hitbox-edge distance, float.MaxValue when empty |
| `TargetHpPercent` | rebuildable | CR t=:hp |
| `TargetInterruptible` | rebuildable | CR| gate flags f=i |
| `EnemiesWithin6y` | derivable | CR ne= - enemy count stands in for the count within 6 y (upper bound) |
| `PlayerTargetedByEnemy` | derivable | CR f=y - y says the CURRENT target attacks the character; any other enemy aimed at the character is not logged |
| `PlayerIsCasting` | missing | not logged |
| `IsMoving` | derivable | CR mv= - mv= above 0.5 yalms/s |
| `PlayerTp` | rebuildable | BT gauge byte 0x08 |
| `FamiliarTp` | rebuildable | BT gauge byte 0x09 |
| `ActiveSlot` | rebuildable | BT battlehorn |
| `ComboState` | rebuildable | BT gauge byte 0x0C |
| `LastAffinity` | rebuildable | BT affinity |
| `KinshipSlot` | rebuildable | BT kinship & 0x0F |
| `MasterStacks` | rebuildable | BT gauge byte 0x10 bits 2-3 |
| `NaturalStacks` | rebuildable | BT gauge byte 0x10 bits 0-1 |
| `Slot1Beast` | rebuildable | BT sl= first |
| `Slot2Beast` | rebuildable | BT sl= second |
| `Slot3Beast` | rebuildable | BT sl= third |
| `SlotBeastsKnown` | derivable | BT sl= - any slot row non-zero |
| `PetObjectPresent` | derivable | BT pet= - pet object logged and (a slot is active or a horn was pressed lately; the press is not logged, so the active slot decides) |
| `PetObjectBeast` | rebuildable | BT pet= BNpcBase - BNpcBase mapped by BST_Beasts.RowFromBNpcBase |
| `OneWithNature` | rebuildable | BT status 4601 - status list is cut at 10 ids; a cut list makes an absent id a gap |
| `LingeringVantage` | rebuildable | BT status 4614 |
| `WaveringHeart` | rebuildable | BT status 4643 |
| `SunOrMoonActive` | rebuildable | BT status 4599/4600 |
| `SunMoon` | rebuildable | BT status 4599/4600 |
| `KinshipHeld` | derivable | BT statuses 4602-4609, 4644-4651 - any Kinship status present (the live read tests the first and last id of each set) |
| `GcdReady` | missing | not logged |
| `CanWeave` | missing | not logged |
| `SinceSummon` | derivable | BT gauge history - time since ActiveSlot became non-zero, from the BT| series in the window; unknown when the window opens with a familiar already out |
| `SinceHornPress` | missing | not logged |
| `SinceTrick` | derivable | BT tk= - tk=- means no Trick inside the 7 s window: never; otherwise unknown |
| `SinceTempered` | missing | not logged |
| `SinceBorrow` | missing | not logged |
| `SinceAxe` | missing | not logged |
| `SincePetHeart` | derivable | BT tk= - tk=- means no Trick lately: never; otherwise unknown |
| `LastAxeAffinity` | missing | not logged |
| `TemperedRecastRemaining` | missing | not logged |
| `ReadyHorn1` | missing | not logged |
| `ReadyHorn2` | missing | not logged |
| `ReadyHorn3` | missing | not logged |
| `ReadyTrick` | missing | not logged |
| `ReadyTempered` | missing | not logged |
| `ReadyBorrow` | missing | not logged |
| `ReadyParting` | missing | not logged |
| `ReadyAxe` | missing | not logged |
| `ReadyRally` | missing | not logged |
| `ReadyCheer` | missing | not logged |
| `BeastModeResolved` | derivable | BT bm= - bm= is the adjusted Beast Mode id; the base id (44886) means none held |
| `ReadyBeastMode` | missing | not logged |
| `ReadyShieldCharge` | missing | not logged |
| `ShieldChargeCharges` | missing | not logged |
| `ShieldChargeMax` | missing | not logged |
| `DashLanding` | derivable | DS v= - newest DS| verdict within 2 s; no DS| line means the live code did not read it (Safe) |
| `DashHold` | missing | not logged |
| `LastComboAction` | missing | not logged |
| `ComboTimerActive` | missing | not logged |
| `CrucibleBoard` | rebuildable | CR b= |
| `CrucibleBattle` | rebuildable | CR bt= |
| `CrucibleDegreeKnown` | derivable | CR dg= - dg=x means LazyCrucible had not published the degree |
| `CrucibleDegreeLevel` | derivable | CR dg= - digit 0-3; unread when dg=x |
| `CrucibleNeeds` | rebuildable | CR nd= |
| `EnemyCount` | rebuildable | CR ne= |
| `HighestEnemyHpPercent` | rebuildable | CR hi= |
| `PlayerHpPercent` | rebuildable | CR hp= |
| `PlayerHp` | missing | not logged |
| `ReadyHealPotion` | missing | not logged |
| `PetHpPercent` | rebuildable | CR pet= |
| `PetHp` | missing | not logged |
| `PlayerIntakePerSecond` | rebuildable | CR in= |
| `TargetVulnerabilityRemaining` | rebuildable | CR vul= (whole seconds) |
| `TargetTimeToDeath` | rebuildable | CR ttd= (whole seconds) |
| `Slot1PetHp` | rebuildable | CR sl= first |
| `Slot2PetHp` | rebuildable | CR sl= second |
| `Slot3PetHp` | rebuildable | CR sl= third |
| `PartingBlowRecast` | missing | not logged |
| `TargetHasDispellableBuff` | rebuildable | CR| gate flags f=D |
| `EnemyHasDispellableBuff` | derivable | CR f=D - implied true when the target carries one; a buff on another enemy in reach is not logged |
| `DispelCawReachable` | missing | not logged |
| `TargetInStance` | rebuildable | CR| gate flags f=S |
| `TargetDoNotAttack` | rebuildable | CR| gate flags f=X |
| `TargetInvulnerable` | missing | not logged |
| `ProtectedNearTarget` | rebuildable | CR| gate flags f=N |
| `TargetHasParry` | rebuildable | CR| gate flags f=P |
| `ParryJustEnded` | rebuildable | CR| gate flags f=J |
| `TargetCastId` | rebuildable | CR c=id |
| `TargetCastRemaining` | rebuildable | CR c=:remaining |
| `PlayerHasCleansableDebuff` | rebuildable | CR| gate flags f=C |
| `EnemyTargetsPet` | rebuildable | CR| gate flags f=p |
| `EnemyTargetsPlayer` | rebuildable | CR| gate flags f=y |
| `ReadySnarl` | rebuildable | CR| gate flags f=s |
| `ReadyChallenge` | rebuildable | CR| gate flags f=c |
| `SinceSnarl` | missing | not logged |
| `SincePartingBlow` | missing | not logged |
| `SinceCurtainsCast` | missing | not logged |
| `TargetNameId` | rebuildable | CR t=nameId |
| `ShellJustBroke` | missing | not logged |
| `BmrSeen` | rebuildable | CR bh= present |
| `BmrHitIn` | rebuildable | CR bh= |
| `BmrHitType` | rebuildable | CR bk= |
| `BmrHitOnPlayer` | rebuildable | CR bp= |
| `BmrHitOnFamiliar` | rebuildable | CR bf= |
| `BmrMaxCast` | rebuildable | CR mct= |
| `PetPoisonStatus` | derivable | CR f=V - V says a shed-eligible poison is on the familiar; the status id is not logged (first shed status stands in) |
| `PetPoisonRemaining` | missing | not logged |
| `ReleaseProtectedNear` | rebuildable | CR| gate flags f=R |
| `PlayerBlink` | rebuildable | CR| gate flags f=B |
| `PetPoisonCastId` | missing | not logged |
| `BattleHasKnockback` | missing | not logged |
| `PetOrder` | missing | not logged |
| `KnockbackCastRemaining` | missing | not logged |
| `ShieldChargeClipsGcd` | missing | not logged |
| `DashSettleRemaining` | missing | not logged |
| `AxesOffGcd` | missing | not logged (constant true since r4; recorded windows predate it, so the baseline is false) |
| `DashCageRemaining` | missing | not logged |
| `NeedleKnown` | missing | not logged (the `NE|` line carries the needle) |
| `NeedleHeading` | missing | not logged (`NE|` h=) |
| `NeedleSinceTurn` | missing | not logged (`NE|` timestamp) |
| `NeedleToPlayerBearing` | missing | not logged (`NE|` pb=) |
| `NeedleToPlayerDistance` | missing | not logged (`NE|` pd=) |
| `HourglassAge` | missing | not logged (`NE|` age=) |

Fields in `BstState`: 122 (`--fields` prints `fields: 122 (56 rebuildable, 17 derivable, 49 missing); BstState declares 122`).

## Proposal: telemetry that would close the gaps (not part of this change)

Ranked by how many corpus ticks the field alone changes the decision (the table printed under the `corpus:` line):

1. `TargetInvulnerable` (442 ticks): a flag letter `I` in `CR| f=`.
2. Readiness bitmap `rd=` (horns 1-3, Trick, Tempered, Borrow, Parting Blow, axe, Rally, Cheer, Beast Mode, Shield Charge, clip flag) plus `scc=`/`scm=` charges: `ReadyHorn1-3` alone change ~100 ticks each, `ReadyAxe`, `ReadyBeastMode`, `ReadyTempered` tens more.
3. `hpot=` (the heal potion the engine would press) and absolute `hpa=cur/max`, `peta=`: `ReadyHealPotion` changes 149 ticks.
4. `gcd=` (GCD remaining) and `weave=0/1`: `GcdReady`, `CanWeave`; `cast=0/1` for `PlayerIsCasting`; `ic=0/1` for `InCombat`.
5. Last-press ages `shp=`, `spb=`, `stp=`, `sbo=`, `sax=`, `ssn=`, `scu=` and `lca=` (last combo action), `ctm=`.
6. Flag letters `W` (Caw reachable), `K` (shell just broke); `po=` (familiar order), `kbr=`, `dsr=`, `ppr=`/`ppc=`, `kb=`.
7. The inputs of the collector's own pure rule, so its fixes become replayable: `mo=` (a Morpho lives) and `ou=` (another enemy is up) for
   `BST_CrucibleLogic.ConditionallyForbidden`, and the club element / gel flag for the Gigantis gels.
8. The recorded configuration (`BstSettings`) once per session, not per line.

All of these fit the existing `CR|` grammar (new `key=value` fields at the end; the emit key does not need them).
