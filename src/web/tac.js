// TAC officer page (docs/tac-plan.md): the squadron matrix. Like the ATC extension it opens its own
// TelemetrySource — NOXMFD.Api has no way to hand contacts to an extension's C# — and reads the
// board itself from this extension's published slice (d.ext.tac, TacBoard.cs). Every change goes to
// TacBoard as a command; the next frame's slice shows the result.
import { TelemetrySource } from '/assets/services/telemetry-source.js';

const { PRIORITIES, STATUSES, sortBoard, status, cellState, addCandidates } = window.TacBoard;
const HOLD_MS = 700;   // an ENGAGED cell withdraws only after a press this long (decision 21)

const $ = (id) => document.getElementById(id);
const matrixEl = $('matrix');
const countEl = $('count');
const flashEl = $('flash');
const selNameEl = $('sel-name');
const selStatusEl = $('sel-status');
const locateBtn = $('locate-btn');
const removeBtn = $('remove-btn');
const priBtnsEl = $('pri-btns');
const scrimEl = $('scrim');
const popEl = $('pop');
const popTitleEl = $('pop-title');
const popSubEl = $('pop-sub');
const popListEl = $('pop-list');
const popAllBtn = $('pop-all');

let board = [];            // the slice's board, sorted for display
let squadrons = [];        // the slice's roster: callsign + flight of each leader running TAC
let pulls = {};            // squadron -> its leader's reply to PULL ([{id, n}]), once messaging exists
let contacts = [];         // the frame's contacts
let contactsById = {};
let selectedId = 0;
let pop = null;            // the open panel: { kind: 'add' } or { kind: 'pull', sq }
let popFresh = [];         // what ADD ALL would add right now
let lastMatrixHtml = '';
let lastCount = '';
let lastPopHtml = '';
let pressing = false;      // a pointer is down on the matrix: a rebuild now would swallow its click
let holdTimer = 0;
let holdFired = false;     // the long press already withdrew: ignore the click that follows it

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function postCommand(payload) {
  // NOXMFD's command endpoint wants an exact application/json Content-Type; a failed command is
  // logged, not thrown — the next frame's slice shows what the board actually holds.
  fetch('/ext/tac/command', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
    keepalive: true,
  }).then((r) => {
    if (!r.ok) console.warn('[TAC] command rejected:', payload.cmd, r.status);
  }, (err) => console.warn('[TAC] command failed:', payload.cmd, err && err.message));
}

function flash(text) { flashEl.textContent = text; }

function selected() {
  return board.find((e) => e.id === selectedId) || board[0] || null;
}

// ── Matrix ──────────────────────────────────────────────────────────────────────────────────────
function obsOf(entry) {
  // A target the frame doesn't list (jammed picture, or no longer known) has no live contact: STALE.
  const c = contactsById[entry.id];
  return c && !c.st ? 'OBSERVED' : 'STALE';
}

function cellHtml(entry, sq) {
  const st = cellState(entry, sq);
  const label = st === 'none' ? 'Assign ' + entry.n + ' to ' + sq
    : st === 'asgn' ? 'Withdraw ' + entry.n + ' from ' + sq + ' (assigned)'
    : 'Hold to withdraw ' + entry.n + ' from ' + sq + ' (engaged)';
  const text = st === 'none' ? '·' : st === 'asgn' ? 'ASGN' : 'ENGD';
  return '<button type="button" class="tac-cell ' + st + '" data-id="' + entry.id + '" data-sq="' + escapeHtml(sq) +
    '" aria-pressed="' + (st !== 'none') + '" aria-label="' + escapeHtml(label) + '">' + text + '</button>';
}

