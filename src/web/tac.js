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
const holderEl = $('holder');
const otherLampEl = $('other-lamp');
const otherMsgEl = $('other-msg');
const otherSubEl = $('other-sub');
const otherNoteEl = $('other-note');
const applyBtn = $('apply-btn');
const officersEl = $('officers');
const releaseBtn = $('release-btn');
const unlinkBtn = $('unlink-btn');
const leadHolderEl = $('lead-holder');
const leadSqEl = $('lead-sq');
const leadCountEl = $('lead-count');
const leadFlashEl = $('lead-flash');
const leadListEl = $('lead-list');
const reportListEl = $('report-list');

let board = [];            // the slice's board, sorted for display
let squadrons = [];        // the slice's roster: callsign + flight of each leader running TAC
let pulls = {};            // squadron -> the targets its leader REPORTed, waiting for a PULL ([{id, n}])
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
const NO_ROLE = { mine: 0, can: 0, sr: 0, offs: [], link: '0', lk: 0 };
let role = NO_ROLE;          // the slice's "r" (TacSession.cs); officer ids are strings
let lastNoteSeq = -1;
let lastOtherKey = '';

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

const FLASH_MS = 6000;   // a notice is read once, then gets out of the way
let flashTimer = 0;
function flash(text) {
  flashEl.textContent = otherNoteEl.textContent = leadFlashEl.textContent = text;
  clearTimeout(flashTimer);
  flashTimer = setTimeout(() => { flashEl.textContent = otherNoteEl.textContent = leadFlashEl.textContent = ''; }, FLASH_MS);
}

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
      const n = (pulls[sq] || []).length;
      html += '<span class="tac-sqh"><span class="tac-sqname">' + escapeHtml(sq) + '</span>' +
        '<button type="button" class="tac-pull' + (n ? ' has' : '') + '" data-sq="' + escapeHtml(sq) + '" aria-label="Pull ' + escapeHtml(sq) +
        ' targets (' + n + ' reported)">PULL ▼' + (n ? '<span class="tac-pull-n">' + n + '</span>' : '') + '</button></span>';
    }
  } else {
    html += '<span class="tac-nosq">NO SQUADRONS LINKED YET</span>';
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
  if (!board.length) html += '<div class="tac-empty">— BOARD EMPTY · ADD FROM MAP OR PULL —</div>';
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
    (it.on ? '<span class="tac-pop-tag">ON BOARD ✓</span>'
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
    note = 'Select enemies on MAP first';
  } else {
    // A pulled report leaves the bucket once it is on the board (TacReports.cs).
    items = pulls[pop.sq] || [];
    popTitleEl.textContent = pop.sq + ' REPORTED (' + items.length + ')';
    popSubEl.textContent = 'Pulled targets start at NORMAL';
    note = 'Nothing reported yet';
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
      flash('Hold to withdraw ' + sq);
      return;
    }
    postCommand({ cmd: cell.classList.contains('asgn') ? 'unassign' : 'assign', id, sq });
    return;
  }
  const pull = e.target.closest('.tac-pull');
  if (pull) {
    openPop({ kind: 'pull', sq: pull.dataset.sq });
    return;
  }
  const name = e.target.closest('.tac-name');
  if (name) {
    selectedId = Number(name.dataset.id);
    render();
  }
});

// ── Role ───────────────────────────────────────────────────────────────────────────────────────
// What a player who isn't an officer sees: the faction's officers, LINK for a squad leader
// (decision 24), and APPLY for a player without a squad.
function otherText(r) {
  const any = r.offs.length > 0;
  if (r.sr === 1) {
    if (!any) return { msg: 'NO TAC OFFICER IN YOUR FACTION', sub: 'Link to an officer here once one is on duty.' };
    if (r.lk === 1) return { msg: 'LINKING…', taken: true, sub: 'Waiting for the officer to answer.' };
    return { msg: 'LINK TO A TAC OFFICER', taken: true, sub: 'Pick the officer to take assignments from.' };
  }
  // Officers announce themselves to squad leaders only, so nobody else has a list to show.
  if (r.can) return { msg: 'BECOME A TAC OFFICER', sub: 'Squad leaders link to you to take your assignments.' };
  if (r.sr === 2) return { msg: 'SQUAD LEADERS WORK WITH TAC', sub: 'Your squad leader links to a TAC officer.' };
  return { msg: 'NOT IN A FACTION', sub: 'Join a faction to apply for TAC.' };
}

