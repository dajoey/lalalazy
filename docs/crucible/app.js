// Crucible Planner — page logic. Data: data/advisor.json (advisor tables), data/boards.json (map graphs + panels).
// No framework, no build step, no tracking. Roster and route live in localStorage and the URL hash.
import { createAdvisor, WEAKNESS, NEEDS } from './advisor.js';

const CONFIG = {
  kofi: '',            // Ko-fi handle (e.g. 'dajoey'). Empty hides the support button.
  // Legendary-rank score lines as reported by the board guides (not in the game files). Verify in game.
  legendary: { 1: 17500, 2: 17500, 3: 17875, 4: 18800, 5: 18500 },
};
const KIN_NAMES = ['', 'Beastkin', 'Vilekin', 'Cloudkin', 'Seedkin', 'Wavekin', 'Scalekin', 'Soulkin', 'Ashkin'];
const NODE_STYLE = {
  'Start': ['c-start', '▶'], 'Enemy': ['c-enemy', 'E'], 'Elite Enemy': ['c-elite', 'X'], 'Boss': ['c-boss', 'B'],
  'Campsite': ['c-camp', 'C'], 'Shop': ['c-shop', 'S'], 'Treasure': ['c-treasure', 'T'], 'Random': ['c-random', '?'],
};
const STAR_KEYS = [['STR', 'STR'], ['INT', 'INT'], ['PHY_R', 'Phys R'], ['MAG_R', 'Mag R'], ['CON', 'CON']];
const HP_CHOICES = [100, 75, 50, 25, 15, 0];
const STORAGE = 'crucible-planner:v1';

const state = { board: 1, owned: new Set(), hp: new Map(), trackHp: false, paths: {}, selected: null };
let data, boards, version, adv;

const $ = (sel) => document.querySelector(sel);
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const cap = (s) => s ? s[0].toUpperCase() + s.slice(1) : s;
const beastName = (row) => cap(data.beasts[row].name);
const captured = (row) => state.owned.has(row);
const elementChip = (w) => w ? `<span class="chip el-${WEAKNESS[w]}">${cap(WEAKNESS[w])}</span>` : '';

// ---------------------------------------------------------------- persistence
function save() {
  try {
    localStorage.setItem(STORAGE, JSON.stringify({
      board: state.board, owned: [...state.owned], hp: [...state.hp], trackHp: state.trackHp, paths: state.paths,
    }));
  } catch { /* private mode: fine */ }
  updateHash();
}

function load() {
  try {
    const s = JSON.parse(localStorage.getItem(STORAGE) || 'null');
    if (s) {
      if (s.board >= 1 && s.board <= 5) state.board = s.board;
      state.owned = new Set((s.owned || []).filter((r) => r >= 1 && r <= 50));
      state.hp = new Map((s.hp || []).filter(([r, h]) => r >= 1 && r <= 50 && h >= 0 && h <= 100));
      state.trackHp = !!s.trackHp;
      state.paths = s.paths && typeof s.paths === 'object' ? s.paths : {};
    }
  } catch { /* ignore */ }
  parseHash();
}

function ownedHex() {
  let n = 0n;
  for (const r of state.owned) n |= 1n << BigInt(r - 1);
  return n.toString(16);
}

function ownedFromHex(h) {
  const s = new Set();
  let n;
  try { n = BigInt('0x' + (h || '0')); } catch { return s; }
  for (let r = 1; r <= 50; r++) if ((n >> BigInt(r - 1)) & 1n) s.add(r);
  return s;
}

function updateHash() {
  const p = state.paths[state.board] || [0];
  const h = `#b=${state.board}&o=${ownedHex()}&p=${p.join(',')}`;
  if (location.hash !== h) history.replaceState(null, '', h);
}

function parseHash() {
  if (!location.hash || location.hash.length < 2) return;
  const q = new URLSearchParams(location.hash.slice(1));
  const b = Number(q.get('b'));
  if (b >= 1 && b <= 5) state.board = b;
  if (q.has('o')) state.owned = ownedFromHex(q.get('o'));
  if (q.has('p')) {
    const p = q.get('p').split(',').map(Number).filter((n) => Number.isInteger(n) && n >= 0);
    if (p.length && p[0] === 0) state.paths[state.board] = p;
  }
}

