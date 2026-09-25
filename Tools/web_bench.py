"""
Load the WebGL build (Builds/WebGL) in a throwaway headless Chrome and report what a player's browser would:
the GPU Chrome ended up on, how long the city takes to load, frames per second in a set of scenarios, and
any errors. Scenarios use the build's benchmark switches (GameManager.Bench):
    ?level=1&spot=Dataran&bench=noshadow,nopeds,notraffic,nosigns,nopost,far=600
Nothing here touches the user's own browser profile.

    uv run --no-project --with websocket-client --with requests python Tools/web_bench.py [scenario ...]
"""
import json, os, subprocess, sys, tempfile, time
import requests
import websocket

HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.path.normpath(os.path.join(HERE, '..', 'Builds', 'WebGL'))
CHROME = r'C:\Program Files\Google\Chrome\Application\chrome.exe'
PORT, DBG = 8766, 9333
MEASURE = 8.0

SCENARIOS = sys.argv[1:] or [
    '',                                            # the title screen, orbiting the towers
    'level=1',                                     # at home in the kampung
    'level=1&spot=Dataran',
    'level=1&spot=Dataran&bench=noshadow',
    'level=1&spot=Dataran&bench=nopeds,notraffic',
    'level=1&spot=Dataran&bench=nosigns',
    'level=1&spot=Dataran&bench=far=600',
    'level=1&spot=Dataran&bench=noshadow,nopeds,notraffic,nosigns,nopost,far=600',
    'level=1&spot=Towers',
    'level=1&spot=ChowKit',
]

server = subprocess.Popen([sys.executable, '-m', 'http.server', str(PORT), '--directory', BUILD],
                          stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
profile = tempfile.mkdtemp(prefix='kr-bench-')
chrome = subprocess.Popen([CHROME, '--headless=new', f'--remote-debugging-port={DBG}', f'--user-data-dir={profile}',
                           '--window-size=1280,720', '--use-angle=d3d11', '--enable-gpu', '--ignore-gpu-blocklist',
                           '--autoplay-policy=no-user-gesture-required', '--no-first-run', 'about:blank'],
                          stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
try:
    page = None
    for _ in range(60):
        try:
            page = next(t for t in requests.get(f'http://127.0.0.1:{DBG}/json', timeout=2).json() if t['type'] == 'page')
            break
        except Exception:
            time.sleep(0.5)
    ws = websocket.create_connection(page['webSocketDebuggerUrl'], timeout=5, suppress_origin=True)
    msg_id = [0]
    logs = []

    def send(method, params=None):
        msg_id[0] += 1
        ws.send(json.dumps({'id': msg_id[0], 'method': method, 'params': params or {}}))
        return msg_id[0]

    def pump(until=None, timeout=1.0):
        end = time.time() + timeout
        while time.time() < end:
            try:
                m = json.loads(ws.recv())
            except websocket.WebSocketTimeoutException:
                continue
            if m.get('method') == 'Runtime.consoleAPICalled':
                logs.append((m['params']['type'], ' '.join(str(a.get('value', a.get('description', ''))) for a in m['params']['args'])))
            elif m.get('method') == 'Runtime.exceptionThrown':
                d = m['params']['exceptionDetails']
                logs.append(('exception', d.get('text', '') + ' ' + str(d.get('exception', {}).get('description', ''))))
            if until is not None and m.get('id') == until:
                return m
        return None

    def evaluate(js, timeout=60):
        m = pump(until=send('Runtime.evaluate', {'expression': js, 'awaitPromise': True, 'returnByValue': True}), timeout=timeout)
        return m and m.get('result', {}).get('result', {}).get('value')

    send('Runtime.enable')
    send('Page.enable')
    gpu = None
    for sc in SCENARIOS:
        logs.clear()
        t0 = time.time()
        send('Page.navigate', {'url': f'http://127.0.0.1:{PORT}/index.html' + ('?' + sc if sc else '')})
        marker = '[Bench] ready' if 'level=' in sc else '[City] build times'
        ok = False
        while time.time() - t0 < 300:
            pump(timeout=1.0)
            if any(marker in t for _, t in logs): ok = True; break
        load = time.time() - t0
        if gpu is None:
            gpu = evaluate("""(() => { const c = document.createElement('canvas').getContext('webgl2');
                const e = c && c.getExtension('WEBGL_debug_renderer_info'); return e ? c.getParameter(e.UNMASKED_RENDERER_WEBGL) : 'unknown'; })()""")
            print('gpu:', gpu)
        pump(timeout=6.0)                          # settle (crowd and traffic fill in)
        fps = evaluate(f"""new Promise(r => {{ let n = 0; const t0 = performance.now();
            const f = () => {{ n++; if (performance.now() - t0 < {MEASURE * 1000}) requestAnimationFrame(f); else r(n / ((performance.now() - t0) / 1000)); }};
            requestAnimationFrame(f); }})""", timeout=MEASURE + 30)
        city = next((t for _, t in logs if '[City] build times' in t), '')
        print(f'{sc or "title":70s} {"ok" if ok else "NOT READY"} load {load:5.1f} s  fps {fps if fps is None else round(fps, 1)}  {city.replace("[City] build times:", "build")}')
        for kind, text in logs:
            if kind in ('error', 'exception'): print(f'    {kind}: {text[:240]}')
    ws.close()
finally:
    chrome.kill()
    server.kill()
