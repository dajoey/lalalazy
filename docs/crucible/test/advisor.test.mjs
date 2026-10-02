// node --test docs/crucible/test  — proves docs/crucible/advisor.js reproduces the C# advisor (point score) and the
// C# need-first horn picks (BST_CrucibleNeedFirst) exactly.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { createAdvisor, INT_MIN } from '../advisor.js';

const here = dirname(fileURLToPath(import.meta.url));
const data = JSON.parse(readFileSync(join(here, '..', 'data', 'advisor.json'), 'utf8'));
const golden = JSON.parse(readFileSync(join(here, '..', '..', '..', 'tests', 'CruciblePlanner.Golden', 'golden.json'), 'utf8'));
const adv = createAdvisor(data);
const hpMap = (obj) => new Map(Object.entries(obj).map(([k, v]) => [Number(k), v]));
const rosterSet = (name) => new Set(golden.rosters[name]);
const norm = (p) => ({ row: p.row, score: p.score, captured: p.captured, why: p.why });

test('data set matches the golden beast count and table sizes', () => {
  assert.equal(data.beastCount, golden.beastCount);
  assert.equal(data.beasts.length, golden.beastCount + 1);
  assert.equal(data.beastProfiles.length, golden.beastCount + 1);
  assert.equal(data.boards.length, 6);
  assert.equal(data.battles.length, 45);
  assert.equal(data.enemies.length, 122);
});

test('answers() per row', () => {
  golden.answers.forEach((a, row) => assert.equal(adv.answers(row), a, `row ${row}`));
});

test('hpFactor / effectiveScore', () => {
  for (const c of golden.hpFactor) {
    assert.equal(adv.hpFactor(c.hp), c.f, `hpFactor(${c.hp})`);
    assert.equal(adv.effectiveScore(37, c.hp), c.eff, `effectiveScore(37,${c.hp})`);
  }
});

test('hpPercentByRow', () => {
  const pets = [[1, 0, 500], [2, 500, 500], [3, 1, 500], [4, 250, 500], [5, 600, 500], [0, 10, 10], [51, 10, 10], [6, 10, 0], [7, 333, 1000]]
    .map(([row, current, max]) => ({ row, current, max }));
  const got = [...adv.hpPercentByRow(pets).entries()].sort((a, b) => a[0] - b[0]).map(([row, hp]) => ({ row, hp }));
  assert.deepEqual(got, golden.hpByRow);
});

test(`score matrix (${golden.scoreMatrix.length} cells x 3 variants)`, () => {
  let n = 0;
  for (const c of golden.scoreMatrix) {
    const w0 = [], w1 = [], w2 = [];
    assert.equal(adv.score(c.board, c.battle, c.row, 0, w0, 0), c.s0, `s0 b${c.board}/${c.battle} row ${c.row}`);
    assert.equal(w0.join(', '), c.why0, `why0 b${c.board}/${c.battle} row ${c.row}`);
    assert.equal(adv.score(c.board, c.battle, c.row, 7, w1, 1), c.s1, `s1 b${c.board}/${c.battle} row ${c.row}`);
    assert.equal(w1.join(', '), c.why1, `why1 b${c.board}/${c.battle} row ${c.row}`);
    assert.equal(adv.score(c.board, c.battle, c.row, 2, w2, 2), c.s2, `s2 b${c.board}/${c.battle} row ${c.row}`);
    assert.equal(w2.join(', '), c.why2, `why2 b${c.board}/${c.battle} row ${c.row}`);
    n++;
  }
  assert.equal(n, golden.scoreMatrix.length);
});

const normNeed = (n) => ({ kind: n.need.kind, tier: n.need.tier, what: n.need.what, src: n.need.src, row: n.row, whyNot: n.whyNot ?? null });
const normSel = (sel) => ({ picks: sel.picks.map(norm), needs: sel.needs.map(normNeed) });
const goldenSel = (sel) => ({
  picks: sel.picks.map(norm),
  needs: sel.needs.map((n) => ({ kind: n.kind, tier: n.tier, what: n.what, src: n.src, row: n.row, whyNot: n.whyNot ?? null })),
});

