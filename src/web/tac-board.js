// Pure helpers for the TAC officer board (docs/tac-plan.md): ordering, status and the ADD FROM MAP
// list. Loaded by tac.html as a plain script and by tac-board.test.js under node.
(function (root) {
  const PRIORITIES = ['LOW', 'NORMAL', 'HIGH', 'CRITICAL'];
  const STATUSES = ['UNASSIGNED', 'ASSIGNED', 'ENGAGED'];

  // Priority first (CRITICAL on top), then the order targets were added (decision 18).
  function sortBoard(board) {
    return board.slice().sort((a, b) => b.p - a.p || a.s - b.s);
  }

  // 0 UNASSIGNED, 1 ASSIGNED, 2 ENGAGED: engaged as soon as any assignee pressed ENGAGING (decision 9).
  function status(entry) {
    const a = entry.a || [];
    if (!a.length) return 0;
    return a.some((x) => x.e) ? 2 : 1;
  }

  // One squadron's cell for a target: 'none', 'asgn' or 'engd'.
  function cellState(entry, squadron) {
    const a = (entry.a || []).find((x) => x.q === squadron);
    return !a ? 'none' : a.e ? 'engd' : 'asgn';
  }

  // The enemy units in the officer's MAP selection (decision 17; a contact's f is 2 for enemy), in
  // frame order, each flagged when it is already on the board.
  function addCandidates(contacts, board) {
    const onBoard = new Set(board.map((e) => e.id));
    return contacts.filter((c) => c.tg && c.f === 2).map((c) => ({ id: c.id, n: c.t || '', on: onBoard.has(c.id) }));
  }

  const api = { PRIORITIES, STATUSES, sortBoard, status, cellState, addCandidates };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.TacBoard = api;
})(typeof self !== 'undefined' ? self : this);