// ---------------------------------------------------------------- board graph
const boardMap = () => boards.boards[state.board - 1];
const nodeById = (bm, id) => bm.nodes[id];
const children = (bm, id) => bm.edges.filter((e) => e[0] === id).map((e) => e[1]);

function validPath(bm, p) {
  if (!p || p[0] !== 0) return false;
  for (let i = 1; i < p.length; i++) if (!children(bm, p[i - 1]).includes(p[i])) return false;
  return true;
}

function currentPath() {
  const bm = boardMap();
  let p = state.paths[state.board];
  if (!validPath(bm, p)) p = [0];
  autoAdvance(bm, p);
  state.paths[state.board] = p;
  return p;
}

function autoAdvance(bm, p) {
  for (;;) {
    const c = children(bm, p[p.length - 1]);
    if (c.length === 1 && !p.includes(c[0])) p.push(c[0]);
    else break;
  }
}

function reachableFrom(bm, id) {
  const seen = new Set([id]);
  const stack = [id];
  while (stack.length) {
    for (const c of children(bm, stack.pop())) if (!seen.has(c)) { seen.add(c); stack.push(c); }
  }
  return seen;
}

function clickNode(id) {
  const bm = boardMap();
  const p = currentPath();
  const idx = p.indexOf(id);
  if (idx >= 0) {
    p.length = idx + 1;
  } else if (children(bm, p[p.length - 1]).includes(id)) {
    p.push(id);
  } else {
    // Not the next fork: walk forward if there is exactly one way to reach it from the current end.
    const route = routeTo(bm, p[p.length - 1], id);
    if (!route) { state.selected = id; renderAll(); return; }
    p.push(...route.slice(1));
  }
  autoAdvance(bm, p);
  state.selected = id;
  save();
  renderAll();
}

function routeTo(bm, from, to) {
  // Depth-first; return the unique simple route if exactly one exists, else null.
  const routes = [];
  const walk = (id, acc) => {
    if (routes.length > 1) return;
    if (id === to) { routes.push([...acc, id]); return; }
    for (const c of children(bm, id)) walk(c, [...acc, id]);
  };
  walk(from, []);
  return routes.length === 1 ? routes[0] : null;
}

// ---------------------------------------------------------------- picks
function getPicks(board, battle) {
  if (state.trackHp) {
    // Explicit 100% for every owned familiar without a set HP, so the reasons do not say "HP assumed full".
    const rows = [...state.owned].sort((a, b) => a - b);
    const hp = new Map(rows.map((r) => [r, state.hp.has(r) ? state.hp.get(r) : 100]));
    return adv.pickSlots(board, battle, rows, hp, 3);
  }
  return adv.pick(board, battle, captured, 3);
}

function battleNeeds(board, battle) {
  let n = 0;
  for (const e of data.enemies) if (e.board === board && e.battle === battle) n |= e.needs;
  return n;
}

function needChips(n) {
  const out = [];
  if (n & NEEDS.Interrupt) out.push('<span class="chip need">Interrupt</span>');
  if (n & NEEDS.Dispel) out.push('<span class="chip need">Dispel</span>');
  if (n & NEEDS.Cleanse) out.push('<span class="chip need">Cleanse</span>');
  return out.join('');
}

// ---------------------------------------------------------------- rendering
function renderKofi() {
  const slot = $('#kofi-slot');
  slot.innerHTML = CONFIG.kofi
    ? `<a class="btn kofi" href="https://ko-fi.com/${esc(CONFIG.kofi)}" target="_blank" rel="noopener">Support on Ko-fi</a>` : '';
}

function renderVersion() {
  const stamp = `Game data: patch ${esc(version.game)} (${esc(version.versionKey || 'unknown')}), exported ${esc((version.exportedAt || '').slice(0, 10))}`;
  $('#version').textContent = stamp;
  $('#version-foot').textContent = stamp + '.';
}

function renderTabs() {
  $('#boards').innerHTML = boards.boards.map((b) => {
    const ab = data.boards[b.board];
    return `<button type="button" class="cp-tab${b.board === state.board ? ' active' : ''}" data-board="${b.board}">
      <b>${esc(b.name)}</b>
      <small>Lv ${b.level}${b.itemLevel ? ` · iLv ${b.itemLevel}` : ''} · rank ${b.rankSync} · ${b.teamSize} familiars · ${ab.battles} fights</small>
    </button>`;
  }).join('');
}

