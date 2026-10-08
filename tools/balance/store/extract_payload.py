"""Write the Open a store facts Big Copilot extracts from a save, for store_yardstick.cjs.

    python3 tools/balance/store/extract_payload.py SAVE.hsg OUT.json [--big-copilot ~/big-copilot]

Read-only: Big Copilot's own parser loads the save; nothing in the save or in Big Copilot changes.
The output holds the city's buildings, rents, market and furniture prices. Keep it out of git:
it comes from a personal save.
"""
import argparse
import json
import os
import sys

p = argparse.ArgumentParser(description=__doc__)
p.add_argument("save")
p.add_argument("out")
p.add_argument("--big-copilot", default=os.path.expanduser("~/big-copilot"))
a = p.parse_args()
sys.path.insert(0, a.big_copilot)
os.chdir(a.big_copilot)
from ba_save import load_locale, load_save  # noqa: E402
from ba_dashboard import Names, extract  # noqa: E402

save = load_save(a.save)
payload = extract(save, Names(load_locale()), None)
out = {k: payload.get(k) for k in ("openStore", "premises", "meta")}
out["build"] = save.root.get("buildNumberAtLastSave")
with open(a.out, "w", encoding="utf-8") as fh:
    json.dump(out, fh, separators=(",", ":"))
print(f"wrote {a.out}: {len(out['premises']['buildings'])} buildings, build {out['build']}")
