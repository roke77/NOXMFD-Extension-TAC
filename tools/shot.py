"""README screenshots of the TAC page, taken from the preview's mock data with headless Chrome.

    python tools/shot.py [port]      # preview.py must be running on that port (default 8793)

Writes, at 1000x1000, into docs/images/:
  tac-officer.png  the officer's squadron matrix, a row selected, reports waiting on two PULLs
  tac-pull.png     VIPER 1's PULL panel open: what its leader reported
  tac-add.png      ADD FROM MAP open: the officer's MAP selection, one already on the board
  tac-link.png     a squad leader picking the officer to link to
  tac-leader.png   a linked squad leader's assignments and REPORT list
"""
import asyncio, base64, json, subprocess, sys, tempfile, time, urllib.request
from pathlib import Path
import websockets

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8793
OUT = Path(__file__).resolve().parent.parent / "docs" / "images"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
W, H, DEBUG = 1000, 1000, 9334


def scenario(query):
    urllib.request.urlopen(f"http://127.0.0.1:{PORT}/scenario?{query}").read()


async def main():
    OUT.mkdir(parents=True, exist_ok=True)
    prof = tempfile.mkdtemp()
    proc = subprocess.Popen([CHROME, "--headless=new", f"--remote-debugging-port={DEBUG}", f"--user-data-dir={prof}",
                             f"--window-size={W},{H}", "about:blank"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(50):
            try:
                tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{DEBUG}/json"))
                ws_url = next(t["webSocketDebuggerUrl"] for t in tabs if t["type"] == "page")
                break
            except Exception:
                time.sleep(0.2)
        else:
            sys.exit(f"headless Chrome's debugger never answered on port {DEBUG}")
        async with websockets.connect(ws_url, max_size=None) as ws:
            n = 0

            async def cmd(method, **params):
                nonlocal n
                n += 1
                await ws.send(json.dumps({"id": n, "method": method, "params": params}))
                while True:
                    msg = json.loads(await ws.recv())
                    if msg.get("id") == n:
                        return msg.get("result", {})

            async def js(expr):
                return await cmd("Runtime.evaluate", expression=expr, awaitPromise=True)

            async def shot(name):
                await asyncio.sleep(0.6)
                r = await cmd("Page.captureScreenshot", format="png", clip={"x": 0, "y": 0, "width": W, "height": H, "scale": 1})
                (OUT / name).write_bytes(base64.b64decode(r["data"]))
                print("wrote", OUT / name)

            await cmd("Emulation.setDeviceMetricsOverride", width=W, height=H, deviceScaleFactor=1, mobile=False)
            scenario("role=mine&s=board&reset=1")
            await cmd("Page.navigate", url=f"http://127.0.0.1:{PORT}/")
            await asyncio.sleep(2.5)
            await shot("tac-officer.png")
            await js("document.querySelector('.tac-pull[data-sq=\"VIPER 1\"]').click()")
            await shot("tac-pull.png")
            await js("document.getElementById('pop-close').click(); document.getElementById('add-btn').click()")
            await shot("tac-add.png")
            await js("document.getElementById('pop-close').click()")
            scenario("role=leader-pick&s=board")
            await shot("tac-link.png")
            scenario("role=leader-taken&s=board")
            await shot("tac-leader.png")
    finally:
        proc.terminate()
        scenario("role=mine&s=board&reset=1")


asyncio.run(main())