function nodeLabel(bm, n) {
  if (n.battle !== undefined) {
    const b = bm.battles[String(n.battle)];
    const nm = b?.enemies?.[0]?.name || n.type;
    return nm.length > 15 ? nm.slice(0, 14) + '…' : nm;
  }
  if (n.type === 'Campsite') return `rest ×${n.recover ?? '?'}`;
  return n.type.toLowerCase();
}

function renderMap() {
  const bm = boardMap();
  const p = currentPath();
  const last = p[p.length - 1];
  const next = new Set(children(bm, last));
  const reach = reachableFrom(bm, last);
  const xs = bm.nodes.map((n) => n.x), ys = bm.nodes.map((n) => n.y);
  const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
  const colW = 100, rowH = 58, top = 14, bottom = 30;
  const X = (n) => (n.x - minX + 0.5) * colW;
  const Y = (n) => top + (maxY - n.y + 0.5) * rowH;
  const width = (maxX - minX + 1) * colW;
  const height = top + (maxY - minY + 1) * rowH + bottom;
  const onEdge = new Set();
  for (let i = 1; i < p.length; i++) onEdge.add(`${p[i - 1]}-${p[i]}`);
  const edges = bm.edges.map(([a, b]) => {
    const na = nodeById(bm, a), nb = nodeById(bm, b);
    return `<line class="edge${onEdge.has(`${a}-${b}`) ? ' on' : ''}" x1="${X(na)}" y1="${Y(na)}" x2="${X(nb)}" y2="${Y(nb)}"/>`;
  }).join('');
  const nodes = bm.nodes.map((n) => {
    const [cls, glyph] = NODE_STYLE[n.type] || ['c-start', '·'];
    const classes = ['node'];
    if (p.includes(n.id)) classes.push('on');
    else if (!reach.has(n.id)) classes.push('off');
    if (next.has(n.id)) classes.push('next');
    if (state.selected === n.id) classes.push('sel');
    return `<g class="${classes.join(' ')}" data-id="${n.id}" role="button" tabindex="0" aria-label="Move ${n.depth}: ${esc(n.type)}">
      <circle class="${cls}" cx="${X(n)}" cy="${Y(n)}" r="17"/>
      <text class="g" x="${X(n)}" y="${Y(n)}">${glyph}</text>
      <text class="l" x="${X(n)}" y="${Y(n) + 29}">${esc(nodeLabel(bm, n))}</text>
    </g>`;
  }).join('');
  $('#map').innerHTML = `<svg viewBox="0 0 ${width} ${height}" xmlns="http://www.w3.org/2000/svg" aria-label="Board map">${edges}${nodes}</svg>`;
  $('#map-title').textContent = bm.name;
}

function renderSummary() {
  const bm = boardMap();
  const ab = data.boards[state.board];
  const team = adv.boardRoster(state.board, captured);
  const bonuses = Object.entries(bm.bonusPoints || {}).sort((a, b) => b[1] - a[1]);
  const teamHtml = state.owned.size === 0
    ? `<div class="cp-empty">Tick the familiars you own in <a href="#roster">Your roster</a> to get a team and per-fight picks.</div>`
    : team.length === 0
      ? `<div class="cp-empty">None of your familiars scores on this board yet.</div>`
      : team.map((t) => `<span class="chip">${esc(beastName(t.row))} <small>×${t.battles}</small></span>`).join('');
  $('#board-summary').innerHTML = `<div class="cp-card cp-summary">
    <div class="cp-card-head"><span class="cp-title">${esc(bm.name)}</span><span class="cp-sub">${bm.nodes.length} spaces · ${ab.battles} fights · ${bm.timeLimitMin} min limit</span></div>
    <div class="cp-kv">
      <div><b>Level sync</b>${bm.level}${bm.itemLevel ? ` / iLv ${bm.itemLevel}` : ''}</div>
      <div><b>Beast rank sync</b>${bm.rankSync}</div>
      <div><b>Team size</b>up to ${bm.teamSize}</div>
      <div><b>Unlock</b>${esc(bm.unlockQuest?.name || '—')}</div>
      <div><b>Legendary line</b>≈ ${CONFIG.legendary[state.board].toLocaleString()} <span class="cp-sub">(guides)</span></div>
    </div>
    <div class="cp-picks"><h4>Your team for this board <span class="cp-sub">(${Math.min(team.length, bm.teamSize)} of ${bm.teamSize}, familiars that make a fight's top three, most fights first)</span></h4>${teamHtml}</div>
    <details class="cp-casts"><summary>Score bonuses on this board (${bonuses.length})</summary>
      <div class="cp-bonus">${bonuses.map(([n, v]) => `<div>${esc(n)}<span>${v.toLocaleString()}</span></div>`).join('')}</div>
    </details>
  </div>`;
}

