// Crucible Planner — JavaScript port of src/Shared/LalaCrucible/BST_CrucibleAdvisor.cs (2026-09-22).
// PURE: takes the exported advisor tables (data/advisor.json) and answers the same questions the plugin does.
// Proven identical to the C# by docs/crucible/test/advisor.test.mjs against tests/CruciblePlanner.Golden/golden.json.
// Keep the structure parallel to the C# so a change there is a one-to-one change here.

export const WEAKNESS = ['', 'fire', 'wind', 'earth', 'lightning', 'ice', 'water', 'blunt', 'piercing', 'slashing'];
export const NEEDS = { None: 0, Interrupt: 1, Dispel: 2, Cleanse: 4 };
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

  function dedupeCandidates(candidateRows) {
    const out = [];
    const seen = new Set();
    for (const row of candidateRows) {
      if (row < 1 || row > beastCount || seen.has(row)) continue;
      seen.add(row);
      out.push(row);
    }
    return out;
  }

  /** hpPercentByRow: Map<row, hp%> (absent = assumed full). Mirrors PickSlots. */
  function pickSlots(board, battle, candidateRows, hpPercentByRow, slots = 3) {
    const picks = [];
    let covered = NEEDS.None;
    const taken = new Set();
    let weaknessPicks = 0;
    const candidates = dedupeCandidates(candidateRows);

    for (let n = 0; n < slots; n++) {
      let bestRow = 0, bestEff = INT_MIN, bestHp = -1, bestHpKnown = true;
      for (const row of candidates) {
        if (taken.has(row)) continue;
        const hpKnown = hpPercentByRow.has(row);
        const hp = hpKnown ? hpPercentByRow.get(row) : 100;
        if (hp <= 0) continue;
        const raw = score(board, battle, row, covered, null, weaknessPicks);
        if (raw === INT_MIN) continue;
        const eff = effectiveScore(raw, hp);
        const better = eff > bestEff
          || (eff === bestEff && hp > bestHp)
          || (eff === bestEff && hp === bestHp && (bestRow === 0 || row < bestRow));
        if (!better) continue;
        bestEff = eff; bestRow = row; bestHp = hp; bestHpKnown = hpKnown;
      }
      if (bestRow === 0) break;
      const why = [];
      score(board, battle, bestRow, covered, why, weaknessPicks);
      if (!bestHpKnown) why.push('HP assumed full');
      else if (bestHp < 100) why.push(`HP ${bestHp}%`);
      picks.push({ row: bestRow, score: bestEff, captured: true, why: why.join(', ') });
      taken.add(bestRow);
      covered |= answers(bestRow);
      if (hitsWeakness(board, battle, bestRow)) weaknessPicks++;
    }
    return picks;
  }

  function pickSlotsCoverage(board, candidateRows, hpPercentByRow, slots = 3) {
    const boardBattles = [];
    for (const info of battles) if (info.board === board) boardBattles.push(info.battle);
    const picks = [];
    let covered = NEEDS.None;
    const taken = new Set();
    let weaknessPicks = 0;
    const candidates = dedupeCandidates(candidateRows);
    if (boardBattles.length === 0) return picks;

    for (let n = 0; n < slots; n++) {
      let bestRow = 0, bestEff = INT_MIN, bestHp = -1, bestHpKnown = true;
      for (const row of candidates) {
        if (taken.has(row)) continue;
        const hpKnown = hpPercentByRow.has(row);
        const hp = hpKnown ? hpPercentByRow.get(row) : 100;
        if (hp <= 0) continue;
        let raw = 0, any = false;
        for (const battle of boardBattles) {
          const s = score(board, battle, row, covered, null, weaknessPicks);
          if (s === INT_MIN) continue;
          raw += s; any = true;
        }
        if (!any) continue;
        const eff = effectiveScore(raw, hp);
        const better = eff > bestEff
          || (eff === bestEff && hp > bestHp)
          || (eff === bestEff && hp === bestHp && (bestRow === 0 || row < bestRow));
        if (!better) continue;
        bestEff = eff; bestRow = row; bestHp = hp; bestHpKnown = hpKnown;
      }
      if (bestRow === 0) break;
      const why = [`coverage board ${board}, battle unidentified`];
      const sampleBattle = boardBattles.includes(1) ? 1 : boardBattles[0];
      score(board, sampleBattle, bestRow, covered, why, weaknessPicks);
      if (!bestHpKnown) why.push('HP assumed full');
      else if (bestHp < 100) why.push(`HP ${bestHp}%`);
      picks.push({ row: bestRow, score: bestEff, captured: true, why: why.join(', ') });
      taken.add(bestRow);
      covered |= answers(bestRow);
      if (hitsWeakness(board, sampleBattle, bestRow)) weaknessPicks++;
    }
    return picks;
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

  /** captured: (row) => boolean. Mirrors Pick. */
  function pick(board, battle, captured, count = 3) {
    const picks = [];
    let covered = NEEDS.None;
    const taken = new Set();
    let weaknessPicks = 0;
    for (let n = 0; n < count; n++) {
      let bestRow = 0, bestScore = INT_MIN;
      for (let row = 1; row <= beastCount; row++) {
        if (taken.has(row) || !captured(row)) continue;
        const s = score(board, battle, row, covered, null, weaknessPicks);
        if (s > bestScore) { bestScore = s; bestRow = row; }
      }
      if (bestRow === 0) break;
      const why = [];
      score(board, battle, bestRow, covered, why, weaknessPicks);
      picks.push({ row: bestRow, score: bestScore, captured: true, why: why.join(', ') });
      taken.add(bestRow);
      covered |= answers(bestRow);
      if (hitsWeakness(board, battle, bestRow)) weaknessPicks++;
    }
    return picks;
  }

  function worthCapturing(board, battle, captured, picks, count = 2) {
    const level = boards[board].level;
    let bar = INT_MIN;
    if (picks.length >= 3) bar = Math.min(...picks.map(p => p.score)) + 3;
    let covered = NEEDS.None;
    let weaknessPicks = 0;
    for (const p of picks) {
      covered |= answers(p.row);
      if (hitsWeakness(board, battle, p.row)) weaknessPicks++;
    }
    const result = [];
    for (let row = 1; row <= beastCount; row++) {
      if (captured(row) || beasts[row].captureLevel > level) continue;
      const why = [];
      const s = score(board, battle, row, covered, why, Math.max(0, weaknessPicks - 1));
      if (s >= bar) result.push({ row, score: s, captured: false, why: why.join(', ') });
    }
    result.sort((a, b) => b.score - a.score); // stable, like LINQ OrderByDescending
    return result.slice(0, count);
  }

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

  return { answers, score, hpFactor, effectiveScore, hitsWeakness, pickSlots, pickSlotsCoverage, hpPercentByRow, pick, worthCapturing, boardRoster, maxStat };
}