function officerRowHtml(o, r) {
  const action = r.sr !== 1 ? ''
    : o.id === r.link ? '<span class="tac-pop-tag">LINKING…</span>'
    : '<button type="button" class="tac-btn tac-add" data-of="' + escapeHtml(o.id) + '">LINK</button>';
  return '<div class="tac-off-row"><span class="tac-pop-name">' + escapeHtml(o.n || '—') + '</span>' + action + '</div>';
}

function linkedName(r) {
  const o = r.offs.find((x) => x.id === r.link);
  return o ? o.n : '';
}

function renderRole() {
  document.body.classList.toggle('mine', !!role.mine);
  document.body.classList.toggle('lead', !role.mine && role.sr === 1 && role.lk === 2);
  const key = JSON.stringify(role);
  if (key === lastOtherKey) return;
  lastOtherKey = key;
  const n = role.offs.length;
  holderEl.textContent = role.sr !== 1 ? '' : n ? n + ' OFFICER' + (n === 1 ? '' : 'S') : '— NONE —';
  holderEl.className = 'tac-holder' + (n ? '' : ' none');
  holderEl.previousElementSibling.style.visibility = role.sr === 1 ? '' : 'hidden';   // the "TAC:" label
  const t = otherText(role);
  otherMsgEl.textContent = t.msg;
  otherSubEl.textContent = t.sub;
  otherLampEl.classList.toggle('taken', !!t.taken);
  officersEl.innerHTML = role.offs.map((o) => officerRowHtml(o, role)).join('');
  applyBtn.hidden = !role.can;
}

applyBtn.addEventListener('click', () => postCommand({ cmd: 'apply' }));
officersEl.addEventListener('click', (e) => {
  const b = e.target.closest('button[data-of]');
  if (b) postCommand({ cmd: 'link', of: b.dataset.of });
});

// RELEASE clears the whole board and every leader's list, and UNLINK clears this leader's
// assignments, so both need the same long press as an ENGAGED cell (decision 22); a tap only says so.
function holdButton(btn, fire, hint) {
  let timer = 0;
  let fired = false;
  const cancel = () => { clearTimeout(timer); btn.classList.remove('holding'); };
  btn.addEventListener('pointerdown', () => {
    fired = false;
    btn.classList.add('holding');
    timer = setTimeout(() => { fired = true; btn.classList.remove('holding'); fire(); }, HOLD_MS);
  });
  for (const ev of ['pointerup', 'pointerleave', 'pointercancel']) btn.addEventListener(ev, cancel);
  btn.addEventListener('click', () => {
    if (fired) { fired = false; return; }
    flash(hint);
  });
}
holdButton(releaseBtn, () => postCommand({ cmd: 'release' }), 'Hold RELEASE to give up TAC');
holdButton(unlinkBtn, () => postCommand({ cmd: 'unlink' }), 'Hold UNLINK to leave this officer');

// ── Squad leader ───────────────────────────────────────────────────────────────────────────────
let lead = { q: '', l: [] };              // the slice's "lead" (TacLeader.cs)
const reported = new Set();               // ids reported this session, shown as SENT TO TAC
let lastLeadHtml = '';
let lastLeadCount = '';
let lastReportHtml = '';
let leadPressing = false;

function leadRowHtml(t) {
  const on = !!t.e;
  return '<div class="tac-lead-row"><span class="tac-lead-name">' + escapeHtml(t.n || '—') + '</span>' +
    '<span class="tac-pri p' + t.p + '">' + PRIORITIES[t.p] + '</span>' +
    '<button type="button" class="lit tac-engaging' + (on ? ' on' : '') + '" data-id="' + t.id + '" aria-pressed="' + on + '">ENGAGING</button>' +
    '<button type="button" class="tac-btn tac-acquire" data-id="' + t.id + '">ACQUIRE</button></div>';
}