function enemyHtml(e, isBoss) {
  const stars = STAR_KEYS.map(([k, label]) => `<span><b>${label}</b>${'★'.repeat(e.stars[k] || 0)}${'☆'.repeat(5 - (e.stars[k] || 0))}</span>`).join('');
  const vuln = e.vulnerableTo.length ? e.vulnerableTo.map((v) => `<span class="chip vuln">${esc(v)}</span>`).join('') : '<span class="cp-sub">no vulnerabilities</span>';
  const casts = e.panel.map((c) => {
    const bits = [];
    if (c.castS) bits.push(`${c.castS}s cast`);
    if (c.area) bits.push(c.area.toLowerCase());
    if (c.target) bits.push(`on ${c.target.toLowerCase()}`);
    if (c.attackType) bits.push(c.attackType.toLowerCase() + (c.aspect && c.aspect !== 'Unaspected' ? `/${c.aspect.toLowerCase()}` : ''));
    const st = c.status ? ` → <b>${esc(c.status.name)}</b>${c.status.dispellable ? ' <span class="chip need">dispel it</span>' : ''}${c.status.cleansable ? ' <span class="chip need">cleanse it</span>' : ''}` : '';
    const intr = c.interruptible ? ' <span class="chip need">interruptible</span>' : '';
    return `<div class="cp-cast"><b>${esc(c.name)}</b>${intr} <span class="m">${esc(bits.join(' · '))}</span>${st}</div>`;
  }).join('') || '<div class="cp-cast cp-sub">no panel casts</div>';
  return `<div class="cp-enemy">
    <div class="nm">${esc(cap(e.name))}${isBoss ? '<small>boss</small>' : ''} ${elementChip(e.weakness)}</div>
    <div class="cp-stars">${stars}</div>
    <div style="margin-top:5px">${vuln}</div>
    <details class="cp-casts"><summary>Casts (${e.panel.length})</summary>${casts}</details>
  </div>`;
}

function picksHtml(board, battle) {
  const bm = boardMap();
  if (state.owned.size === 0) return `<div class="cp-empty">Tick your familiars in <a href="#roster">Your roster</a> to see who to bring.</div>`;
  const picks = getPicks(board, battle);
  const base = adv.pick(board, battle, captured, 3);
  const worth = adv.worthCapturing(board, battle, captured, base, 2);
  const list = picks.length
    ? picks.map((p, i) => `<div class="cp-pick"><span class="slot">${i + 1}</span><span class="nm">${esc(beastName(p.row))}</span><span class="why">${esc(p.why || '—')}</span><span class="sc">${p.score}</span></div>`).join('')
    : `<div class="cp-empty">${state.trackHp ? 'Every familiar you own is knocked out.' : 'No familiar you own scores here.'}</div>`;
  const worthHtml = worth.length
    ? `<h4>Worth capturing before this board <span class="cp-sub">(capturable by Lv ${bm.level})</span></h4>${worth.map((p) => `<div class="cp-pick"><span class="slot">+</span><span class="nm">${esc(beastName(p.row))}</span><span class="why">${esc(p.why || '—')}</span><span class="sc">${p.score}</span></div>`).join('')}`
    : '';
  return `<div class="cp-picks"><h4>Bring${state.trackHp ? ' <span class="cp-sub">(run HP applied)</span>' : ''}</h4>${list}${worthHtml}</div>`;
}

function fightCard(bm, node, battleNo, opts = {}) {
  const b = bm.battles[String(battleNo)];
  if (!b) return `<div class="cp-card"><div class="cp-title">Unknown battle ${battleNo}</div></div>`;
  const names = b.enemies.map((e) => cap(e.name)).join(', ');
  const role = battleNo === 0 ? 'Boss' : b.role;
  const needs = battleNeeds(state.board, battleNo);
  const inner = `
    <div class="cp-card-head">
      <span class="cp-move">Move ${node.depth}</span>
      <span class="cp-title">${esc(role)}: ${esc(names)}</span>
      <span class="cp-sub">${b.enemies.map((e) => elementChip(e.weakness)).join('')}${needChips(needs)}</span>
    </div>
    <div class="cp-enemies">${b.enemies.map((e) => enemyHtml(e, battleNo === 0 && e.sub === 0)).join('')}</div>
    ${picksHtml(state.board, battleNo)}`;
  return opts.nested ? `<div class="cp-enemy" style="margin-top:6px">${inner}</div>` : inner;
}

