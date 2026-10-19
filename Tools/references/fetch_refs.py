"""Fetch reference photos from Wikimedia Commons.
usage: python fetch_refs.py <folder> "<search terms>" [count]
Saves <folder>/<n>_<title>.jpg (1280 px wide) and appends the source page to <folder>/SOURCES.md."""
import json, os, re, sys, urllib.parse, urllib.request

UA = {'User-Agent': 'KampungRunRefs/1.0 (game reference research; faiqhilman97@gmail.com)'}

def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60) as r:
        return r.read()

def main():
    folder, q = sys.argv[1], sys.argv[2]
    n = int(sys.argv[3]) if len(sys.argv) > 3 else 6
    os.makedirs(folder, exist_ok=True)
    api = 'https://commons.wikimedia.org/w/api.php?' + urllib.parse.urlencode({
        'action': 'query', 'generator': 'search', 'gsrsearch': q + ' filetype:bitmap', 'gsrnamespace': 6, 'gsrlimit': n * 2,
        'prop': 'imageinfo', 'iiprop': 'url|size', 'iiurlwidth': 1280, 'format': 'json'})
    pages = json.loads(get(api)).get('query', {}).get('pages', {})
    got = 0
    with open(os.path.join(folder, 'SOURCES.md'), 'a', encoding='utf-8') as src:
        for p in sorted(pages.values(), key=lambda p: p.get('index', 0)):
            ii = (p.get('imageinfo') or [{}])[0]
            url = ii.get('thumburl') or ii.get('url')
            if not url or ii.get('width', 0) < 600: continue
            name = re.sub(r'[^A-Za-z0-9]+', '_', p['title'][5:].rsplit('.', 1)[0])[:60]
            path = os.path.join(folder, f'{len(os.listdir(folder)):02d}_{name}.jpg')
            try:
                open(path, 'wb').write(get(url))
            except Exception as e:
                print('fail', url, e); continue
            src.write(f'- {os.path.basename(path)}: {ii.get("descriptionurl", "https://commons.wikimedia.org/wiki/" + urllib.parse.quote(p["title"]))} (search: {q})\n')
            print(path)
            got += 1
            if got >= n: break

main()