test(`needItems (guide counters, tiered) are in the data and cover every fight`, () => {
  assert.equal(data.needItems.length, 45);
  for (const m of golden.needModels) assert.ok(data.needItems.some((f) => f.board === m.board && f.battle === m.battle), `fight ${m.board}:${m.battle}`);
});

test(`needModel: tiered ability needs per fight (${golden.needModels.length} fights)`, () => {
  for (const m of golden.needModels) {
    const got = adv.needModel(m.board, m.battle);
    assert.deepEqual(got.items, m.items, `items ${m.board}:${m.battle}`);
    assert.equal(got.required, m.required, `required ${m.board}:${m.battle}`);
    assert.equal(got.useful, m.useful, `useful ${m.board}:${m.battle}`);
    assert.equal(got.crowdControl, m.crowdControl, `crowdControl ${m.board}:${m.battle}`);
  }
});

test(`selectCaptured: need-first picks and need status (${golden.needFirst.length} cases)`, () => {
  for (const c of golden.needFirst) {
    const set = rosterSet(c.roster);
    const got = normSel(adv.selectCaptured(c.board, c.battle, (r) => set.has(r), c.count));
    assert.deepEqual(got, goldenSel(c.selection), `selectCaptured ${c.roster} b${c.board}/${c.battle} x${c.count}`);
  }
});

test(`select with run HP: need-first picks and need status (${golden.needFirstSlots.length} cases)`, () => {
  for (const c of golden.needFirstSlots) {
    const got = normSel(adv.select(c.board, c.battle, golden.rosters[c.roster], hpMap(golden.scenarios[c.scenario]), c.slots, 'in the run roster'));
    assert.deepEqual(got, goldenSel(c.selection), `select ${c.roster} b${c.board}/${c.battle} ${c.scenario} x${c.slots}`);
  }
});

test(`worthCapturing: need-answering familiars first (${golden.needFirstWorth.length} cases)`, () => {
  for (const c of golden.needFirstWorth) {
    const set = rosterSet(c.roster);
    const captured = (r) => set.has(r);
    const selection = adv.selectCaptured(c.board, c.battle, captured, 3);
    const got = adv.worthCapturing(c.board, c.battle, captured, selection, c.count).map(norm);
    assert.deepEqual(got, c.picks.map(norm), `worth ${c.roster} b${c.board}/${c.battle} x${c.count}`);
  }
});

test(`boardRoster from need-first picks (${golden.needFirstBoardRoster.length} cases)`, () => {
  for (const c of golden.needFirstBoardRoster) {
    const set = rosterSet(c.roster);
    const got = adv.boardRoster(c.board, (r) => set.has(r));
    assert.deepEqual(got, c.rows, `boardRoster ${c.roster} b${c.board}`);
  }
});

// The reported defect: a point score let weakness and stats outvote an ability the fight needs. Strix Piece (b4:1) needs
// a dispeller only: the game never flags its Aero III as interruptible, so no horn is spent on a Soulkin for it.
test('board 4 battle 1 with a Wavekin and a Soulkin captured: the Wavekin is picked, no horn goes to the Soulkin (dispel is the only need)', () => {
  const owned = new Set([4, 7, 1, 2, 3]); // pugil (Wavekin), coblyn (Soulkin), three non-answering familiars
  const sel = adv.selectCaptured(4, 1, (r) => owned.has(r), 3);
  const rows = sel.picks.map((p) => p.row);
  assert.ok(rows.includes(4) && !rows.includes(7), `picks ${rows}`);
  assert.equal(sel.needs.length, 1, 'one need row');
  assert.ok(sel.needs.every((n) => n.row !== 0), 'every need covered');
});

test('INT_MIN sentinel matches C# int.MinValue', () => assert.equal(INT_MIN, -2147483648));