function renderSheet() {
  const bm = boardMap();
  const p = currentPath();
  const cards = [];
  for (const id of p) {
    const n = nodeById(bm, id);
    if (n.type === 'Start') continue;
    let body = '';
    if (n.battle !== undefined) body = fightCard(bm, n, n.battle);
    else if (n.type === 'Campsite') body = `<div class="cp-card-head"><span class="cp-move">Move ${n.depth}</span><span class="cp-title">Campsite</span></div><div class="cp-outcomes">Rest here. Heals you and up to ${n.recover ?? '?'} familiars; the split you choose decides how much each gets.</div>`;
    else if (n.type === 'Shop') body = `<div class="cp-card-head"><span class="cp-move">Move ${n.depth}</span><span class="cp-title">Shop</span></div><div class="cp-outcomes">Spend Territory Tokens: feed first, then potions and gear. The list scrolls.</div>`;
    else if (n.type === 'Treasure') body = `<div class="cp-card-head"><span class="cp-move">Move ${n.depth}</span><span class="cp-title">Treasure</span></div><div class="cp-outcomes">Pick one item (two with a Thief's Knife).</div>`;
    else if (n.type === 'Random') {
      const outs = (n.outcomes || []).map((o) => {
        if (o.xbm_content_battle) {
          const bt = Number(o.xbm_content_battle.split(':')[1]);
          return `<li><b>${esc(o.type)}</b>${fightCard(bm, n, bt, { nested: true })}</li>`;
        }
        if (o.type === 'Campsite') return `<li><b>Campsite</b> — rest, up to ${o.familiar_recover_count ?? '?'} familiars</li>`;
        return `<li><b>${esc(o.type)}</b></li>`;
      }).join('');
      body = `<div class="cp-card-head"><span class="cp-move">Move ${n.depth}</span><span class="cp-title">Random space</span><span class="cp-sub">resolves on arrival to one of:</span></div><ul class="cp-outcomes">${outs || '<li>unknown</li>'}</ul>`;
    } else body = `<div class="cp-card-head"><span class="cp-move">Move ${n.depth}</span><span class="cp-title">${esc(n.type)}</span></div>`;
    cards.push(`<div class="cp-card${state.selected === id ? ' sel' : ''}" data-node="${id}">${body}</div>`);
  }
  const bmChildren = children(bm, p[p.length - 1]);
  if (bmChildren.length > 1) cards.push(`<div class="cp-card"><div class="cp-title">Fork ahead</div><div class="cp-outcomes">Tap the next space on the map to choose: ${bmChildren.map((c) => `<span class="chip">${esc(nodeLabel(bm, nodeById(bm, c)))}</span>`).join(' ')}</div></div>`);
  $('#run-sheet').innerHTML = cards.join('');
}

function renderRoster() {
  const groups = KIN_NAMES.map(() => []);
  for (let r = 1; r <= data.beastCount; r++) groups[data.beasts[r].kin].push(r);
  $('#roster-count').textContent = `${state.owned.size} / ${data.beastCount}`;
  $('#roster-grid').innerHTML = groups.map((rows, kin) => {
    if (!rows.length) return '';
    const own = rows.filter(captured).length;
    const role = kin === 7 ? 'interrupt (Soul Crush)' : kin === 5 ? 'dispel (Quelling Wave)' : kin === 8 ? 'cleanse (Scouring Ash)' : '';
    return `<div class="cp-kin"><h4>${KIN_NAMES[kin]} <small>${own}/${rows.length}${role ? ' · ' + role : ''}</small></h4>${rows.map((r) => {
      const b = data.beasts[r];
      const prof = data.beastProfiles[r];
      const owned = captured(r);
      const hp = state.hp.has(r) ? state.hp.get(r) : 100;
      const hpSel = state.trackHp && owned
        ? `<select data-hp="${r}" aria-label="HP of ${esc(beastName(r))}">${HP_CHOICES.map((h) => `<option value="${h}"${h === hp ? ' selected' : ''}>${h === 0 ? 'KO' : h + '%'}</option>`).join('')}</select>` : '';
      const extra = r === 11 ? ' <span class="chip need">dispel</span>' : r === 19 ? ' <span class="chip need">cleanse</span>' : '';
      return `<label class="cp-beast${owned ? '' : ' unowned'}"><input type="checkbox" data-row="${r}"${owned ? ' checked' : ''}> ${esc(beastName(r))} ${elementChip(prof.autoElement)}${extra}${hpSel}<span class="lv">Lv ${b.captureLevel}</span></label>`;
    }).join('')}</div>`;
  }).join('');
}