function renderLead() {
  leadHolderEl.textContent = linkedName(role) || '— NONE —';
  leadSqEl.textContent = lead.q || '';
  const n = lead.l.length;
  const count = '— <b>' + n + '</b> ASSIGNMENT' + (n === 1 ? '' : 'S');
  if (count !== lastLeadCount) leadCountEl.innerHTML = lastLeadCount = count;
  const html = '<div class="tac-lead-head"><span class="tac-colh">TARGET</span><span class="tac-colh">PRIORITY</span>' +
    '<span class="tac-colh">STATUS</span><span></span></div>' +
    (n ? lead.l.map(leadRowHtml).join('') : '<div class="tac-lead-empty">— NO ASSIGNMENTS FROM TAC YET —</div>');
  if (html !== lastLeadHtml && !leadPressing) {
    leadListEl.innerHTML = html;
    lastLeadHtml = html;
  }

  // REPORT: this leader's own locked enemies, from telemetry like ADD FROM MAP (decision 17) —
  // minus the ones TAC assigned to this squadron (locked through ACQUIRE, say): TAC has them already.
  const assigned = new Set(lead.l.map((t) => t.id));
  const locks = contacts.filter((c) => c.tg && c.f === 2 && !assigned.has(c.id));
  const rhtml = locks.length ? locks.map((c) => '<div class="tac-report-row"><span class="tac-pop-name">' + escapeHtml(c.t || '—') + '</span>' +
    (reported.has(c.id) ? '<span class="tac-pop-tag">SENT TO TAC ✓</span>'
      : '<button type="button" class="tac-btn tac-add" data-id="' + c.id + '" data-n="' + escapeHtml(c.t || '') + '">REPORT</button>') + '</div>').join('')
    : '<div class="tac-report-empty">— NO ENEMY LOCKS —</div>';
  if (rhtml !== lastReportHtml && !leadPressing) {
    reportListEl.innerHTML = rhtml;
    lastReportHtml = rhtml;
  }
}

for (const el of [leadListEl, reportListEl]) el.addEventListener('pointerdown', () => { leadPressing = true; });
for (const ev of ['pointerup', 'pointercancel']) window.addEventListener(ev, () => setTimeout(() => { leadPressing = false; }, 0));
leadListEl.addEventListener('click', (e) => {
  const eng = e.target.closest('.tac-engaging');
  if (eng) { postCommand({ cmd: 'engaging', id: Number(eng.dataset.id), on: eng.getAttribute('aria-pressed') !== 'true' }); return; }
  const acq = e.target.closest('.tac-acquire');
  if (acq) postCommand({ cmd: 'acquire', id: Number(acq.dataset.id) });
});
$('acquire-all').addEventListener('click', () => postCommand({ cmd: 'acquire-all' }));
reportListEl.addEventListener('click', (e) => {
  const b = e.target.closest('button[data-id]');
  if (!b) return;
  postCommand({ cmd: 'report', id: Number(b.dataset.id) });
  reported.add(Number(b.dataset.id));
  flash('REPORTED — ' + b.dataset.n);
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
  if (document.body.classList.contains('lead')) renderLead();
}

function renderFrame(d) {
  document.body.classList.remove('no-mission');
  const slice = (d.ext && d.ext.tac) || {};
  board = sortBoard(Array.isArray(slice.board) ? slice.board : []);
  squadrons = Array.isArray(slice.sq) ? slice.sq : [];
  pulls = slice.pull || {};
  role = { ...NO_ROLE, ...slice.r };
  if (!Array.isArray(role.offs)) role.offs = [];
  const ld = slice.lead || {};
  lead = { q: ld.q || '', l: Array.isArray(ld.l) ? ld.l : [] };
  renderRole();
  if (!role.mine) closePop();
  // Each notice once: the page may have been opened long after it was raised, so the first frame
  // only records the sequence.
  const note = slice.note;
  if (note && note.s !== lastNoteSeq) {
    if (lastNoteSeq !== -1 && note.t) flash(note.t);
    lastNoteSeq = note.s;
  }
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
