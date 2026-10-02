// Crucible Planner — JavaScript port of the shared Crucible advisor: src/Shared/LalaCrucible/BST_CrucibleAdvisor.cs
// (the point score, kept as the tie-break) and src/Shared/LalaCrucible/BST_CrucibleNeedFirst.cs (the need-first horn
// picks LazyCrucible 0.1.9.0 uses; ported 2026-10-02). PURE: takes the exported advisor tables (data/advisor.json) and
// answers the same questions the plugin does.
// Proven identical to the C# by docs/crucible/test/advisor.test.mjs against tests/CruciblePlanner.Golden/golden.json.
// Keep the structure parallel to the C# so a change there is a one-to-one change here.

export const WEAKNESS = ['', 'fire', 'wind', 'earth', 'lightning', 'ice', 'water', 'blunt', 'piercing', 'slashing'];
export const NEEDS = { None: 0, Interrupt: 1, Dispel: 2, Cleanse: 4 };
export const TIER = { Useful: 1, Required: 2 };
/** Ability kinds in priority order (BST_CrucibleNeedFirst.Kinds). */
export const KINDS = [NEEDS.Interrupt, NEEDS.Dispel, NEEDS.Cleanse];
const ALL_ABILITIES = NEEDS.Interrupt | NEEDS.Dispel | NEEDS.Cleanse;
export const KIN = { None: 0, Beastkin: 1, Vilekin: 2, Cloudkin: 3, Seedkin: 4, Wavekin: 5, Scalekin: 6, Soulkin: 7, Ashkin: 8 };
export const RELEASE = { Damage: 1, AoE: 2, Exit: 4, Sleep: 8, Knockback: 16, DrawIn: 32, CrowdControl: 64, TargetDebuff: 128, PartyBuff: 256, Mitigation: 512, PetBuff: 1024, PetCast: 2048 };
export const INT_MIN = -2147483648;

const VULTURE_ROW = 11;
const BAT_ROW = 19;
const HP_FACTOR_FLOOR = 0.20;
const CROWD_CONTROL_BITS = 0x7FF & ~(1 << 3);
const STAT = { Str: 0, Int: 1, PhysRes: 2, MagRes: 3, Con: 4 };

/** C# Math.Round default: round half to even (banker's rounding). */
export function roundHalfEven(x) {
  const f = Math.floor(x);
  const d = x - f;
  if (d < 0.5) return f;
  if (d > 0.5) return f + 1;
  return (f % 2 === 0) ? f : f + 1;
}

const starStr = (e) => (e.stars >> 12) & 7;
const starInt = (e) => (e.stars >> 9) & 7;
const starPhysRes = (e) => (e.stars >> 6) & 7;
const starMagRes = (e) => (e.stars >> 3) & 7;

/**
 * Build an advisor bound to one data set. `data` is docs/crucible/data/advisor.json:
 *   boards[1..5] {board, level, roster, ...}; enemies[] in table order {board, battle, sub, weakness, vulnerable, needs, stars, name};
 *   battles[] in table order {board, battle, role, randomOnly}; beastProfiles[row] {autoElement, autoMagic, inflicts, stats[25]};
 *   beasts[row] {name, kin, release, captureLevel, ...}.  Index 0 of beastProfiles/beasts is null.
 *   needItems[] {board, battle, items[{kind, tier, what, src}]}: the guide's counters with their Required/Useful tier.
 */
