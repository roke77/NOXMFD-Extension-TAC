"""Browser preview of the TAC page without the game.

    python tools/preview.py [port] [path/to/NOXMFD]

Serves /ext/tac/* from this repo's src/web, /assets/shared|services/* from a NOXMFD checkout
(default: a sibling ../NOXMFD), and a mock /stream of enemy contacts with an ext.tac board slice.
POST /ext/tac/command applies add/remove/priority/assign/unassign to that board the way TacBoard.cs
does, and answers a pull from mock squad leaders; GET /commands lists what was received.
GET /scenario?s=board|empty|nosq|nomission&role=mine|free|taken|leader-free|leader-taken|member
switches what the stream sends (&note=<text> raises a notice, &reset=1 reseeds the board); apply and
release move the role between free and mine.
"""
import json, sys, threading, time
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from pathlib import Path
from urllib.parse import urlparse, parse_qs

REPO = Path(__file__).resolve().parent.parent
EXT = REPO / "src" / "web"
NOX = (Path(sys.argv[2]) if len(sys.argv) > 2 else REPO.parent / "NOXMFD") / "src" / "web"
MIME = {".css": "text/css", ".js": "text/javascript", ".html": "text/html", ".woff2": "font/woff2"}

# (id, name, stale, targeted on the officer's MAP)
CONTACTS = [
    (1, "SA-15 Tor", 0, 1), (2, "Radar Mast", 1, 0), (3, "ZSU-23-4", 0, 0), (4, "Pantsir-S1", 1, 0),
    (5, "BMP-2", 0, 0), (6, "Truck", 0, 0), (7, "KamAZ Fuel", 0, 0), (8, "HLT Flatbed", 1, 0),
    (9, "2S6 Tunguska", 0, 0), (10, "Command Post", 1, 0), (11, "Ural Truck", 0, 0), (12, "Fuel Depot", 0, 0),
    (21, "BTR-80", 0, 1), (22, "Supply Truck", 0, 1),
]
NAMES = {c[0]: c[1] for c in CONTACTS}
SQUADRONS = ["VIPER 1", "COBRA 1", "HAMMER 2", "RAVEN 3", "LANCER 1", "SABER 2"]
# What each mock leader has locked, sent back a moment after a pull.
LOCKS = {"VIPER 1": [3, 7, 1], "COBRA 1": [4, 21], "HAMMER 2": [3, 22], "RAVEN 3": [], "LANCER 1": [5, 22], "SABER 2": [8]}


def seed():
    rows = [(1, 3, [("VIPER 1", 1), ("COBRA 1", 0)]), (2, 2, [("VIPER 1", 0)]), (3, 2, [("HAMMER 2", 1)]),
            (4, 1, []), (5, 1, [("LANCER 1", 1)]), (6, 0, [("RAVEN 3", 0)]), (7, 1, [("SABER 2", 0)]),
            (8, 0, []), (9, 2, [("COBRA 1", 0)]), (10, 1, []), (11, 0, [("HAMMER 2", 1)]), (12, 0, [])]
    return [{"id": i, "n": NAMES[i], "p": p, "s": n + 1, "a": [{"q": q, "e": e} for q, e in a]}
            for n, (i, p, a) in enumerate(rows)]


# role: mine (this player holds TAC), free (nobody does, may apply), taken (MAVERICK holds it),
# leader-free / leader-taken (a squad leader's view), member (a squad member, role free).
ROLES = {
    "mine": {"mine": 1, "h": "YOU", "can": 0, "sr": 0},
    "free": {"mine": 0, "h": "", "can": 1, "sr": 0},
    "taken": {"mine": 0, "h": "MAVERICK", "can": 0, "sr": 0},
    "leader-free": {"mine": 0, "h": "", "can": 0, "sr": 1},
    "leader-taken": {"mine": 0, "h": "MAVERICK", "can": 0, "sr": 1},
    "member": {"mine": 0, "h": "", "can": 0, "sr": 2},
}
state = {"s": "board", "role": "mine"}
board = seed()
pulls = {}
note = {"s": 0, "t": ""}
commands = []
lock = threading.Lock()


def slice_():
    common = {"r": ROLES.get(state["role"], ROLES["mine"]), "note": note}
    if state["s"] == "empty":
        return {**common, "board": [], "sq": SQUADRONS}
    return {**common, "board": board, "sq": [] if state["s"] == "nosq" else SQUADRONS, "pull": pulls}