function matrixHtml(sel) {
  let html = '<div class="tac-mhead"><div class="tac-pin"><span class="tac-colh">TARGET</span>' +
    '<span class="tac-colh">OBS</span><span class="tac-colh">PRI</span></div>';
  if (squadrons.length) {
    for (const sq of squadrons) {
      html += '<span class="tac-sqh"><span class="tac-sqname">' + escapeHtml(sq) + '</span>' +
        '<button type="button" class="tac-pull" data-sq="' + escapeHtml(sq) + '" aria-label="Pull ' + escapeHtml(sq) +
        ' targets">PULL ▼</button></span>';
    }
  } else {
    html += '<span class="tac-nosq">NO SQUAD LEADERS RUNNING TAC YET</span>';
  }
  html += '</div>';

  for (const e of board) {
    const obs = obsOf(e);
    html += '<div class="tac-row' + (sel && e.id === sel.id ? ' selected' : '') + '">' +
      '<div class="tac-pin"><button type="button" class="tac-name" data-id="' + e.id + '" aria-pressed="' +
      (!!sel && e.id === sel.id) + '">' + escapeHtml(e.n || '—') + '</button>' +
      '<span class="tac-obs' + (obs === 'STALE' ? ' stale' : '') + '">' + obs + '</span>' +
      '<span class="tac-pri p' + e.p + '">' + PRIORITIES[e.p] + '</span></div>' +
      squadrons.map((sq) => cellHtml(e, sq)).join('') + '</div>';
  }
  if (!board.length) html += '<div class="tac-empty">— BOARD EMPTY — SELECT UNITS ON MAP, THEN ADD FROM MAP —</div>';
  return html;
}

// ── Bottom section ─────────────────────────────────────────────────────────────────────────────
const priBtns = PRIORITIES.map((name, p) => {
  const b = document.createElement('button');
  b.type = 'button';
  b.className = 'lit tac-pri-btn';
  b.textContent = name;
  b.style.setProperty('--accent', ['var(--no-text-faint)', 'var(--no-label)', 'var(--no-amber)', 'var(--no-red)'][p]);
  b.addEventListener('click', () => {
    const sel = selected();
    if (sel && sel.p !== p) postCommand({ cmd: 'priority', id: sel.id, p });
  });
  priBtnsEl.appendChild(b);
  return b;
});

let lastFootKey = '';
function renderFoot(sel) {
  // Most frames change nothing here; skip the DOM writes unless the selection or its state did.
  const key = sel ? sel.id + '|' + sel.n + '|' + sel.p + '|' + status(sel) : '';
  if (key === lastFootKey) return;
  lastFootKey = key;
  selNameEl.textContent = sel ? sel.n || '—' : 'NONE';
  selNameEl.classList.toggle('none', !sel);
  const s = sel ? status(sel) : 0;
  selStatusEl.textContent = sel ? STATUSES[s] : '—';
  selStatusEl.className = 'tac-sel-status s' + s;
  locateBtn.disabled = removeBtn.disabled = !sel;
  priBtns.forEach((b, p) => {
    const on = !!sel && sel.p === p;
    b.classList.toggle('on', on);
    b.setAttribute('aria-pressed', String(on));
    b.disabled = !sel;
  });
}

// ── ADD / PULL panel ───────────────────────────────────────────────────────────────────────────
function openPop(p) {
  pop = p;
  lastPopHtml = '';
  scrimEl.hidden = popEl.hidden = false;
  renderPop();
}

function closePop() {
  pop = null;
  scrimEl.hidden = popEl.hidden = true;
}

function itemHtml(it) {
  return '<div class="tac-pop-item' + (it.on ? ' on' : '') + '"><span class="tac-pop-name">' + escapeHtml(it.n || '—') + '</span>' +
    (it.on ? '<span class="tac-pop-tag">on board ✓</span>'
      : '<button type="button" class="tac-btn tac-add" data-id="' + it.id + '" data-n="' + escapeHtml(it.n) + '">ADD</button>') +
    '</div>';
}

function renderPop() {
  if (!pop) return;
  let items, note;
  if (pop.kind === 'add') {
    items = addCandidates(contacts, board);
    popTitleEl.textContent = 'SELECTED ON MAP (' + items.length + ')';
    popSubEl.textContent = 'New targets start at NORMAL';
    note = 'No units selected — tap units on MAP first';
  } else {
    const reply = pulls[pop.sq];
    const onBoard = new Set(board.map((e) => e.id));
    items = (reply || []).map((t) => ({ id: t.id, n: t.n, on: onBoard.has(t.id) }));
    popTitleEl.textContent = pop.sq + '’S TARGETS';
    popSubEl.textContent = 'Enemy locks at the moment of the pull';
    note = reply ? 'No enemy locks' : 'Waiting for ' + pop.sq + '’s reply…';
  }
  popFresh = items.filter((it) => !it.on);
  popAllBtn.hidden = !popFresh.length;
  const html = items.length ? items.map(itemHtml).join('') : '<div class="tac-pop-note">' + escapeHtml(note) + '</div>';
  if (html !== lastPopHtml) {
    popListEl.innerHTML = html;
    lastPopHtml = html;
  }
}