export function createAdvisor(data) {
  const beastCount = data.beastCount;
  const enemies = data.enemies;
  const battles = data.battles;
  const profiles = data.beastProfiles;
  const beasts = data.beasts;
  const boards = data.boards;
  const maxStats = new Map();

  function byRow(row) { return row >= 1 && row <= beastCount ? beasts[row] : null; }

  function statAtBoard(profile, board, stat) {
    if (!profile.stats || board < 1 || board > 5) return 0;
    return profile.stats[(board - 1) * 5 + stat];
  }

  function maxStat(board, stat) {
    const key = board * 8 + stat;
    let max = maxStats.get(key);
    if (max !== undefined) return max;
    max = 1;
    for (let row = 1; row <= beastCount; row++) max = Math.max(max, statAtBoard(profiles[row], board, stat));
    maxStats.set(key, max);
    return max;
  }

  function answers(row) {
    const beast = byRow(row);
    if (!beast) return NEEDS.None;
    let needs = NEEDS.None;
    if (beast.kin === KIN.Soulkin) needs |= NEEDS.Interrupt;
    if (beast.kin === KIN.Wavekin || row === VULTURE_ROW) needs |= NEEDS.Dispel;
    if (beast.kin === KIN.Ashkin || row === BAT_ROW) needs |= NEEDS.Cleanse;
    return needs;
  }

  function score(board, battle, row, covered, why = null, weaknessPicks = 0) {
    if (row < 1 || row > beastCount) return INT_MIN;
    const profile = profiles[row];
    const beast = beasts[row];
    const weaknessFactor = weaknessPicks === 0 ? 1.0 : weaknessPicks === 1 ? 0.6 : 0.2;
    let weaknessScore = 0.0;
    let s = 0;
    let weaknessHits = 0;
    let cc = 0;
    let needs = NEEDS.None;
    let boss = false;

    for (const e of enemies) {
      if (e.board !== board || e.battle !== battle) continue;
      needs |= e.needs;
      const weight = e.sub === 0 ? 3 : 1;
      if (e.sub === 0 && battle === 0) boss = true;

      if (profile.autoElement !== 0 && profile.autoElement === e.weakness) {
        weaknessScore += 4 * weight * weaknessFactor;
        weaknessHits++;
      } else if (e.weakness === 0
        && (profile.autoMagic ? starMagRes(e) < starPhysRes(e) : starPhysRes(e) < starMagRes(e))) {
        s += weight;
      }

      if (cc < 3 && (profile.inflicts & e.vulnerable & CROWD_CONTROL_BITS) !== 0) cc++;
    }

    s += roundHalfEven(weaknessScore);
    if (weaknessHits > 0 && why) why.push(weaknessHits > 1 ? `${WEAKNESS[profile.autoElement]} x${weaknessHits}` : WEAKNESS[profile.autoElement]);

    s += cc;
    if (cc > 0 && why) why.push('crowd control');

    const answered = needs & ~covered & answers(row);
    if ((answered & NEEDS.Interrupt) !== 0) { s += 6; if (why) why.push('Soul Crush'); }
    if ((answered & NEEDS.Dispel) !== 0) { s += 5; if (why) why.push(row === VULTURE_ROW ? 'Bloodcurdling Caw' : 'Quelling Wave'); }
    if ((answered & NEEDS.Cleanse) !== 0) { s += 4; if (why) why.push(row === BAT_ROW ? 'Ultrasonics' : 'Scouring Ash'); }

    const stat = profile.autoMagic ? STAT.Int : STAT.Str;
    s += roundHalfEven(3.0 * statAtBoard(profile, board, stat) / maxStat(board, stat));
    s += roundHalfEven(2.0 * statAtBoard(profile, board, STAT.Con) / maxStat(board, STAT.Con));

    if (battle === 5 && board === 1 && beast.kin === KIN.Wavekin) {
      s += 4;
      if (why) why.push('Quelling Wave for wisps');
    }

    if (boss && (beast.release & RELEASE.Exit) !== 0) {
      s += 1;
      if (why) why.push('Final Sting');
    }

    return s;
  }

  function hpFactor(hpPercent) {
    if (hpPercent >= 100) return 1.0;
    if (hpPercent <= 0) return 0.0;
    return HP_FACTOR_FLOOR + (1.0 - HP_FACTOR_FLOOR) * (hpPercent / 100.0);
  }

  function effectiveScore(battleScore, hpPercent) {
    if (hpPercent <= 0 || battleScore === INT_MIN) return INT_MIN;
    return Math.floor(battleScore * hpFactor(hpPercent));
  }

  function hitsWeakness(board, battle, row) {
    const element = profiles[row].autoElement;
    if (element === 0) return false;
    for (const e of enemies) if (e.board === board && e.battle === battle && e.weakness === element) return true;
    return false;
  }

  // ------------------------------------------------------------ need-first horn picks (BST_CrucibleNeedFirst)

  const popCount = (mask) => ((mask & 1) + ((mask >> 1) & 1) + ((mask >> 2) & 1));

  /** Higher is more important: interrupt, then dispel, then cleanse. */
  function priority(mask) {
    return ((mask & NEEDS.Interrupt) !== 0 ? 4 : 0) + ((mask & NEEDS.Dispel) !== 0 ? 2 : 0) + ((mask & NEEDS.Cleanse) !== 0 ? 1 : 0);
  }

  /** The ability name for a familiar answering a kind. */
  function abilityName(row, kind) {
    if (kind === NEEDS.Interrupt) return 'Soul Crush';
    if (kind === NEEDS.Dispel) return row === VULTURE_ROW ? 'Bloodcurdling Caw' : 'Quelling Wave';
    return row === BAT_ROW ? 'Ultrasonics' : 'Scouring Ash';
  }

  /** Which familiars answer a kind, as a player would say it. */
  function kinName(kind) {
    return kind === NEEDS.Interrupt ? 'Soulkin' : kind === NEEDS.Dispel ? 'Wavekin or Vulture' : 'Ashkin or Bat';
  }

  const needModels = new Map();

  /**
   * What one battle needs, in ONE list (CrucibleNeedModel.For): the guide's tiered counters (data.needItems), then
   * the enemy panel's Required needs for every kind no Required item covers yet. Required = the panel calls for it, the
   * guide marks it mandatory, or two or more sources agree and none disputes it; Useful = single-sourced or disputed.
   */
  function needModel(board, battle) {
    const key = board * 100 + battle;
    const cached = needModels.get(key);
    if (cached) return cached;

    const items = [];
    const guide = data.needItems.find((f) => f.board === board && f.battle === battle);
    if (guide) for (const i of guide.items) items.push({ kind: i.kind, tier: i.tier, what: i.what, src: i.src });

    let panel = NEEDS.None;
    let cc = 0;
    for (const e of enemies) {
      if (e.board !== board || e.battle !== battle) continue;
      panel |= e.needs;
      cc |= e.vulnerable & CROWD_CONTROL_BITS;
    }
    for (const kind of KINDS) {
      if ((panel & kind) !== 0 && !items.some((i) => i.kind === kind && i.tier === TIER.Required)) {
        items.push({ kind, tier: TIER.Required, what: 'the enemy panel calls for it', src: ['panel'] });
      }
    }

    let required = NEEDS.None, any = NEEDS.None;
    for (const i of items) {
      any |= i.kind;
      if (i.tier === TIER.Required) required |= i.kind;
    }
    const model = { board, battle, items, required, useful: any & ~required, crowdControl: cc };
    needModels.set(key, model);
    return model;
  }

  /** Candidates in roster order: valid rows only, de-duplicated; absent HP is full (and said in why). */
  function candidates(rows, hpByRow) {
    const out = [];
    const seen = new Set();
    for (const row of rows) {
      if (row < 1 || row > beastCount || seen.has(row)) continue;
      seen.add(row);
      const known = hpByRow.has(row);
      out.push({ row, hp: known ? hpByRow.get(row) : 100, hpKnown: known });
    }
    return out;
  }

  /** Points that rank familiars covering the same needs: score with every ability need counted as answered, scaled by HP. */
  function points(board, battle, c, weaknessPicks) {
    return effectiveScore(score(board, battle, c.row, ALL_ABILITIES, null, weaknessPicks), c.hp);
  }

  function compareKeys(a, b) {
    for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return a[i] < b[i] ? -1 : 1;
    return 0;
  }

  function whyUncovered(kind, everyone, slots, pool) {
    const answerers = everyone.filter((c) => (answers(c.row) & kind) !== 0);
    if (answerers.length === 0) return `no ${kinName(kind)} ${pool}`;
    if (answerers.every((c) => c.hp <= 0)) return answerers.length === 1 ? `the ${kinName(kind)} is knocked out` : `every ${kinName(kind)} is knocked out`;
    return `the ${slots} horn slots went to other needs`;
  }

  /**
   * Horn picks for one battle from an explicit candidate roster and per-familiar HP% (Map<row, hp%>; absent = full).
   * Every Required need is covered by a healthy familiar first (one familiar covering several is preferred), then the
   * Useful needs; the point score only chooses between familiars that cover the same needs and orders the final slots.
   * Knocked-out familiars are never picked. Returns {picks, needs}: needs[] {need, row (0 = uncovered), whyNot}.
   */
  function select(board, battle, candidateRows, hpPercentByRow, slots = 3, pool = 'captured') {
    const model = needModel(board, battle);
    const everyone = candidates(candidateRows, hpPercentByRow);
    const cands = everyone.filter((c) => c.hp > 0);

    const chosen = [];
    let covered = NEEDS.None;
    const coveredBy = new Map();
    const taken = new Set();
    let weaknessPicks = 0;

    for (let n = 0; n < slots; n++) {
      let best = null, bestKey = null;
      for (const c of cands) {
        if (taken.has(c.row)) continue;
        const ans = answers(c.row);
        const req = ans & model.required & ~covered;
        const use = ans & model.useful & ~covered;
        const key = [popCount(req), popCount(use), priority(req), priority(use), points(board, battle, c, weaknessPicks), c.hp, -c.row];
        if (best === null || compareKeys(key, bestKey) > 0) { best = c; bestKey = key; }
      }
      if (best === null) break;

      const now = answers(best.row) & (model.required | model.useful) & ~covered;
      for (const kind of KINDS) if ((now & kind) !== 0) coveredBy.set(kind, best.row);
      chosen.push({ c: best, now, weak: weaknessPicks });
      taken.add(best.row);
      covered |= now;
      if (hitsWeakness(board, battle, best.row)) weaknessPicks++;
    }

    const picks = chosen.map(({ c, now, weak }) => {
      const why = [];
      for (const kind of KINDS) {
        if ((now & kind) === 0) continue;
        why.push(abilityName(c.row, kind) + ((model.required & kind) !== 0 ? '' : ' (useful)'));
      }
      score(board, battle, c.row, ALL_ABILITIES, why, weak);
      if (!c.hpKnown) why.push('HP assumed full');
      else if (c.hp < 100) why.push(`HP ${c.hp}%`);
      return { pick: { row: c.row, score: points(board, battle, c, 0), captured: true, why: why.join(', ') }, hp: c.hp };
    });

    // Slot order: best fit first (HP-scaled), healthiest on a tie, then lowest row. The set itself is need-first.
    picks.sort((a, b) => (b.pick.score - a.pick.score) || (b.hp - a.hp) || (a.pick.row - b.pick.row));

    const needs = model.items.map((item) => (coveredBy.has(item.kind)
      ? { need: item, row: coveredBy.get(item.kind), whyNot: null }
      : { need: item, row: 0, whyNot: whyUncovered(item.kind, everyone, slots, pool) }));

    return { picks: picks.map((p) => p.pick), needs };
  }

  /** The fixed-roster view with every captured familiar at full HP. captured: (row) => boolean. */
  function selectCaptured(board, battle, captured, count = 3) {
    const rows = [];
    for (let row = 1; row <= beastCount; row++) if (captured(row)) rows.push(row);
    return select(board, battle, rows, new Map(), count);
  }

  /** Picks only. Mirrors BST_CrucibleNeedFirst.Pick. */
  function pick(board, battle, captured, count = 3) {
    return selectCaptured(board, battle, captured, count).picks;
  }

  /**
   * Uncaptured familiars (capturable at the board's level) worth going for: first those that answer a need the picks
   * leave uncovered (Required before Useful), then any that beat the weakest pick on points by 3 or more.
   */
  function worthCapturing(board, battle, captured, selection, count = 2) {
    const level = boards[board].level;
    const result = [];
    const listed = new Set();

    const open = selection.needs.filter((s) => s.row === 0)
      .sort((a, b) => (b.need.tier - a.need.tier) || (priority(b.need.kind) - priority(a.need.kind)));
    for (const status of open) {
      for (let row = 1; row <= beastCount; row++) {
        if (captured(row) || beasts[row].captureLevel > level || listed.has(row) || (answers(row) & status.need.kind) === 0) continue;
        listed.add(row);
        const tag = status.need.tier === TIER.Required ? '' : ' (useful)';
        result.push({ row, score: points(board, battle, { row, hp: 100 }, 0), captured: false, why: abilityName(row, status.need.kind) + tag });
      }
    }

    const bar = selection.picks.length < 3 ? INT_MIN : Math.min(...selection.picks.map((p) => p.score)) + 3;
    const weaknessPicks = selection.picks.filter((p) => hitsWeakness(board, battle, p.row)).length;
    for (let row = 1; row <= beastCount; row++) {
      if (captured(row) || beasts[row].captureLevel > level || listed.has(row)) continue;
      const why = [];
      const s = score(board, battle, row, ALL_ABILITIES, why, Math.max(0, weaknessPicks - 1));
      if (s >= bar) result.push({ row, score: s, captured: false, why: why.join(', ') });
    }

    // Need-answering familiars come first in listed order; the points ones follow, best first (stable, like LINQ).
    const rest = result.slice(listed.size).sort((a, b) => b.score - a.score);
    return result.slice(0, listed.size).concat(rest).slice(0, count);
  }

  /** A roster for the whole board from the need-first picks. Mirrors BST_CrucibleNeedFirst.BoardRoster. */
  function boardRoster(board, captured) {
    const info = boards[board];
    const tally = new Map(); // insertion-ordered, like Dictionary without removals
    for (const battle of battles) {
      if (battle.board !== board) continue;
      for (const p of pick(board, battle.battle, captured)) {
        const t = tally.get(p.row) ?? { weight: 0, battles: 0, score: 0 };
        tally.set(p.row, { weight: t.weight + (battle.randomOnly ? 0.5 : 1.0), battles: t.battles + 1, score: t.score + p.score });
      }
    }
    return [...tally.entries()]
      .sort((a, b) => (b[1].weight - a[1].weight) || (b[1].score - a[1].score))
      .slice(0, info.roster)
      .map(([row, t]) => ({ row, battles: t.battles }));
  }
  /** pets: [{row, current, max}] -> Map<row, hp%>. Mirrors HpPercentByRow. */
  function hpPercentByRow(pets) {
    const map = new Map();
    for (const { row, current, max } of pets) {
      if (row < 1 || row > beastCount) continue;
      if (max === 0 || current === 0) { map.set(row, 0); continue; }
      if (current > max) continue;
      map.set(row, Math.min(100, Math.max(0, roundHalfEven(100.0 * current / max))));
    }
    return map;
  }

  return { answers, score, hpFactor, effectiveScore, hitsWeakness, hpPercentByRow, needModel, select, selectCaptured, pick, worthCapturing, boardRoster, maxStat };
}
