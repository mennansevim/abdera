#!/usr/bin/env python3
"""Rebuild the checked-in, source-attributed Mutopia catalogue (no build-time network).

Only advertised downloads which return a successful HTTP response are retained.
One source record is one edition; paper sizes and alternate formats are not counted.
The source supplies no graded syllabus: unreviewed levels remain null.
"""
import concurrent.futures
import datetime
import hashlib
from html import unescape
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import time
from urllib.parse import urljoin, urlparse
from urllib.request import Request, urlopen

BASE = 'https://www.mutopiaproject.org/'
CACHE = Path('/tmp/abdera-sheet-music-import')
OUTPUT = Path(__file__).resolve().parents[1] / 'src/data/sheet-music.json'
CACHE.mkdir(exist_ok=True)


def get(url):
    path = CACHE / (hashlib.sha256(url.encode()).hexdigest() + '.html')
    if path.exists():
        return path.read_text()
    for attempt in range(3):
        try:
            with urlopen(Request(url, headers={'User-Agent': 'Abdera-Catalog/1.0 (educational sheet music index)'}), timeout=40) as response:
                text = response.read().decode('utf-8')
            path.write_text(text)
            time.sleep(.15)
            return text
        except Exception:
            if attempt == 2:
                raise
            time.sleep(2 + attempt)


class Cells(HTMLParser):
    def __init__(self):
        super().__init__()
        self.cells, self.links, self.current = [], [], None

    def handle_starttag(self, tag, attrs):
        if tag == 'td':
            self.current = ''
        if tag == 'a':
            self.links.append(dict(attrs).get('href', ''))

    def handle_endtag(self, tag):
        if tag == 'td' and self.current is not None:
            self.cells.append(re.sub(r'\s+', ' ', self.current).strip())
            self.current = None

    def handle_data(self, data):
        if self.current is not None:
            self.current += data


def parse_page(offset):
    html = get(BASE + f'cgibin/make-table.cgi?startat={offset}')
    records = []
    for table in re.findall(r'<table class="table-bordered result-table">(.*?)</table>', html, re.S):
        parser = Cells()
        parser.feed(table)
        c, links = parser.cells, parser.links
        info = next((x for x in links if 'piece-info.cgi?id=' in x), None)
        downloads = [urljoin(BASE, x) for x in links if re.search(r'-a4(?:\.pdf|-pdfs\.zip)$', x)]
        if not info or not downloads or len(c) < 12:
            continue
        raw = c[4].removeprefix('for ').strip()
        instrument_rules = {'piano': r'\bpiano|pianoforte', 'violin': r'\bviolin\b|\bviolins\b|\bstring', 'guitar': r'\bguitar', 'flute': r'\bflute|\brecorder', 'cello': r'\bcello', 'voice': r'\bvoice|\bchoir|\bvocal|\bsatb', 'organ': r'\borgan|harpsichord'}
        instruments = [key for key, rule in instrument_rules.items() if re.search(rule, raw, re.I)] or ['other']
        title = c[0]
        category = 'education' if c[6] == 'Technique' or re.search(r'\betude|\bétude|\betüde|\bstudy|\bstudies|\bexercise|\blesson|\bétudes|übungsstücke', title, re.I) else 'world' if c[6] == 'Folk' else 'classical'
        if re.search('Burgmüller', c[1], re.I) and re.search(r'\b100\b', c[2]):
            category = 'education'
        if re.search(r'twinkle|fr[eè]re jacques|children|kinders|kinderlieder|nursery', title, re.I):
            category = 'children'
        records.append({
            'id': 'mutopia-' + info.split('id=')[1], 'title': title,
            'composer': c[1].removeprefix('by '), 'opus': c[2],
            'instrumentation': raw, 'instruments': instruments, 'style': c[6],
            'arranger': c[7].removeprefix('arr. ').strip(), 'editionSource': c[8],
            'license': c[9], 'sourceUrl': urljoin(BASE + 'cgibin/', info),
            'downloadUrl': downloads[0], 'format': 'pdf' if downloads[0].endswith('.pdf') else 'zip',
            'previewUrl': next((urljoin(BASE, x) for x in links if re.search(r'-preview\.(svg|png)$', x)), None),
            'midiUrl': next((urljoin(BASE, x) for x in links if x.endswith('.mid')), None),
            'category': category, 'level': None, 'levelBasis': None,
        })
    return records


def check_download(record):
    path = CACHE / (hashlib.sha256(record['downloadUrl'].encode()).hexdigest() + '.check')
    if path.exists():
        return record if path.read_text() == 'ok' else None
    for attempt in range(3):
        try:
            with urlopen(Request(record['downloadUrl'], method='HEAD'), timeout=30) as response:
                content_type = response.headers.get('Content-Type', '')
                if response.status != 200 or 'text/html' in content_type:
                    raise ValueError('Not a score download')
            path.write_text('ok')
            return record
        except Exception:
            if attempt < 2:
                time.sleep(1 + attempt)
    path.write_text('failed')
    return None


def main():
    home = get(BASE)
    total = int(re.search(r'(\d+) pieces of music', home).group(1))
    items = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        for index, records in enumerate(pool.map(parse_page, range(0, total, 10))):
            items.extend(records)
            if index % 30 == 0:
                print(f'Indexed {len(items)} editions', flush=True)
    items = list({x['id']: x for x in items}.values())
    print(f'Checking {len(items)} score downloads', flush=True)
    checked = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool:
        for index, record in enumerate(pool.map(check_download, items)):
            if record:
                checked.append(record)
            if index % 200 == 0:
                print(f'Checked {index + 1}: {len(checked)} available', flush=True)
    assert len(checked) > 1200, f'Only {len(checked)} reachable editions'
    assert len({x['downloadUrl'] for x in checked}) == len(checked), 'Duplicate downloads'
    date = datetime.datetime.now(datetime.timezone.utc).date().isoformat()
    payload = {'source': 'Mutopia Project', 'sourceUrl': BASE, 'licenseUrl': BASE + 'legal.html', 'checkedAt': date, 'count': len(checked), 'items': checked}
    OUTPUT.write_text(json.dumps(payload, ensure_ascii=False, separators=(',', ':')) + '\n')
    print(json.dumps({'count': len(checked), 'piano': sum('piano' in x['instruments'] for x in checked), 'violin': sum('violin' in x['instruments'] for x in checked), 'pdf': sum(x['format'] == 'pdf' for x in checked)}, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