function addTarget(id, n) { postCommand({ cmd: 'add', id, name: n }); }

$('add-btn').addEventListener('click', () => openPop({ kind: 'add' }));
$('pop-close').addEventListener('click', closePop);
scrimEl.addEventListener('click', closePop);
document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closePop(); });
popListEl.addEventListener('click', (e) => {
  const b = e.target.closest('button[data-id]');
  if (b) addTarget(Number(b.dataset.id), b.dataset.n);
});
popAllBtn.addEventListener('click', () => { for (const it of popFresh) addTarget(it.id, it.n); });

// ── Matrix input ───────────────────────────────────────────────────────────────────────────────
function cancelHold() {
  clearTimeout(holdTimer);
  holdTimer = 0;
  for (const c of matrixEl.querySelectorAll('.tac-cell.holding')) c.classList.remove('holding');
}

matrixEl.addEventListener('pointerdown', (e) => {
  pressing = true;
  holdFired = false;
  const cell = e.target.closest('.tac-cell.engd');
  if (!cell) return;
  cell.classList.add('holding');
  holdTimer = setTimeout(() => {
    holdFired = true;
    cell.classList.remove('holding');
    postCommand({ cmd: 'unassign', id: Number(cell.dataset.id), sq: cell.dataset.sq });
  }, HOLD_MS);
});
// Released after the click that follows the pointerup has been dispatched.
for (const ev of ['pointerup', 'pointercancel']) {
  window.addEventListener(ev, () => { cancelHold(); setTimeout(() => { pressing = false; render(); }, 0); });
}
matrixEl.addEventListener('pointerleave', cancelHold);

matrixEl.addEventListener('click', (e) => {
  const cell = e.target.closest('.tac-cell');
  if (cell) {
    const id = Number(cell.dataset.id), sq = cell.dataset.sq;
    if (cell.classList.contains('engd')) {
      if (holdFired) { holdFired = false; return; }
      flash('ENGAGED — hold the cell to withdraw ' + sq);
      return;
    }
    postCommand({ cmd: cell.classList.contains('asgn') ? 'unassign' : 'assign', id, sq });
    return;
  }
  const pull = e.target.closest('.tac-pull');
  if (pull) {
    postCommand({ cmd: 'pull', sq: pull.dataset.sq });
    openPop({ kind: 'pull', sq: pull.dataset.sq });
    return;
  }
  const name = e.target.closest('.tac-name');
  if (name) {
    selectedId = Number(name.dataset.id);
    render();
  }
});

locateBtn.addEventListener('click', () => { const sel = selected(); if (sel) postCommand({ cmd: 'locate', id: sel.id }); });
removeBtn.addEventListener('click', () => { const sel = selected(); if (sel) postCommand({ cmd: 'remove', id: sel.id }); });

// ── Frames ─────────────────────────────────────────────────────────────────────────────────────
function render() {
  const sel = selected();
  const count = '<b>' + board.length + '</b> TARGETS · <i>' + squadrons.length + '</i> SQUADRONS';
  if (count !== lastCount) countEl.innerHTML = lastCount = count;
  // ponytail: every changed frame (~10 Hz) rewrites the whole matrix in one innerHTML assignment —
  // fine for a few dozen targets × a handful of squadrons. A frame that changes nothing, or one that
  // arrives mid-press (the rebuild would drop the click and cancel a long press), leaves it alone.
  // Upgrade path for big boards: update rows and cells in place, keyed by unit id and squadron.
  const html = matrixHtml(sel);
  if (html !== lastMatrixHtml && !pressing) {
    matrixEl.innerHTML = html;
    lastMatrixHtml = html;
  }
  renderFoot(sel);
  renderPop();
}

function renderFrame(d) {
  document.body.classList.remove('no-mission');
  const slice = (d.ext && d.ext.tac) || {};
  board = sortBoard(Array.isArray(slice.board) ? slice.board : []);
  squadrons = Array.isArray(slice.sq) ? slice.sq : [];
  pulls = slice.pull || {};
  contacts = Array.isArray(d.contacts) ? d.contacts : [];
  contactsById = {};
  for (const c of contacts) contactsById[c.id] = c;
  render();
}

function handleNoMission() {
  document.body.classList.add('no-mission');
  closePop();
}

const source = new TelemetrySource({ onFrame: renderFrame, onNoMission: handleNoMission });
source.connect();
window.addEventListener('pagehide', () => source.disconnect());
