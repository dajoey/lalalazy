// node --test docs/crucible/test  — proves docs/crucible/advisor.js reproduces the C# advisor exactly.
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

test(`pick (${golden.pick.length} cases)`, () => {
  for (const c of golden.pick) {
    const set = rosterSet(c.roster);
    const got = adv.pick(c.board, c.battle, (r) => set.has(r), c.count).map(norm);
    assert.deepEqual(got, c.picks.map(norm), `pick ${c.roster} b${c.board}/${c.battle} x${c.count}`);
  }
});

test(`pickSlots (${golden.pickSlots.length} cases)`, () => {
  for (const c of golden.pickSlots) {
    const got = adv.pickSlots(c.board, c.battle, golden.rosters[c.roster], hpMap(golden.scenarios[c.scenario]), c.slots).map(norm);
    assert.deepEqual(got, c.picks.map(norm), `pickSlots ${c.roster} b${c.board}/${c.battle} ${c.scenario} x${c.slots}`);
  }
});

test(`pickSlotsCoverage (${golden.coverage.length} cases)`, () => {
  for (const c of golden.coverage) {
    const got = adv.pickSlotsCoverage(c.board, golden.rosters[c.roster], hpMap(golden.scenarios[c.scenario]), c.slots).map(norm);
    assert.deepEqual(got, c.picks.map(norm), `coverage ${c.roster} b${c.board} ${c.scenario} x${c.slots}`);
  }
});

test(`worthCapturing (${golden.worth.length} cases)`, () => {
  for (const c of golden.worth) {
    const set = rosterSet(c.roster);
    const captured = (r) => set.has(r);
    const picks = adv.pick(c.board, c.battle, captured);
    const got = adv.worthCapturing(c.board, c.battle, captured, picks, c.count).map(norm);
    assert.deepEqual(got, c.picks.map(norm), `worth ${c.roster} b${c.board}/${c.battle} x${c.count}`);
  }
});

test(`boardRoster (${golden.boardRoster.length} cases)`, () => {
  for (const c of golden.boardRoster) {
    const set = rosterSet(c.roster);
    const got = adv.boardRoster(c.board, (r) => set.has(r));
    assert.deepEqual(got, c.rows, `boardRoster ${c.roster} b${c.board}`);
  }
});

test('INT_MIN sentinel matches C# int.MinValue', () => assert.equal(INT_MIN, -2147483648));