def notice(text):
    note["s"] += 1
    note["t"] = text


def frame():
    contacts = [{"id": i, "t": n, "f": 2, "x": 0, "z": 0, "h": 0, "st": st, "tg": tg} for (i, n, st, tg) in CONTACTS]
    # A friendly the officer also has selected on MAP: ADD FROM MAP must not offer it.
    contacts.append({"id": 31, "t": "FS-12 Revoker", "f": 1, "x": 0, "z": 0, "h": 0, "st": 0, "tg": 1})
    with lock:
        return {"world": {"x": 0, "z": 0}, "contacts": contacts, "ext": {"tac": slice_()}}


def apply(c):
    global board
    cmd, uid = c.get("cmd"), c.get("id", 0)
    # The role, as TacSession.cs handles it: APPLY only while free; RELEASE empties the board.
    if cmd == "apply":
        if state["role"] == "free":
            state["role"] = "mine"
        return
    if cmd == "release":
        if state["role"] == "mine":
            state["role"] = "free"
            board = []
            pulls.clear()
        return
    if state["role"] != "mine":
        return
    e = next((x for x in board if x["id"] == uid), None)
    if cmd == "add" and uid and not e:
        board.append({"id": uid, "n": c.get("name", ""), "p": 1, "s": max([x["s"] for x in board] + [0]) + 1, "a": []})
    elif cmd == "remove" and e:
        board.remove(e)
    elif cmd == "priority" and e and 0 <= c.get("p", -1) <= 3:
        e["p"] = c["p"]
    elif cmd == "assign" and e and not any(a["q"] == c.get("sq") for a in e["a"]):
        e["a"].append({"q": c["sq"], "e": 0})
    elif cmd == "unassign" and e:
        e["a"] = [a for a in e["a"] if a["q"] != c.get("sq")]
    elif cmd == "pull":
        sq = c.get("sq")
        pulls.pop(sq, None)

        def reply():
            time.sleep(0.6)
            with lock:
                pulls[sq] = [{"id": i, "n": NAMES[i]} for i in LOCKS.get(sq, [])]
        threading.Thread(target=reply, daemon=True).start()


class H(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def _file(self, p):
        if not p.is_file():
            self.send_error(404); return
        self.send_response(200)
        self.send_header("Content-Type", MIME.get(p.suffix, "application/octet-stream"))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(p.read_bytes())

    def _json(self, obj):
        body = json.dumps(obj).encode()
        self.send_response(200); self.send_header("Content-Type", "application/json"); self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        global board
        u = urlparse(self.path)
        path = u.path
        if path in ("/", "/ext/tac"):
            return self._file(EXT / "tac.html")
        if path.startswith("/ext/tac/"):
            return self._file(EXT / path[len("/ext/tac/"):])
        if path.startswith("/assets/shared/"):
            return self._file(NOX / "shared" / path[len("/assets/shared/"):])
        if path.startswith("/assets/services/"):
            return self._file(NOX / "services" / path[len("/assets/services/"):])
        if path == "/scenario":
            q = parse_qs(u.query)
            with lock:
                state["s"] = q.get("s", [state["s"]])[0]
                state["role"] = q.get("role", [state["role"]])[0]
                if "note" in q:
                    notice(q["note"][0])
                if q.get("reset", ["0"])[0] == "1":
                    board = seed(); pulls.clear()
            return self._json(state)
        if path == "/commands":
            return self._json(commands)
        if path == "/stream":
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            try:
                while True:
                    d = {"ping": True, "missionRunning": False} if state["s"] == "nomission" else frame()
                    self.wfile.write(("data: " + json.dumps(d) + "\n\n").encode())
                    self.wfile.flush()
                    time.sleep(0.1)
            except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
                return
        self.send_error(404)

    def do_POST(self):
        if urlparse(self.path).path != "/ext/tac/command":
            self.send_error(404); return
        if self.headers.get("Content-Type") != "application/json":
            self.send_error(415); return
        c = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))))
        with lock:
            commands.append(c)
            apply(c)
        self.send_response(204); self.end_headers()


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8793
    ThreadingHTTPServer(("127.0.0.1", port), H).serve_forever()