function renderAll() {
  renderTabs();
  renderMap();
  renderSummary();
  renderSheet();
  renderRoster();
}

function toast(msg) {
  const t = document.createElement('div');
  t.className = 'cp-toast';
  t.textContent = msg;
  document.body.appendChild(t);
  setTimeout(() => t.remove(), 1600);
}

// ---------------------------------------------------------------- events
function wire() {
  document.addEventListener('click', (ev) => {
    const tab = ev.target.closest('.cp-tab');
    if (tab) { state.board = Number(tab.dataset.board); state.selected = null; save(); renderAll(); return; }
    const node = ev.target.closest('.node');
    if (node) { clickNode(Number(node.dataset.id)); return; }
    const card = ev.target.closest('.cp-card[data-node]');
    if (card && !ev.target.closest('a, summary, details')) { state.selected = Number(card.dataset.node); renderMap(); renderSheet(); return; }
    if (ev.target.closest('#reset-route')) { state.paths[state.board] = [0]; state.selected = null; save(); renderAll(); return; }
    const rt = ev.target.closest('[data-roster]');
    if (rt) {
      const mode = rt.dataset.roster;
      const lv = { lv30: 30, lv40: 40, lv50: 50 }[mode];
      state.owned = new Set();
      if (mode === 'all') for (let r = 1; r <= data.beastCount; r++) state.owned.add(r);
      if (lv) for (let r = 1; r <= data.beastCount; r++) if (data.beasts[r].captureLevel <= lv) state.owned.add(r);
      save(); renderAll(); return;
    }
    if (ev.target.closest('#share')) {
      updateHash();
      const url = location.href;
      (navigator.clipboard ? navigator.clipboard.writeText(url) : Promise.reject()).then(() => toast('Link copied'), () => prompt('Copy this link', url));
    }
  });
  document.addEventListener('keydown', (ev) => {
    if ((ev.key === 'Enter' || ev.key === ' ') && ev.target.classList?.contains('node')) { ev.preventDefault(); clickNode(Number(ev.target.dataset.id)); }
  });
  document.addEventListener('change', (ev) => {
    const cb = ev.target.closest('input[data-row]');
    if (cb) {
      const r = Number(cb.dataset.row);
      if (cb.checked) state.owned.add(r); else { state.owned.delete(r); state.hp.delete(r); }
      save(); renderSummary(); renderSheet(); renderRoster(); return;
    }
    const hp = ev.target.closest('select[data-hp]');
    if (hp) {
      const r = Number(hp.dataset.hp), v = Number(hp.value);
      if (v >= 100) state.hp.delete(r); else state.hp.set(r, v);
      save(); renderSummary(); renderSheet(); return;
    }
    if (ev.target.id === 'track-hp') { state.trackHp = ev.target.checked; save(); renderSheet(); renderRoster(); }
  });
  window.addEventListener('hashchange', () => { parseHash(); renderAll(); });
}

async function main() {
  const [a, b, v] = await Promise.all(['data/advisor.json', 'data/boards.json', 'data/version.json'].map((u) => fetch(u).then((r) => {
    if (!r.ok) throw new Error(`${u}: HTTP ${r.status}`);
    return r.json();
  })));
  data = a; boards = b; version = v;
  adv = createAdvisor(data);
  load();
  $('#track-hp').checked = state.trackHp;
  renderKofi();
  renderVersion();
  renderAll();
  wire();
  updateHash();
}

main().catch((err) => {
  $('#version').textContent = `Could not load the game data (${err.message}). Reload, or open an issue on GitHub.`;
  console.error(err);
});
