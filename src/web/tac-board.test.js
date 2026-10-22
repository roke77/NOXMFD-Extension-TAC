// Self-check for the TAC board helpers. Run: `node src/web/tac-board.test.js`.
const assert = require('assert');
const { sortBoard, status, cellState, addCandidates } = require('./tac-board.js');

// ── sortBoard: priority desc, then added order ──────────────────────────────
const board = [
  { id: 1, p: 1, s: 1, a: [] },
  { id: 2, p: 3, s: 2, a: [{ q: 'VIPER 1', e: 1 }, { q: 'COBRA 1', e: 0 }] },
  { id: 3, p: 1, s: 3, a: [{ q: 'HAMMER 2', e: 0 }] },
  { id: 4, p: 3, s: 4, a: [] },
];
assert.deepStrictEqual(sortBoard(board).map((e) => e.id), [2, 4, 1, 3]);
assert.deepStrictEqual(board.map((e) => e.id), [1, 2, 3, 4], 'sortBoard must not reorder its input');

// ── status: any engaged assignee makes the target ENGAGED ───────────────────
assert.strictEqual(status(board[0]), 0);
assert.strictEqual(status(board[2]), 1);
assert.strictEqual(status(board[1]), 2);
assert.strictEqual(status({ id: 9 }), 0, 'a missing assignee list reads as unassigned');

// ── cellState ───────────────────────────────────────────────────────────────
assert.strictEqual(cellState(board[1], 'VIPER 1'), 'engd');
assert.strictEqual(cellState(board[1], 'COBRA 1'), 'asgn');
assert.strictEqual(cellState(board[1], 'RAVEN 3'), 'none');

// ── addCandidates: only targeted contacts, flagged when already on the board ─
const contacts = [
  { id: 2, t: 'SA-15 Tor', tg: 1 },
  { id: 7, t: 'BTR-80', tg: 1 },
  { id: 8, t: 'Truck', tg: 0 },
];
assert.deepStrictEqual(addCandidates(contacts, board), [
  { id: 2, n: 'SA-15 Tor', on: true },
  { id: 7, n: 'BTR-80', on: false },
]);

console.log('tac-board: all checks passed');
