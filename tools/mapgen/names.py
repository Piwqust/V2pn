# Writes Assets/Map/names.json: country names for the UI languages the map data (ru/en) lacks.
# Uses CLDR through Babel (pip install babel). Run after gen.mjs: python names.py
import json
import os
from babel import Locale

ROOT = os.path.join(os.path.dirname(__file__), '..', '..', 'v2rayN', 'v2rayN.Desktop', 'Assets', 'Map')
LANGS = {'zh-Hans': 'zh_Hans', 'zh-Hant': 'zh_Hant_TW', 'fa': 'fa', 'fr': 'fr', 'hu': 'hu', 'id': 'id', 'az': 'az'}

with open(os.path.join(ROOT, 'world.json'), encoding='utf-8') as f:
    codes = sorted(json.load(f)['countries'])

out = {}
for ui, cldr in LANGS.items():
    territories = Locale.parse(cldr).territories
    out[ui] = {c: territories[c] for c in codes if c in territories}

with open(os.path.join(ROOT, 'names.json'), 'w', encoding='utf-8') as f:
    json.dump(out, f, ensure_ascii=False, separators=(',', ':'), sort_keys=True)
print({k: len(v) for k, v in out.items()}, 'of', len(codes))
